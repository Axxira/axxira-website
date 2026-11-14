local A = DungeonMapPilot
local UI = {}; A.UI = UI

local function Label(parent, font, text)
    local f = parent:CreateFontString(nil, "OVERLAY", font or "GameFontNormal")
    f:SetText(text or ""); return f
end

local function Button(parent, width, text, fn)
    local b = CreateFrame("Button", nil, parent, "UIPanelButtonTemplate")
    b:SetSize(width, 24); b:SetText(text); b:SetScript("OnClick", fn); return b
end

local function Tip(owner, title, body)
    GameTooltip:SetOwner(owner, "ANCHOR_RIGHT")
    GameTooltip:SetText(title)
    if body then GameTooltip:AddLine(body, 0.9, 0.85, 0.7, true) end
    GameTooltip:Show()
end

function UI:Cursor()
    local x, y = GetCursorPosition()
    local scale = self.viewport:GetEffectiveScale()
    return x / scale - self.viewport:GetLeft(), self.viewport:GetTop() - y / scale
end

function UI:ApplyView()
    local v = self.view
    self.canvas:SetSize(v.width * v.zoom, v.height * v.zoom)
    self.viewport:SetHorizontalScroll(v.panX); self.viewport:SetVerticalScroll(v.panY)
    for i, tile in ipairs(self.floor and self.floor.tiles or {}) do
        local tex = self.tiles[i]
        tex:ClearAllPoints(); tex:SetPoint("TOPLEFT", self.canvas, "TOPLEFT", tile.x * v.width * v.zoom, -tile.y * v.height * v.zoom)
        tex:SetSize(tile.w * v.width * v.zoom, tile.h * v.height * v.zoom)
    end
    self:UpdatePins()
    self:UpdateRoutePins()
    self:UpdatePosition()
end

function UI:Reset()
    self.view.zoom, self.view.panX, self.view.panY = 1, 0, 0
    self:ApplyView()
end

function UI:MakePin()
    local b = CreateFrame("Button", nil, self.canvas)
    b:SetSize(22, 22); b:SetFrameLevel(self.canvas:GetFrameLevel() + 4)
    b.icon = b:CreateTexture(nil, "ARTWORK")
    b.icon:SetAllPoints(); b.icon:SetTexture("Interface\\TargetingFrame\\UI-RaidTargetingIcon_1")
    b:SetScript("OnEnter", function(s) Tip(s, s.pin.label, A.L("personal")) end)
    b:SetScript("OnLeave", function() GameTooltip:Hide() end)
    b:SetScript("OnClick", function(s) self:EditPin(s.pin.x, s.pin.y, s.index) end)
    return b
end

function UI:UpdatePins()
    for _, b in ipairs(self.pins) do b:Hide() end
    if not self.floor then return end
    local v = self.view
    for i, pin in ipairs(A.Pins(self.floor.key)) do
        local b = self.pins[i] or self:MakePin(); self.pins[i] = b
        b.pin, b.index = pin, i
        b:ClearAllPoints(); b:SetPoint("CENTER", self.canvas, "TOPLEFT", pin.x * v.width * v.zoom, -pin.y * v.height * v.zoom)
        b:Show()
    end
end

function UI:UpdatePosition()
    self.player:Hide()
    local status = self.context and A.Status(self.context, self.floor, self.here) or "unsupported"
    self.status:SetText(A.L(status))
    self.status:SetTextColor(status == "live" and 0.35 or 0.9, status == "live" and 0.95 or 0.77, status == "live" and 0.65 or 0.5)
    if status ~= "live" then return end
    local x, y = A.Position(self.floor)
    if not x then return end
    self.player:ClearAllPoints()
    self.player:SetPoint("CENTER", self.canvas, "TOPLEFT", x * self.view.width * self.view.zoom, -y * self.view.height * self.view.zoom)
    local angle = A.Number(A.Call(GetPlayerFacing))
    self.player.icon:SetRotation(angle or 0); self.player:Show()
end

function UI:EditPin(x, y, index)
    if not self.floor then return end
    if not index and #A.Pins(self.floor.key) >= 40 then A.Print("pinLimit"); return end
    if not self.editor then
        local f = CreateFrame("Frame", nil, self.frame, "BasicFrameTemplateWithInset")
        self.editor = f; f:SetSize(360, 148); f:SetPoint("CENTER"); f:SetFrameLevel(self.frame:GetFrameLevel() + 30)
        f.TitleText:SetText(A.L("pinTitle"))
        f.input = CreateFrame("EditBox", nil, f, "InputBoxTemplate")
        f.input:SetSize(306, 30); f.input:SetPoint("TOPLEFT", 24, -40); f.input:SetAutoFocus(false); f.input:SetMaxLetters(60)
        f.save = Button(f, 108, A.L("save"), function()
            if A.SavePin(f.key, f.x, f.y, f.input:GetText(), f.index) then f:Hide(); self:UpdatePins(); self:UpdateGuide() end
        end); f.save:SetPoint("BOTTOMLEFT", 15, 17)
        f.delete = Button(f, 94, A.L("delete"), function() A.DeletePin(f.key, f.index); f:Hide(); self:UpdatePins(); self:UpdateGuide() end)
        f.delete:SetPoint("LEFT", f.save, "RIGHT", 6, 0)
        f.cancel = Button(f, 94, A.L("cancel"), function() f:Hide() end); f.cancel:SetPoint("LEFT", f.delete, "RIGHT", 6, 0)
        f.input:SetScript("OnEnterPressed", function() f.save:Click() end)
        f.input:SetScript("OnEscapePressed", function() f:Hide() end)
        f:SetScript("OnHide", function() f.input:ClearFocus() end)
    end
    local f = self.editor
    f.key, f.x, f.y, f.index = self.floor.key, x, y, index
    f.TitleText:SetText(A.L("pinTitle")); f.save:SetText(A.L("save")); f.delete:SetText(A.L("delete")); f.cancel:SetText(A.L("cancel"))
    f.delete:SetEnabled(index ~= nil)
    f.input:SetText(index and A.Pins(f.key)[index].label or "")
    f:Show(); f.input:SetFocus(); f.input:HighlightText()
end

function UI:Menu(items, anchor, width)
    if self.menu and self.menu:IsShown() then self.menu:Hide(); return end
    if not self.menu then
        local f = CreateFrame("Frame", nil, self.frame, "BackdropTemplate")
        self.menu = f; f:SetFrameLevel(self.frame:GetFrameLevel() + 20); f:SetClampedToScreen(true)
        f:SetBackdrop({ bgFile = "Interface\\DialogFrame\\UI-DialogBox-Background", edgeFile = "Interface\\Tooltips\\UI-Tooltip-Border", edgeSize = 16, insets = { left = 4, right = 4, top = 4, bottom = 4 } })
        f:EnableMouse(true)
        f.scroll = CreateFrame("ScrollFrame", nil, f, "UIPanelScrollFrameTemplate")
        f.scroll:SetPoint("TOPLEFT", 7, -7); f.scroll:SetPoint("BOTTOMRIGHT", -28, 7)
        f.content = CreateFrame("Frame", nil, f.scroll); f.scroll:SetScrollChild(f.content)
        f.buttons = {}
    end
    local m = self.menu
    m:ClearAllPoints(); m:SetPoint("TOPLEFT", anchor, "BOTTOMLEFT", 0, -2)
    m:SetSize(width or 320, math.min(430, #items * 26 + 16))
    m.content:SetSize((width or 320) - 38, #items * 26)
    for _, b in ipairs(m.buttons) do b:Hide() end
    for i, item in ipairs(items) do
        local b = m.buttons[i] or Button(m.content, (width or 320) - 38, "", function(s) m:Hide(); s.action() end)
        m.buttons[i] = b; b:SetWidth((width or 320) - 38); b:ClearAllPoints(); b:SetPoint("TOPLEFT", 0, -(i - 1) * 26)
        b:SetText(item.text); b.action = item.action; b:Show()
    end
    m.scroll:SetVerticalScroll(0); m:Show()
end

function UI:ChooseMap()
    local items={}
    local level=A.PlayerDungeonLevel()
    local levelMatches=A.CatalogAtLevel(level)
    if self.mapBrowseMode~="level" then self.mapBrowseMode="all" end
    if level and #levelMatches>0 then
        local mode=self.mapBrowseMode
        items[#items+1]={text=mode=="level" and A.L("guideAllMaps",#A.CatalogList())
            or A.L("guideMyLevel",level,#levelMatches),action=function()
                self.mapBrowseMode=mode=="level" and "all" or "level"
                self:ChooseMap()
            end}
    else self.mapBrowseMode="all" end
    local entries=self.mapBrowseMode=="level" and #levelMatches>0 and levelMatches or A.CatalogList()
    for _,item in ipairs(entries) do
        local id=item.id
        local meta=A.catalog[id]
        local min,max=A.CatalogLevelRange(id)
        local range=min and max and ("["..min.."-"..max.."] ") or ""
        local suffix=meta and meta.rosterOnly and A.L("guideBrowseRoster")
            or meta and meta.previewOnly and A.L("guideBrowsePreview")
            or meta and meta.guideOnly and A.L("guideBrowseText")
        items[#items+1]={text=range..item.name..(suffix and (" - "..suffix) or ""),
            action=function()
                A.db.lastReference=id
                self.follow=false
                self:SetContext(A.ReferenceContext(id) or A.GuideContext(id))
            end}
    end
    self:Menu(items,self.browse,365)
end

function UI:ChooseFloor()
    local items = {}
    for i, floor in ipairs(self.context and self.context.floors or {}) do
        local index = i
        items[#items + 1] = { text = A.FloorName(floor, i), action = function() self:SelectFloor(index, false) end }
    end
    if #items > 0 then self:Menu(items, self.floorButton, 300) end
end

function UI:SelectFloor(index, follow)
    if not self.context or #self.context.floors == 0 then return end
    self.follow = follow
    self.context.index = A.Clamp(math.floor(index), 1, #self.context.floors)
    if self.context.source == "reference" then A.db.floors[self.context.instanceID] = self.context.index end
    if self.editor then self.editor:Hide() end
    self:Render()
end

function UI:SetContext(context)
    if self.editor then self.editor:Hide() end
    if self.menu then self.menu:Hide() end
    self.restartConfirm=nil
    self.context = context; self:Render()
end

function UI:Current()
    self.here = A.CurrentContext(); self.follow = true
    self:SetContext(self.here)
end

function UI:Render()
    for _, t in ipairs(self.tiles) do t:Hide() end
    local c = self.context
    self.floor = c and c.floors[c.index or 1]
    local floor = self.floor
    self.name:SetText(c and (c.source == "reference" and A.CatalogName(c.instanceID) or c.name) or A.L("title"))
    self.floorButton:SetText(floor and A.FloorName(floor, c.index) or A.L("empty"))
    self.floorButton:SetEnabled(floor ~= nil)
    self.previous:SetEnabled(floor ~= nil and c.index > 1)
    self.next:SetEnabled(floor ~= nil and c.index < #c.floors)
    self.source:SetText(floor and A.L(floor.source) or "")
    self.sourceButton:SetShown(floor ~= nil)
    self.empty:SetShown(not floor or not floor.tiles)
    self.empty:SetText(c and c.source=="guide-only" and
        (A.L("guideNoArt").."\n\n"..A.L("guideTextOnly")) or
        (A.L("unsupported") .. "\n\n" .. A.L("unsupportedHelp")))
    local aspect = floor and floor.aspect or 1.5
    local width = math.min(884, 590 * aspect); local height = width / aspect
    self.viewport:SetSize(width, height)
    self.view.width, self.view.height = width, height
    self.artMissing = false
    for i, tile in ipairs(floor and floor.tiles or {}) do
        local t = self.tiles[i] or self.canvas:CreateTexture(nil, "BACKGROUND", nil, 1); self.tiles[i] = t
        local accepted = t:SetTexture(tile.file)
        if accepted == false then self.artMissing = true end
        t:SetTexCoord(0, tile.u, 0, tile.v); t:Show()
    end
    if self.artMissing then self.empty:SetText(A.L("missingArt")); self.empty:Show() end
    self:UpdateGuide(); self:Reset()
end

-- The Options screen is one click from the map. Donations never appear on map controls.
local optionsLabels={enUS="Options",enGB="Options",frFR="Options",deDE="Optionen",
    esES="Opciones",esMX="Opciones",itIT="Opzioni",ptBR="Opções",
    ruRU="Настройки",koKR="설정",zhCN="选项",zhTW="選項"}
function UI:OptionsCaption()
    local code=A.db and A.db.language
    if code=="auto" or code==nil then code=A.Call(GetLocale) end
    return optionsLabels[code] or optionsLabels.enUS
end
function UI:OpenOptions()
    if not self.frame then self:Create() end
    if self.optionsPanel then
        self.optionsPanel:SetShown(not self.optionsPanel:IsShown())
        return
    end
    if self.menu then self.menu:Hide() end
    local p=CreateFrame("Frame","DungeonMapPilotOptionsFrame",self.frame,"BackdropTemplate")
    self.optionsPanel=p
    p:SetSize(465,280);p:SetPoint("CENTER",self.frame,"CENTER",0,0)
    p:SetFrameStrata("DIALOG");p:SetFrameLevel(self.frame:GetFrameLevel()+13)
    p:SetClampedToScreen(true);p:EnableMouse(true)
    p:SetBackdrop({bgFile="Interface\\Buttons\\WHITE8X8",
        edgeFile="Interface\\Buttons\\WHITE8X8",edgeSize=1})
    p:SetBackdropColor(.048,.063,.078,.98);p:SetBackdropBorderColor(.65,.53,.29,1)
    p.title=Label(p,"GameFontNormalLarge",self:OptionsCaption())
    p.title:SetPoint("TOPLEFT",20,-17)
    p.description=Label(p,"GameFontHighlightSmall",A.L("hint"))
    p.description:SetPoint("TOPLEFT",20,-52)
    p.description:SetWidth(420)
    p.language=Button(p,280,A.L("language"),function()
        local choices={}
        for _,entry in ipairs(A.LanguageChoices()) do
            local code=entry[1]
            choices[#choices+1]={text=entry[2],action=function()
                A.db.language=code;self:Localize()
            end}
        end
        if self.menu then self.menu:Hide() end
        self:Menu(choices,p.language,215)
    end)
    p.language:SetPoint("TOPLEFT",20,-102)
    p.close=Button(p,92,A.L("cancel"),function() p:Hide() end)
    p.close:SetPoint("TOPRIGHT",-16,-16)
    if A.Support then
        p.supportHint=Label(p,"GameFontHighlightSmall",A.Support.Text("optional"))
        p.supportHint:SetPoint("TOPLEFT",20,-155)
        p.supportHint:SetWidth(420)
        A.Support.Attach(p,{
            point="TOPLEFT",relativePoint="TOPLEFT",x=20,y=-185,width=239,height=33})
    end
    p:SetScript("OnHide",function() if self.menu then self.menu:Hide() end end)
    if UISpecialFrames then UISpecialFrames[#UISpecialFrames+1]="DungeonMapPilotOptionsFrame" end
    p:Show()
end

function UI:Localize()
    self.frame.TitleText:SetText("DungeonMapPilot · " .. A.L("title"))
    self.browse:SetText(A.L("browse")); self.current:SetText(A.L("current")); self.reset:SetText(A.L("reset"))
    if self.proUpgrade then self.proUpgrade:SetText(A.L("proUpgrade")) end
    self.lang:SetText(self:OptionsCaption()); self.hint:SetText(A.L("hint"))
    if self.optionsPanel then
        self.optionsPanel.title:SetText(self:OptionsCaption())
        self.optionsPanel.language:SetText(A.L("language"))
        if A.Support then self.optionsPanel.supportHint:SetText(A.Support.Text("optional")) end
    end
    if A.worldButton then A.worldButton:SetText(A.L("worldButton")) end
    self:Render()
end

function UI:Create()
    if self.frame then return end
    local f = CreateFrame("Frame", "DungeonMapPilotFrame", UIParent, "BasicFrameTemplateWithInset")
    self.frame = f; f:Hide(); f:SetSize(920, 728); f:SetPoint("CENTER"); f:SetFrameStrata("HIGH")
    f:SetScale(math.min(1, UIParent:GetWidth() / 960, UIParent:GetHeight() / 768))
    f:SetClampedToScreen(true); f:SetMovable(true); f:EnableMouse(true); f:RegisterForDrag("LeftButton")
    f:SetScript("OnDragStart", function() f:StartMoving() end)
    f:SetScript("OnDragStop", function()
        f:StopMovingOrSizing()
        local point, _, relativePoint, x, y = f:GetPoint()
        A.db.window = { point = point, relativePoint = relativePoint, x = x, y = y }
    end)
    local saved = A.db.window
    if type(saved) == "table" and A.Number(saved.x) and A.Number(saved.y) then
        local valid = { CENTER = true, TOP = true, BOTTOM = true, LEFT = true, RIGHT = true, TOPLEFT = true, TOPRIGHT = true, BOTTOMLEFT = true, BOTTOMRIGHT = true }
        if valid[saved.point] and valid[saved.relativePoint] then f:ClearAllPoints(); f:SetPoint(saved.point, UIParent, saved.relativePoint, saved.x, saved.y) end
    end
    UISpecialFrames[#UISpecialFrames + 1] = "DungeonMapPilotFrame"
    self.tiles, self.pins = {}, {}
    self.browse = Button(f, 100, "", function() self:ChooseMap() end); self.browse:SetPoint("TOPLEFT", 18, -35)
    self.current = Button(f, 146, "", function() self:Current() end); self.current:SetPoint("LEFT", self.browse, "RIGHT", 6, 0)
    self.previous = Button(f, 28, "<", function() self:SelectFloor(self.context.index - 1, false) end); self.previous:SetPoint("LEFT", self.current, "RIGHT", 16, 0)
    self.floorButton = Button(f, 338, "", function() self:ChooseFloor() end); self.floorButton:SetPoint("LEFT", self.previous, "RIGHT", 4, 0)
    self.next = Button(f, 28, ">", function() self:SelectFloor(self.context.index + 1, false) end); self.next:SetPoint("LEFT", self.floorButton, "RIGHT", 4, 0)
    -- One compact row directly below the selected dungeon floor (e.g. "Cimetière").
    -- It does not move or cover the map, the dungeon title, or the route panel.
    self.proUpgrade = Button(f, 338, A.L("proUpgrade"), function() self:OpenProSubscription() end)
    self.proUpgrade:SetHeight(19)
    self.proUpgrade:SetPoint("TOPLEFT", self.floorButton, "BOTTOMLEFT", 0, -4)
    self.proUpgrade:SetScript("OnEnter", function(s) Tip(s, A.L("proUpgrade"), A.L("proUpgradeHelp")) end)
    self.proUpgrade:SetScript("OnLeave", function() GameTooltip:Hide() end)
    self.lang = Button(f, 100, "", function() self:OpenOptions() end)
    self.lang:SetPoint("TOPRIGHT", -18, -35)
    self.name = Label(f, "GameFontNormal"); self.name:SetPoint("TOPLEFT", 21, -64)
    self.source = Label(f, "GameFontHighlightSmall"); self.source:SetPoint("TOPRIGHT", -21, -65)
    self.sourceButton = CreateFrame("Button", nil, f); self.sourceButton:SetAllPoints(self.source)
    self.sourceButton:SetScript("OnEnter", function(s) if self.floor and self.floor.source == "reference" then Tip(s, A.L("reference"), A.L("referenceHelp")) end end)
    self.sourceButton:SetScript("OnLeave", function() GameTooltip:Hide() end)
    self.viewport = CreateFrame("ScrollFrame", nil, f)
    self.viewport:SetPoint("TOPLEFT", 18, -84); self.viewport:EnableMouse(true); self.viewport:EnableMouseWheel(true)
    self.canvas = CreateFrame("Frame", nil, self.viewport); self.viewport:SetScrollChild(self.canvas)
    self.view = { width = 884, height = 590, zoom = 1, panX = 0, panY = 0 }
    self.viewport:SetScript("OnMouseWheel", function(_, delta)
        local x, y = self:Cursor(); A.ZoomAt(self.view, self.view.zoom + delta * 0.25, x, y); self:ApplyView()
    end)
    self.viewport:SetScript("OnMouseDown", function(_, button)
        if self.menu then self.menu:Hide() end
        if button ~= "LeftButton" then return end
        local x, y = self:Cursor()
        if IsShiftKeyDown() then
            local u, v = A.CursorToMap(self.view, x, y); if u then self:EditPin(u, v) end
        else self.drag = { x = x, y = y, panX = self.view.panX, panY = self.view.panY } end
    end)
    self.viewport:SetScript("OnMouseUp", function() self.drag = nil end)
    self.player = CreateFrame("Frame", nil, self.canvas); self.player:SetSize(28, 28)
    self.player:SetFrameLevel(self.canvas:GetFrameLevel() + 6)
    self.player.icon = self.player:CreateTexture(nil, "ARTWORK"); self.player.icon:SetAllPoints()
    self.player.icon:SetTexture("Interface\\Minimap\\MinimapArrow")
    self.empty = Label(f, "GameFontNormalLarge"); self.empty:SetPoint("CENTER", self.viewport); self.empty:SetWidth(640)
    self.status = Label(f, "GameFontNormal"); self.status:SetPoint("BOTTOMLEFT", 20, 32)
    self.hint = Label(f, "GameFontHighlightSmall"); self.hint:SetPoint("BOTTOMLEFT", 20, 14)
    self.reset = Button(f, 120, "", function() self:Reset() end); self.reset:SetPoint("BOTTOMRIGHT", -18, 20)
    self:CreateGuide()
    f:SetScript("OnHide", function()
        self.drag = nil
        if self.menu then self.menu:Hide() end
        if self.editor then self.editor:Hide() end
        if self.optionsPanel then self.optionsPanel:Hide() end
    end)
    local positionTime, contextTime = 0, 0
    f:SetScript("OnUpdate", function(_, dt)
        if self.drag then
            if not IsMouseButtonDown("LeftButton") then self.drag = nil
            else
                local x, y = self:Cursor()
                self.view.panX, self.view.panY = A.PanBounds(self.view, self.drag.panX + self.drag.x - x, self.drag.panY + self.drag.y - y)
                self:ApplyView()
            end
        end
        positionTime, contextTime = positionTime + dt, contextTime + dt
        if contextTime >= 1 then
            contextTime = 0
            if not A.HasAccess() then self.frame:Hide(); A.Print("access"); return end
            self:RefreshCurrent()
        end
        if positionTime >= 0.15 then positionTime = 0; self:UpdatePosition() end
    end)
    if f.TitleText then
        f.TitleText:ClearAllPoints()
        f.TitleText:SetPoint("TOPLEFT",16,-7)
        f.TitleText:SetWidth(560)
        f.TitleText:SetJustifyH("LEFT")
    end
    self:Localize()
end

function UI:RefreshCurrent()
    self.here = A.CurrentContext()
    if not self.follow then return end
    local c, h = self.context, self.here
    local key = c and c.floors[c.index or 1] and c.floors[c.index or 1].key
    local nextKey = h and h.floors[h.index or 1] and h.floors[h.index or 1].key
    if key ~= nextKey or (c and c.instanceID) ~= (h and h.instanceID) or (c and c.source) ~= (h and h.source) then self:SetContext(h) end
end

function UI:OpenProSubscription()
    -- Safe in-game behavior: selects the official link, never fabricates
    -- an external-browser launch that protected WoW Lua cannot perform.
    self:AccessHelp()
    local box=self.accessFrame and self.accessFrame.url
    if box then box:SetFocus(); box:HighlightText() end
end

function UI:AccessHelp()
    if not self.accessFrame then
        local f = CreateFrame("Frame", "DungeonMapPilotAccessFrame", UIParent, "BasicFrameTemplateWithInset")
        self.accessFrame = f; f:SetSize(610, 290); f:SetPoint("CENTER"); f:SetFrameStrata("DIALOG"); f:SetClampedToScreen(true)
        f.steps = Label(f, "GameFontHighlight"); f.steps:SetPoint("TOPLEFT", 24, -44); f.steps:SetWidth(562)
        f.caption = Label(f, "GameFontNormal"); f.caption:SetPoint("TOPLEFT", 24, -130)
        f.url = CreateFrame("EditBox", nil, f, "InputBoxTemplate"); f.url:SetSize(550, 28); f.url:SetPoint("TOPLEFT", 30, -151); f.url:SetAutoFocus(false)
        local address = A.subscriptionURL or "https://dungeonmappilot-subscriptions.tyronixstudio.workers.dev"
        f.url:SetText(address)
        f.url:SetScript("OnEditFocusGained", function(s) s:HighlightText() end)
        f.url:SetScript("OnTextChanged", function(s) if s:GetText() ~= address then s:SetText(address) end end)
        f.url:SetScript("OnEscapePressed", function() f:Hide() end)
        f.note = Label(f, "GameFontHighlightSmall"); f.note:SetPoint("TOPLEFT", 24, -192); f.note:SetWidth(562)
        f.close = Button(f, 120, "", function() f:Hide() end); f.close:SetPoint("BOTTOM", 0, 16)
        f:SetScript("OnHide", function() f.url:ClearFocus() end)
        UISpecialFrames[#UISpecialFrames + 1] = "DungeonMapPilotAccessFrame"
    end
    local f = self.accessFrame
    f.TitleText:SetText(A.L("accessTitle")); f.steps:SetText(A.L("accessSteps")); f.caption:SetText(A.L("portal")); f.note:SetText(A.L("accessNote")); f.close:SetText(A.L("close")); f:Show()
end

function A.Toggle()
    if UI.frame and UI.frame:IsShown() then UI.frame:Hide(); return end
    if A.Call(InCombatLockdown) then A.Print("combat"); return end
    if not A.HasAccess() then UI:AccessHelp(); return end
    if UI.accessFrame then UI.accessFrame:Hide() end
    UI:Create(); UI:Current(); UI.frame:Show()
    if not UI.here then
        local last = A.ID(A.db.lastReference)
        if last and A.catalog[last] then UI.follow = false;
            UI:SetContext(A.ReferenceContext(last) or A.GuideContext(last))
        else UI:ChooseMap() end
    end
end
