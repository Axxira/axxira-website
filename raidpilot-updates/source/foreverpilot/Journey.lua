-- A chosen outing survives score/distance fluctuations. Acquisition is only
-- confirmed by the actual quest log; reaching a giver never counts as pickup.
local B=ForeverPilotLeveling
local J={};B.Journey=J
local function distance(a,b) return B.Travel:Distance(a,b) or math.huge end
local function best(list)
    local found
    for _,a in ipairs(list) do
        if not found or (a.score or 0)>(found.score or 0) or ((a.score or 0)==(found.score or 0) and a.key<found.key) then found=a end
    end
    return found
end
function J:Reset() B.char.trip=nil end
function J:Related(a,b,s)
    if b.kind~="accept" or b.node.manual or distance(a.point,b.point)>180 then return false end
    local ac,bc=a.context,b.context
    if ac and bc and ac.guide==bc.guide and ac.index==bc.index then return true end
    for _,t in ipairs(B.Graph:Locations(a.node,"complete")) do
        local p=B.Travel:Nearest(t.points,s)
        if p then for _,u in ipairs(B.Graph:Locations(b.node,"complete")) do
            local q=B.Travel:Nearest(u.points,s)
            if q and distance(p,q)<300 then return true end
        end end
    end
    return false
end
function J:Start(winner,actions,s)
    if not winner or not winner.q then return end
    local trip={ids={winner.q},accepted={},at=time(),level=s.level};local seen={[winner.q]=true}
    local planned={};for _,q in ipairs(winner.plan and winner.plan.ids or {}) do planned[q]=true end
    for _,a in ipairs(actions) do
        local include=planned[a.q] and (s.log[a.q] or (a.kind=="accept" and distance(a.point,winner.point)<180))
        if not include and winner.kind=="accept" then include=self:Related(winner,a,s) end
        if include and not seen[a.q] and #trip.ids<4 and not a.node.manual then
            trip.ids[#trip.ids+1]=a.q;seen[a.q]=true
        end
    end
    B.char.trip=trip
end
function J:Choose(winner,actions,s)
    local byQuest={};for _,a in ipairs(actions) do byQuest[a.q]=a end
    -- A ready quest outside the saved outing can also free a full journal.
    -- Likewise, hand in at an NPC already beside us instead of coming back.
    local immediate
    for _,a in ipairs(actions) do
        if a.kind=="turnin" and (s.count>=s.limit-1 or a.travel<6)
            and (not immediate or a.travel<immediate.travel or (a.travel==immediate.travel and a.key<immediate.key)) then immediate=a end
    end
    if immediate then self:Start(immediate,actions,s);immediate.tripIDs=B.char.trip.ids;return immediate end
    local trip=B.char.trip
    if trip then
        trip.level=s.level
        for i=#trip.ids,1,-1 do
            local q=trip.ids[i]
            -- The action disappears when live validation rejects it, a skip is
            -- requested, its objective is unsafe, or the player abandons it.
            local abandoned=trip.accepted[q] and not s.log[q] and not s:IsDone(q)
            if abandoned then B.char.deferred['accept:'..q]=time()+300;byQuest[q]=nil end
            local a=byQuest[q]
            local remotePickup=a and a.kind=="accept" and not a.classPriority and a.travel>300
                and winner and winner.q~=q and winner.travel<=180
            if s:IsDone(q) or not a or remotePickup then
                if not s:IsDone(q) and s.log[q] then
                    local node=B.Graph:Node(q)
                    local fit=node and B.Progression:Evaluate(node,s.log[q].ready and "turnin" or "complete",s)
                    if fit and fit.retire then B.Trace("levelTransition",q..":outleveled:"..s.level) end
                end
                table.remove(trip.ids,i)
            end
        end
        if #trip.ids==0 then self:Reset();trip=nil end
    end
    if not trip then
        -- An abandoned quest can still exist in this refresh's candidate list.
        if winner and winner.q and not byQuest[winner.q] then
            local remaining={};for _,a in ipairs(actions) do if byQuest[a.q] then remaining[#remaining+1]=a end end
            winner=best(remaining)
        end
        self:Start(winner,actions,s);trip=B.char.trip
    end
    if not trip then return winner end
    -- Reconcile the outing with the live journal. New useful field quests must
    -- not be invisible merely because they were absent when the trip started.
    local members={};for _,q in ipairs(trip.ids) do members[q]=true end
    local additions={}
    for _,a in ipairs(actions) do
        if s.log[a.q] and a.kind=="complete" and not members[a.q] then
            for _,q in ipairs(trip.ids) do
                local held=byQuest[q]
                if held and (B.Planner:BeforeReturn(held,a,s) or
                    (held.kind=="complete" and distance(held.point,a.point)<450 and a.travel<=180)) then
                    additions[#additions+1]=a;break
                end
            end
        end
    end
    table.sort(additions,function(a,b) if a.travel==b.travel then return a.key<b.key end return a.travel<b.travel end)
    for _,a in ipairs(additions) do if #trip.ids<6 then trip.ids[#trip.ids+1]=a.q;members[a.q]=true end end
    local pending,held,confirmed={},{},0
    for _,q in ipairs(trip.ids) do
        local a=byQuest[q]
        if s.log[q] then trip.accepted[q]=true;confirmed=confirmed+1;held[#held+1]=a
        elseif a and a.kind=="accept" then pending[#pending+1]=a end
    end
    local chosen
    if #pending>0 then
        -- Collect the selected hub batch before leaving for its field targets.
        table.sort(pending,function(a,b) if a.travel==b.travel then return a.key<b.key end return a.travel<b.travel end)
        chosen=pending[1]
        local old=B.Planner.current
        if old then for _,a in ipairs(pending) do if a.key==old.key then chosen=a;break end end end
        chosen.collection={confirmed=confirmed,total=#trip.ids}
    else
        local old=B.Planner.current
        if old then for _,a in ipairs(held) do if a.key==old.key then chosen=a;break end end end
        if chosen and chosen.kind=="turnin" then
            local field={}
            for _,a in ipairs(held) do
                local saved=B.Planner:BeforeReturn(chosen,a,s)
                if saved then a.savedTravel=saved;field[#field+1]=a end
            end
            chosen=best(field) or chosen
        end
        if not chosen then
            local field={}
            for _,a in ipairs(held) do if a.kind=="complete" then field[#field+1]=a end end
            chosen=best(field) or best(held)
        end
    end
    if chosen then chosen.tripIDs=trip.ids end
    return chosen or winner
end
