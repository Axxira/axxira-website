-- Original adapter to ChangeLayer's exported keybinding. No provider code or protocol copied.
local B=ForeverPilotLeveling
local L={state="idle",detail=B.T("Utilise le réseau ChangeLayer pour trouver une invitation.")};B.Layers=L
local function clean(v) v=B.Public(v);return type(v)=="string" and v or nil end
function L:Enabled() return B.settings and B.settings.enabled~=false end
function L:Provider()
    return B.Call(C_AddOns and C_AddOns.IsAddOnLoaded,"ChangeLayer")~=false and type(ChangeLayer_Go)=="function"
end
function L:Blocked(allowGroup)
    if not self:Enabled() then return B.T("Active ForeverPilot avec /fpl on.") end
    if B.Call(InCombatLockdown) or B.Call(UnitAffectingCombat,"player") then return B.T("Attends la fin du combat.") end
    if B.Call(UnitIsDeadOrGhost,"player") then return B.T("Reviens en vie avant de changer de layer.") end
    if B.Call(IsInInstance) then return B.T("Disponible uniquement dans le monde ouvert.") end
    if B.Call(C_ChatInfo and C_ChatInfo.InChatMessagingLockdown) then return B.T("Communication temporairement bloquée par WoW.") end
    -- The provider can offer leave-and-retry in some states. Never call its action while grouped.
    if not allowGroup and B.Call(IsInGroup) then return B.T("Tu es déjà en groupe. Gère ton groupe dans WoW avant une nouvelle demande.") end
end
function L:Notify(state,detail)
    self.state=state;self.detail=detail
    if B.LayerUI then B.LayerUI:Refresh() end
end
function L:Missing()
    self:Notify("missing",B.T("Installe ChangeLayer depuis le launcher, puis redémarre WoW. Demander dans Général reste disponible."))
end
function L:Start()
    local blocked=self:Blocked(false)
    if blocked then self:Notify("blocked",blocked);return end
    if not self:Provider() then self:Missing();return end
    if self.clickedAt and B.Now()-self.clickedAt<.75 then return end
    self.clickedAt=B.Now()
    -- Public entry point used by ChangeLayer's Bindings.xml; user click only.
    local ok,err=pcall(ChangeLayer_Go)
    if not ok then self.lastError=tostring(err);self:Notify("error",B.T("ChangeLayer a refusé la commande. Consulte son panneau ou /changelayer status."));return end
    self:Notify("provider",B.T("Commande transmise à ChangeLayer. Consulte son état sous la minicarte. Reclique pour arrêter une recherche."))
end
function L:Options()
    if not self:Provider() or type(SlashCmdList and SlashCmdList.CHANGELAYER)~="function" then self:Missing();return end
    if B.Call(InCombatLockdown) then self:Notify("blocked",B.T("Ouvre les réglages après le combat."));return end
    local ok,err=pcall(SlashCmdList.CHANGELAYER,"options")
    if not ok then self.lastError=tostring(err);self:Notify("error",B.T("Ouvre les réglages avec /changelayer.")) end
end
function L:Pending()
    local imminent=B.Call(StaticPopup_Visible,"SHARD_TRANSFER_IMMINENT_EVENT")
    local immediate=B.Call(StaticPopup_Visible,"SHARD_TRANSFER_IMMEDIATE")
    return self.pending==true or (imminent~=nil and imminent~=false) or (immediate~=nil and immediate~=false)
end
function L:Confirm()
    local blocked=self:Blocked(true)
    if blocked then self:Notify("blocked",blocked);return end
    if not self:Pending() then self:Notify("idle",B.T("WoW ne propose aucun transfert pour le moment."));return end
    local fn=C_PlayerInteractionManager and C_PlayerInteractionManager.ShardTransferConfirm
    if type(fn)~="function" then self:Notify("blocked",B.T("Utilise le bouton de changement affiché par WoW."));return end
    local ok,err=pcall(fn)
    if not ok then self.lastError=tostring(err);self:Notify("blocked",B.T("WoW a refusé le changement. Utilise sa fenêtre de confirmation."));return end
    self.transferDeadline=B.Now()+30
    self:Notify("transfer",B.T("Changement demandé. En attente de la confirmation du serveur…"))
end
function L:DraftGeneral()
    local blocked=self:Blocked(false)
    if blocked then self:Notify("blocked",blocked);return end
    local fn=ChatFrameUtil and ChatFrameUtil.OpenChat or ChatFrame_OpenChat
    local id
    if type(GetChannelList)=="function" then
        local channels={GetChannelList()}
        for i=1,#channels,3 do
            local n=B.Public(channels[i]);local info=n and B.Call(C_ChatInfo and C_ChatInfo.GetChannelInfoFromIdentifier,n)
            if type(info)=="table" and B.Public(info.zoneChannelID)==1 then id=n;break end
        end
    end
    if type(fn)~="function" or not id then self:Notify("unavailable",B.T("Rejoins le canal Général, puis demande une invitation pour changer de layer."));return end
    local zone=clean(B.Call(GetRealZoneText)) or "this zone"
    zone=zone:gsub("[|%c]","")
    local ok=pcall(fn,"/"..id.." Could someone invite me to change layers in "..zone.."? Thanks!")
    if ok then self:Notify("draft",B.T("Message préparé dans Général. Entrée pour l'envoyer, Échap pour annuler.")) end
end
function L:Event(event)
    if event=="SHARD_TRANSFER" then
        self.pending=nil;self.transferDeadline=nil;self:Notify("success",B.T("Changement de layer confirmé par WoW."))
    elseif event=="SHARD_TRANSFER_IMMINENT" then
        self.pending=true;self:Notify("ready",B.T("WoW propose un transfert. Clique Changer maintenant hors combat."))
    elseif event=="SHARD_TRANSFER_CANCELLED" then
        self.pending=nil;self.transferDeadline=nil;self:Notify("idle",B.T("Transfert annulé par WoW."))
    end
end
function L:Tick()
    if not self:Enabled() then return end
    if self.state=="transfer" and B.Now()>=(self.transferDeadline or 0) then
        self:Notify("unconfirmed",B.T("Aucun transfert confirmé par WoW. Consulte aussi l'état de ChangeLayer."))
    end
    if B.LayerUI then B.LayerUI:Refresh() end
end
function L:Safe(fn,...)
    local ok,err=pcall(fn,self,...)
    if not ok then
        self.lastError=tostring(err);self.state="error";self.detail=B.T("Assistance layers indisponible. Le guidage continue. /fpl debug")
        if B.LayerUI then pcall(B.LayerUI.Refresh,B.LayerUI) end
    end
end
function L:SyncEnabled()
    if not self:Enabled() and B.LayerUI and B.LayerUI.frame then B.LayerUI.frame:Hide() end
end
function L:Init()
    if self.frame then return end
    self.detail=B.T("Utilise le réseau ChangeLayer pour trouver une invitation.")
    if B.char.layerHelper then B.Call(LeaveChannelByName,"ForeverPilotLayers");B.char.layerHelper=false end
    local f=CreateFrame("Frame");self.frame=f
    for _,ev in ipairs({"SHARD_TRANSFER","SHARD_TRANSFER_IMMINENT","SHARD_TRANSFER_CANCELLED"}) do pcall(f.RegisterEvent,f,ev) end
    f:SetScript("OnEvent",function(_,...) self:Safe(self.Event,...) end)
    local elapsed=0
    f:SetScript("OnUpdate",function(_,dt) elapsed=elapsed+dt;if elapsed>=.5 then elapsed=0;self:Safe(self.Tick) end end)
    B.LayerUI:Init();self:SyncEnabled()
end
