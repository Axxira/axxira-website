local A = DungeonMapPilot

function A.RouteText(value)
    if type(value) == "table" then return value[A.Language()] or value.en or "" end
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
    for _, required in ipairs(step.requires or {}) do
        if s.done[required] ~= true then
            for i, other in ipairs(route.steps) do if other.id==required then missing[#missing+1]={index=i,step=other} end end
        end
    end
    return missing
end

function A.RouteSelect(route, index)
    if not A.Number(index) or index < 1 or index > #route.steps then return false end
    A.RouteState(route).current=math.floor(index)
    return true
end

function A.RouteAdvance(route, skip)
    local s = A.RouteState(route)
    local step = route.steps[s.current]
    if not step or (skip and step.kind~="optional") or #A.RouteMissing(route,step)>0 then return false end
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
    A.db.routes[route.id] = nil
    return A.RouteState(route)
end

function A.RouteNumber(index) return index==1 and A.L("departureShort") or tostring(index-1) end

function A.RouteCounts(route)
    local s, done, skipped = A.RouteState(route), 0, 0
    for _, step in ipairs(route.steps) do
        if s.done[step.id] then done=done+1 elseif s.skipped[step.id] then skipped=skipped+1 end
    end
    return done,#route.steps,skipped
end
