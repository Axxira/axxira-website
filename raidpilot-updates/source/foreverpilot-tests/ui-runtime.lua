-- Minimal strict WoW UI surface. Captures the actual UI.lua layout, rather than
-- maintaining a separate mockup. Unsupported method names fail the test.
TEST.regions={};TEST.frames={}
SlashCmdList={}
local methods={}
local function create(kind,name,parent)
 local o=setmetatable({kind=kind,name=name,parent=parent,shown=true,scripts={}},{__index=methods})
 if kind=='Frame' then TEST.frames[#TEST.frames+1]=o else TEST.regions[#TEST.regions+1]=o end
 return o
end
function CreateFrame(kind,name,parent) return create('Frame',name,parent) end
UIParent=CreateFrame('Frame','UIParent');STANDARD_TEXT_FONT='Friz Quadrata'
function methods:CreateTexture() return create('Texture',nil,self) end
function methods:CreateFontString() return create('FontString',nil,self) end
function methods:SetSize(w,h) self.w=w;self.h=h end
function methods:SetWidth(w) self.w=w end
function methods:SetHeight(h) self.h=h end
function methods:SetPoint(anchor,parent,relative,x,y) self.x=x;self.y=-y;self.anchor=anchor end
function methods:GetPoint() return self.anchor,nil,nil,self.x,-self.y end
function methods:SetFont(name,size,flags) self.font=name;self.fontSize=size;self.flags=flags end
function methods:SetText(text) self.text=text end
function methods:SetAutoFocus(value) self.autoFocus=value end
function methods:SetFontObject(value) self.fontObject=value end
function methods:SetFocus() self.focused=true end
function methods:ClearFocus() self.focused=false end
function methods:HighlightText() self.highlighted=true end
function methods:SetTextColor(r,g,b,a) self.color={r,g,b,a or 1} end
function methods:SetColorTexture(r,g,b,a) self.fill={r,g,b,a or 1} end
function methods:SetTexture(path) self.texture=path end
function methods:SetAllPoints(parent) self.allPoints=parent end
function methods:SetFrameStrata(strata) self.strata=strata end
function methods:SetFrameLevel(level) self.frameLevel=level end
function methods:GetFrameLevel() return self.frameLevel or 1 end
function methods:SetTexCoord(...) self.uv={...} end
function methods:SetRotation(angle) self.rotation=angle end
function methods:SetJustifyH(value) self.align=value end
function methods:SetJustifyV(value) self.valign=value end
function methods:SetWordWrap(value) self.wrap=value end
function methods:SetShown(value) self.shown=value end
function methods:IsShown() return self.shown end
function methods:Show() self.shown=true end
function methods:Hide() self.shown=false end
function methods:SetScale(v) self.scale=v end
function methods:SetScript(event,fn) self.scripts[event]=fn end
function methods:SetEnabled(value) self.enabled=value end
function methods:HookScript(event,fn)
 local previous=self.scripts[event]
 self.scripts[event]=function(...) if previous then previous(...) end;return fn(...) end
end
function methods:RegisterEvent(event) self.events=self.events or {};self.events[event]=true end
function methods:SetMovable(v) self.movable=v end
function methods:EnableMouse(v) self.mouse=v end
function methods:RegisterForDrag() end
function methods:RegisterForClicks(...) self.clicks={...} end
function methods:SetClampedToScreen(v) self.clamped=v end
function methods:StartMoving() end
function methods:StopMovingOrSizing() end
GameTooltip={SetOwner=function() end,SetText=function() end,AddLine=function() end,Show=function() end,Hide=function() end}
Ding.db.panel={shown=true};Ding.db.arrow={shown=true}
Ding.Panel={frame=CreateFrame('Frame'),Update=function() end,Toggle=function() end}
Ding.Arrow={frame=CreateFrame('Frame'),Tick=function() end,MarkWorld=function() end,Bearing=function(dx,dy) return math.atan(-dx,-dy) end}
Ding.AutoQuest={Wanted=function() return {},{},{} end,OnQuestList=function() end}
Ding.Compat.GetFacing=function() return 0 end
Ding.Compat.UserWaypoint=function() local p=TEST.waypoint;if p then return p.map,p.x,p.y end end
Ding.Compat.SetUserWaypoint=function(map,x,y) TEST.waypoint={map=map,x=x,y=y};return true end
Ding.Compat.ClearUserWaypoint=function() TEST.waypoint=nil end
Ding.Compat.DialogQuestID=function() return TEST.dialogQuest end
UI_SNAPSHOTS={}
function CaptureUI(label)
 local rows={}
 for _,o in ipairs(TEST.regions) do
  if o.parent==ForeverPilotLeveling.UI.frame then
   local row={};for _,k in ipairs({'kind','x','y','w','h','fontSize','text','color','fill','texture','uv','rotation','align','wrap','shown'}) do row[k]=o[k] end
   rows[#rows+1]=row
  end
 end
 UI_SNAPSHOTS[#UI_SNAPSHOTS+1]={label=label,width=ForeverPilotLeveling.UI.width,height=ForeverPilotLeveling.UI.height,regions=rows}
end
