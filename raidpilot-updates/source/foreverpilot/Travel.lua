local B=ForeverPilotLeveling
local T={};B.Travel=T
function T:World(p)
    if not p or not p.map or not p.x or not p.y then return nil end
    if p.x<0 or p.x>100 or p.y<0 or p.y>100 then return nil end
    if not p.continent then p.continent,p.wx,p.wy=Ding.Compat.MapToWorld(p.map,p.x,p.y) end
    return p.continent
end
function T:Distance(a,b)
    if not a or not b then return nil end
    self:World(a);self:World(b)
    if a.continent and a.continent==b.continent and a.wx and b.wx then
        return math.sqrt((a.wx-b.wx)^2+(a.wy-b.wy)^2)
    end
end
function T:Nearest(points,s)
    local best,dist
    for _,p in ipairs(points or {}) do
        if not p.dungeon and self:World(p) then
            local d=self:Distance(p,s)
            local cost=d or (p.map==s.map and 10000 or 50000)
            if not best or cost<dist then best,dist=p,cost end
        end
    end
    return best,dist
end
function T:Seconds(p,s)
    local seconds=self.reach and self.reach:Seconds(p)
    if seconds then return seconds end
    local d=self:Distance(p,s)
    return d and d/7 or 1800
end
function T:Reachable(p,s)
    -- Reach:Seconds explicitly returns nil when no known transport connects the
    -- destination. Never replace that answer with a fictional 30-minute route.
    if self.reach then
        local seconds=self.reach:Seconds(p)
        return type(seconds)=="number" and seconds>=0 and seconds<math.huge
    end
    -- Without a transport graph, only a known local position can be guided.
    return p and p.map==s.map and self:Distance(p,s)~=nil or false
end
function T:Between(a,b,s,first)
    if first then return self:Seconds(b,s) end
    local d=self:Distance(a,b)
    -- Future legs do not reuse a hearthstone/flight from the player's current
    -- location. Walking distance is an estimate, not obstacle pathfinding.
    return d and d/7 or 1800
end
function T:Prepare(s)
    if not self.at or s.now-self.at>15 or self.map~=s.map then
        self.reach=B.Call(Ding.Travel.Reach,Ding.Travel);self.at=s.now;self.map=s.map
    end
end
function T:Leg(p)
    return p and B.Call(Ding.Travel.Leg,Ding.Travel,p)
end
