-- Optional drawer and tooltip rows. The navigation panel stays the same size.
local B=ForeverPilotLeveling
local A={page=1};B.Advisor=A
local profileOrder={"auto","feral","tank","soin","caster","melee","agile","hybride","chasseur"}
local function text(parent,size,x,y,w,h)
    local t=parent:CreateFontString(nil,"OVERLAY");t:SetFont(STANDARD_TEXT_FONT,size,"")
    t:SetPoint("TOPLEFT",parent,"TOPLEFT",x,-y);t:SetSize(w,h);t:SetJustifyH("LEFT");t:SetJustifyV("TOP");t:SetWordWrap(true)
    t:SetTextColor(.86,.90,.94);return t
end
local function button(parent,label,x,y,w,callback)
    local f=CreateFrame("Button",nil,parent);f:SetPoint("TOPLEFT",parent,"TOPLEFT",x,-y);f:SetSize(w,26)
    local bg=f:CreateTexture(nil,"BACKGROUND");bg:SetAllPoints(f);bg:SetColorTexture(.13,.23,.28,.95)
    local t=text(f,11,7,6,w-14,16);t:SetText(label);f.label=t;f:SetScript("OnClick",callback)
    return f
end
function A:Tooltip(tip)
    if self.busy or not B.settings or not B.settings.enabled then return end
    if tip~=GameTooltip and tip~=ItemRefTooltip then return end
    local _,link=B.Call(TooltipUtil and TooltipUtil.GetDisplayedItem or tip.GetItem,tip)
    if type(link)~="string" then return end
    self.busy=true
    local ok,err=pcall(function()
        local r=B.Items:Compare(link);local info=r and r.info or B.Items:Info(link)
        tip:AddLine(" ");tip:AddLine("ForeverPilot · Objets",.4,.85,.8)
        if r then
            local green=r.status=="upgrade"
            tip:AddLine(r.text,green and .4 or .9,green and .9 or .77,green and .65 or .5,true)
            if r.profile then
                tip:AddLine(r.profile..(r.gain and string.format(" · %+.1f points estimés",r.gain) or ""),.7,.78,.84,true)
                if r.slot then tip:AddLine("Comparé à : "..(B.Items.slotNames[r.slot] or r.slot),.7,.78,.84,true) end
            end
            local d={};for i,v in ipairs(r.deltas or {}) do if i<=3 then d[#d+1]=v.text end end
            if #d>0 then tip:AddLine(table.concat(d," · "),.8,.85,.9,true) end
            tip:AddLine("Score indicatif ; effets spéciaux non simulés.",.58,.65,.7,true)
        end
        if info and info.vendor then tip:AddLine("Marchand / unité : "..B.Market:Money(info.vendor),.75,.8,.85,true) end
        if info and info.bindType==1 then
            tip:AddLine("Lié au ramassage · prix HDV non pertinent",.65,.7,.75,true)
        else
            local p,stale=B.Market:Get(link)
            if p then
                tip:AddLine("HDV min. observé / unité : "..B.Market:Money(p.price),.94,.77,.43,true)
                tip:AddLine(p.source.." · "..B.Market:Age(p.at)..(stale and " · ANCIEN" or "")..(p.partial and " · partiel" or ""),.7,.75,.8,true)
                tip:AddLine("Annonces observées ; vente non garantie. Objet lié : non vendable.",.58,.65,.7,true)
            else tip:AddLine("HDV : aucun prix observé pour cette variante",.65,.7,.75,true) end
        end
    end)
    self.busy=false
    if not ok then self.lastError=tostring(err) end
end
function A:InstallTooltips()
    if self.hooked then return end
    if TooltipDataProcessor and TooltipDataProcessor.AddTooltipPostCall and Enum and Enum.TooltipDataType then
        local ok=pcall(TooltipDataProcessor.AddTooltipPostCall,Enum.TooltipDataType.Item,function(tip) self:Tooltip(tip) end)
        self.hooked=ok
    elseif GameTooltip and GameTooltip.HookScript then
        self.hooked=pcall(GameTooltip.HookScript,GameTooltip,"OnTooltipSetItem",function(tip) self:Tooltip(tip) end)
    end
end
function A:Audit()
    local lines={"Guidage : Ding + ForeverPilot", "Comparaison : intégrée · profils de leveling", "Prix HDV : scan et recherches intégrés"}
    for _,name in ipairs({"TankPilot","Polestar","Auctionator","Pawn","BugSack","!BugGrabber"}) do
        local exists=B.Call(C_AddOns and C_AddOns.DoesAddOnExist,name)
        local loaded=B.Call(C_AddOns and C_AddOns.IsAddOnLoaded or IsAddOnLoaded,name)
        if exists==nil then exists=B.Call(C_AddOns and C_AddOns.GetAddOnMetadata or GetAddOnMetadata,name,"Title")~=nil end
        lines[#lines+1]=name.." : "..(loaded and "chargé" or exists and "installé, non chargé" or "absent")
    end
    lines[#lines+1]="Aucun addon d'objets supplémentaire obligatoire."
    lines[#lines+1]="Polestar peut doubler le guidage ; ses réglages restent à ton choix."
    lines[#lines+1]="BugSack + BugGrabber : utiles pour diagnostiquer les erreurs."
    return table.concat(lines,"\n")
end
function A:RefreshIfShown() if self.frame and self.frame:IsShown() then self:Render() end end
function A:Render()
    if not self.frame then return end
    local profile=B.Items:Profile()
    self.profileButton.label:SetText("Profil : "..((B.char.gearProfile or "auto")=="auto" and "Auto / " or "")..(profile and profile.label or "à choisir"))
    local tab=self.tab or "gear"
    if tab=="market" then
        self.body:SetText("PRIX CONSULTABLES PARTOUT\n\n"..(B.Market.status or "Ouvre l'hôtel des ventes, puis lance un scan.").."\n\n"..
            tostring(B.Market.db and B.Market.db.count or 0).." variantes mémorisées pour ce personnage.\n\n"..
            "Le survol d'un objet affiche le prix minimum observé, sa source et son âge.\n\n"..
            "Les recherches faites dans l'HDV alimentent aussi l'historique. Les prix anciens ou partiels sont signalés.\n\n"..
            "Un scan ne donne pas un prix permanent : le marché change. Les annonces ne sont pas des ventes réalisées.")
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
        local lines={"ÉQUIPEMENT DES SACS · "..self.page.."/"..pages,"Survole un objet pour les détails et son prix.",""}
        for i=(self.page-1)*3+1,math.min(self.page*3,#rows) do
            local r=rows[i];lines[#lines+1]=r.info.name
            lines[#lines+1]=r.text..(r.gain and string.format(" (%+.1f pts)",r.gain) or "")
            local d={};for j,v in ipairs(r.deltas or {}) do if j<=2 then d[#d+1]=v.text end end
            if #d>0 then lines[#lines+1]=table.concat(d," · ") end
            lines[#lines+1]=""
        end
        if #rows==0 then lines[#lines+1]="Aucun équipement comparable dans les sacs." end
        if pending>0 then lines[#lines+1]=pending.." objet(s) en cours de chargement." end
        lines[#lines+1]="Profils indicatifs : choisis celui de ton rôle. Effets spéciaux non simulés."
        self.body:SetText(table.concat(lines,"\n"));self.pages=pages
    end
    self.scan:SetShown(tab=="market");self.previous:SetShown(tab=="gear");self.next:SetShown(tab=="gear")
    self.profileButton:SetShown(tab=="gear")
end
function A:Create()
    if self.frame then return end
    local f=CreateFrame("Frame","ForeverPilotAdvisorFrame",UIParent);self.frame=f
    f:SetSize(470,465);f:SetPoint("CENTER",UIParent,"CENTER",0,0);f:SetClampedToScreen(true);f:SetFrameStrata("DIALOG");f:EnableMouse(true)
    local bg=f:CreateTexture(nil,"BACKGROUND");bg:SetAllPoints(f);bg:SetColorTexture(.035,.06,.08,.98)
    local title=text(f,17,20,18,390,24);title:SetText("ForeverPilot · Objets & HDV")
    button(f,"×",430,12,28,function() f:Hide() end)
    for i,t in ipairs({{"gear","Équipement"},{"market","Prix HDV"},{"addons","Addons"}}) do
        local tab=t[1];button(f,t[2],20+(i-1)*145,52,135,function() self.tab=tab;self:Render() end)
    end
    self.profileButton=button(f,"Profil",20,91,425,function()
        local current=B.char.gearProfile or "auto";local index=1
        for i,k in ipairs(profileOrder) do if k==current then index=i end end
        B.Items:SetProfile(profileOrder[index%#profileOrder+1]);self:Render()
    end)
    self.body=text(f,12,20,130,425,280)
    self.scan=button(f,"Scanner l'HDV",20,422,200,function() B.Market:Scan();self:Render() end)
    self.previous=button(f,"Précédent",20,422,130,function() self.page=math.max(1,self.page-1);self:Render() end)
    self.next=button(f,"Suivant",315,422,130,function() self.page=math.min(self.pages or 1,self.page+1);self:Render() end)
    if UISpecialFrames then table.insert(UISpecialFrames,"ForeverPilotAdvisorFrame") end
    f:Hide()
end
function A:Toggle(tab)
    self:Create();if tab then self.tab=tab end
    if self.frame:IsShown() and not tab then self.frame:Hide() else self.frame:Show();self:Render() end
end
function A:Init()
    B.Market:Init();self:InstallTooltips()
    local f=B.UI.frame
    self.openButton=button(f,"Objets",173,6,54,function() self:Toggle() end)
    local events=CreateFrame("Frame");self.events=events
    for _,e in ipairs({"AUCTION_HOUSE_SHOW","AUCTION_HOUSE_CLOSED","REPLICATE_ITEM_LIST_UPDATE","COMMODITY_SEARCH_RESULTS_UPDATED","COMMODITY_SEARCH_RESULTS_ADDED","ITEM_SEARCH_RESULTS_UPDATED","ITEM_SEARCH_RESULTS_ADDED","BAG_UPDATE_DELAYED","PLAYER_EQUIPMENT_CHANGED","GET_ITEM_INFO_RECEIVED","PLAYER_REGEN_ENABLED"}) do pcall(events.RegisterEvent,events,e) end
    events:SetScript("OnEvent",function(_,e,arg)
        local ok,err=pcall(function()
            B.Market:Event(e,arg)
            if B.settings.enabled and not self.refreshPending then
                self.refreshPending=true;C_Timer.After(.25,function() self.refreshPending=false;B.Call(self.RefreshIfShown,self) end)
            end
        end)
        if not ok then self.lastError=tostring(err);B.Market:Cancel("Prix indisponibles : voir /fpl debug.") end
    end)
end
