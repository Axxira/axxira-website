local B=ForeverPilotLeveling
local U={};B.LayerUI=U
local function label(f,size,x,y,w,h)
    local t=f:CreateFontString(nil,"OVERLAY");t:SetFont(STANDARD_TEXT_FONT,size,"")
    t:SetPoint("TOPLEFT",f,"TOPLEFT",x,-y);t:SetSize(w,h);t:SetJustifyH("LEFT");t:SetJustifyV("TOP");t:SetTextColor(.83,.89,.93);return t
end
local function button(f,x,y,w,text,click)
    local b=CreateFrame("Button",nil,f);b:SetPoint("TOPLEFT",f,"TOPLEFT",x,-y);b:SetSize(w,23)
    local bg=b:CreateTexture(nil,"BACKGROUND");bg:SetAllPoints(b);bg:SetColorTexture(.13,.23,.28,.97)
    b.label=label(b,10,0,5,w,16);b.label:SetJustifyH("CENTER");b.label:SetText(text)
    b:SetScript("OnEnter",function() bg:SetColorTexture(.20,.34,.38,1) end)
    b:SetScript("OnLeave",function() bg:SetColorTexture(.13,.23,.28,.97) end)
    b:SetScript("OnClick",function() B.Layers:Safe(click) end);return b
end
function U:Toggle()
    if not self.frame then return end
    self.frame:SetShown(not self.frame:IsShown());self:Refresh()
end
function U:Refresh()
    if not self.frame then return end
    local l=B.Layers;local pending=l:Pending();local busy=l:Busy()
    self.primary.label:SetText(pending and "Changer maintenant" or busy and "Annuler la recherche" or "Chercher un volontaire")
    self.primary:SetEnabled(l.state~="joining")
    self.detail:SetText(l.detail)
    self.helper.label:SetText(B.char.layerHelper and "Aider les autres : OUI" or "Aider les autres : NON")
    local status=pending and "TRANSFERT PRÊT" or l.state=="success" and "CONFIRMÉ" or busy and "EN COURS…" or "SUR DEMANDE"
    self.status:SetText(status);self.status:SetTextColor(pending and .94 or .40,pending and .76 or .85,pending and .43 or .80)
    if l.state=="search" and l.request then self.status:SetText("RECHERCHE · "..math.max(0,math.ceil(l.request.untilAt-B.Now())).." s") end
end
function U:Init()
    if self.frame then return end
    local main=B.UI.frame
    -- A separate slim shelf keeps the quest action, arrow and Skip untouched.
    local shelf=CreateFrame("Frame",nil,main);self.shelf=shelf
    shelf:SetPoint("TOPLEFT",main,"BOTTOMLEFT",0,-3);shelf:SetSize(344,30)
    local bg=shelf:CreateTexture(nil,"BACKGROUND");bg:SetAllPoints(shelf);bg:SetColorTexture(.055,.085,.105,.96)
    local edge=shelf:CreateTexture(nil,"ARTWORK");edge:SetPoint("TOPLEFT",shelf,"TOPLEFT",14,0);edge:SetSize(316,1);edge:SetColorTexture(.40,.85,.80,.3)
    self.button=button(shelf,14,4,135,"Changer de layer",function() self:Toggle() end)
    self.status=label(shelf,9,160,11,170,14);self.status:SetJustifyH("RIGHT")
    local f=CreateFrame("Frame","ForeverPilotLayerFrame",shelf);self.frame=f
    f:SetPoint("TOPLEFT",shelf,"BOTTOMLEFT",0,-4);f:SetSize(344,208);f:EnableMouse(true);f:SetClampedToScreen(true)
    f:SetFrameLevel(main:GetFrameLevel()+10)
    local panel=f:CreateTexture(nil,"BACKGROUND");panel:SetAllPoints(f);panel:SetColorTexture(.045,.07,.09,.98)
    local title=label(f,12,14,13,280,18);title:SetText("CHANGER DE LAYER");title:SetTextColor(.40,.85,.80)
    self.close=button(f,306,7,24,"×",function() f:Hide() end)
    self.detail=label(f,11,14,40,316,43);self.detail:SetWordWrap(true)
    self.primary=button(f,14,89,316,"Chercher un volontaire",function(l)
        if l:Pending() then l:Confirm() elseif l:Busy() then l:Stop() else l:Start() end
    end)
    self.general=button(f,14,118,316,"Demander dans Général",function(l) l:DraftGeneral() end)
    self.helper=button(f,14,147,316,"Aider les autres : NON",function(l) l:SetHelper(not B.char.layerHelper) end)
    local hint=label(f,9,14,178,316,25);hint:SetWordWrap(true)
    hint:SetText("Volontaires ForeverPilot · même zone.\nInvitation à accepter dans WoW; aucun départ automatique.");hint:SetTextColor(.58,.68,.74)
    f:Hide();self:Refresh()
end
