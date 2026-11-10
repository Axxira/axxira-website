local A = DungeonMapPilot
local U = A.UI
local function text(parent, font, width, height)
    local t=parent:CreateFontString(nil,"OVERLAY",font or "GameFontHighlightSmall")
    t:SetWidth(width); if height then t:SetHeight(height) end
    t:SetJustifyH("LEFT"); t:SetJustifyV("TOP")
    return t
end
local function button(parent, width, fn)
    local b=CreateFrame("Button",nil,parent,"UIPanelButtonTemplate")
    b:SetSize(width,24); b:SetScript("OnClick",fn); return b
end

function U:CreateGuide()
    self.routePins={}
    local p=CreateFrame("Frame",nil,self.frame,"BackdropTemplate"); self.guide=p
    p:SetSize(240,590); p:SetPoint("TOPRIGHT",-18,-84)
    p:SetBackdrop({bgFile="Interface\\DialogFrame\\UI-DialogBox-Background",edgeFile="Interface\\Tooltips\\UI-Tooltip-Border",edgeSize=16,insets={left=4,right=4,top=4,bottom=4}})
    p.heading=text(p,"GameFontNormal",216,22); p.heading:SetPoint("TOPLEFT",12,-12)
    p.mode=button(p,216,function()
        if not self.context then return end
        local id=self.context.instanceID
        self:Menu({
            {text=A.L("suggestedRoute"),action=function() A.db.routeMode[id]=nil; self:UpdateGuide() end},
            {text=A.L("personalRoute"),action=function() A.db.routeMode[id]="personal"; self:UpdateGuide() end},
        },p.mode,240)
    end); p.mode:SetPoint("TOPLEFT",12,-38)
    p.count=text(p,"GameFontHighlightSmall",216,22); p.count:SetPoint("TOPLEFT",12,-70)
    p.scroll=CreateFrame("ScrollFrame",nil,p,"UIPanelScrollFrameTemplate")
    p.scroll:SetPoint("TOPLEFT",12,-96); p.scroll:SetSize(192,200)
    p.content=CreateFrame("Frame",nil,p.scroll); p.content:SetSize(190,200); p.scroll:SetScrollChild(p.content); p.rows={}
    p.title=text(p,"GameFontNormal",216,36); p.title:SetPoint("TOPLEFT",12,-310)
    p.body=text(p,"GameFontHighlight",216,92); p.body:SetPoint("TOPLEFT",12,-350)
    p.warning=text(p,"GameFontHighlightSmall",216,42); p.warning:SetPoint("TOPLEFT",12,-447)
    p.done=button(p,216,function()
        local r=self.route; if not r then return end
        local s=A.RouteState(r); local step=r.steps[s.current]; if not step then return end
        local missing=A.RouteMissing(r,step)
        if #missing>0 then self:FocusRouteStep(missing[1].index); return end
        A.RouteAdvance(r,false); self:FollowRouteSelection()
    end); p.done:SetPoint("BOTTOMLEFT",12,64)
    p.back=button(p,104,function() if self.route then self:FocusRouteStep(math.max(1,A.RouteState(self.route).current-1)) end end)
    p.back:SetPoint("BOTTOMLEFT",12,36)
    p.skip=button(p,104,function() if self.route and A.RouteAdvance(self.route,true) then self:FollowRouteSelection() end end)
    p.skip:SetPoint("BOTTOMRIGHT",-12,36)
    p.restart=button(p,216,function()
        if not self.route then return end
        if self.restartConfirm~=self.route.id then self.restartConfirm=self.route.id; p.restart:SetText(A.L("confirmRestart")); return end
        A.RouteRestart(self.route); self.restartConfirm=nil; self:FollowRouteSelection()
    end); p.restart:SetPoint("BOTTOMLEFT",12,8)
    self.guideToggle=button(self.frame,150,function()
        A.db.guideHidden=not A.db.guideHidden; self:LayoutGuide(); self:UpdateGuide()
    end); self.guideToggle:SetPoint("RIGHT",self.reset,"LEFT",-8,0)
    self:LayoutGuide()
end

function U:LayoutGuide()
    local hidden=A.db.guideHidden==true
    self.frame:SetWidth(hidden and 920 or 1180)
    self.frame:SetScale(math.min(1,UIParent:GetWidth()/(hidden and 960 or 1220),UIParent:GetHeight()/768))
    self.guide:SetShown(not hidden)
    self.guideToggle:SetText(A.L(hidden and "showGuide" or "hideGuide"))
end

function U:FocusRouteStep(index)
    if not self.route or not A.RouteSelect(self.route,index) then return end
    self.restartConfirm=nil
    self:FollowRouteSelection()
end

function U:FollowRouteSelection()
    local r=self.route; local s=r and A.RouteState(r); local step=s and r.steps[s.current]
    if step and step.floor~=self.context.index then self:SelectFloor(step.floor,false) else self:UpdateGuide() end
    if step then
        local v=self.view
        v.panX,v.panY=A.PanBounds(v,step.x*v.width*v.zoom-v.width/2,step.y*v.height*v.zoom-v.height/2)
        self:ApplyView()
        self.guide.scroll:SetVerticalScroll(math.max(0,math.min((s.current-1)*27-80,#r.steps*27-200)))
    end
end

function U:UpdateGuide()
    if not self.guide then return end
    local p=self.guide
    self.route=A.GetRoute(self.context)
    local r=self.route
    p.heading:SetText(A.L("guideTitle")); p.mode:SetEnabled(self.context~=nil and self.context.source=="reference")
    p.mode:SetText(A.L(r and r.personal and "personalRoute" or "suggestedRoute"))
    self.guideToggle:SetText(A.L(A.db.guideHidden and "showGuide" or "hideGuide"))
    p.back:SetText(A.L("previous")); p.skip:SetText(A.L("skipOptional"))
    p.restart:SetText(A.L(self.restartConfirm and "confirmRestart" or "newRun"))
    for _, row in ipairs(p.rows) do row:Hide() end
    if not r then
        p.count:SetText(A.L("noGuide")); p.title:SetText(A.L("makePersonalRoute")); p.body:SetText(A.L("makePersonalHelp"))
        p.warning:SetText(A.L("guideManual")); p.done:SetText(A.L("stepDone")); p.done:SetEnabled(false)
        p.back:SetEnabled(false); p.skip:SetEnabled(false); p.restart:SetEnabled(false); p.content:SetHeight(1)
        self:UpdateRoutePins(); return
    end
    local s=A.RouteState(r); local done,total,skipped=A.RouteCounts(r)
    p.count:SetText(A.L("routeProgress",done,total)..(skipped>0 and A.L("routeSkipped",skipped) or ""))
    p.content:SetHeight(math.max(200,#r.steps*27))
    for i,step in ipairs(r.steps) do
        local row=p.rows[i] or button(p.content,190,function(b) self:FocusRouteStep(b.index) end)
        p.rows[i]=row; row.index=i; row:ClearAllPoints(); row:SetPoint("TOPLEFT",0,-(i-1)*27)
        local color=s.done[step.id] and "|cff78d79a" or s.skipped[step.id] and "|cff999999" or s.current==i and "|cffffd36a" or "|cffdddddd"
        row:SetText(color..A.RouteNumber(i)..". "..A.RouteText(step.title).."|r"); row:Show()
    end
    local step=r.steps[s.current]
    local missing=step and A.RouteMissing(r,step) or {}
    p.title:SetText(step and (A.RouteNumber(s.current).." · "..A.RouteText(step.title)) or A.L("routeFinished"))
    p.body:SetText(step and A.RouteText(step.body) or A.L("routeFinishedBody"))
    local warning=A.L(r.personal and "personalRouteNote" or "referenceRouteNote")
    if #missing>0 then warning=A.L("missingRouteStep",A.RouteText(missing[1].step.title))
    elseif step and step.kind=="optional" then warning=A.L("optionalRouteStep") end
    p.warning:SetText(warning)
    p.done:SetText(A.L(#missing>0 and "showRequired" or "stepDone")); p.done:SetEnabled(step~=nil)
    p.skip:SetEnabled(step~=nil and step.kind=="optional"); p.back:SetEnabled(s.current>1); p.restart:SetEnabled(true)
    self:UpdateRoutePins()
end

function U:UpdateRoutePins()
    if not self.routePins then return end
    -- Duplicate anchors (start and return) leave holes in this table.
    for _,pin in pairs(self.routePins) do pin:Hide() end
    if not self.route or not self.floor or self.floor.source~="reference" or A.db.guideHidden then return end
    local r,s,v=self.route,A.RouteState(self.route),self.view
    local chosen={}
    for i,step in ipairs(r.steps) do
        if step.floor==self.context.index then
            local key=step.x..":"..step.y
            if not chosen[key] or s.current==i then chosen[key]=i end
        end
    end
    for _,i in pairs(chosen) do
        local step=r.steps[i]
        local b=self.routePins[i]
        if not b then
            b=CreateFrame("Button",nil,self.canvas,"BackdropTemplate"); self.routePins[i]=b
            b:SetBackdrop({bgFile="Interface\\Buttons\\WHITE8X8",edgeFile="Interface\\Tooltips\\UI-Tooltip-Border",edgeSize=12,insets={left=2,right=2,top=2,bottom=2}})
            b:SetFrameLevel(self.canvas:GetFrameLevel()+5)
            b.number=b:CreateFontString(nil,"OVERLAY","GameFontNormal"); b.number:SetPoint("CENTER")
            b:RegisterForClicks("LeftButtonUp","RightButtonUp")
            b:SetScript("OnClick",function(pin,mouseButton)
                local target=self.route and self.route.steps[pin.index]
                if mouseButton=="RightButton" and target and self.route.personal then
                    self:EditPin(target.x,target.y,target.pinIndex)
                else self:FocusRouteStep(pin.index) end
            end)
            b:SetScript("OnEnter",function(pin)
                local target=self.route and self.route.steps[pin.index]; if not target then return end
                GameTooltip:SetOwner(pin,"ANCHOR_RIGHT"); GameTooltip:SetText(A.RouteText(target.title))
                GameTooltip:AddLine(A.RouteText(target.body),.95,.9,.75,true)
                GameTooltip:AddLine(A.L("routeMarkerHelp"),.7,.7,.7,true)
                if self.route.personal then GameTooltip:AddLine(A.L("routeEditHelp"),.7,.7,.7,true) end
                GameTooltip:Show()
            end)
            b:SetScript("OnLeave",function() GameTooltip:Hide() end)
        end
        b.index=i; b:ClearAllPoints(); b:SetPoint("CENTER",self.canvas,"TOPLEFT",step.x*v.width*v.zoom,-step.y*v.height*v.zoom)
        local active=s.current==i; b:SetSize(active and 34 or 27,active and 34 or 27)
        if s.done[step.id] then b:SetBackdropColor(.08,.32,.17,.95)
        elseif active then b:SetBackdropColor(.55,.30,.04,1)
        elseif s.skipped[step.id] then b:SetBackdropColor(.22,.22,.22,.95)
        elseif step.kind=="optional" then b:SetBackdropColor(.10,.22,.35,.95)
        else b:SetBackdropColor(.12,.10,.08,.95) end
        b.number:SetText(A.RouteNumber(i)); b:Show()
    end
end
