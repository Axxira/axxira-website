-- FastXP navigation: prefer the game's quest marker for the SAME active quest.
-- C_QuestLog APIs can return nil on some Forever builds. Never invent points,
-- take another quest's marker, or override a player's manual waypoint.
local B=ForeverPilotLeveling
local M={};B.QuestMarker=M
local function number(v)
    v=B.Public(v)
    return type(v)=="number" and v==v and v>-math.huge and v<math.huge and v or nil
end
local function coords(map,x,y,source)
    map,x,y=number(map),number(x),number(y)
    if not (map and map>0 and map%1==0 and x and y) then return nil end
    -- Blizzard's QuestLog map values are normalized [0,1]. Reject invalid
    -- coordinates rather than misinterpreting them as an arbitrary spot.
    if x<0 or x>1 or y<0 or y>1 then return nil end
    return {map=map,x=x*100,y=y*100,source=source,blizzardQuestMarker=true}
end
local function logQuest(q,state)
    if type(q)~="number" or q<=0 or q%1~=0 then return false end
    if state and state.log then return state.log[q]~=nil end
    return Ding and Ding.Compat and Ding.Compat.IsInLog
        and B.Call(Ding.Compat.IsInLog,q)==true or false
end
local function find(q,map)
    local api=C_QuestLog
    if type(api)~="table" then return nil end
    if type(api.GetNextWaypoint)=="function" then
        local m,x,y=B.Call(api.GetNextWaypoint,q)
        local p=coords(m,x,y,"quest-next-waypoint")
        if p then return p end
    end
    if map and type(api.GetNextWaypointForMap)=="function" then
        local x,y=B.Call(api.GetNextWaypointForMap,q,map)
        local p=coords(map,x,y,"quest-waypoint-on-map")
        if p then return p end
    end
    if map and type(api.GetQuestsOnMap)=="function" then
        local rows=B.Call(api.GetQuestsOnMap,map)
        if type(rows)=="table" then
            local found
            for _,row in ipairs(rows) do
                if type(row)=="table" and number(row.questID)==q and
                    B.Public(row.isQuestStart)~=true then
                    local m=number(row.mapID) or map
                    local p=coords(m,row.x,row.y,"quest-map-pin")
                    if p then
                        if found then return nil end -- multiple pins: ambiguous
                        found=p
                    end
                end
            end
            if found then return found end
        end
    end
end
function M:Point(action,state,now)
    if type(action)~="table" or (action.kind~="complete" and
        action.kind~="turnin") or not logQuest(action.q,state) then return nil end
    now=number(now) or (GetTime and B.Call(GetTime)) or 0
    local q=action.q
    local map=number(action.point and action.point.map)
    if not map and C_Map and type(C_Map.GetBestMapForUnit)=="function" then
        map=B.Call(C_Map.GetBestMapForUnit,"player")
    end
    local key=tostring(q)..":"..action.kind..":"..tostring(map or "")
    if key==self.key and self.nextAt and now<self.nextAt then return self.last end
    local result=find(q,map)
    if not result and self.key==key and self.last and
        self.lastOkAt and now-self.lastOkAt<3 then result=self.last end
    self.key=key;self.nextAt=now+2;self.last=result
    if result then self.lastOkAt=now end
    return result
end
function M:Direction(point)
    local C=Ding and Ding.Compat
    if not C or type(C.MapToWorld)~="function" or
        type(C.GetPlayerWorld)~="function" then return nil end
    local pc,px,py=B.Call(C.GetPlayerWorld)
    local c,x,y=B.Call(C.MapToWorld,point.map,point.x,point.y)
    if not (number(pc) and c==pc and number(px) and number(py) and
        number(x) and number(y)) then return nil end
    local dx,dy=x-px,y-py
    local dist=math.sqrt(dx*dx+dy*dy)
    local facing=C.GetFacing and B.Call(C.GetFacing)
    if not number(facing) or not (Ding.Arrow and Ding.Arrow.Bearing) then
        return dist,nil
    end
    local bearing=B.Call(Ding.Arrow.Bearing,dx,dy)
    return dist,number(bearing) and (bearing-facing) or nil
end
-- Native routing may be walking to a flight master, zeppelin, boat, etc.
-- Never point across the continent instead of showing a transport leg.
function M:CanOverrideTravel(core)
    local t=core and core.Nav and core.Nav.travel
    if not t then return true end
    local active=t.Active and B.Call(t.Active)
    local leg=active and t.CurrentLeg and B.Call(t.CurrentLeg)
    return not (type(leg)=="table" and leg.kind~="walk")
end
function M:Tick(ui,action,state,core,now)
    if not self:CanOverrideTravel(core) then return false end
    local point=self:Point(action,state,now)
    if not point then return false end
    local dist,angle=self:Direction(point)
    if not number(dist) then return false end
    ui:Symbol("Interface\\AddOns\\Ding\\Media\\Textures\\arrow_halo_gold")
    if number(angle) then ui.arrow:SetRotation(angle) end
    ui.distance:SetText(dist>=1000 and string.format(B.T("%.1f k yd"),dist/1000)
        or string.format(B.T("%.0f yd"),dist))
    ui.routeHint=B.T("Repère de quête du jeu · ")..tostring(point.source)
    return true
end
function M:Reset() self.key=nil;self.nextAt=nil;self.last=nil;self.lastOkAt=nil end
