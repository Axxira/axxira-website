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
    self:TickPickup(s,a)
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
        self.notice={text=B.T("Progression bloquée · nouvel itinéraire"),untilAt=s.now+15}
        B.Trace("recovery",a.key..":noProgress");self.watch=nil;B.dirty=true
        self.revision=(self.revision or 0)+1
    end
end
function R:CanReportUnavailable(a,s)
    if not a or a.kind~="accept" or not a.q or a.itemStart or not a.point or not a.point.npc or not s or s.log[a.q] or s:IsDone(a.q) then return false end
    if B.Validator.dialogQuest==a.q or (B.Validator.npc and B.Validator.offers[a.q]) then return false end
    local d=B.Travel:Distance(a.point,s)
    return d~=nil and d<=25
end
function R:Unavailable(a,s,reason)
    if not self:CanReportUnavailable(a,s) then return false end
    -- Missing interaction is not proof of a universal quest restriction. Keep
    -- it character-local and temporary, cleared by a real offer or prerequisite.
    self:Block(a.q,a.point.npc,s,reason or "reportedUnavailable",1800)
    self.notice={text=B.T("Prise non confirmée · autre étape"),untilAt=s.now+20}
    B.Print(B.T("Quête indisponible ici : ")..(a.node and a.node.name or tostring(a.q))..B.T(". Guidage recalculé; journal conservé. Nouvelle vérification dans 30 min, ou dès qu'un prérequis change."))
    self:Replan();return true
end
function R:TickPickup(s,a)
    local v=B.Validator
    if B.settings.paused or s.afk or s.dead or s.taxi or s.combat or s.moving or (s.count and s.limit and s.count>=s.limit)
        or not self:CanReportUnavailable(a,s) or v.npc or v.dialogOpen
        or s.targetNpc~=a.point.npc or s.targetNear~=true then self.pickupWatch=nil;return end
    local w=self.pickupWatch
    if not w or w.key~=a.key then self.pickupWatch={key=a.key,last=s.now,active=0};return end
    -- Count consecutive nearby, stationary observations only, never elapsed
    -- time across pause, combat, tab-away stalls or a quest-reading dialog.
    local dt=s.now-w.last;w.last=s.now
    if dt<0 or dt>6 then w.active=0;return end
    w.active=w.active+dt
    if w.active>=45 then self:Unavailable(a,s,"pickupUnconfirmed") end
end
function R:Reset()
    if B.Journey then B.Journey:Reset() end
    self.avoid={};self.watch=nil;self.pickupWatch=nil;self.notice=nil;B.char.deferred={};self:Invalidate()
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
function R:Block(q,npc,s,reason,duration)
    local signature=self:Availability(q,s)
    local previous=B.char.unavailable[q]
    if previous and previous.signature==signature and previous.npc==npc and (not previous.expires or previous.expires>time()) then return end
    B.char.unavailable[q]={npc=npc,reason=reason or "notOffered",at=time(),level=s.level,signature=signature,expires=duration and time()+duration or nil}
    B.Trace("recovery",q..":"..(reason or "notOffered"));B.dirty=true
    self.revision=(self.revision or 0)+1
end
function R:IsBlocked(q,s)
    local r=B.char.unavailable[q]
    if r and (not r.expires or r.expires>time()) and r.level==s.level then
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
        self.notice={text=B.T("Quête mise de côté"),untilAt=B.Now()+12}
        B.Print(B.T("Quête mise de côté : ")..B.char.skipped[action.q].name..B.T(". Journal conservé. Clic droit sur Passer pour la reprendre."))
    else
        B.char.deferred[action.key]=time()+300
        self.notice={text=B.T("Étape reportée · 5 min"),untilAt=B.Now()+12}
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
    self.notice={text=B.T("Quête(s) réactivée(s)"),untilAt=B.Now()+12}
    B.Print(q=="all" and B.T("Quêtes écartées réactivées.") or B.T("Quête réactivée : ")..row.name..B.T(". Le guidage recalcule son intérêt."))
    B.Trace("resume",q);self:Replan()
    return true
end
function R:Replan()
    if B.Journey then B.Journey:Reset() end
    self.watch=nil;self.pickupWatch=nil
    B.Planner.current=nil;B.Planner.signature=nil;B.Planner.actions={};B.Planner.farm=nil
    B.Travel.at=nil;B.dirty=true
    self.revision=(self.revision or 0)+1
end
