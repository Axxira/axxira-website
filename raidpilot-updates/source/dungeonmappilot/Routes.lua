local A = DungeonMapPilot

function A.RouteText(value)
    if type(value) == "table" then
        if value.localeKey and A.HasText(value.localeKey) then return A.L(value.localeKey) end
        local language = A.Language()
        return value[language] or value[language == "frFR" and "fr" or "en"] or ""
    end
    return A.SafeLabel(value)
end

function A.GetRoute(context)
    if not context or context.source ~= "reference" then return nil end
    local id = context.instanceID
    if A.db.routeMode[id] ~= "personal" and A.routeData[id] then return A.routeData[id] end
    local route = {id="personal:"..id, personal=true, steps={}}
    local revision = {}
    for floorIndex, floor in ipairs(context.floors) do
        for i, pin in ipairs(A.Pins(floor.key)) do
            route.steps[#route.steps+1] = {id=floorIndex..":"..i, floor=floorIndex, pinIndex=i, x=pin.x,y=pin.y,
                title=pin.label, body=A.L("personalRouteBody"), kind=#route.steps == 0 and "start" or "waypoint"}
            revision[#revision+1] = floor.key..":"..pin.x..":"..pin.y..":"..pin.label
        end
    end
    if #route.steps == 0 then return nil end
    route.revision = table.concat(revision,";")
    return route
end

function A.RouteState(route)
    local s = A.db.routes[route.id]
    if type(s) ~= "table" or s.revision ~= route.revision then
        s = {revision=route.revision, current=1, done={}, skipped={}}
        A.db.routes[route.id] = s
    end
    s.history = type(s.history)=="table" and s.history or {}
    while #s.history>20 do table.remove(s.history,1) end
    s.done = type(s.done)=="table" and s.done or {}
    s.skipped = type(s.skipped)=="table" and s.skipped or {}
    for _, step in ipairs(route.steps) do
        if s.done[step.id] ~= true then s.done[step.id] = nil end
        if s.skipped[step.id] ~= true or step.kind ~= "optional" or s.done[step.id] then s.skipped[step.id] = nil end
    end
    s.current = A.Clamp(math.floor(A.Number(s.current) or 1),1,#route.steps+1)
    return s
end

function A.RouteMissing(route, step)
    local s, missing = A.RouteState(route), {}
    if step.requires~=nil and type(step.requires)~="table" then return {{step={title=A.L("routeInvalidRequirement")},invalid=true}} end
    local requirements, count, seen = step.requires or {}, 0, {}
    for key,required in pairs(requirements) do
        count=count+1
        if type(key)~="number" or key%1~=0 or key<1 or key>128
            or (type(required)~="string" and type(required)~="number")
            or (type(required)=="number" and (not A.ID(required)))
            or (type(required)=="string" and required=="")
            or seen[required] then
            return {{step={title=A.L("routeInvalidRequirement")},invalid=true}}
        end
        seen[required]=true
    end
    for index=1,count do
        if requirements[index]==nil then return {{step={title=A.L("routeInvalidRequirement")},invalid=true}} end
    end
    for _, required in ipairs(requirements) do
        local found, index
        for i, other in ipairs(route.steps) do
            if other.id==required then
                if found then return {{step={title=A.L("routeInvalidRequirement")},invalid=true}} end
                found,index=other,i
            end
        end
        -- Validate identity even if corrupt saved data claims it was completed.
        if not found or required==step.id then
            missing[#missing+1]={step={title=A.L("routeInvalidRequirement")},invalid=true}
        elseif s.done[required]~=true then missing[#missing+1]={index=index,step=found} end
    end
    return missing
end

function A.RouteSelect(route, index)
    if not A.Number(index) or index < 1 or index > #route.steps then return false end
    A.RouteState(route).current=math.floor(index)
    return true
end

local function snapshot(route,s)
    local out={revision=route.revision,current=s.current,done={},skipped={}}
    for _,step in ipairs(route.steps) do
        if s.done[step.id] then out.done[step.id]=true end
        if s.skipped[step.id] then out.skipped[step.id]=true end
    end
    return out
end
local function checkpoint(route,s)
    s.history[#s.history+1]=snapshot(route,s)
    while #s.history>20 do table.remove(s.history,1) end
end
function A.RouteCanUndo(route)
    local s=A.RouteState(route);local previous=s.history[#s.history]
    return type(previous)=="table" and previous.revision==route.revision
        and type(previous.done)=="table" and type(previous.skipped)=="table" and A.Number(previous.current)~=nil
end
function A.RouteUndo(route)
    if not A.RouteCanUndo(route) then return false end
    local s=A.RouteState(route);local previous=table.remove(s.history)
    -- Copy booleans only for the current route; reject unrelated saved fields.
    s.current=A.Clamp(math.floor(previous.current),1,#route.steps+1);s.done={};s.skipped={}
    for _,step in ipairs(route.steps) do
        if previous.done[step.id]==true then s.done[step.id]=true
        elseif step.kind=="optional" and previous.skipped[step.id]==true then s.skipped[step.id]=true end
    end
    return true
end
function A.RouteAdvance(route, skip)
    local s = A.RouteState(route)
    local step = route.steps[s.current]
    if not step or (skip and step.kind~="optional") or #A.RouteMissing(route,step)>0 then return false end
    checkpoint(route,s)
    s.done[step.id] = not skip or nil
    s.skipped[step.id] = skip or nil
    local nextIndex = s.current + 1
    while route.steps[nextIndex] and (s.done[route.steps[nextIndex].id] or s.skipped[route.steps[nextIndex].id]) do nextIndex=nextIndex+1 end
    if nextIndex > #route.steps then
        for i, other in ipairs(route.steps) do
            if not s.done[other.id] and not s.skipped[other.id] then nextIndex=i; break end
        end
    end
    s.current=nextIndex
    return true
end

function A.RouteRestart(route)
    local s=A.RouteState(route)
    checkpoint(route,s)
    s.current=1;s.done={};s.skipped={}
    return s
end

function A.RouteNumber(index) return index==1 and A.L("departureShort") or tostring(index-1) end

function A.RouteCounts(route)
    local s, done, skipped = A.RouteState(route), 0, 0
    for _, step in ipairs(route.steps) do
        if s.done[step.id] then done=done+1 elseif s.skipped[step.id] then skipped=skipped+1 end
    end
    return done,#route.steps,skipped
end
