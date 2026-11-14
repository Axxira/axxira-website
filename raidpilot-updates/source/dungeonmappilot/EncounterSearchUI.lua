-- Boss Finder: a small search workflow independent of quest/route progression.
-- A search selection previews a boss without altering saved completion, skip flags or route history.
local A=DungeonMapPilot
local U=A.UI
local function Button(parent,width,title,fn)
    local b=CreateFrame("Button",nil,parent,"UIPanelButtonTemplate")
    b:SetSize(width,25);b:SetText(title);b:SetScript("OnClick",fn);return b
end
local function Caption(parent,width,title)
    local t=parent:CreateFontString(nil,"OVERLAY","GameFontHighlightSmall")
    t:SetWidth(width);t:SetText(title);t:SetJustifyH("LEFT");return t
end
function U:FindEncounter()
    if not self.frame then self:Create() end
    if self.searchDialog then
        if self.menu then self.menu:Hide() end
        self.searchDialog:Show();self.searchDialog.input:SetFocus();self.searchDialog.input:HighlightText()
        return
    end
    local f=CreateFrame("Frame","DungeonMapPilotEncounterFinderFrame",self.frame,"BasicFrameTemplateWithInset")
    self.searchDialog=f
    f:SetSize(560,180);f:SetPoint("CENTER",self.frame,"CENTER",0,0)
    f:SetFrameLevel(self.frame:GetFrameLevel()+32);f:SetClampedToScreen(true)
    f:EnableMouse(true)
    f.TitleText:SetText(A.L("searchBossTitle"))
    f.input=CreateFrame("EditBox",nil,f,"InputBoxTemplate")
    f.input:SetSize(495,29);f.input:SetPoint("TOPLEFT",25,-45)
    f.input:SetAutoFocus(false);f.input:SetMaxLetters(80)
    f.input:SetText("")
    f.help=Caption(f,500,A.L("searchBossHint"))
    f.help:SetPoint("TOPLEFT",25,-87)
    f.find=Button(f,270,A.L("searchBossAction"),function() self:SearchBossResults() end)
    f.find:SetPoint("BOTTOMLEFT",20,15)
    f.close=Button(f,170,A.L("cancel"),function() f:Hide() end)
    f.close:SetPoint("BOTTOMRIGHT",-20,15)
    f.input:SetScript("OnEnterPressed",function() self:SearchBossResults() end)
    f.input:SetScript("OnEscapePressed",function() f:Hide() end)
    f:SetScript("OnHide",function()
        f.input:ClearFocus()
        if self.menu then self.menu:Hide() end
    end)
    if UISpecialFrames then UISpecialFrames[#UISpecialFrames+1]="DungeonMapPilotEncounterFinderFrame" end
    if self.menu then self.menu:Hide() end
    f:Show();f.input:SetFocus()
end
function U:SearchBossResults()
    local f=self.searchDialog
    if not f or not f:IsShown() then return end
    local term=f.input:GetText()
    local results,truncated=A.EncounterSearch(term,24)
    if self.menu then self.menu:Hide() end
    if #results==0 then
        f.help:SetText(A.L("searchBossEmpty"))
        return
    end
    f.help:SetText(A.L("searchBossResults",#results)..
        (truncated and ("  /  "..A.L("searchBossLimit")) or ""))
    local options={}
    for _,match in ipairs(results) do
        local picked=match
        local suffix=picked.rosterOnly and A.L("guideBrowseRoster")
            or picked.guideOnly and A.L("guideTextBadge") or ""
        local title=picked.boss.."  -  "..picked.dungeon
        options[#options+1]={text=title..(suffix~="" and "  ["..suffix.."]" or ""),
            action=function() self:PreviewBossResult(picked) end}
    end
    self:Menu(options,f.find,510)
end
function U:PreviewBossResult(result)
    if type(result)~="table" or not A.ID(result.id) or not A.ID(result.stepIndex) then return false end
    local context=A.ReferenceContext(result.id) or A.GuideContext(result.id)
    if not context then return false end
    local variants=A.RouteVariants(context)
    if #variants>0 then
        if not A.RouteChooseVariant(context,result.variantIndex) then return false end
    end
    if self.searchDialog then self.searchDialog:Hide() end
    self.follow=false
    self:SetContext(context)
    local route=self.route
    if not route or route.id~=result.routeID or not route.steps[result.stepIndex] then return false end
    self.encounterPreview={routeID=result.routeID,index=result.stepIndex}
    self:UpdateGuide()
    return true
end
local oldCreate=U.Create
U.Create=function(self,...)
    if self.frame then return end
    oldCreate(self,...)
    self.bossSearch=Button(self.frame,98,A.L("searchBoss"),function() self:FindEncounter() end)
    self.bossSearch:SetPoint("LEFT",self.next,"RIGHT",8,0)
end
local oldSetContext=U.SetContext
U.SetContext=function(self,context,...)
    self.encounterPreview=nil
    return oldSetContext(self,context,...)
end
local oldFocus=U.FocusRouteStep
U.FocusRouteStep=function(self,index,...)
    self.encounterPreview=nil
    return oldFocus(self,index,...)
end
local oldFollow=U.FollowRouteSelection
U.FollowRouteSelection=function(self,...)
    self.encounterPreview=nil
    return oldFollow(self,...)
end
local oldUpdate=U.UpdateGuide
U.UpdateGuide=function(self,...)
    oldUpdate(self,...)
    local preview=self.encounterPreview
    local r=self.route;local p=self.guide
    if not (p and r and preview and r.id==preview.routeID) then return end
    local step=r.steps[preview.index]
    if not step then return end
    p.title:SetText(A.L("searchBossInspect")..": "..A.RouteText(step.title))
    p.body:SetText(A.RouteText(step.body))
    p.warning:SetText(A.L("searchBossPreview"))
    p.done:SetEnabled(false);p.skip:SetEnabled(false)
    p.scroll:SetVerticalScroll(math.max(0,(preview.index-1)*27-32))
end
local oldLocalize=U.Localize
U.Localize=function(self,...)
    oldLocalize(self,...)
    if self.bossSearch then self.bossSearch:SetText(A.L("searchBoss")) end
    if self.searchDialog then
        local f=self.searchDialog
        f.TitleText:SetText(A.L("searchBossTitle"))
        f.find:SetText(A.L("searchBossAction"))
        f.close:SetText(A.L("cancel"))
        if not self.menu or not self.menu:IsShown() then f.help:SetText(A.L("searchBossHint")) end
    end
end
