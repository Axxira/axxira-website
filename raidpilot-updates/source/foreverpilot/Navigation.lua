local B=ForeverPilotLeveling
local N={};B.Navigation=N
local function same(a,map,x,y)
    return a and a.map==map and x and y and math.abs(a.x-x)<0.1 and math.abs(a.y-y)<0.1
end
function N:Mark(point,force)
    if B.settings.paused and not force then return end
    local C=Ding.Compat
    local key=point and string.format("%d:%.2f:%.2f",point.map,point.x,point.y) or "none"
    if key==self.key then return end
    local map,x,y=C.UserWaypoint()
    local mine=B.char.waypoint
    -- A manually placed player waypoint always wins.
    if map and not same(mine,map,x,y) then return end
    self.key=key
    if not point then if mine and map then C.ClearUserWaypoint() end;B.char.waypoint=nil;return end
    if C.SetUserWaypoint(point.map,point.x,point.y) then
        B.char.waypoint={map=point.map,x=point.x,y=point.y}
    end
end
function N:Install()
    if self.installed then return end
    self.originalTick=Ding.Arrow.Tick;self.installed=true
    -- Stop the hidden static arrow from writing a contradictory map target or travel plan.
    Ding.Arrow:MarkWorld(nil)
    Ding.Arrow.Tick=function(arrow,...)
        if B.settings.enabled then return end
        return N.originalTick(arrow,...)
    end
end
function N:Restore()
    if self.originalTick then Ding.Arrow.Tick=self.originalTick end
    self.installed=false
    self.key=nil;self:Mark(nil,true)
end
