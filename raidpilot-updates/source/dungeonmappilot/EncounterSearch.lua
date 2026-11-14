-- Search encounter rosters across the whole map and route catalog.
-- No player data, route completion, inventory or map coordinates are modified here.
local A=DungeonMapPilot
local accent={
 ["é"]="e",["è"]="e",["ê"]="e",["ë"]="e",["É"]="e",["È"]="e",["Ê"]="e",
 ["à"]="a",["â"]="a",["ä"]="a",["À"]="a",["Â"]="a",
 ["ô"]="o",["ö"]="o",["Ô"]="o",["ù"]="u",["û"]="u",["ü"]="u",
 ["ï"]="i",["î"]="i",["ç"]="c",["Ç"]="c",["œ"]="oe"}
local function normalize(value)
    if type(value)~="string" then return "" end
    for before,after in pairs(accent) do value=value:gsub(before,after) end
    return value:lower():gsub("[%z\1-\31]"," "):gsub("%p",""):gsub("%s+"," ")
end
function A.EncounterSearch(term,maxResults)
    term=A.Public(term)
    if type(term)~="string" or #term>95 then return {},false end
    local query=normalize(term):gsub("^%s+",""):gsub("%s+$","")
    if #query<2 then return {},false end
    maxResults=A.ID(maxResults) and math.min(maxResults,40) or 24
    local result,seen={},{}
    local function append(instanceID,variant,wing,step,index)
        if step.kind=="start" or step.kind=="note" then return end
        local dungeon=A.CatalogName(instanceID)
        local boss=A.RouteText(step.title)
        local entry=A.catalog and A.catalog[instanceID] or {}
        local full=normalize(dungeon.." "..(entry.en or "").." "..(entry.fr or "")..
            " "..boss.." "..A.RouteText(variant.name or ""))
        if not full:find(query,1,true) then return end
        local key=variant.id..":"..tostring(index)
        if seen[key] then return end
        seen[key]=true
        result[#result+1]={id=instanceID,routeID=variant.id,variantIndex=wing,
            stepIndex=index,kind=step.kind,boss=boss,dungeon=dungeon,
            guideOnly=variant.guideOnly==true,rosterOnly=variant.rosterOnly==true,
            optional=step.kind=="optional"}
    end
    for id in pairs(A.catalog or {}) do
        local routes=A.routeVariants and A.routeVariants[id]
        if type(routes)~="table" or #routes==0 then
            local legacy=A.routeData and A.routeData[id]
            if legacy then routes={legacy} end
        end
        for wing,route in ipairs(routes or {}) do
            for index,step in ipairs(route.steps or {}) do
                append(id,route,wing,step,index)
            end
        end
    end
    table.sort(result,function(x,y)
        local a,b=normalize(x.boss),normalize(y.boss)
        if a~=b then return a<b end
        if x.dungeon~=y.dungeon then return x.dungeon<y.dungeon end
        if x.id~=y.id then return x.id<y.id end
        if x.variantIndex~=y.variantIndex then return x.variantIndex<y.variantIndex end
        return x.stepIndex<y.stepIndex
    end)
    local truncated=#result>maxResults
    while #result>maxResults do table.remove(result) end
    return result,truncated
end
