-- In-game notebook and "next actionable" guide button.
-- Local per-character notes only; no combat, quest, inventory or currency actions.
local A=DungeonMapPilot
local U=A.UI
local function btn(parent,w,title,fn)
    local b=CreateFrame("Button",nil,parent,"UIPanelButtonTemplate")
    b:SetSize(w,24);b:SetText(title);b:SetScript("OnClick",fn)
    return b
end
local function label(parent,text)
    local t=parent:CreateFontString(nil,"OVERLAY","GameFontHighlightSmall")
    t:SetText(text);t:SetJustifyH("LEFT");return t
end
function U:NoteTarget()
    local route=self.route
    if not route then return nil end
    local preview=self.encounterPreview
    local index=preview and preview.routeID==route.id and preview.index or
        A.RouteState(route).current
    if not A.ID(index) or not route.steps[index] then return nil end
    return route,index
end
function U:OpenBossNote()
    if A.Call(InCombatLockdown)~=false then A.Print("combat");return false end
    local route,index=self:NoteTarget()
    if not route then return false end
    if not self.noteDialog then
        local f=CreateFrame("Frame","DungeonMapPilotBossNoteFrame",
            self.frame,"BasicFrameTemplateWithInset")
        self.noteDialog=f
        f:SetSize(548,205);f:SetPoint("CENTER",self.frame,"CENTER")
        f:SetFrameLevel(self.frame:GetFrameLevel()+36)
        f:SetClampedToScreen(true);f:EnableMouse(true)
        f.TitleText:SetText(A.L("bossNoteTitle"))
        f.subtitle=label(f,A.L("bossNoteHelp"))
        f.subtitle:SetPoint("TOPLEFT",22,-39);f.subtitle:SetWidth(498)
        f.boss=label(f,"")
        f.boss:SetPoint("TOPLEFT",22,-69);f.boss:SetWidth(498)
        f.input=CreateFrame("EditBox",nil,f,"InputBoxTemplate")
        f.input:SetSize(490,29);f.input:SetPoint("TOPLEFT",25,-94)
        f.input:SetAutoFocus(false);f.input:SetMaxLetters(200)
        f.status=label(f,"");f.status:SetPoint("TOPLEFT",25,-127)
        f.status:SetWidth(495)
        f.save=btn(f,108,A.L("bossNoteSave"),function()
            if not self.route or self.route.id~=f.route.id then
                f.status:SetText(A.L("bossNoteBlocked"));return
            end
            local ok,reason=A.NoteSet(f.route,f.index,f.input:GetText())
            if ok then f:Hide();self:UpdateGuide()
            else f.status:SetText(A.L(reason=="COMBAT" and "combat" or "bossNoteBlocked")) end
        end)
        f.save:SetPoint("BOTTOMLEFT",12,16)
        f.clear=btn(f,100,A.L("bossNoteClear"),function()
            if not self.route or self.route.id~=f.route.id then return end
            local ok=A.NoteSet(f.route,f.index,"")
            if ok then f:Hide();self:UpdateGuide()
            else f.status:SetText(A.L("bossNoteBlocked")) end
        end)
        f.clear:SetPoint("LEFT",f.save,"RIGHT",7,0)
        f.undo=btn(f,100,A.L("bossNoteUndo"),function()
            if not self.route or self.route.id~=f.route.id then return end
            if A.NoteUndo(f.route,f.index) then
                f.input:SetText(A.NoteGet(f.route,f.index) or "")
                f.undo:SetEnabled(A.NoteCanUndo(f.route,f.index))
                f.status:SetText(A.L("bossNoteUndone"));self:UpdateGuide()
            else f.status:SetText(A.L("bossNoteBlocked")) end
        end)
        f.undo:SetPoint("LEFT",f.clear,"RIGHT",7,0)
        f.close=btn(f,102,A.L("close"),function() f:Hide() end)
        f.close:SetPoint("BOTTOMRIGHT",-12,16)
        f.input:SetScript("OnEnterPressed",function() f.save:Click() end)
        f.input:SetScript("OnEscapePressed",function() f:Hide() end)
        f:SetScript("OnHide",function() f.input:ClearFocus() end)
        if UISpecialFrames then
            UISpecialFrames[#UISpecialFrames+1]="DungeonMapPilotBossNoteFrame"
        end
    end
    local f=self.noteDialog
    f.route=route;f.index=index
    f.TitleText:SetText(A.L("bossNoteTitle"))
    f.subtitle:SetText(A.L("bossNoteHelp"))
    f.boss:SetText(A.RouteText(route.steps[index].title))
    f.save:SetText(A.L("bossNoteSave"))
    f.clear:SetText(A.L("bossNoteClear"))
    f.undo:SetText(A.L("bossNoteUndo"))
    f.close:SetText(A.L("close"))
    f.input:SetText(A.NoteGet(route,index) or "")
    f.undo:SetEnabled(A.NoteCanUndo(route,index))
    f.status:SetText(A.L("bossNotePersonal"))
    f:Show();f.input:SetFocus()
    return true
end
local createGuide=U.CreateGuide
U.CreateGuide=function(self,...)
    createGuide(self,...)
    local p=self.guide
    p.heading:SetWidth(112)
    p.notes=btn(p,80,A.L("bossNoteButton"),function() self:OpenBossNote() end)
    p.notes:SetPoint("TOPRIGHT",-40,-9)
    p.notes:SetScript("OnEnter",function(owner)
        local route,index=self:NoteTarget()
        if not route or not GameTooltip then return end
        GameTooltip:SetOwner(owner,"ANCHOR_LEFT")
        GameTooltip:SetText(A.L("bossNoteTitle"))
        GameTooltip:AddLine(A.RouteText(route.steps[index].title),.98,.85,.62,true)
        GameTooltip:AddLine(A.NoteGet(route,index) or A.L("bossNoteEmpty"),.85,.91,.93,true)
        GameTooltip:Show()
    end)
    p.notes:SetScript("OnLeave",function() if GameTooltip then GameTooltip:Hide() end end)
    p.mode:SetWidth(125)
    p.resume=btn(p,82,A.L("resumeNextStep"),function()
        if A.Call(InCombatLockdown)~=false or not self.route then return end
        local index=A.RouteNextReady(self.route)
        if not index then return end
        self.restartConfirm=nil
        self:FocusRouteStep(index)
    end)
    p.resume:SetPoint("TOPRIGHT",-12,-38)
    p.resume:SetScript("OnEnter",function(owner)
        if not GameTooltip then return end
        GameTooltip:SetOwner(owner,"ANCHOR_LEFT")
        GameTooltip:SetText(A.L("resumeNextStep"))
        GameTooltip:AddLine(A.L("resumeNextHelp"),.84,.9,.98,true)
        GameTooltip:Show()
    end)
    p.resume:SetScript("OnLeave",function() if GameTooltip then GameTooltip:Hide() end end)
end
local updateGuide=U.UpdateGuide
U.UpdateGuide=function(self,...)
    updateGuide(self,...)
    if not self.guide then return end
    local p=self.guide
    local route,index=self:NoteTarget()
    local hasNote=route and A.NoteGet(route,index)
    p.notes:SetText(A.L("bossNoteButton")..(hasNote and " *" or ""))
    p.notes:SetEnabled(route~=nil and A.Call(InCombatLockdown)==false)
    p.resume:SetText(A.L("resumeNextStep"))
    p.resume:SetEnabled(self.route~=nil and
        A.RouteNextReady(self.route)~=nil and A.Call(InCombatLockdown)==false)
end
local setContext=U.SetContext
U.SetContext=function(self,context,...)
    if self.noteDialog then self.noteDialog:Hide() end
    return setContext(self,context,...)
end
