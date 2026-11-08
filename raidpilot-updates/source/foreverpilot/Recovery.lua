local B=ForeverPilotLeveling
local R={avoid={}};B.Recovery=R
function R:Signature(log)
    if not log then return "none" end
    local out={log.ready and "ready" or "active"}
    for i,o in ipairs(log.objectives or {}) do out[#out+1]=i..":"..tostring(o.numFulfilled or 0)..":"..tostring(o.finished) end
    return table.concat(out,"|")
end
function R:Avoided(q,p,s,target)
    for _,r in ipairs(self.avoid[q] or {}) do
        if r.untilAt>s.now and (not r.target or not target or r.target==target.name) then
            local d=B.Travel:Distance(r.point,p)
            if d and d<140 then return true end
        end
    end
    return false
end
function R:Tick(s,a)
    if B.settings.paused or s.afk or s.dead or s.taxi or not a or a.kind~="complete" or not a.q then self.watch=nil;return end
    local sig=self:Signature(s.log[a.q]);local w=self.watch
    if not w or w.key~=a.key or w.signature~=sig then
        if w and w.key==a.key and w.signature~=sig then self.avoid[a.q]=nil end
        self.watch={key=a.key,signature=sig,last=s.now,active=0};return
    end
    local dt=math.max(0,math.min(3,s.now-w.last));w.last=s.now
    local d=B.Travel:Distance(a.point,s)
    if s.moving or s.combat then w.engaged=s.now end
    -- A player taking a break must not be mistaken for a failed objective.
    if d and d<140 and w.engaged and s.now-w.engaged<20 then w.active=w.active+dt end
    local threshold=a.target and a.target.kind=="drop" and 240 or 180
    if w.active>=threshold and not s.combat then
        local list=self.avoid[a.q] or {};self.avoid[a.q]=list
        for i=#list,1,-1 do if list[i].untilAt<=s.now then table.remove(list,i) end end
        list[#list+1]={point=B.Copy(a.point),target=a.target and a.target.name,untilAt=s.now+600}
        if #list>=3 then B.char.deferred[a.key]=time()+300 end
        while #list>6 do table.remove(list,1) end
        self.notice={text="Progression bloquée · nouvel itinéraire",untilAt=s.now+15}
        B.Trace("recovery",a.key..":noProgress");self.watch=nil;B.dirty=true
        self.revision=(self.revision or 0)+1
    end
end
function R:Reset()
    if B.Journey then B.Journey:Reset() end
    self.avoid={};self.watch=nil;self.notice=nil;B.char.deferred={};self:Invalidate()
end
function R:Prerequisite(n,s)
    local function holds(q) return q<0 and s.log[-q]~=nil or q>0 and s:IsDone(q) end
    for _,q in ipairs(n.all) do if not holds(q) then return math.abs(q),q<0 end end
    if #n.any>0 then
        for _,q in ipairs(n.any) do if holds(q) then return nil end end
        local best
        for _,q in ipairs(n.any) do
            local p=B.Graph:Node(math.abs(q))
            if p and Ding.Data:QuestOpenToPlayer(p.id) and (not best or p.min<best.min) then best=p end
        end
        return best and best.id or math.abs(n.any[1]),n.any[1]<0
    end
end
function R:Block(q,npc,s,reason)
    local signature=self:Availability(q,s)
    local previous=B.char.unavailable[q]
    if previous and previous.signature==signature and previous.npc==npc then return end
    B.char.unavailable[q]={npc=npc,reason=reason or "notOffered",at=time(),level=s.level,signature=signature}
    B.Trace("recovery",q..":"..(reason or "notOffered"));B.dirty=true
    self.revision=(self.revision or 0)+1
end
function R:IsBlocked(q,s)
    local r=B.char.unavailable[q]
    if r and r.level==s.level then
        r.signature=r.signature or self:Availability(q,s)
        if r.signature==self:Availability(q,s) then return true end
    end
    if r then B.char.unavailable[q]=nil end
    return false
end
function R:Availability(q,s)
    local n=B.Graph:Node(q);local parts={tostring(s.level)}
    for _,list in ipairs(n and {n.any,n.all,n.exclusive} or {}) do
        for _,id in ipairs(list) do
            id=math.abs(id);parts[#parts+1]=id..":"..(s:IsDone(id) and "done" or s.log[id] and "active" or "none")
        end
    end
    return table.concat(parts,"|")
end
function R:Invalidate(q)
    -- Ordinary quest events do not erase evidence about unrelated missing offers.
    -- A relevant prerequisite/level change is checked by IsBlocked. A real offer
    -- clears its own record immediately; /fpl resync explicitly clears all.
    if not q then B.char.unavailable={} end
    self.revision=(self.revision or 0)+1;B.dirty=true
end
function R:Defer(action)
    if not action or not self:CanSkip(action) then return false end
    if B.Journey then B.Journey:Reset() end
    if action.q then
        B.char.skipSequence=(B.char.skipSequence or 0)+1
        B.char.skipped[action.q]={name=action.node and action.node.name or tostring(action.q),at=time(),order=B.char.skipSequence,point=B.Copy(action.point)}
        self.notice={text="Quête mise de côté",untilAt=B.Now()+12}
        B.Print("Quête mise de côté : "..B.char.skipped[action.q].name..". Journal conservé. Clic droit sur Passer pour la reprendre.")
    else
        B.char.deferred[action.key]=time()+300
        self.notice={text="Étape reportée · 5 min",untilAt=B.Now()+12}
    end
    B.Trace("defer",action.key)
    self:Replan()
    return true
end
function R:CanSkip(action)
    return action and ((action.q and (action.kind=="accept" or action.kind=="complete" or action.kind=="turnin")) or action.kind=="grind" or action.kind=="service" or action.kind=="rest")
end
function R:IsSkipped(q)
    return B.char and B.char.skipped and B.char.skipped[q]~=nil
end
function R:LastSkipped()
    local id,latest
    for q,row in pairs(B.char.skipped or {}) do
        if not latest or (row.order or row.at or 0)>(latest.order or latest.at or 0) or ((row.order or row.at or 0)==(latest.order or latest.at or 0) and q>id) then id,latest=q,row end
    end
    return id,latest
end
function R:Resume(q)
    q=q or self:LastSkipped()
    local row=q and B.char.skipped[q]
    if q=="all" then
        if not next(B.char.skipped) then return false end
        B.char.skipped={}
    elseif row then B.char.skipped[q]=nil
    else return false end
    self.notice={text="Quête(s) réactivée(s)",untilAt=B.Now()+12}
    B.Print(q=="all" and "Quêtes écartées réactivées." or "Quête réactivée : "..row.name..". Le guidage recalcule son intérêt.")
    B.Trace("resume",q);self:Replan()
    return true
end
function R:Replan()
    if B.Journey then B.Journey:Reset() end
    self.watch=nil
    B.Planner.current=nil;B.Planner.signature=nil;B.Planner.actions={};B.Planner.farm=nil
    B.Travel.at=nil;B.dirty=true
    self.revision=(self.revision or 0)+1
end
