-- Optional drawer and tooltip rows. The navigation panel stays the same size.
local B=ForeverPilotLeveling
local A={page=1};B.Advisor=A
local function text(parent,size,x,y,w,h)
    local t=parent:CreateFontString(nil,"OVERLAY");B.SetUIFont(t,size)
    t:SetPoint("TOPLEFT",parent,"TOPLEFT",x,-y);t:SetSize(w,h);t:SetJustifyH("LEFT");t:SetJustifyV("TOP");t:SetWordWrap(true)
    t:SetTextColor(.86,.90,.94);return t
end
local function button(parent,label,x,y,w,callback)
    local f=CreateFrame("Button",nil,parent);f:SetPoint("TOPLEFT",parent,"TOPLEFT",x,-y);f:SetSize(w,26)
    local bg=f:CreateTexture(nil,"BACKGROUND");bg:SetAllPoints(f);bg:SetColorTexture(.13,.23,.28,.95)
    local t=text(f,11,7,6,w-14,16);t:SetText(label);f.label=t;f:SetScript("OnClick",callback)
    f:SetScript("OnEnter",function() bg:SetColorTexture(.20,.34,.38,1) end)
    f:SetScript("OnLeave",function() bg:SetColorTexture(.13,.23,.28,.95);GameTooltip:Hide() end)
    return f
end
function A:ItemContext(tip)
    -- Use the hovered instance, never a count summed across all matching bags.
    local data=B.Call(tip.GetPrimaryTooltipData,tip)
    local guid=B.Items:Text(B.Items:Field(data,"guid"))
    local location=guid and B.Call(C_Item and C_Item.GetItemLocation,guid)
    if not location then return end
    local count=B.Items:Number(B.Call(C_Item and C_Item.GetStackCount,location))
    local bound=B.Call(C_Item and C_Item.IsBound,location)
    return count and count>=1 and math.floor(count) or nil,bound==true
end
function A:MarketTooltip(tip,link,info)
    local count,bound=self:ItemContext(tip)
    if bound or (info and info.bindType==1) then
        tip:AddLine(B.T("HDV : non vendable (lié)"),.65,.7,.75,true)
    else
        local p,stale=B.Market:Get(link)
        if p then
            tip:AddLine(B.T("HDV : ")..B.Market:Money(p.price)..(count and count>1 and B.T(" / unité") or ""),.94,.77,.43,true)
            if count and count>1 then tip:AddLine(B.T("Pile de ")..count.." : "..B.Market:Money(p.price*count),.94,.77,.43,true) end
            tip:AddLine(B.T("Prix min. · ")..B.Market:Age(p.at)..(stale and B.T(" · ANCIEN") or "")..(p.partial and B.T(" · partiel") or ""),.7,.75,.8,true)
        else
            tip:AddLine(B.T("HDV : prix inconnu · scan requis"),.65,.7,.75,true)
        end
    end
    -- WoW already displays the vendor sell price in the base item tooltip.
end
function A:UpgradeSummary(r)
    local yes=r.status=="upgrade"
    local no=r.status=="lower" or r.status=="similar" or r.status=="unusable"
    local title=B.T("Upgrade : ")..(yes and B.T("OUI") or no and B.T("NON") or B.T("À VÉRIFIER"))
    local lines={};local gains,losses={},{}
    for _,v in ipairs(r.deltas or {}) do
        local value=v.value or tonumber(v.text:match("^([%+%-][%d%.]+)")) or 0
        if value>0 then gains[#gains+1]=v.text elseif value<0 then losses[#losses+1]=v.text end
    end
    local function compact(values)
        local out={};for i=1,math.min(2,#values) do out[i]=values[i] end
        return table.concat(out," · ")..(#values>2 and " …" or "")
    end
    if #gains>0 then lines[#lines+1]=B.T("Gains : ")..compact(gains) end
    if #losses>0 then lines[#lines+1]=B.T("Pertes : ")..compact(losses) end
    if r.status=="unusable" then lines[#lines+1]=r.text
    elseif not yes and not no then lines[#lines+1]=r.status=="compare" and B.T("Effets ou restrictions à vérifier.") or r.text
    elseif #gains==0 and #losses==0 then lines[#lines+1]=B.T("Aucun gain de stats.") end
    return title,lines,yes,no
end
function A:Tooltip(tip)
    if self.busy or not B.settings or not B.settings.enabled then return end
    if tip~=GameTooltip and tip~=ItemRefTooltip then return end
    local _,link=B.Call(TooltipUtil and TooltipUtil.GetDisplayedItem or tip.GetItem,tip)
    if type(link)~="string" then _,link=B.Call(tip.GetItem,tip) end
    if type(link)~="string" then return end
    self.busy=true
    local ok,err=pcall(function()
        local info=B.Call(B.Items.Info,B.Items,link)
        local r=B.Call(B.Items.Compare,B.Items,link)
        tip:AddLine(" ");tip:AddLine("ForeverPilot"..(r and r.profile and " · "..r.profile or ""),.4,.85,.8)
        -- Missing comparison data must not hide a perfectly usable market price.
        if r then
            local title,lines,yes,no=self:UpgradeSummary(r)
            tip:AddLine(title,yes and .4 or .94,yes and .9 or no and .60 or .77,yes and .65 or .43,true)
            for _,line in ipairs(lines) do tip:AddLine(line,.8,.85,.9,true) end
        end
        self:MarketTooltip(tip,link,info)
    end)
    self.busy=false
    if not ok then self.lastError=tostring(err) end
end
function A:InstallTooltips()
    if self.hooked then return end
    if TooltipDataProcessor and TooltipDataProcessor.AddTooltipPostCall and Enum and Enum.TooltipDataType then
        local ok=pcall(TooltipDataProcessor.AddTooltipPostCall,Enum.TooltipDataType.Item,function(tip) self:Tooltip(tip) end)
        self.hooked=ok
    end
    if not self.hooked and GameTooltip and GameTooltip.HookScript then
        self.hooked=pcall(GameTooltip.HookScript,GameTooltip,"OnTooltipSetItem",function(tip) self:Tooltip(tip) end)
    end
end
function A:Audit()
    local lines={B.T("Guidage : Ding + ForeverPilot"), B.T("Comparaison : intégrée · profils de leveling"), B.T("Prix HDV : scan et recherches intégrés")}
    for _,name in ipairs({"TankPilot","Polestar","Auctionator","Pawn","BugSack","!BugGrabber"}) do
        local exists=B.Call(C_AddOns and C_AddOns.DoesAddOnExist,name)
        local loaded=B.Call(C_AddOns and C_AddOns.IsAddOnLoaded or IsAddOnLoaded,name)
        if exists==nil then exists=B.Call(C_AddOns and C_AddOns.GetAddOnMetadata or GetAddOnMetadata,name,"Title")~=nil end
        lines[#lines+1]=name.." : "..(loaded and B.T("chargé") or exists and B.T("installé, non chargé") or B.T("absent"))
    end
    lines[#lines+1]=B.T("Aucun addon d'objets supplémentaire obligatoire.")
    lines[#lines+1]=B.T("Polestar peut doubler le guidage ; ses réglages restent à ton choix.")
    lines[#lines+1]=B.T("BugSack + BugGrabber : utiles pour diagnostiquer les erreurs.")
    return table.concat(lines,"\n")
end
function A:RefreshIfShown()
    if self.frame and self.frame:IsShown() then self:Render() end
    if self.profileFrame and self.profileFrame:IsShown() then self:RenderProfile() end
end
function A:Render()
    if not self.frame then return end
    local _,_,label=B.Items:Profile()
    self.profileButton.label:SetText(B.T("Profil : ")..(label or B.T("à choisir")).."  ›")
    local tab=self.tab or "gear"
    if tab=="market" then
        self.body:SetText(B.T("PRIX CONSULTABLES PARTOUT\n\n")..(B.Market.status or B.T("Ouvre l'hôtel des ventes, puis lance un scan.")).."\n\n"..
            tostring(B.Market.db and B.Market.db.count or 0)..B.T(" variantes mémorisées pour ce personnage.\n\n")..
            B.T("Le survol d'un objet affiche le prix minimum observé, sa source et son âge.\n\n")..
            B.T("Les recherches faites dans l'HDV alimentent aussi l'historique. Les prix anciens ou partiels sont signalés.\n\n")..
            B.T("Un scan ne donne pas un prix permanent : le marché change. Les annonces ne sont pas des ventes réalisées."))
    elseif tab=="addons" then self.body:SetText(self:Audit())
    else
        local rows={};local pending=0;local seen={}
        for _,item in ipairs(B.Items:Bags()) do
            if not seen[item.link] then
                seen[item.link]=true
                local r=B.Items:Compare(item.link)
                if r then
                    if r.info then rows[#rows+1]=r else pending=pending+1 end
                end
            end
        end
        table.sort(rows,function(a,b)
            local x,y=a.status=="upgrade",b.status=="upgrade";if x~=y then return x end
            if (a.gain or -1e9)~=(b.gain or -1e9) then return (a.gain or -1e9)>(b.gain or -1e9) end
            return a.info.name<b.info.name
        end)
        local pages=math.max(1,math.ceil(#rows/3));self.page=math.min(self.page,pages)
        local lines={B.T("ÉQUIPEMENT DES SACS · ")..self.page.."/"..pages,B.T("Survole un objet pour les détails et son prix."),""}
        for i=(self.page-1)*3+1,math.min(self.page*3,#rows) do
            local r=rows[i];lines[#lines+1]=r.info.name
            local title,details=self:UpgradeSummary(r)
            lines[#lines+1]=title
            for _,line in ipairs(details) do lines[#lines+1]=line end
            lines[#lines+1]=""
        end
        if #rows==0 then lines[#lines+1]=B.T("Aucun équipement comparable dans les sacs.") end
        if pending>0 then lines[#lines+1]=pending..B.T(" objet(s) en cours de chargement.") end
        lines[#lines+1]=B.T("Conseil selon ta classe et ta spécialisation. Effets spéciaux non simulés.")
        self.body:SetText(table.concat(lines,"\n"));self.pages=pages
    end
    self.scan:SetShown(tab=="market");self.previous:SetShown(tab=="gear");self.next:SetShown(tab=="gear")
    self.profileButton:SetShown(tab=="gear")
    for key,b in pairs(self.tabButtons) do b.label:SetText((tab==key and "• " or "")..b.title) end
end
function A:Create()
    if self.frame then return end
    local f=CreateFrame("Frame","ForeverPilotAdvisorFrame",UIParent);self.frame=f
    f:SetSize(470,465);f:SetPoint("CENTER",UIParent,"CENTER",0,0);f:SetClampedToScreen(true);f:SetFrameStrata("DIALOG");f:EnableMouse(true)
    local bg=f:CreateTexture(nil,"BACKGROUND");bg:SetAllPoints(f);bg:SetColorTexture(.035,.06,.08,.98)
    local title=text(f,17,20,18,390,24);title:SetText(B.T("ForeversPilot FastXp · Objets & HDV"))
    button(f,"×",430,12,28,function() f:Hide() end)
    self.tabButtons={}
    for i,t in ipairs({{"gear",B.T("Équipement")},{"market",B.T("Prix HDV")},{"addons",B.T("Addons")}}) do
        local tab=t[1];local b=button(f,t[2],20+(i-1)*145,52,135,function() self.tab=tab;self:Render() end)
        b.title=t[2];self.tabButtons[tab]=b
    end
    self.profileButton=button(f,B.T("Profil"),20,91,425,function() self:ToggleProfile() end)
    self.body=text(f,12,20,130,425,280)
    self.scan=button(f,B.T("Scanner l'HDV"),20,422,200,function() B.Market:Scan();self:Render() end)
    self.previous=button(f,B.T("Précédent"),20,422,130,function() self.page=math.max(1,self.page-1);self:Render() end)
    self.next=button(f,B.T("Suivant"),315,422,130,function() self.page=math.min(self.pages or 1,self.page+1);self:Render() end)
    if UISpecialFrames then table.insert(UISpecialFrames,"ForeverPilotAdvisorFrame") end
    f:Hide()
end
function A:Toggle(tab)
    self:Create();if tab then self.tab=tab end
    if self.frame:IsShown() and not tab then self.frame:Hide() else self.frame:Show();self:Render() end
end
function A:ProfileChoices(title,choices,selected,callback)
    local f=self.choiceFrame
    if not f then
        f=CreateFrame("Frame",nil,self.profileFrame);self.choiceFrame=f;self.choicePool={}
        local bg=f:CreateTexture(nil,"BACKGROUND");bg:SetAllPoints(f);bg:SetColorTexture(.045,.09,.12,1)
        self.choiceTitle=text(f,14,14,13,330,24)
        button(f,"×",350,7,27,function() f:Hide() end)
    end
    f:SetSize(388,65+math.ceil(#choices/2)*34);f:SetPoint("CENTER",self.profileFrame,"CENTER",0,0)
    f:SetFrameStrata("DIALOG");f:SetFrameLevel(self.profileFrame:GetFrameLevel()+10);f:EnableMouse(true)
    self.choiceTitle:SetText(title);self.choiceButtons={}
    for _,b in ipairs(self.choicePool) do b:Hide() end
    for i,choice in ipairs(choices) do
        local key=choice.key
        local b=self.choicePool[i]
        if not b then b=button(f,"",12+((i-1)%2)*184,48+math.floor((i-1)/2)*34,178,function() end);self.choicePool[i]=b end
        b.label:SetText((key==selected and "• " or "")..choice.label)
        b:SetScript("OnClick",function()
            callback(key);f:Hide();self:RenderProfile()
        end)
        self.choiceButtons[key]=b;b:Show()
    end
    f:Show()
end
function A:RenderProfile()
    local I=B.Items;local preview=self.profileMode=="preview"
    local class=preview and self.preview.class or I:PlayerClass()
    local def=I.classes[class];local raceName=B.Call(UnitRace,"player") or B.T("Inconnue")
    if preview then
        raceName=B.T("Inconnue")
        for _,r in ipairs(I:RaceChoices()) do if r.key==self.preview.race then raceName=r.label end end
    end
    self.raceChoice.label:SetText(B.T("Race : ")..raceName..(preview and "  ›" or B.T(" · détectée")))
    self.classChoice.label:SetText(B.T("Classe : ")..(def and def.label or B.T("Inconnue"))..B.T(" · voir les 9 classes ›"))
    self.raceChoice:SetEnabled(preview);self.classChoice:SetEnabled(true)
    local selection=preview and self.preview.selection or self.liveSelection
    local selectionLabel=I.profiles[selection] and I.profiles[selection].label or B.T("À choisir")
    for _,c in ipairs(I:SpecChoices(class,not preview)) do if c.key==selection then selectionLabel=c.label end end
    self.specChoice.label:SetText(B.T("Spé : ")..selectionLabel.."  ›")
    local profile,_,label,reason=I:ResolveProfile(class,selection,not preview)
    self.profileModeLabel:SetText(preview and B.T("CONSULTER UN AUTRE PERSONNAGE") or B.T("CONSEILS POUR MON PERSONNAGE"))
    self.profileInfo:SetText((label or "")..B.T("\n\nPriorités de comparaison :\n")..I:Priorities(profile)..
        (reason and "\n\n"..reason or "")..(preview and
        B.T("\n\nAperçu des priorités. Tes objets gardent le profil de ton personnage connecté. Combinaisons race/classe et bonus raciaux non évalués.") or
        B.T("\n\nAutomatique suit tes talents. Un choix manuel reste prioritaire jusqu'au retour en automatique.")))
    self.profileApply.label:SetText(preview and B.T("Garder cet aperçu") or B.T("Utiliser ce profil"))
    self.profileApply:SetEnabled(preview or selection=="auto" or profile~=nil)
end
function A:CreateProfile()
    if self.profileFrame then return end
    local f=CreateFrame("Frame","ForeverPilotProfileFrame",UIParent);self.profileFrame=f
    f:SetSize(430,478);f:SetPoint("CENTER",UIParent,"CENTER",0,0);f:SetClampedToScreen(true)
    f:SetFrameStrata("DIALOG");f:SetFrameLevel((self.frame and self.frame:GetFrameLevel() or 0)+5);f:EnableMouse(true)
    local bg=f:CreateTexture(nil,"BACKGROUND");bg:SetAllPoints(f);bg:SetColorTexture(.035,.06,.08,.99)
    text(f,17,20,16,350,26):SetText(B.T("ForeversPilot FastXp · Profil"))
    button(f,"×",390,10,28,function() f:Hide() end)
    self.liveModeButton=button(f,B.T("Mon personnage"),20,52,190,function() self.profileMode="live";if self.choiceFrame then self.choiceFrame:Hide() end;self:RenderProfile() end)
    self.previewModeButton=button(f,B.T("Autre personnage"),220,52,190,function() self.profileMode="preview";if self.choiceFrame then self.choiceFrame:Hide() end;self:RenderProfile() end)
    self.profileModeLabel=text(f,10,20,88,390,18);self.profileModeLabel:SetTextColor(.3,.85,.8)
    self.raceChoice=button(f,B.T("Race"),20,110,390,function()
        if self.profileMode~="preview" then return end
        self:ProfileChoices(B.T("Choisir une race"),B.Items:RaceChoices(),self.preview.race,function(key) self.preview.race=key end)
    end)
    self.classChoice=button(f,B.T("Classe"),20,144,390,function()
        local liveClass=B.Items:PlayerClass()
        local selected=self.profileMode=="preview" and self.preview.class or liveClass
        self:ProfileChoices(B.T("Toutes les classes"),B.Items:ClassChoices(),selected,function(key)
            if self.profileMode~="preview" and key==liveClass then return end
            -- A foreign class is a clearly labelled preview, never the live gear profile.
            self.profileMode="preview"
            self.preview.class=key;self.preview.selection="spec:"..key..":1"
        end)
    end)
    self.specChoice=button(f,B.T("Spécialisation"),20,178,390,function()
        local preview=self.profileMode=="preview";local class=preview and self.preview.class or B.Items:PlayerClass()
        self:ProfileChoices(B.T("Choisir une spécialisation"),B.Items:SpecChoices(class,not preview),preview and self.preview.selection or self.liveSelection,function(key)
            if preview then self.preview.selection=key else self.liveSelection=key end
        end)
    end)
    self.profileInfo=text(f,12,20,218,390,208)
    self.profileApply=button(f,B.T("Utiliser ce profil"),20,440,390,function()
        if self.profileMode=="preview" then B.char.gearPreview=B.Copy(self.preview)
        elseif not B.Items:SetProfile(self.liveSelection) then return end
        f:Hide();self:RefreshIfShown()
    end)
    f:SetScript("OnHide",function() if self.choiceFrame then self.choiceFrame:Hide() end end)
    if UISpecialFrames then table.insert(UISpecialFrames,"ForeverPilotProfileFrame") end
    f:Hide()
end
function A:ToggleProfile()
    self:CreateProfile()
    if self.profileFrame:IsShown() then self.profileFrame:Hide();return end
    local I=B.Items;local class=I:PlayerClass()
    local _,race=B.Call(UnitRace,"player")
    self.liveSelection=I:ProfileSelection();self.profileMode="live"
    local saved=type(B.char.gearPreview)=="table" and B.char.gearPreview or {}
    self.preview=B.Copy(saved)
    if not I.classes[self.preview.class] then self.preview.class=I.classes[class] and class or "WARRIOR" end
    local validRace=false;for _,r in ipairs(I:RaceChoices()) do validRace=validRace or r.key==self.preview.race end
    if not validRace then self.preview.race=race or "Human" end
    local validSpec=false;for _,c in ipairs(I:SpecChoices(self.preview.class,false)) do validSpec=validSpec or c.key==self.preview.selection end
    if not validSpec then self.preview.selection="spec:"..self.preview.class..":1" end
    self:RenderProfile();self.profileFrame:Show()
end
function A:Init()
    B.Market:Init();self:InstallTooltips()
    if B.AuctionUI then B.AuctionUI:Init() end
    local f=B.UI.frame
    self.openButton=button(f,B.T("Objets"),174,5,70,function() if B.Options.frame then B.Options.frame:Hide() end;self:Toggle() end)
    self.openButton.label:SetAllPoints(self.openButton);self.openButton.label:SetJustifyV("MIDDLE");self.openButton.label:SetJustifyH("CENTER");B.SetUIFont(self.openButton.label,12)
    local events=CreateFrame("Frame");self.events=events
    for _,e in ipairs({"AUCTION_HOUSE_SHOW","AUCTION_HOUSE_CLOSED","REPLICATE_ITEM_LIST_UPDATE","COMMODITY_SEARCH_RESULTS_UPDATED","COMMODITY_SEARCH_RESULTS_ADDED","ITEM_SEARCH_RESULTS_UPDATED","ITEM_SEARCH_RESULTS_ADDED","BAG_UPDATE_DELAYED","PLAYER_EQUIPMENT_CHANGED","GET_ITEM_INFO_RECEIVED","PLAYER_REGEN_ENABLED"}) do pcall(events.RegisterEvent,events,e) end
    for e in pairs(B.Items.profileEvents) do pcall(events.RegisterEvent,events,e) end
    events:SetScript("OnEvent",function(_,e,arg)
        local ok,err=pcall(function()
            if B.Items.profileEvents[e] then B.Items:InvalidateProfile() end
            B.Market:Event(e,arg)
            if B.settings.enabled and not self.refreshPending then
                self.refreshPending=true;C_Timer.After(.25,function() self.refreshPending=false;B.Call(self.RefreshIfShown,self) end)
            end
        end)
        if not ok then self.lastError=tostring(err);B.Market:Cancel(B.T("Prix indisponibles : voir /fpl debug.")) end
    end)
end
