-- FastXP route-first engine for WoW Forever.
-- Uses ordered guide steps, game-confirmed quest state, and safe route recovery.
-- No third-party proprietary guide code or paid route data is included.
local B=ForeverPilotLeveling
local R={syncAt=0,guideId=nil};B.SpeedRoute=R
local function point(p)
    return type(p)=="table" and type(p.map)=="number" and
        type(p.x)=="number" and type(p.y)=="number"
end
local function done(s,q)
    if s.done and s.done[q] then return true end
    return s.IsDone and B.Call(s.IsDone,s,q)==true or false
end
function R:Enabled(s)
    return s and s.level and s.level<60 and B.settings and B.settings.enabled and
        B.settings.speedRoute~=false and B.Mode()=="fast" and not s.dead and
        not s.taxi and Ding and Ding.Data and Ding.Data.available and
        Ding.Engine and type(Ding.SuggestGuide)=="function" or false
end
function R:Signature()
    local E=Ding and Ding.Engine
    local g=E and E.guide
    return (B.settings and B.settings.speedRoute==false and "off" or "on")
        ..":"..tostring(g and g.id or "none")..":"..tostring(E and E.index or 0)
        ..":"..tostring(E and E.hold or false)
end
function R:Engine(s)
    local E=Ding and Ding.Engine
    if not E or type(E.CurrentStep)~="function" or E.hold then return nil end
    local g=E.guide
    if g and g.pack=="ding" and (not g.maxLevel or s.level<=g.maxLevel+2) then
        return E
    end
    if s.combat or s.dead or s.taxi or E.hold or (s.now or 0)-(self.syncAt or -999)>0 and
        (s.now or 0)-(self.syncAt or -999)<12 then
        return g and g.pack=="ding" and E or nil
    end
    local wanted=B.Call(Ding.SuggestGuide,Ding,"ding")
    if type(wanted)~="table" or wanted.pack~="ding" then return nil end
    if g and g.id==wanted.id then return E end
    self.syncAt=s.now or B.Now()
    B.Call(E.Resume,E,wanted.id,true)
    if E.guide and E.guide.id==wanted.id then
        self.guideId=wanted.id
        if B.Trace then B.Trace("route","Guide "..wanted.id.." selected") end
        return E
    end
end
function R:QuestOK(q,kind,s)
    if type(q)~="number" or
        B.Recovery and B.Recovery:IsSkipped(q) then return false end
    local row=s.log and s.log[q]
    if kind=="turnin" then return row and row.ready or false end
    if kind=="complete" then
        if not row or row.ready then return false end
        if B.Planner and B.Planner.FastEligible then
            return B.Planner:FastEligible({q=q,kind="complete",
                node=B.Graph and B.Graph:Node(q)},s)
        end
        return true
    end
    if kind~="accept" or row or done(s,q) or (s.count or 0)>=(s.limit or 20) then
        return false
    end
    local node=B.Graph and B.Graph:Node(q)
    if not node or node.manual or node.profession then return false end
    if B.Recovery and B.Recovery.IsBlocked and B.Recovery:IsBlocked(q,s) then return false end
    if not (B.Validator and B.Validator.Eligible) then return false end
    local eligible=B.Call(B.Validator.Eligible,B.Validator,node,s,"accept")
    if eligible~=true then return false end
    return true
end
function R:QuestAction(s,E,step,lookahead)
    local lines=not lookahead and B.Call(E.Describe,E,step,false) or {}
    for _,line in ipairs(type(lines)=="table" and lines or {}) do
        if type(line)=="table" and not line.done and not line.impossible and
            self:QuestOK(line.quest,line.action,s) then
            return line.quest,line.action,line.text
        end
    end
    -- Fallback for special Forever clients with a missing localized line.
    for _,kind in ipairs({"turnin","complete","accept"}) do
        local groups=kind=="complete" and {"complete","turnin"} or {kind}
        for _,key in ipairs(groups) do
            for _,q in ipairs(B.List(step[key])) do
                if self:QuestOK(q,kind,s) then return q,kind end
            end
        end
    end
end
function R:Nearest(points,s)
    if not B.Travel or not B.Travel.Nearest then return nil end
    local result=B.Call(B.Travel.Nearest,B.Travel,points,s)
    if point(result) and (not B.Travel.Reachable or
        B.Call(B.Travel.Reachable,B.Travel,result,s)~=false) then return result end
end
function R:Point(s,E,step,q,kind,index)
    if point(step.go and {map=step.go[1],x=step.go[2],y=step.go[3]}) then
        return {map=step.go[1],x=step.go[2],y=step.go[3]}
    end
    if q and B.Graph then
        local n=B.Graph:Node(q)
        if n then
            local places=B.Call(B.Graph.Locations,B.Graph,n,kind)
            if kind=="complete" then
                local all,hadLocations={},false
                for _,target in ipairs(type(places)=="table" and places or {}) do
                    for _,p in ipairs(target.points or {}) do
                        hadLocations=true
                        if not (B.Recovery and B.Recovery.Avoided and
                            B.Recovery:Avoided(q,p,s,target)) then all[#all+1]=p end
                    end
                end
                if hadLocations and #all==0 then return nil end
                local p=self:Nearest(all,s)
                if p then return p end
            else
                local p=self:Nearest(type(places)=="table" and places or {},s)
                if p then return p end
            end
        end
    end
    local candidates=B.Call(E.StepCandidates,E,step,index or E.index)
    if type(candidates)=="table" and #candidates>0 then
        if q and kind=="complete" and B.Recovery and B.Recovery.Avoided then
            local filtered={}
            for _,p in ipairs(candidates) do
                if not B.Recovery:Avoided(q,p,s,nil) then filtered[#filtered+1]=p end
            end
            candidates=filtered
        end
        local p=#candidates>0 and B.Call(E.NearestPoint,E,candidates,self.lastTarget)
        if point(p) then self.lastTarget=p.label;return p end
    end
end
-- A blocked guide quest should not trap a player in one obsolete section.
-- Look only at nearby reachable goals in the same guide; never complete or skip
-- a quest in the quest log and never move Ding's saved cursor without consent.
function R:LookAhead(s,E,guide)
    if not guide or not guide.steps or type(E.index)~="number" then return nil end
    local best,bestCost
    for i=E.index+1,math.min(#guide.steps,E.index+9) do
        local candidate=guide.steps[i]
        if type(candidate)~="table" then break end
        if candidate.level and candidate.level>s.level then break end
        if candidate.fly or candidate.train or candidate.vendor or candidate.repair
            or candidate.hearth or candidate.sethearth then break end
        if candidate.go and not candidate.accept and not candidate.complete and
            not candidate.turnin then break end
        local applies=not Ding.StepApplies or B.Call(Ding.StepApplies,Ding,candidate)==true
        local isComplete=E.IsStepDone and B.Call(E.IsStepDone,E,candidate,i)==true
        if applies and not isComplete then
            local q,kind,instruction=self:QuestAction(s,E,candidate,true)
            if q and kind then
                local where=self:Point(s,E,candidate,q,kind,i)
                local seconds=where and B.Call(B.Travel.Seconds,B.Travel,where,s)
                if point(where) and type(seconds)=="number" and seconds>=0 and seconds<=480
                    and B.Travel:Reachable(where,s) then
                    local cost=seconds+(i-E.index)*35
                        +(kind=="accept" and 30 or kind=="turnin" and -40 or 0)
                    local key="route:"..tostring(guide.id)..":"..tostring(i)..":"..tostring(q)
                    local untilAt=B.char and B.char.deferred and B.char.deferred[key]
                    if (type(untilAt)~="number" or untilAt<=time()) and
                        (not bestCost or cost<bestCost) then
                        bestCost=cost
                        best={q=q,kind=kind,step=candidate,index=i,
                            instruction=instruction,point=where}
                    end
                end
            end
        end
    end
    return best
end

function R:Urgent(s)
    local chosen
    for _,a in ipairs(B.Planner and B.Planner.actions or {}) do
        if a.kind=="turnin" and a.q and s.log[a.q] and s.log[a.q].ready and
            type(a.travel)=="number" and a.travel<=40 and
            (not chosen or a.travel<chosen.travel) then chosen=a end
    end
    return chosen
end
function R:Choose(s,winner)
    if not self:Enabled(s) then return winner end
    -- Class progression and a nearby hand-in may interrupt a guide step.
    if winner and winner.classPriority and (winner.kind~="accept" or
        type(winner.travel)=="number" and winner.travel<=180) then return winner end
    local urgent=self:Urgent(s)
    if urgent then return urgent end
    local E=self:Engine(s)
    local guide=E and E.guide
    local step=guide and B.Call(E.CurrentStep,E)
    if type(step)~="table" or type(E.index)~="number" then return winner end
    if step.level and s.level>=step.level then return winner end

    local q,kind,instruction=self:QuestAction(s,E,step)
    local routeIndex=E.index
    local preview=false
    if not q and (#(step.accept or {})>0 or #(step.complete or {})>0 or
        #(step.turnin or {})>0) then
        local ahead=self:LookAhead(s,E,guide)
        if not ahead then return winner end
        q,kind,instruction,step,routeIndex=ahead.q,ahead.kind,ahead.instruction,
            ahead.step,ahead.index
        preview=true
    end
    local target=self:Point(s,E,step,q,kind,routeIndex)
    local note=type(instruction)=="string" and instruction~="" and instruction
        or type(step.note)=="string" and step.note
        or type(step.text)=="string" and step.text or nil
    local node=q and B.Graph and B.Graph:Node(q)
    local name=node and node.name or (q and s.log[q] and s.log[q].title)
    local actionKind=kind or "zone"
    local routeVerb
    if not kind and step.level and s.level<step.level then
        actionKind="grind"
        name=B.T("Atteins le niveau ")..tostring(step.level)
        routeVerb=B.T("OBJECTIF DE NIVEAU")
        note=note or B.T("Étape de niveau : combats adaptés sur ton itinéraire.")
    elseif not kind and (step.train or step.vendor or step.repair or step.fly) then
        actionKind="service"
        name=step.train and B.T("Entraîneur de classe")
            or step.fly and B.T("Maître de vol")
            or step.repair and B.T("Réparation") or B.T("Vendeur")
        routeVerb=B.T("ÉTAPE DE SERVICE")
    elseif not kind and (step.hearth or step.sethearth) then
        name=B.T("Pierre de foyer")
        routeVerb=B.T("ÉTAPE DE TRAJET")
    elseif not kind then
        name=step.text or B.T("Continue l'itinéraire")
        routeVerb=B.T("ÉTAPE DE TRAJET")
    end
    if not q and not target and not step.level then return winner end
    local action={kind=actionKind,q=q,node=node,
        key="route:"..tostring(guide.id)..":"..tostring(routeIndex)..":"..tostring(q or actionKind),
        label=name or tostring(q or ""),point=target,batch={},confidence="ROUTE",
        routeStep=true,routeGuide=guide.title or guide.id,routeGuideId=guide.id,
        routeIndex=routeIndex,routeTotal=#guide.steps,routeVerb=routeVerb,
        routePreview=preview,
        routeSource="Ding",note=note}
    if target and B.Travel and B.Travel.Seconds then
        action.travel=B.Call(B.Travel.Seconds,B.Travel,target,s)
    end
    if B.char and B.char.deferred and type(B.char.deferred[action.key])=="number" and
        B.char.deferred[action.key]>time() then return winner end
    if q and not target then
        action.kind="recovery"
        action.questReview=true
        action.note=B.T("Quête de l'itinéraire sans repère vérifié. Vérifie le journal et la carte.")
    end
    if preview then
        action.note=B.T("Étape réalisable à proximité; le guide principal reste inchangé.")
        action.confidence="ROUTE PREVIEW"
    end
    self.last={guide=guide.id,step=routeIndex,quest=q,kind=action.kind}
    return action
end
function R:SkipStep(a)
    local E=Ding and Ding.Engine
    if a and a.routePreview and a.routeStep and E and E.guide and
        E.guide.id==a.routeGuideId and type(a.key)=="string" and
        B.char and B.char.deferred then
        B.char.deferred[a.key]=time()+300
        B.Recovery:Replan()
        return true
    end
    if not a or not a.routeStep or not E or not E.guide or
        E.guide.id~=a.routeGuideId or E.index~=a.routeIndex or
        type(E.Skip)~="function" then return false end
    local old=E.index
    B.Call(E.Skip,E)
    if E.index==old then return false end
    if B.Trace then B.Trace("route","Skipped step "..tostring(old)) end
    if B.Recovery and B.Recovery.Replan then B.Recovery:Replan() end
    return true
end
function R:Reset()
    self.syncAt=0;self.last=nil;self.lastTarget=nil
    if B.Recovery and B.Recovery.Replan then B.Recovery:Replan() end
end
function R:Status()
    local E=Ding and Ding.Engine
    local g=E and E.guide
    return {enabled=B.settings and B.settings.speedRoute~=false and B.Mode()=="fast",
        guide=g and g.title or nil,index=E and E.index or nil,
        total=g and #g.steps or nil}
end
