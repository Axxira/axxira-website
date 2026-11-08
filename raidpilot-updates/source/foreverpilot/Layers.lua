-- Original ForeverPilot layer assistance. No layer IDs are inferred from GUIDs.
-- A group invitation is only an attempt; SHARD_TRANSFER is the confirmation.
local B=ForeverPilotLeveling
local L={prefix="FPLLayers1",channel="ForeverPilotLayers",state="idle",detail="Cherche un volontaire dans ta zone.",sequence=0}
B.Layers=L
local function now() return B.Now() end
local function clean(v) v=B.Public(v);return type(v)=="string" and v or nil end
local function playerKey(name)
    name=clean(name)
    if not name or name=="" or #name>100 or name:find("[|%c]") then return nil end
    local realm=B.Call(GetNormalizedRealmName) or ""
    if not name:find("-",1,true) then name=name.."-"..realm end
    return name:lower()
end
function L:Enabled() return B.settings and B.settings.enabled~=false end
function L:Busy() return self.state=="search" or self.state=="invite" or self.state=="transfer" end
function L:Context()
    return B.Call(C_Map and C_Map.GetBestMapForUnit,"player"),B.Call(UnitFactionGroup,"player")
end
function L:Blocked(allowGroup)
    if not self:Enabled() then return "Active ForeverPilot avec /fpl on." end
    if B.Call(InCombatLockdown) or B.Call(UnitAffectingCombat,"player") then return "Attends la fin du combat." end
    if B.Call(UnitIsDeadOrGhost,"player") then return "Reviens en vie avant de changer de layer." end
    if B.Call(IsInInstance) then return "Disponible uniquement dans le monde ouvert." end
    if B.Call(C_ChatInfo and C_ChatInfo.InChatMessagingLockdown) then return "Communication temporairement bloquée par WoW." end
    if not allowGroup and B.Call(IsInGroup) then return "Tu es déjà en groupe. Aucun départ automatique." end
end
function L:Notify(state,detail)
    self.state=state;self.detail=detail
    if B.LayerUI then B.LayerUI:Refresh() end
end
function L:ChannelID()
    local id=B.Call(GetChannelName,self.channel)
    return type(id)=="number" and id>0 and id or nil
end
function L:Join()
    if self:ChannelID() then return true end
    if type(JoinChannelByName)~="function" then return false end
    self.ownsChannel=true
    local ok=pcall(JoinChannelByName,self.channel)
    if not ok then self.ownsChannel=nil;return false end
    return self:ChannelID()~=nil
end
function L:ReleaseChannel()
    if self.ownsChannel and not (B.char and B.char.layerHelper) then
        B.Call(LeaveChannelByName,self.channel);self.ownsChannel=nil
    end
end
function L:Stop(state,detail)
    self.request=nil;self.offer=nil;self:ReleaseChannel()
    self:Notify(state or "idle",detail or "Recherche annulée. Ton groupe reste inchangé.")
end
function L:Send(kind,token,map,faction,channel,target)
    if not self.registered or B.Call(C_ChatInfo and C_ChatInfo.InChatMessagingLockdown) then return false end
    local fn=C_ChatInfo and C_ChatInfo.SendAddonMessage
    if type(fn)~="function" then return false end
    local ok,result=pcall(fn,self.prefix,table.concat({"1",kind,token,tostring(map),faction},"|"),channel,target)
    result=B.Public(result)
    -- Current Forever returns an enum (Success=0), not a boolean.
    return ok and (result==0 or result==true)
end
function L:Start()
    if self:Busy() then return end
    local blocked=self:Blocked(false)
    if blocked then self:Notify("blocked",blocked);return end
    if not self.registered then self:Notify("unavailable","Communication addon indisponible. Utilise Demander dans Général.");return end
    if self.nextSearch and now()<self.nextSearch then
        self:Notify("cooldown","Nouvelle demande possible dans "..math.ceil(self.nextSearch-now()).." s (anti-spam ForeverPilot).");return
    end
    local map,faction=self:Context()
    if type(map)~="number" or (faction~="Horde" and faction~="Alliance") then self:Notify("blocked","Zone ou faction indisponible. Réessaie dans un instant.");return end
    local joined=self:Join()
    if not joined and not self.joinAttempted then
        self.joinAttempted=true;self.joinDeadline=now()+5
        self:Notify("joining","Connexion au canal ForeverPilot. Reclique sur Chercher lorsqu'il est prêt.");return
    end
    self.joinAttempted=nil;self.sequence=self.sequence+1
    local token=string.format("%d-%d",math.floor(now()*1000),self.sequence)
    self.request={token=token,map=map,faction=faction,untilAt=now()+20}
    self.nextSearch=now()+60
    B.char.layerNextSearch=time()+60
    local sent=false
    if B.Call(IsInGuild) then sent=self:Send("Q",token,map,faction,"GUILD") end
    local channelID=joined and self:ChannelID()
    -- CHANNEL addon packets are not supported reliably by Forever. Only a
    -- direct button click sends this visible, single request in our own channel.
    if channelID and C_ChatInfo and type(C_ChatInfo.SendChatMessage)=="function" then
        local ok,result=pcall(C_ChatInfo.SendChatMessage,"ForeverPilot layer: "..token.."|"..map.."|"..faction,"CHANNEL",nil,channelID)
        result=B.Public(result)
        sent=sent or (ok and result~=false)
    end
    if not sent then self:Stop("unavailable","Réseau indisponible. Utilise Demander dans Général.");return end
    self:Notify("search","Recherche parmi les utilisateurs ForeverPilot de ta zone…")
end
function L:SetHelper(enabled)
    B.char.layerHelper=enabled==true;self.offer=nil
    if enabled then self:Join() else self:ReleaseChannel() end
    if B.LayerUI then B.LayerUI:Refresh() end
end
function L:Receive(text,channel,sender)
    text=clean(text);channel=clean(channel);sender=clean(sender)
    if not self:Enabled() or not text or #text>160 or not sender then return end
    local key=playerKey(sender);local own=playerKey(B.Call(UnitName,"player"))
    if not key or key==own then return end
    local kind,token,map,faction=text:match("^1|([QOPI])|([%d%-]+)|(%d+)|(%a+)$")
    if not kind or #token>40 then return end
    map=tonumber(map)
    local currentMap,currentFaction=self:Context()
    if map~=currentMap or faction~=currentFaction then return end
    local r=self.request
    if kind=="Q" then
        if channel~="GUILD" and channel~="CHANNEL" then return end
        if not B.char.layerHelper or self:Busy() or self:Blocked(false) or self.pendingInvite then return end
        if self.helperAt and now()<self.helperAt then return end
        -- Reserve one helper slot for 12 seconds; repeats cannot fan out invites.
        self.helperAt=now()+12;self.offer={key=key,sender=sender,token=token,map=map,faction=faction,untilAt=now()+12}
        if not self:Send("O",token,map,faction,"WHISPER",sender) then self.offer=nil end
    elseif kind=="O" and channel=="WHISPER" and self.state=="search" and r and r.token==token and now()<r.untilAt then
        if self:Blocked(false) then return end
        r.helper=key;r.sender=sender;r.untilAt=now()+30
        -- Lock the first offer before sending; later offers are ignored.
        self:Notify("invite","Volontaire trouvé. Accepte son invitation WoW lorsqu'elle arrive.")
        if not self:Send("P",token,map,faction,"WHISPER",sender) then self:Stop("unavailable","Demande refusée par WoW. Utilise Demander dans Général.") end
    elseif kind=="P" and channel=="WHISPER" then
        local o=self.offer
        if not o or o.key~=key or o.token~=token or now()>=o.untilAt or not B.char.layerHelper or self:Blocked(false) or self.pendingInvite then return end
        self.offer=nil;self.helperAt=now()+60
        local fn=C_PartyInfo and C_PartyInfo.InviteUnit
        if type(fn)~="function" or B.Call(C_PartyInfo.CanInvite)==false then return end
        local ok=pcall(fn,sender)
        if ok then self:Send("I",token,map,faction,"WHISPER",sender) end
    elseif kind=="I" and channel=="WHISPER" and r and r.helper==key and r.token==token and self.state=="invite" then
        self:Notify("invite","Invitation demandée. Valide l'invitation WoW; le serveur décide du transfert.")
    end
end
function L:Pending()
    -- Time remaining alone has an ambiguous zero when no transfer is pending.
    -- Use the server event or a visible native popup, including after /reload.
    local imminent=B.Call(StaticPopup_Visible,"SHARD_TRANSFER_IMMINENT_EVENT")
    local immediate=B.Call(StaticPopup_Visible,"SHARD_TRANSFER_IMMEDIATE")
    return self.pending==true or (imminent~=nil and imminent~=false) or (immediate~=nil and immediate~=false)
end
function L:Confirm()
    local blocked=self:Blocked(true)
    if blocked then self:Notify("blocked",blocked);return end
    if not self:Pending() then self:Notify("idle","WoW ne propose aucun transfert pour le moment.");return end
    local fn=C_PlayerInteractionManager and C_PlayerInteractionManager.ShardTransferConfirm
    if type(fn)~="function" then self:Notify("blocked","Utilise le bouton de changement affiché par WoW.");return end
    local ok,err=pcall(fn)
    if not ok then self.lastError=tostring(err);self:Notify("blocked","WoW a refusé le changement. Utilise sa fenêtre de confirmation.");return end
    self.request=nil;self.transferDeadline=now()+30
    self:Notify("transfer","Changement demandé. En attente de la confirmation du serveur…")
end
function L:DraftGeneral()
    local blocked=self:Blocked(false)
    if blocked then self:Notify("blocked",blocked);return end
    local fn=ChatFrameUtil and ChatFrameUtil.OpenChat or ChatFrame_OpenChat
    local id
    -- Resolve General by zoneChannelID, never assume chat channel #1 is General.
    if type(GetChannelList)=="function" then
        local channels={GetChannelList()}
        for i=1,#channels,3 do
            local n=B.Public(channels[i]);local info=n and B.Call(C_ChatInfo and C_ChatInfo.GetChannelInfoFromIdentifier,n)
            if type(info)=="table" and B.Public(info.zoneChannelID)==1 then id=n;break end
        end
    end
    if type(fn)~="function" or not id then self:Notify("unavailable","Rejoins le canal Général, puis demande une invitation pour changer de layer.");return end
    if self:Busy() then self:Stop() end
    local zone=clean(B.Call(GetRealZoneText)) or "this zone"
    zone=zone:gsub("[|%c]","")
    local ok=pcall(fn,"/"..id.." Could someone invite me to change layers in "..zone.."? Thanks!")
    if ok then self:Notify("draft","Message préparé dans Général. Entrée pour l'envoyer, Échap pour annuler.") end
end
function L:Event(event,...)
    if event=="SHARD_TRANSFER" then
        self.pending=nil;self.transferDeadline=nil;self:Stop("success","Changement de layer confirmé par WoW.");return
    elseif event=="SHARD_TRANSFER_IMMINENT" then
        self.pending=true;self:Stop("ready","WoW propose un transfert. Clique Changer maintenant hors combat.");return
    elseif event=="SHARD_TRANSFER_CANCELLED" then
        self.pending=nil;self.transferDeadline=nil;self:Stop("idle","Transfert annulé par WoW.");return
    end
    if not self:Enabled() then return end
    if event=="CHAT_MSG_ADDON" then
        local prefix,text,channel,sender=...
        if clean(prefix)==self.prefix then self:Receive(text,channel,sender) end
    elseif event=="CHAT_MSG_CHANNEL" then
        local text,sender,_,_,_,_,_,_,base=...
        text=clean(text);base=clean(base)
        if base and base:lower()==self.channel:lower() and text then
            local payload=text:match("^ForeverPilot layer: ([%d%-]+|%d+|%a+)$")
            if payload then self:Receive("1|Q|"..payload,"CHANNEL",sender) end
        end
    elseif event=="CHAT_MSG_CHANNEL_NOTICE" then
        local notice,_,_,_,_,_,_,_,base=...
        notice=clean(notice);base=clean(base)
        if base and base:lower()==self.channel:lower() and (notice=="NOT_ALLOWED_IN_CHANNEL" or notice=="YOU_LEFT" or notice=="BANNED" or notice=="WRONG_PASSWORD") then
            if self.state=="search" or self.state=="joining" then self:Stop("unavailable","Canal refusé par WoW. Utilise Demander dans Général.") end
        end
    elseif event=="PARTY_INVITE_REQUEST" then self.pendingInvite=true
    elseif event=="PARTY_INVITE_CANCEL" or event=="GROUP_ROSTER_UPDATE" then
        self.pendingInvite=nil
        if event=="GROUP_ROSTER_UPDATE" and B.Call(IsInGroup) and self:Busy() then
            self.request=nil;self.transferDeadline=now()+30
            self:Notify("transfer","Groupe rejoint. En attente de WoW; le changement n'est pas encore confirmé.")
        end
    elseif event=="PLAYER_REGEN_DISABLED" or event=="PLAYER_DEAD" or event=="ZONE_CHANGED_NEW_AREA" or event=="PLAYER_ENTERING_WORLD" then
        self.offer=nil
        if self.state=="search" or self.state=="invite" or self.state=="joining" then self:Stop("idle","Situation modifiée. Relance la recherche quand tu es prêt.") end
    end
end
function L:Tick()
    if not self:Enabled() then return end
    if self.offer and now()>=self.offer.untilAt then self.offer=nil end
    if self.state=="joining" then
        if self:ChannelID() then self:Notify("idle","Canal prêt. Clique Chercher pour envoyer une demande unique.")
        elseif now()>(self.joinDeadline or 0) then self:Stop("unavailable","Connexion au canal impossible. Utilise Demander dans Général.") end
    end
    if self.request then
        local map=self:Context()
        if self:Blocked(false) or map~=self.request.map then self:Stop("idle","Recherche interrompue : ta situation a changé.")
        elseif now()>=self.request.untilAt then self:Stop("timeout",self.state=="search" and "Aucun volontaire trouvé. Essaie Demander dans Général." or "Invitation non aboutie. Essaie Demander dans Général.") end
    elseif self.state=="transfer" and now()>=(self.transferDeadline or 0) then
        self:Stop("unconfirmed","Aucun transfert confirmé. Vous êtes peut-être sur le même layer. Ton groupe reste inchangé.")
    end
    if B.LayerUI then B.LayerUI:Refresh() end
end
function L:Safe(fn,...)
    local ok,err=pcall(fn,self,...)
    if not ok then
        self.lastError=tostring(err);self.request=nil;self.offer=nil
        self.state="error";self.detail="Assistance layers indisponible. Le guidage continue. /fpl debug"
        if B.LayerUI then pcall(B.LayerUI.Refresh,B.LayerUI) end
    end
end
function L:SyncEnabled()
    if not self:Enabled() then
        self.request=nil;self.offer=nil
        self.transferDeadline=nil;self.state="idle";self.detail="Cherche un volontaire dans ta zone."
        if self.ownsChannel then B.Call(LeaveChannelByName,self.channel);self.ownsChannel=nil end
        if B.LayerUI and B.LayerUI.frame then B.LayerUI.frame:Hide() end
    elseif B.char.layerHelper then self:Join() end
end
function L:Init()
    if self.frame then return end
    local result=B.Call(C_ChatInfo and C_ChatInfo.RegisterAddonMessagePrefix,self.prefix)
    self.registered=result==0 or result==1 or result==true
    local remaining=tonumber(B.char.layerNextSearch or 0)-time()
    if remaining>0 and remaining<=60 then self.nextSearch=now()+remaining end
    local f=CreateFrame("Frame");self.frame=f
    for _,ev in ipairs({"CHAT_MSG_ADDON","CHAT_MSG_CHANNEL","CHAT_MSG_CHANNEL_NOTICE","SHARD_TRANSFER","SHARD_TRANSFER_IMMINENT","SHARD_TRANSFER_CANCELLED","PARTY_INVITE_REQUEST","PARTY_INVITE_CANCEL","GROUP_ROSTER_UPDATE","PLAYER_REGEN_DISABLED","PLAYER_DEAD","ZONE_CHANGED_NEW_AREA","PLAYER_ENTERING_WORLD"}) do pcall(f.RegisterEvent,f,ev) end
    f:SetScript("OnEvent",function(_,...) self:Safe(self.Event,...) end)
    local elapsed=0
    f:SetScript("OnUpdate",function(_,dt) elapsed=elapsed+dt;if elapsed>=.5 then elapsed=0;self:Safe(self.Tick) end end)
    B.LayerUI:Init();self:SyncEnabled()
end
