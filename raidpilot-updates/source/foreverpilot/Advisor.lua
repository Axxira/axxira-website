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
        tip:AddLine("HDV : non vendable (lié)",.65,.7,.75,true)
    else
        local p,stale=B.Market:Get(link)
        if p then
            tip:AddLine("HDV : "..B.Market:Money(p.price)..(count and count>1 and " / unité" or ""),.94,.77,.43,true)
            if count and count>1 then tip:AddLine("Pile de "..count.." : "..B.Market:Money(p.price*count),.94,.77,.43,true) end
            tip:AddLine("Prix min. · "..B.Market:Age(p.at)..(stale and " · ANCIEN" or "")..(p.partial and " · partiel" or ""),.7,.75,.8,true)
        else
            tip:AddLine("HDV : prix inconnu · scan requis",.65,.7,.75,true)
        end
    end
    -- WoW already displays the vendor sell price in the base item tooltip.
end
function A:UpgradeSummary(r)
    local yes=r.status=="upgrade"
    local no=r.status=="lower" or r.status=="similar" or r.status=="unusable"
    local title="Upgrade : "..(yes and "OUI" or no and "NON" or "À VÉRIFIER")
    local lines={};local gains,losses={},{}
    for _,v in ipairs(r.deltas or {}) do
        local value=v.value or tonumber(v.text:match("^([%+%-][%d%.]+)")) or 0
        if value>0 then gains[#gains+1]=v.text elseif value<0 then losses[#losses+1]=v.text end
    end
    local function compact(values)
        local out={};for i=1,math.min(2,#values) do out[i]=values[i] end
        return table.concat(out," · ")..(#values>2 and " …" or "")
    end
    if #gains>0 then lines[#lines+1]="Gains : "..compact(gains) end
    if #losses>0 then lines[#lines+1]="Pertes : "..compact(losses) end
    if r.status=="unusable" then lines[#lines+1]=r.text
    elseif not yes and not no then lines[#lines+1]=r.status=="compare" and "Effets ou restrictions à vérifier." or r.text
    elseif #gains==0 and #losses==0 then lines[#lines+1]="Aucun gain de stats." end
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
            local title,details=self:UpgradeSummary(r)
            lines[#lines+1]=title
            for _,line in ipairs(details) do lines[#lines+1]=line end
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
    if B.AuctionUI then B.AuctionUI:Init() end
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
