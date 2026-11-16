-- FastXP map route: reveal only the bundled engine's route drawing.
-- The same quest ID and Blizzard coordinates feed its optimized travel graph.
-- Never create a second quest pin, overwrite a manual target, or complete a quest.
local B=ForeverPilotLeveling
local M={};B.MapRoute=M

local function finite(v)
    return type(v)=="number" and v==v and v<math.huge and v>-math.huge
end
local function valid(p)
    return type(p)=="table" and finite(p.map) and p.map>0 and p.map%1==0
        and finite(p.x) and finite(p.y) and p.x>=0 and p.x<=100 and p.y>=0 and p.y<=100
end
local function equal(a,b)
    return a and b and a.map==b.map and math.abs(a.x-b.x)<0.0001
        and math.abs(a.y-b.y)<0.0001 and not b.f
end
function M:Runtime()
    return B.EmbeddedEngine and B.EmbeddedEngine:Runtime() or nil
end
function M:Visible(ns,on)
    if not (ns and type(ns.Get)=="function" and type(ns.Set)=="function") then return end
    on=on and true or false
    if B.Call(ns.Get,"nav.fastxpRouteOverlay")~=on then
        B.Call(ns.Set,"nav.fastxpRouteOverlay",on)
    end
end
function M:Reset(ns)
    ns=ns or self:Runtime()
    local nav=ns and ns.Nav
    if self.owned and nav and type(nav.GetTarget)=="function"
        and B.Call(nav.GetTarget)==self.owned and
        type(nav.IsOverride)=="function" and B.Call(nav.IsOverride)==true then
        B.Call(nav.ReleaseTarget)
    end
    self.owned=nil;self.key=nil
end
function M:Sync(a,s)
    local ns=self:Runtime()
    local active=ns and B.settings and B.settings.enabled and
        B.settings.mapRoute~=false and not B.settings.paused and
        B.Mode()=="fast" and a and a.embedded and s and not s.dead and not s.taxi
    self:Visible(ns,active)
    if not active then self:Reset(ns);return end
    local nav=ns.Nav
    if not (nav and type(nav.GetTarget)=="function" and
        type(nav.SetTarget)=="function" and type(nav.IsOverride)=="function") then
        self:Reset(ns);return
    end
    -- A new guide step may arrive before FastXP refreshes its HUD.
    local e=ns.Engine
    local index=e and e.GetLiveStep and B.Call(e.GetLiveStep)
    if not index or index~=a.routeIndex then self:Reset(ns);return end
    local guide=e.GetGuide and B.Call(e.GetGuide)
    if not guide or guide.id~=a.routeGuideId then self:Reset(ns);return end
    -- Never override a player-selected destination or a death/corpse hold.
    if type(nav.IsHeld)=="function" and B.Call(nav.IsHeld)==true then return end
    local current=B.Call(nav.GetTarget)
    if self.owned and current~=self.owned then self.owned=nil;self.key=nil end
    if B.Call(nav.IsOverride)==true and not self.owned then return end
    local marker=a.q and (a.kind=="complete" or a.kind=="turnin") and
        B.QuestMarker and B.QuestMarker:Point(a,s,B.Now())
    if not (marker and marker.blizzardQuestMarker and valid(marker)) then
        self:Reset(ns)
        return
    end
    local key=tostring(a.routeGuideId)..":"..tostring(a.routeIndex)..":"..
        tostring(a.q)..":"..tostring(a.kind)..":"..
        tostring(marker.map)..":"..string.format("%.4f:%.4f",marker.x,marker.y)
    if self.owned and self.key==key and current==self.owned then return end
    if not self.owned and B.Call(nav.IsOverride)~=true and equal(marker,current) then
        self.key=key
        return -- native engine is already aiming exactly at the quest pin
    end
    if self.owned then self:Reset(ns) end
    local t=B.Call(nav.SetTarget,marker.map,marker.x,marker.y,a.label or "FastXP Quest")
    if t and B.Call(nav.GetTarget)==t then
        self.owned=t;self.key=key
    end
end
function M:Status()
    local ns=self:Runtime()
    local nav=ns and ns.Nav
    local geometry=nav and nav.GetRouteGeometry and B.Call(nav.GetRouteGeometry)
    local legs=geometry and geometry.legs
    local count=type(legs)=="table" and #legs or 0
    local estimated=false
    for _,leg in ipairs(type(legs)=="table" and legs or {}) do
        if leg.straight then estimated=true end
    end
    return {enabled=ns and B.Call(ns.Get,"nav.fastxpRouteOverlay")==true or false,
        legs=count,estimated=estimated,pinSynced=self.owned~=nil}
end
