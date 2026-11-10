TEST = { count = 0, frames = {}, now = 1000, locale = "frFR", best = 9001, instance = 43, kind = "party", px = 0.4, py = 0.6 }
local T = TEST
function check(condition, why) T.count = T.count + 1; assert(condition, why) end
function near(a, b) return math.abs(a - b) < 0.00001 end
local methods = {}
local function object(kind)
    local o = { kind = kind, scripts = {}, shown = true, enabled = true, width = 100, height = 100, scale = 1 }
    return setmetatable(o, { __index = function(_, key)
        if key:match("^[A-Z]") then assert(methods[key], "Unknown frame API: " .. tostring(key)) end
        return methods[key]
    end })
end
function methods:SetText(text) self.text = text end
function methods:GetText() return rawget(self, "text") or "" end
function methods:SetSize(w, h) self.width, self.height = w, h end
function methods:SetWidth(w) self.width = w end
function methods:SetHeight(h) self.height = h end
function methods:SetJustifyH() end
function methods:SetJustifyV() end
function methods:SetBackdropColor(...) self.backdropColor={...} end
function methods:SetPoint(...) self.point = {...} end
function methods:GetPoint() return "CENTER", UIParent, "CENTER", 1, 2 end
function methods:ClearAllPoints() self.point = nil end
function methods:SetAllPoints() end
function methods:SetScript(key, fn) self.scripts[key] = fn end
function methods:Hide() self.shown = false; if self.scripts.OnHide then self.scripts.OnHide(self) end end
function methods:Show() self.shown = true end
function methods:IsShown() return self.shown end
function methods:SetShown(v) if v then self:Show() else self:Hide() end end
function methods:SetEnabled(v) assert(type(v) == "boolean"); self.enabled = v end
function methods:GetWidth() return self.width end
function methods:GetHeight() return self.height end
function methods:SetScale(v) self.scale = v end
function methods:GetEffectiveScale() return self.scale end
function methods:GetFrameLevel() return 5 end
function methods:SetFrameLevel() end
function methods:SetFrameStrata() end
function methods:GetLeft() return 0 end
function methods:GetTop() return 600 end
function methods:SetTexture(file) self.file = file; return true end
function methods:SetTexCoord(...) self.uv = {...} end
function methods:SetRotation(v) self.rotation = v end
function methods:SetTextColor() end
function methods:SetClampedToScreen() end
function methods:SetMovable() end
function methods:EnableMouse() end
function methods:EnableMouseWheel() end
function methods:RegisterForDrag() end
function methods:RegisterForClicks() end
function methods:StartMoving() end
function methods:StopMovingOrSizing() end
function methods:SetAutoFocus() end
function methods:SetMaxLetters() end
function methods:SetFocus() end
function methods:ClearFocus() end
function methods:HighlightText() end
function methods:SetBackdrop() end
function methods:SetScrollChild(v) self.child = v end
function methods:SetHorizontalScroll(v) self.scrollX = v end
function methods:SetVerticalScroll(v) self.scrollY = v end
function methods:RegisterEvent(event) T.events = T.events or {}; T.events[event] = self end
function methods:CreateFontString() return object("FontString") end
function methods:CreateTexture() return object("Texture") end
function methods:Click(mouseButton) if self.enabled and self.scripts.OnClick then self.scripts.OnClick(self,mouseButton or "LeftButton") end end
function CreateFrame(kind, name, parent, template)
    local f = object(kind); T.frames[#T.frames + 1] = f
    if name then _G[name] = f end
    if template == "BasicFrameTemplateWithInset" then f.TitleText = object("FontString") end
    return f
end
UIParent = object("Frame"); UIParent:SetSize(1920, 1080)
UISpecialFrames, SlashCmdList = {}, {}
DEFAULT_CHAT_FRAME = { AddMessage = function(_, msg) T.message = msg end }
GameTooltip = { SetOwner = function() end, SetText = function() end, AddLine = function() end, Show = function() end, Hide = function() end }
WorldMapFrame = CreateFrame("Frame")
function GetLocale() return T.locale end
function time() return T.now end
function GetInstanceInfo() return "Wailing Caverns", T.kind, 1, "Normal", 5, 0, false, T.instance end
function InCombatLockdown() return T.combat end
function GetPlayerFacing() return 0.3 end
function GetCursorPosition() return 100, 400 end
function IsShiftKeyDown() return false end
function IsMouseButtonDown() return false end
T.secret = {}; function issecretvalue(v) return rawequal(v, T.secret) end
Enum = { UIMapType = { Dungeon = 4 } }
C_Map = {
    GetBestMapForUnit = function() return T.best end,
    GetMapInfo = function(id) if id == 9001 or id == 9002 then return { name = "Native " .. id, mapID = id, mapType = 4 } end return { name = "Outdoor", mapID = id, mapType = 3 } end,
    GetMapArtID = function() return 8 end,
    GetMapArtLayers = function() return { { layerWidth = 1002, layerHeight = 668, tileWidth = 256, tileHeight = 256 } } end,
    GetMapArtLayerTextures = function() return { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 } end,
    GetMapGroupID = function() return 77 end,
    GetMapGroupMembersInfo = function() return { { mapID = 9002, relativeHeightIndex = 2 }, { mapID = 9001, relativeHeightIndex = 1 }, { mapID = 9001, relativeHeightIndex = 1 }, { mapID = 20 } } end,
    GetPlayerMapPosition = function() if T.noPos then return nil end return { GetXY = function() return T.px, T.py end } end,
}
