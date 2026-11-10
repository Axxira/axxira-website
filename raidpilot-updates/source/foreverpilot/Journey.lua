-- A character's outing has three phases: collect, work, return. Only the live
-- quest log confirms pickup/readiness; unavailable actions never lock a phase.
local B=ForeverPilotLeveling
local J={};B.Journey=J
local function distance(a,b) return a and b and B.Travel:Distance(a,b) or math.huge end
local function nearest(list)
    table.sort(list,function(a,b)
        if not not a.classPriority~=not not b.classPriority then return not not a.classPriority end
        if a.travel~=b.travel then return a.travel<b.travel end
        return a.key<b.key
    end)
    return list[1]
end
local function retain(list)
    local chosen=nearest(list);local old=B.Planner.current
    if old then for _,a in ipairs(list) do
        if a.key==old.key and (not chosen.classPriority or a.classPriority) then return a end
    end end
    return chosen
end
function J:Reset() B.char.trip=nil end
function J:OrderWork(list,trip,s)
    if #list<2 or #list>25 then return retain(list) end
    -- Class work keeps its own decision tier. Otherwise compare closed outings,
    -- not just the distance to the first objective. Bound the search to one
    -- greedy continuation per possible first stop; the nearest route is included.
    local eligible={};local hasClass=false
    for _,a in ipairs(list) do if a.classPriority then hasClass=true end end
    for _,a in ipairs(list) do if not hasClass or a.classPriority then eligible[#eligible+1]=a end end
    table.sort(eligible,function(a,b) return a.key<b.key end)
    if #eligible<2 then return eligible[1] end
    local edge,home={},{}
    for i,a in ipairs(eligible) do
        edge[i]={}
        local d=distance(a.point,trip.hub)
        if d==math.huge then return retain(list) end
        home[i]=d/7
        for j,b in ipairs(eligible) do
            local between=distance(a.point,b.point)
            if between==math.huge then return retain(list) end
            edge[i][j]=between/7
        end
    end
    local costs={};local best,bestCost
    for first,a in ipairs(eligible) do
        local used={[first]=true};local last=first;local cost=a.travel
        for _=2,#eligible do
            local nextIndex,nextCost
            for j in ipairs(eligible) do if not used[j] and (not nextCost or edge[last][j]<nextCost) then nextIndex,nextCost=j,edge[last][j] end end
            used[nextIndex]=true;cost=cost+nextCost;last=nextIndex
        end
        cost=cost+home[last];costs[a.key]=cost
        if not bestCost or cost<bestCost then best,bestCost=a,cost end
    end
    local old=B.Planner.current
    if old and costs[old.key] then
        for _,a in ipairs(eligible) do if a.key==old.key then
            -- Keep a current nearby objective and small differences. Switch only
            -- for a material estimated saving, and never mid-combat.
            if s.combat or distance(a.point,s)<=60 or costs[a.key]-bestCost<math.max(20,bestCost*0.08) then return a end
        end end
    end
    return best
end
function J:Zone(a,s)
    if a.kind=="complete" then return a.point and a.point.map end
    if a.kind=="accept" then
        for _,t in ipairs(B.Graph:Locations(a.node,"complete")) do
            local p=B.Travel:Nearest(B.Planner:SafePoints(t,s,a.q),s)
            if p then return p.map end
        end
        return a.point and a.point.map
    end
    return s.map
end
function J:LocalPickup(a,trip,s)
    if a.kind~="accept" or a.node.manual or a.point.map~=trip.hub.map
        or distance(a.point,trip.hub)>700 then return false end
    local targets=B.Graph:Locations(a.node,"complete")
    -- The selected quest can still be investigated, and class work keeps its
    -- priority. Do not fill the journal with speculative "local" errands when
    -- no objective establishes that they belong to this zone's outing.
    if #targets==0 and not a.classPriority then return false end
    for _,t in ipairs(targets) do
        local localPoint=false
        for _,p in ipairs(B.Planner:SafePoints(t,s,a.q)) do
            if p.map==trip.zone and B.Travel:Reachable(p,s) then localPoint=true;break end
        end
        if not localPoint then return false end
    end
    return true
end
function J:Start(winner,actions,s)
    if not winner or not winner.q then return end
    local hub=winner.kind=="accept" and winner.point or {map=s.map,x=s.x,y=s.y}
    local phase=winner.kind=="accept" and "collect" or "work"
    if phase=="work" then
        local nearby={}
        for _,a in ipairs(actions) do if a.kind=="accept" and distance(a.point,s)<=120 then nearby[#nearby+1]=a end end
        local pickup=nearest(nearby)
        if pickup then hub=pickup.point;phase="collect" end
    end
    B.char.trip={ids={winner.q},accepted={},at=time(),level=s.level,zone=self:Zone(winner,s),
        hub=hub,phase=phase,schema=2}
end
function J:Choose(winner,actions,s)
    local byQuest={};for _,a in ipairs(actions) do if a.q then byQuest[a.q]=a end end
    local trip=B.char.trip
    if trip then
        trip.accepted=trip.accepted or {};trip.level=s.level
        -- Migrate previous outings without resetting character history.
        if trip.schema~=2 then
            trip.zone=s.map;trip.hub={map=s.map,x=s.x,y=s.y};trip.phase="work";trip.schema=2
            for _,q in ipairs(trip.ids) do
                local a=byQuest[q]
                if a and a.kind=="accept" and a.travel<=300 then
                    trip.phase="collect";trip.hub=a.point;trip.zone=self:Zone(a,s);break
                end
            end
        end
        for i=#trip.ids,1,-1 do
            local q=trip.ids[i];local a=byQuest[q]
            local abandoned=trip.accepted[q] and not s.log[q] and not s:IsDone(q)
            if abandoned then B.char.deferred['accept:'..q]=time()+300;byQuest[q]=nil end
            local remotePickup=a and a.kind=="accept" and not a.classPriority and a.travel>300
                and winner and winner.q~=q and winner.travel<=180
            local otherZone=a and a.kind=="complete" and a.point.map~=trip.zone
            if s:IsDone(q) or not a or abandoned or remotePickup or otherZone then table.remove(trip.ids,i) end
        end
        if #trip.ids==0 then self:Reset();trip=nil end
    end
    if not trip then
        if winner and not byQuest[winner.q] then
            local remaining={};for _,a in ipairs(actions) do if byQuest[a.q] then remaining[#remaining+1]=a end end
            winner=nearest(remaining)
        end
        self:Start(winner,actions,s);trip=B.char.trip
    end
    if not trip then return winner end
    -- On reaching the return hub, collect the newly unlocked round together.
    if trip.phase=="return" and distance(trip.hub,s)<=120 then trip.phase="collect" end
    local members={};for _,q in ipairs(trip.ids) do members[q]=true end
    local function add(a) if not members[a.q] then trip.ids[#trip.ids+1]=a.q;members[a.q]=true end end
    -- Include every eligible held objective in this zone, not only four/six.
    -- Journal capacity does not force a return while field work remains.
    for _,a in ipairs(actions) do
        if s.log[a.q] and ((a.kind=="complete" and a.point.map==trip.zone)
            or (a.kind=="turnin" and a.point.map==trip.hub.map)) then add(a) end
    end
    local pickup={};local pendingCount=0
    for _,q in ipairs(trip.ids) do if byQuest[q] and byQuest[q].kind=="accept" then pendingCount=pendingCount+1 end end
    for _,a in ipairs(actions) do
        if not members[a.q] and self:LocalPickup(a,trip,s) then
            if trip.phase=="collect" or distance(a.point,s)<=120 then pickup[#pickup+1]=a end
        end
    end
    nearest(pickup)
    local free=math.max(0,(s.limit or 20)-(s.count or 0)-pendingCount)
    for i=1,math.min(free,#pickup) do add(pickup[i]) end
    local pending,work,ready={},{},{};local confirmed=0
    for _,q in ipairs(trip.ids) do
        local a=byQuest[q]
        if a then
            if s.log[q] then
                trip.accepted[q]=true;confirmed=confirmed+1
                if a.kind=="complete" then work[#work+1]=a elseif a.kind=="turnin" then ready[#ready+1]=a end
            elseif a.kind=="accept" then pending[#pending+1]=a end
        end
    end
    -- A hand-in already beside the player does not require going back. Keep
    -- the current outing after it instead of starting a new one at the hub.
    local beside={};for _,a in ipairs(actions) do if a.kind=="turnin" and a.travel<6 then beside[#beside+1]=a end end
    local chosen=nearest(beside)
    if not chosen and trip.phase=="collect" then
        local hubReturns={}
        for _,a in ipairs(ready) do
            if a.point.map==trip.hub.map and distance(a.point,trip.hub)<=700 and a.travel<=120 then hubReturns[#hubReturns+1]=a end
        end
        chosen=nearest(hubReturns)
    end
    if not chosen and #pending>0 then
        chosen=retain(pending);chosen.collection={confirmed=confirmed,total=#trip.ids}
        if trip.phase=="return" then trip.phase="collect" end
    elseif not chosen and #work>0 then
        trip.phase="work";chosen=self:OrderWork(work,trip,s)
    elseif not chosen then
        trip.phase="return";chosen=nearest(ready)
        if chosen then trip.hub=chosen.point end
    end
    if winner and winner.classPriority and chosen and not chosen.classPriority
        and chosen.kind~="accept" and winner.kind~="turnin" then chosen=winner end
    if chosen then
        chosen.tripIDs=trip.ids
        chosen.outing={phase=trip.phase,remaining=#work,ready=#ready,total=#trip.ids,confirmed=confirmed,
            nearby=chosen.kind=="turnin" and chosen.travel<6}
    end
    return chosen or winner
end
