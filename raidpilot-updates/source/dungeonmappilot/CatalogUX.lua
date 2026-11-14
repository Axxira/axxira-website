-- Optional level-based discovery using already authored map brackets.
-- Only labels/sorts suggestions; it never joins a queue or changes a character.
local A=DungeonMapPilot
function A.PlayerDungeonLevel()
    local level=A.Call(UnitLevel,"player")
    if type(level)~="number" or level~=math.floor(level)
        or level<1 or level>60 then return nil end
    return level
end
function A.CatalogLevelRange(id)
    local meta=A.customGuideMeta and A.customGuideMeta[id]
    if type(meta)=="table" and A.Number(meta.min) and A.Number(meta.max) then
        return meta.min,meta.max
    end
    if id==389 then return 13,18 end
    if id==43 then return 15,24 end
    local variants=A.routeVariants and A.routeVariants[id]
    if type(variants)=="table" and #variants>0 then
        local min,max=100,0
        for _,r in ipairs(variants) do
            if type(r)=="table" and type(r.levelMin)=="number" and type(r.levelMax)=="number" then
                min=math.min(min,r.levelMin);max=math.max(max,r.levelMax)
            end
        end
        if min>=1 and max>=min and max<=60 then return min,max end
    end
    return nil
end
function A.CatalogAtLevel(level)
    if type(level)~="number" or level~=math.floor(level) or level<1 or level>60 then return {} end
    local results={}
    for id,info in pairs(A.catalog or {}) do
        local min,max=A.CatalogLevelRange(id)
        if min and max and level>=min and level<=max and not info.raid then
            results[#results+1]={id=id,name=A.CatalogName(id),minimum=min,maximum=max,
                preview=info.previewOnly==true,roster=info.rosterOnly==true}
        end
    end
    table.sort(results,function(a,b)
        if a.minimum~=b.minimum then return a.minimum<b.minimum end
        if a.maximum~=b.maximum then return a.maximum<b.maximum end
        return a.name<b.name
    end)
    return results
end
