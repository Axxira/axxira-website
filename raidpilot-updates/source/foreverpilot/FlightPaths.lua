-- Nearby discovery only. Arrival is never proof that a flight was learned.
local B=ForeverPilotLeveling
local F={revision=0};B.FlightPaths=F
local function known(npc,s)
    return (s.flights and s.flights[npc]) or B.char.flightDiscoveries[npc]
end
function F:Nearby(s)
    local c=self.cache
    local moved=c and B.Travel:Distance(c.origin,s)
    if not c or c.map~=s.map or s.now-c.at>=8 or (moved and moved>50) then
        c={map=s.map,at=s.now,origin={map=s.map,x=s.x,y=s.y,continent=s.continent,wx=s.wx,wy=s.wy},list=B.Call(Ding.Services and Ding.Services.Nearest,Ding.Services,"flight",8) or {},nodes={}}
        self.cache=c
        local nodes=B.Call(C_TaxiMap and C_TaxiMap.GetTaxiNodesForMap,s.map)
        local faction=s.faction=="Horde" and 1 or s.faction=="Alliance" and 2
        for _,node in ipairs(type(nodes)=="table" and nodes or {}) do
            local pos=B.Public(node.position);local side=B.Public(node.faction)
            local x=type(pos)=="table" and B.Public(pos.x);local y=type(pos)=="table" and B.Public(pos.y)
            local unknown=B.Public(node.isUndiscovered)
            if faction and (side==0 or side==faction) and type(x)=="number" and type(y)=="number" and type(unknown)=="boolean" then
                c.nodes[#c.nodes+1]={point={map=s.map,x=x*100,y=y*100},unknown=unknown}
            end
        end
    end
    return c
end
function F:Status(npc,s,cache)
    if known(npc.npc,s) then return "known" end
    local best,range
    for _,node in ipairs(cache.nodes) do
        local d=B.Travel:Distance(npc.point,node.point)
        if d and d<=100 and (not range or d<range) then best=node;range=d end
    end
    if best then return best.unknown and "undiscovered" or "known" end
    return "unverified"
end
function F:Choose(s,quest)
    if s.combat or s.dead or s.taxi or B.Call(IsInInstance) or not s.map or not Ding.Services then return quest end
    local cache=self:Nearby(s);local candidates={}
    local old=B.Planner.current
    for _,npc in ipairs(cache.list) do
        local key="service:flight:"..npc.npc
        local d=B.Travel:Distance(npc.point,s)
        local retained=old and old.key==key
        local status=self:Status(npc,s,cache)
        if status~="known" and d and d<=(retained and 450 or 320) and not npc.point.dungeon
            and not (B.char.deferred[key] and B.char.deferred[key]>time()) and B.Travel:Reachable(npc.point,s) then
            candidates[#candidates+1]={npc=npc,key=key,distance=d,status=status,retained=retained}
        end
    end
    table.sort(candidates,function(a,b)
        if not not a.retained~=not not b.retained then return not not a.retained end
        if a.distance==b.distance then return a.npc.npc<b.npc.npc end
        return a.distance<b.distance
    end)
    local pick=candidates[1];if not pick then return quest end
    -- Finish a conversation/reward already within reach, then learn the local
    -- flight before leaving. Do not replace an ongoing combat objective mid-fight.
    local direct=quest and quest.point and B.Travel:Distance(quest.point,s)
    if direct and direct<20 and (quest.kind=="accept" or quest.kind=="turnin") then return quest end
    return {kind="service",service="flight",key=pick.key,flightNPC=pick.npc.npc,walkOnly=true,
        label=pick.status=="undiscovered" and B.T("Débloque ce trajet aérien") or B.T("Vérifie ce trajet aérien"),
        note=B.T("Parle à ")..(pick.npc.name or B.T("ce maître de vol")),point=pick.npc.point,
        confidence=pick.status=="undiscovered" and "LIVE / POSITION DB" or "DATABASE",
        batch={},plan=quest and quest.plan,tripIDs=quest and quest.tripIDs}
end
function F:Event(event)
    self.cache=nil;self.revision=self.revision+1;B.Travel.at=nil;B.Planner.signature=nil;B.dirty=true
    if event~="TAXIMAP_OPENED" then return end
    local s=B.State:Refresh();local id=B.Call(Ding.Compat.NpcUnitID)
    local matches={}
    for _,npc in ipairs(self:Nearby(s).list) do
        local d=B.Travel:Distance(npc.point,s)
        if d and d<=60 and (not id or id==npc.npc) then matches[#matches+1]=npc end
    end
    if #matches~=1 then return end -- no guessing across ambiguous nearby NPCs
    local npc=matches[1]
    local fresh=not B.char.flightDiscoveries[npc.npc]
    B.char.flightDiscoveries[npc.npc]=true
    if fresh then B.Trace("flightLearned",npc.npc) end
end
