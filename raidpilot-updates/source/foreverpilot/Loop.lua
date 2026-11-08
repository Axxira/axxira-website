-- Bounded look-ahead. XP is credited only after a planned turn-in and every
-- scored route closes all quests it starts. No invented accept/complete events.
local B=ForeverPilotLeveling
local L={width=10,depth=8,limit=10};B.Loop=L
local function task(a,kind,point,work)
    return {q=a.q,name=a.node.name,kind=kind,point=point,work=work}
end
function L:Project(a,s)
    local n=a.node;local tasks={};local targets={}
    if a.kind~="turnin" then
        local all=B.Graph:Locations(n,"complete")
        local live=s.log[a.q] and s.log[a.q].objectives or {}
        local match=Ding.Data:MatchObjectives(all,live)
        for i,t in ipairs(all) do
            local obj=match[i] and live[match[i]]
            if (#live==0 or obj) and (not obj or not obj.finished) then
                local point=B.Travel:Nearest(B.Planner:SafePoints(t,s,a.q),s)
                if not point or not B.Travel:Reachable(point,s) then return nil end
                targets[#targets+1]={target=t,point=point}
            end
        end
    end
    local work=a.kind=="complete" and a.work or B.Learning:Duration(a.q,300)
    local piece=work/math.max(1,#targets)
    if a.kind=="accept" then tasks[#tasks+1]=task(a,"accept",a.point,a.node.manual and 45 or 20)
    elseif a.kind=="complete" then tasks[#tasks+1]=task(a,"complete",a.point,piece)
    else tasks[#tasks+1]=task(a,"turnin",a.point,15) end
    local used={}
    if a.kind=="complete" then for i,t in ipairs(targets) do if t.target==a.target then used[i]=true;break end end end
    if a.kind~="turnin" then
        for _=1,#targets do
            local best,dist
            local last=tasks[#tasks].point
            for i,t in ipairs(targets) do
                if not used[i] then
                    local d=B.Travel:Between(last,t.point,s)
                    if not dist or d<dist then best=i;dist=d end
                end
            end
            if not best then break end
            used[best]=true;tasks[#tasks+1]=task(a,"complete",targets[best].point,piece)
        end
        local last=tasks[#tasks].point
        local finish=B.Travel:Nearest(B.Graph:Locations(n,"turnin"),last)
        if not finish or not B.Travel:Reachable(finish,s) then return nil end
        tasks[#tasks+1]=task(a,"turnin",finish,15)
    end
    -- A live offer is certain only for availability, not for completion time.
    local certainty=a.kind=="accept" and (B.Validator.offers[a.q] and 0.95 or 0.82) or 1
    local chain=0;for _ in pairs(n.next or {}) do chain=chain+1 end
    return {a=a,tasks=tasks,xp=a.xp,value=a.xp*certainty*(1+math.min(0.15,chain*0.03))/(1+(a.risk or 0))*(a.levelFit and a.levelFit.factor or 1)*(a.regionFactor or 1)*(1+(a.reward and a.reward.bonus or 0))}
end
local function advance(state,project,index,s,costs)
    local step=state.progress[index] or 1;local t=project.tasks[step]
    if not t or (t.kind=="accept" and state.count>=s.limit) then return nil end
    local key=state.positionID..">"..index..":"..step
    local travel=costs[key]
    if not travel then travel=B.Travel:Between(state.position,t.point,s,state.steps==0);costs[key]=travel end
    local nextState={position=t.point,positionID=index..":"..step,seconds=state.seconds+travel+t.work,
        xp=state.xp,value=state.value,count=state.count,progress={},route={},steps=state.steps+1,first=state.first or project.a.key}
    for i,v in pairs(state.progress) do nextState.progress[i]=v end
    nextState.progress[index]=step+1
    for i,v in ipairs(state.route) do nextState.route[i]=v end
    nextState.route[#nextState.route+1]=t
    if t.kind=="accept" then nextState.count=nextState.count+1 end
    if t.kind=="turnin" then nextState.count=math.max(0,nextState.count-1);nextState.xp=nextState.xp+project.xp;nextState.value=nextState.value+project.value end
    return nextState
end
local function classify(state,projects)
    local closed=true;local credit=state.value;local active=0
    for i,p in ipairs(projects) do
        local step=state.progress[i] or 1
        if step>1 and step<=#p.tasks then
            closed=false;active=active+1
            credit=credit+p.value*0.45*(step-1)/#p.tasks
        end
    end
    state.rank=credit/(state.seconds+45)/(1+math.max(0,active-3)*0.1)
    return closed and state.xp>0
end
local function pack(state,projects)
    local quests=0;local ids={}
    for i,p in ipairs(projects) do if (state.progress[i] or 1)>1 then quests=quests+1;ids[#ids+1]=p.a.q end end
    local steps={}
    for i,t in ipairs(state.route) do steps[i]={q=t.q,name=t.name,kind=t.kind,point=t.point} end
    return {first=state.first,score=state.value/(state.seconds+1),seconds=state.seconds,xp=state.xp,quests=quests,ids=ids,steps=steps}
end
function L:Choose(actions,s)
    local projects,seen={},{}
    local function add(a)
        if not a or seen[a.q] then return end;seen[a.q]=true
        local p=self:Project(a,s);if p then projects[#projects+1]=p end
    end
    -- Keep the current eligible objective in the search, even near the shortlist boundary.
    local old=B.Planner.current
    if old then for _,a in ipairs(actions) do if a.key==old.key then add(a);break end end end
    -- Nearby alternatives need a full-loop evaluation too: a large remote
    -- batch can otherwise crowd them out before its travel is fully scored.
    local nearby={}
    for _,a in ipairs(actions) do
        local d=B.Travel:Distance(a.point,s)
        if d and d<4200 then nearby[#nearby+1]=a end
    end
    table.sort(nearby,function(a,b) if a.travel==b.travel then return a.key<b.key end return a.travel<b.travel end)
    for i=1,math.min(3,#nearby) do add(nearby[i]) end
    for _,a in ipairs(actions) do if #projects>=self.limit then break end;add(a) end
    local base={position=s,positionID="player",seconds=0,xp=0,value=0,count=s.count,progress={},route={},steps=0}
    local costs,plans={},{}
    local function consider(state)
        if classify(state,projects) then
            local plan=pack(state,projects);local prev=plans[plan.first]
            if not prev or plan.score>prev.score then plans[plan.first]=plan end
        end
    end
    -- Complete single-quest routes provide a valid baseline even if a wide
    -- branching search is pruned, or a quest has more tasks than the horizon.
    for i,p in ipairs(projects) do
        local state=base
        for _=1,#p.tasks do state=state and advance(state,p,i,s,costs) end
        if state then consider(state) end
    end
    local beam={base};local expanded=0
    for _=1,self.depth do
        local nextBeam,unique={},{}
        for _,state in ipairs(beam) do
            for i,p in ipairs(projects) do
                local nextState=advance(state,p,i,s,costs)
                if nextState then
                    expanded=expanded+1;consider(nextState)
                    local parts={nextState.positionID,nextState.first}
                    for j=1,#projects do parts[#parts+1]=tostring(nextState.progress[j] or 1) end
                    local key=table.concat(parts,",");local prev=unique[key]
                    if not prev or nextState.seconds<prev.seconds then unique[key]=nextState end
                end
            end
        end
        for _,state in pairs(unique) do nextBeam[#nextBeam+1]=state end
        table.sort(nextBeam,function(a,b)
            if a.rank==b.rank then if a.first==b.first then return a.positionID<b.positionID end return a.first<b.first end
            return a.rank>b.rank
        end)
        beam={};for i=1,math.min(self.width,#nextBeam) do beam[i]=nextBeam[i] end
        if #beam==0 then break end
    end
    self.expanded=expanded;self.plans=plans
    local winner
    for _,a in ipairs(actions) do
        a.plan=plans[a.key]
        -- Preserve the field-work preference after replacing the one-step score.
        -- Otherwise a ready quest wins repeatedly by spending an already-earned
        -- reward, ignoring the need to return to the field afterwards.
        a.score=a.plan and a.plan.score*(a.returnPenalty or 1) or a.score*0.25
        if a.plan and (not winner or a.score>winner.score or (a.score==winner.score and a.key<winner.key)) then winner=a end
    end
    return winner
end
