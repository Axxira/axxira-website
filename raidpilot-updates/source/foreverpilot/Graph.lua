local B=ForeverPilotLeveling
local G={nodes={},routes={},count=0,classQuests={}}; B.Graph=G
function G:Node(q)
    if self.nodes[q] then return self.nodes[q] end
    if not Ding.Data:QuestExists(q) then return nil end
    local raw=LibDingDB and LibDingDB.Get("quest",q) or {}
    local one,all=Ding.Data:QuestPrereqs(q)
    local n={id=q,name=Ding.Data:QuestName(q),level=Ding.Data:QuestLevel(q),min=Ding.Data:QuestRequiredLevel(q),
        any=B.List(one),all=B.List(all),exclusive=B.List(raw.exclusiveTo),next={},routes={},
        starters=Ding.Data:StarterNpcs(q),finishers=Ding.Data:FinisherNpcs(q),
        startItems=raw.startedBy and raw.startedBy[3] or {}, raw=raw}
    -- These open-data profession introductions are optional, not prerequisites
    -- for leveling. Live Mining objectives require skill 20, not killing or
    -- merely visiting the trainer listed as a creature in the export.
    local canonical=Ding.DB and Ding.DB.quests[q] and Ding.DB.quests[q][1] or raw.name or n.name
    n.profession=type(canonical)=="string" and canonical:find("Camping 101:",1,true)==1
    self.nodes[q]=n; self.count=self.count+1
    return n
end
function G:Build()
    self.nodes={};self.routes={};self.count=0;self.classQuests={}
    for _,g in Ding:IterateGuides() do
        if g.pack=="ding-class" and Ding:GuideMatchesPlayer(g) then
            for _,step in ipairs(g.steps) do if Ding:StepMatchesPlayer(step) then
                for _,kind in ipairs({"accept","complete","turnin"}) do
                    for _,q in ipairs(B.List(step[kind])) do self.classQuests[q]=true end
                end
            end end
        end
        if (g.pack=="ding" or g.pack=="ding-zones" or g.pack=="ding-class") and Ding:GuideMatchesPlayer(g) then
            self.routes[#self.routes+1]=g
            for i,step in ipairs(g.steps) do
                if Ding:StepMatchesPlayer(step) then
                    for _,kind in ipairs({"accept","complete","turnin"}) do
                        for _,q in ipairs(B.List(step[kind])) do
                            local n=self:Node(q)
                            if n then
                                if g.pack=="ding" then n.fast=true end
                                if g.pack=="ding-class" and kind=="accept" then
                                    n.classParents=n.classParents or {}
                                    for _,parent in ipairs(B.List(step.turnin)) do
                                        if parent~=q then n.classParents[parent]=true end
                                    end
                                end
                                n.routes[#n.routes+1]={guide=g,index=i,step=step,kind=kind}
                                if step.why and step.why.quest==q and step.why.xp then n.xp=step.why.xp end
                                if step.noauto then for _,id in ipairs(B.List(step.noauto)) do if id==q then n.manual=true end end end
                            end
                        end
                    end
                end
            end
        end
    end
    -- Some Forever class entries omit their prerequisites in the database.
    -- Use only explicit turnin+accept links in the matching class guide, never
    -- the previous arbitrary guide step. A live NPC offer still wins.
    for _,n in pairs(self.nodes) do
        if #n.any==0 and #n.all==0 and n.classParents then
            for q in pairs(n.classParents) do n.any[#n.any+1]=q end
            table.sort(n.any)
        end
    end
    -- Preserve the old quest-first additions as candidates, without editing Ding routes.
    if UnitFactionGroup("player")=="Horde" then
        for _,q in ipairs({5082,6603,5083,5781,5845,9422}) do local n=self:Node(q); if n then n.extension=true;n.fast=true end end
    end
    local visited={}
    local function link(n,depth)
        if visited[n.id] or depth>40 then return end;visited[n.id]=true
        for _,list in ipairs({n.any,n.all}) do for _,id in ipairs(list) do
            -- Negative preQuest entries mean the parent must be active, not completed.
            local p=G:Node(math.abs(id))
            if p then p.next[n.id]=true; link(p,depth+1) end
        end end
    end
    local roots={} for _,n in pairs(self.nodes) do roots[#roots+1]=n end
    for _,n in ipairs(roots) do link(n,0) end
    -- A prerequisite is part of the fast route even when it has no guide step.
    -- Follow both alternatives and mandatory parents, including active parents.
    local marked={}
    local function protect(n)
        if not n or marked[n.id] then return end;marked[n.id]=true
        for _,list in ipairs({n.any,n.all}) do for _,id in ipairs(list) do
            local p=G:Node(math.abs(id))
            if p then p.fast=true;p.fastPrerequisite=true;protect(p) end
        end end
    end
    for _,n in ipairs(roots) do if n.fast and not n.profession then protect(n) end end
    -- Class chains need their own prerequisites even outside the XP route.
    local classMarked={}
    local function protectClass(n)
        if not n or classMarked[n.id] then return end;classMarked[n.id]=true
        for _,list in ipairs({n.any,n.all}) do for _,id in ipairs(list) do
            local p=G:Node(math.abs(id))
            if p then p.classPrerequisite=true;protectClass(p) end
        end end
    end
    for _,n in ipairs(roots) do if self.classQuests[n.id] then protectClass(n) end end
end
function G:Locations(n,kind)
    if kind=="accept" or kind=="turnin" then
        local key=kind=="accept" and "starts" or "ends"
        n[key]=n[key] or (kind=="accept" and Ding.Data:StartPoints(n.id) or Ding.Data:EndPoints(n.id))
        local observed=B.Knowledge:Points(n.id,kind)
        if #observed==0 then return n[key] end
        local out,knownNPC={},{}
        for _,p in ipairs(observed) do out[#out+1]=p;knownNPC[p.npc]=true end
        for _,p in ipairs(n[key]) do if not p.npc or not knownNPC[p.npc] then out[#out+1]=p end end
        return out
    end
    n.targets=n.targets or Ding.Data:ObjectiveTargets(n.id); return n.targets
end
function G:Context(n,kind,s)
    local best
    for _,r in ipairs(n.routes) do
        if r.kind==kind and Ding:StepApplies(r.step) then
            if not best or math.abs(s.level-r.guide.minLevel)<math.abs(s.level-best.guide.minLevel) then best=r end
        end
    end
    return best
end
function G:XP(n,s)
    local observed,verified=B.Knowledge:XP(n.id,s)
    if observed then return observed,verified end
    local learned=B.char.learning[n.id]
    if learned and learned.xp and learned.xp>0 then return learned.xp,true end
    if n.xp then return n.xp,false end
    -- Explicit heuristic: the export has no XP column. Never label it server verified.
    local level=math.max(1,n.level>0 and n.level or s.level)
    local base=45+level*level*1.8+level*25
    local over=s.level-level
    return base*(over>5 and math.max(0.1,1-(over-5)*0.2) or 1),false
end
-- Expand the outing from the open zone catalog. This is a DATABASE candidate,
-- not proof of availability or pickup; Validator and the live log remain final.
function G:LocalOpportunity(n,s)
    if not s or n.profession or n.manual or not B.Planner:Solo(n) then return false end
    for _,o in ipairs(s.log[n.id] and s.log[n.id].objectives or {}) do
        if not o.finished and o.objectiveType==14 then return false end
    end
    local level=n.level>0 and n.level or s.level
    if level<s.level-2 or level>s.level+2 or n.min>s.level then return false end
    if s.log[n.id] and B.char.opportunities and B.char.opportunities[n.id] then return true end
    local catalog=false
    for _,r in ipairs(n.routes) do
        if r.kind=="accept" and r.guide and r.guide.pack=="ding-zones" then catalog=true;break end
    end
    if not s.log[n.id] and not (B.Validator.npc and B.Validator.offers[n.id]) and not catalog then return false end
    if catalog and not s.log[n.id] then
        local giver=B.Travel:Nearest(self:Locations(n,"accept"),s)
        if not giver or giver.map~=s.map or not B.Travel:Reachable(giver,s) then return false end
    end
    local targets=self:Locations(n,"complete");if #targets==0 and not catalog then return false end
    local origin=s
    for _,t in ipairs(targets) do
        local localPoints={}
        for _,p in ipairs(B.Planner:SafePoints(t,s,n.id)) do if p.map==s.map then localPoints[#localPoints+1]=p end end
        local p=B.Travel:Nearest(localPoints,origin)
        local d=p and B.Travel:Distance(origin,p)
        if not d or not B.Travel:Reachable(p,s) then return false end
        origin=p
    end
    local finish=B.Travel:Nearest(self:Locations(n,"turnin"),origin)
    local d=finish and B.Travel:Distance(origin,finish)
    if not d or finish.map~=s.map or not B.Travel:Reachable(finish,s) then return false end
    if s.log[n.id] then
        B.char.opportunities=B.char.opportunities or {}
        B.char.opportunities[n.id]=true
        for q in pairs(B.char.opportunities) do if not s.log[q] then B.char.opportunities[q]=nil end end
    end
    return true
end
