local B=ForeverPilotLeveling
local P={};B.Planner=P
local function better(a,b)
    if not not a.classPriority~=not not b.classPriority then return not not a.classPriority end
    if a.score==b.score then return a.key<b.key end
    return a.score>b.score
end
function P:ClassPriority(n,s)
    return B.Journal:IsClass(n) or (n.classPrerequisite and B.Progression:Unlock(n,s,false,true)) or false
end
function P:ClassPriorityHere(a,s)
    if not self:ClassPriority(a.node,s) then return false end
    return not not (s.log[a.q] or (B.Validator.npc and B.Validator.offers[a.q])
        or (a.travel<=300 and B.Travel:Distance(a.point,s)))
end
function P:SafePoints(target,s,q)
    local points={}
    for _,p in ipairs(target.points or {}) do
        local info=p.npc and Ding.Data:NpcInfo(p.npc)
        local friendly=B.Progression:Friendly(p.npc,s,info)
        local talk=target.kind=="kill" and friendly
        if (talk or not info or not (info.rank==1 or info.rank==2 or info.rank==3 or (info.minLevel or 0)>s.level+3)) and not (q and B.Recovery:Avoided(q,p,s,target)) then points[#points+1]=p end
    end
    return B.Progression:Preferred(points,s)
end
function P:Solo(n)
    local known=false
    for _,r in ipairs(n.routes) do
        if r.kind=="complete" then
            known=true
            if not r.step.elite and not r.step.dungeon then return true end
        end
    end
    return not known
end
function P:Action(n,kind,s,recovery)
    if B.Recovery:IsSkipped(n.id) then return nil,"skipped" end
    local allowed,reason=B.Validator:Eligible(n,s,kind)
    if not allowed then return nil,reason end
    if kind~="turnin" and n.profession then return nil,"optionalProfession" end
    local wanted,why=B.Journal:Allows(n,s)
    if not wanted then return nil,why end
    if s.log[n.id] then
        n.name=s.log[n.id].title or n.name
        for _,o in ipairs(s.log[n.id].objectives) do
            if kind~="turnin" and not o.finished and o.objectiveType==14 then return nil,"optionalProfession" end
        end
    end
    local a={q=n.id,node=n,kind=kind,key=kind..":"..n.id,confidence=reason=="LIVE" and "LIVE" or (kind~="accept" and "LIVE / POSITION DB" or "DATABASE"),recovery=recovery}
    a.classPriority=not not self:ClassPriority(n,s)
    if B.char.deferred[a.key] and B.char.deferred[a.key]>time() then return nil,"deferred" end
    local context=B.Graph:Context(n,kind,s);a.context=context
    if kind~="turnin" and context and (context.step.elite or context.step.dungeon) then return nil,"group" end
    if kind~="turnin" and not self:Solo(n) then return nil,"group" end
    if kind=="accept" then
        -- Reserve journal space for quests whose known objectives can actually be done.
        local targets=B.Graph:Locations(n,"complete")
        for _,target in ipairs(targets) do
            local point=B.Travel:Nearest(self:SafePoints(target,s),s)
            if not point then return nil,"unsafeObjective" end
            if not B.Travel:Reachable(point,s) then return nil,"noTravelRoute" end
        end
        if #targets==0 and not B.Travel:Nearest(B.Graph:Locations(n,"turnin"),s) then return nil,"unknownDestination" end
    end
    if kind=="complete" then
        if n.level>s.level+3 then return nil,"difficulty" end
        if context and (context.step.elite or context.step.dungeon) then return nil,"group" end
        local targets=B.Graph:Locations(n,kind)
        local live=s.log[n.id].objectives
        local pending=false
        for _,obj in ipairs(live) do if not obj.finished then pending=true end end
        if #live>0 and not pending then return nil,"awaitingServerReady" end
        local match=Ding.Data:MatchObjectives(targets,live)
        local nearest
        local old=self.current;local retained=false
        for i,target in ipairs(targets) do
            local obj=match[i] and live[match[i]]
            if (#live==0 or obj) and (not obj or not obj.finished) then
                local safe=self:SafePoints(target,s,n.id)
                local point,d=B.Travel:Nearest(safe,s)
                if old and old.key==a.key and old.target==target and old.point then
                    for _,p in ipairs(safe) do
                        if p==old.point then
                            local distance=B.Travel:Distance(p,s)
                            if distance and distance>25 then point,d=p,distance;retained=true end
                            break
                        end
                    end
                end
                if point and (retained or not nearest or d<nearest) then a.point,a.target,a.objective,nearest=point,target,obj,d end
                if retained then break end
            end
        end
        if not a.point and #targets==0 and context and context.step.go then
            local g=context.step.go;a.point={map=g[1],x=g[2],y=g[3]};a.note=context.step.note
        end
    else
        a.point=B.Travel:Nearest(B.Graph:Locations(n,kind),s)
        if kind=="accept" then
            for _,item in ipairs(n.startItems) do
                if Ding.Compat.ItemCount(item)>0 and s.map and s.x and s.y then
                    a.point={map=s.map,x=s.x,y=s.y};a.itemStart=item
                    a.note=B.T("Ouvre dans tes sacs : ")..(Ding.Data:ItemName(item) or tostring(item))
                    break
                end
            end
        end
    end
    if not a.point then return nil,"noCoordinates" end
    if not B.Travel:Reachable(a.point,s) then return nil,"noTravelRoute" end
    a.levelFit=B.Progression:Evaluate(n,kind,s,a)
    if a.levelFit.retire and B.Mode()=="fast" then return nil,"outleveled" end
    if B.Mode()=="zone" then a.levelFit.factor=1 end
    a.travel=B.Travel:Seconds(a.point,s)
    -- Keep distant class offers in the candidate set, without interrupting a
    -- local outing for an unconfirmed pickup on another continent. Held class
    -- chains and real offers retain their strict priority.
    a.classPriority=self:ClassPriorityHere(a,s)
    if a.point.observed and reason~="LIVE" then a.confidence=kind=="accept" and B.T("OBSERVÉ / À CONFIRMER") or B.T("LIVE / POSITION OBSERVÉE") end
    local xp,measured=B.Graph:XP(n,s);a.xp=xp;a.measuredXP=measured
    local remaining=1
    if kind=="complete" then
        local have,need=0,0
        for _,o in ipairs(s.log[n.id].objectives) do
            have=have+(o.numFulfilled or (o.finished and 1 or 0));need=need+(o.numRequired or 1)
        end
        if need>0 then remaining=math.max(0.1,1-have/need) end
    end
    a.work=kind=="turnin" and 15 or B.Learning:Duration(n.id,300)*remaining
    if kind=="accept" then
        local t=B.Graph:Locations(n,"complete");local target=t[1] and B.Travel:Nearest(t[1].points,s)
        local onward=target and B.Travel:Distance(a.point,target)
        a.work=a.work+20+(onward and onward/7 or 120)
    end
    local deaths=B.char.learning[n.id] and B.char.learning[n.id].deaths or 0
    local risk=math.max(0,n.level-s.level)*0.18+math.min(1,deaths*0.2)
    a.score=xp/(a.travel+a.work+1)*(1+math.min(0.3,B.Progression:ChainCount(n,s)*0.06))/(1+risk)
    a.risk=risk
    a.score=a.score*a.levelFit.factor
    a.reward=B.Items:QuestReward(n.id)
    if a.reward then a.score=a.score*(1+a.reward.bonus) end
    if kind=="accept" then a.score=a.score*0.8 end
    if recovery then a.score=a.score*1.15;a.confidence="RECOVERY" end
    a.note=a.note or context and context.step.note
    return a
end
function P:Candidates(s)
    local out,seen,blocked={},{},{}
    local function add(n,kind,recovery)
        if not n or seen[kind..":"..n.id] then return end
        seen[kind..":"..n.id]=true
        local a,reason=P:Action(n,kind,s,recovery)
        if a then out[#out+1]=a elseif reason then blocked[reason]=(blocked[reason] or 0)+1 end
    end
    local function recover(n,visited,depth)
        if not n or visited[n.id] or depth>20 or B.Recovery:IsSkipped(n.id) then return end;visited[n.id]=true
        local q=B.Recovery:Prerequisite(n,s)
        if q then
            local p=B.Graph:Node(q)
            if s.log[q] then add(p,s.log[q].ready and "turnin" or "complete",true)
            elseif p then recover(p,visited,depth+1);add(p,"accept",true) end
        end
    end
    for q,log in pairs(s.log) do add(B.Graph:Node(q),log.ready and "turnin" or "complete") end
    local pool={} for _,n in pairs(B.Graph.nodes) do pool[#pool+1]=n end
    local zoneMode=B.Mode()=="zone"
    for _,n in ipairs(pool) do
        local allowed
        if zoneMode then
            allowed=n.min<=s.level and n.level<=s.level+3 and
                (B.ZoneGuide:Eligible(n,s) or B.Validator.offers[n.id] or self:ClassPriority(n,s))
        else
            allowed=n.min<=s.level and n.level<=s.level+2 and
                (n.level>=s.level-5 or self:ClassPriority(n,s))
        end
        if allowed and not s.log[n.id] and not s:IsDone(n.id) then
            local context=B.Graph:Context(n,"accept",s)
            if context or n.extension or B.Validator.offers[n.id] or
                (zoneMode and B.ZoneGuide:Eligible(n,s)) then
                if context or n.extension then recover(n,{},0) end
                add(n,"accept")
            end
        end
    end
    -- Real offers can include new server quests absent from the route's level bracket.
    for q in pairs(B.Validator.offers) do add(B.Graph:Node(q),"accept") end
    -- An unmeasured continent transfer must not displace useful local work on
    -- the strength of its optimistic fixed travel estimate. Held quests remain
    -- eligible, and the bias disappears once the local bracket is exhausted.
    local localWork=false
    for _,a in ipairs(out) do
        local d=B.Travel:Distance(a.point,s)
        if a.kind~="turnin" and a.levelFit.questLevel and a.levelFit.gap<=2 and d and d<4200 then localWork=true;break end
    end
    if localWork then for _,a in ipairs(out) do
        if a.kind=="accept" and not B.Travel:Distance(a.point,s) then a.regionFactor=0.65;a.score=a.score*a.regionFactor end
    end end
    table.sort(out,better)
    self.blocked=blocked
    return out
end
-- Compare two complete itineraries, not just distance to the next NPC.
-- Ready XP has already been earned: banking it must not repeatedly send the
-- player away from useful objectives and back over the same road.
function P:BeforeReturn(hub,field,s)
    if hub.kind~="turnin" or field.kind~="complete" or s.count>=s.limit-1 or hub.travel<45
        or field.travel>180 or (field.work or 300)>900 or field.q==hub.q then return end
    local finish=B.Travel:Nearest(B.Graph:Locations(field.node,"turnin"),field.point)
    if not finish then return end
    if not B.Travel:Distance(s,hub.point) or not B.Travel:Distance(s,field.point)
        or not B.Travel:Distance(finish,hub.point) then return end
    local backtrack=B.Travel:Between(s,hub.point,s,true)+B.Travel:Between(hub.point,field.point,s)
        +B.Travel:Between(field.point,finish,s)
    local together=B.Travel:Between(s,field.point,s,true)+B.Travel:Between(field.point,finish,s)
        +B.Travel:Between(finish,hub.point,s)
    local saved=backtrack-together
    if saved>=45 then return saved end
end
function P:Batch(actions,s)
    local top={};for i=1,math.min(60,#actions) do top[i]=actions[i] end
    for _,a in ipairs(top) do
        a.batch={};a.returnPenalty=1;a.beforeReturn=nil;local unique={[a.q]=true};local bonus=0
        for _,b in ipairs(top) do
            if not unique[b.q] and b~=a and ((a.kind=="turnin" and b.kind=="turnin") or (a.kind~="turnin" and b.kind~="turnin")) then
                local d=B.Travel:Distance(a.point,b.point)
                if d and d<220 and #a.batch<4 then
                    unique[b.q]=true;a.batch[#a.batch+1]=b
                    bonus=bonus+b.xp/(a.travel+a.work+b.work+d/7+1)*0.30
                end
            end
        end
        a.score=a.score+bonus
        -- Account for the subsequent trip back into the field, including its
        -- own turn-in. Unknown continent transfers never become a saving.
        if a.kind=="turnin" and s.count<s.limit-1 then
            for _,b in ipairs(top) do
                local saved=self:BeforeReturn(a,b,s)
                if saved and (not a.beforeReturn or saved>a.beforeReturn.seconds) then
                    a.beforeReturn={q=b.q,seconds=saved}
                    a.returnPenalty=math.max(.15,math.min(.42,(b.work or 300)/((b.work or 300)+saved)))
                end
            end
            a.score=a.score*a.returnPenalty
        end
    end
    -- Limit pair scoring, not the available journal/zone quests.
    table.sort(actions,better)
    return actions
end
function P:Fallback(s)
    if B.Mode()=="zone" and B.ZoneGuide then
        local nextZone=B.ZoneGuide:Transition(s)
        if nextZone then return nextZone end
    end
    -- The old guide cursor may be in a different level bracket. Derive a recovery
    -- camp from the graph and the player's current level, never from that cursor.
    local cache=self.farm
    if not cache or cache.level~=s.level or cache.map~=s.map or s.now-cache.at>20 then
        cache={level=s.level,map=s.map,at=s.now};self.farm=cache
        local seen,best={},nil
        for _,n in pairs(B.Graph.nodes) do
            if n.level>=s.level-6 and n.level<=s.level+4 then
                for _,target in ipairs(Ding.Data:ObjectiveNpcs(n.id)) do
                    local id=target.npc
                    if not seen[id] and not (B.char.deferred["grind:"..id] and B.char.deferred["grind:"..id]>time()) then
                        seen[id]=true
                        local info=Ding.Data:NpcInfo(id)
                        if info and info.rank==0 and not B.Progression:Friendly(id,s,info) and info.minLevel<=s.level+1 and info.maxLevel>=math.max(1,s.level-1) then
                            local point=B.Travel:Nearest(Ding.Data:NpcPoints(id),s)
                            local avoided=false
                            for _,skipped in pairs(B.char.skipped or {}) do
                                local d=skipped.point and point and B.Travel:Distance(skipped.point,point)
                                if d and d<180 then avoided=true;break end
                            end
                            if point and not avoided and B.Travel:Reachable(point,s) then
                                local cost=B.Travel:Seconds(point,s)+math.abs(s.level-(info.minLevel+info.maxLevel)/2)*25
                                if not best or cost<best or (cost==best and id<cache.npc) then
                                    best=cost;cache.npc=id;cache.point=point;cache.name=Ding.Data:NpcName(id)
                                end
                            end
                        end
                    end
                end
            end
        end
    end
    if cache.point then return {kind="grind",key="grind:"..cache.npc,point=cache.point,label=cache.name,confidence="RECOVERY",batch={},note=B.T("XP d'appoint. Les quêtes sont réévaluées automatiquement à chaque progression.")} end
    return {kind="recovery",key="recovery",confidence="RECOVERY",batch={},note=B.T("Aucune destination fiable trouvée. Ouvre un PNJ de quêtes : ses offres réelles seront analysées.")}
end
function P:Signature(s)
    local log={}
    for q,row in pairs(s.log) do log[#log+1]=q..":"..B.Recovery:Signature(row) end
    table.sort(log)
    return table.concat({B.Mode(),B.ZoneGuide and B.ZoneGuide:Current(s) or 0,
        math.floor(((B.char.grindTelemetry or {}).samples or 0)/5),
        s.level,s.map or 0,math.floor((s.wx or 0)/35),math.floor((s.wy or 0)/35),s.count,s.limit,s.bags or -1,
        B.Validator.generation or 0,B.Validator.revision or 0,B.Recovery.revision or 0,B.FlightPaths.revision or 0,
        math.floor((s.health or 1)*20),math.floor((s.mana or 1)*20),math.floor((s.durability or 1)*20),tostring(s.combat),table.concat(log,";")},"|")
end
function P:Plan(s)
    if s.level>=60 or s.dead then
        local kind=s.level>=60 and "done" or "corpse"
        self.actions={};self.current={kind=kind,key=kind,point=kind=="corpse" and s.ghost and Ding.Compat.CorpsePoint() or nil,batch={},confidence="LIVE"}
        self.signature=nil;return self.current
    end
    local signature=self:Signature(s)
    if self.current and self.signature==signature and s.now-(self.plannedAt or 0)<5 then return self.current end
    self.signature=signature;self.plannedAt=s.now
    B.Travel:Prepare(s)
    local actions=self:Candidates(s)
    if B.Mode()=="zone" and B.ZoneGuide then actions=B.ZoneGuide:Filter(actions,s) end
    actions=self:Batch(actions,s)
    self.actions=actions
    local priority={}
    for _,a in ipairs(actions) do if a.classPriority then priority[#priority+1]=a end end
    -- Priority is a separate decision tier; XP scoring and a saved ordinary
    -- outing must not hide an eligible class chain again.
    local choices=#priority>0 and priority or actions
    local winner=B.Loop:Choose(choices,s) or choices[1]
    table.sort(actions,better)
    local old=self.current
    if winner and old then
        for _,a in ipairs(choices) do
            if a.key==old.key and a.score>=winner.score*0.88 and not (winner.kind=="turnin" and winner.travel<8) then winner=a;break end
        end
    end
    winner=B.Journey:Choose(winner,actions,s) or self:Fallback(s)
    if B.Grind then winner=B.Grind:Choose(s,winner) end
    winner=B.FlightPaths:Choose(s,winner)
    winner=B.Needs:Choose(s,winner)
    if not old or old.key~=winner.key then B.Trace("decision",winner.key);winner.since=s.now else winner.since=old.since end
    self.current=winner
    return winner
end
