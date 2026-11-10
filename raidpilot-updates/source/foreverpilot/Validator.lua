local B=ForeverPilotLeveling
local V={generation=0,offers={}};B.Validator=V
function V:Eligible(n,s,kind)
    if s:IsDone(n.id) then return false,"completed" end
    if kind~="accept" then
        if not s.log[n.id] then return false,"notInLog" end
        if kind=="turnin" then return s.log[n.id].ready,"notReady" end
        return not s.log[n.id].ready,"ready"
    end
    if s.log[n.id] then return false,"inLog" end
    if s.count>=s.limit then return false,"logFull" end
    -- A real offer overrides outdated database prerequisites/restrictions.
    if self.npc and self.offers[n.id] then return true,"LIVE" end
    if not Ding.Data:QuestOpenToPlayer(n.id) then return false,"restriction" end
    if n.min>s.level then return false,"level" end
    for _,q in ipairs(n.exclusive) do if s:IsDone(q) or s.log[q] then return false,"exclusive" end end
    if B.Recovery:IsBlocked(n.id,s) then return false,"notOffered" end
    if B.Recovery:Prerequisite(n,s) then return false,"prerequisite" end
    if #n.startItems>0 then
        local has=false;for _,id in ipairs(n.startItems) do if Ding.Compat.ItemCount(id)>0 then has=true end end
        if not has then return false,"startItem" end
    end
    return true,"DATABASE"
end
function V:Close()
    self.generation=self.generation+1;self.npc=nil;self.dialogQuest=nil;self.dialogOpen=nil;self.offers={};self.checks={}
end
function V:Detail(q,npc,s)
    self:Close();self.dialogOpen=true;self.dialogQuest=q;self.npc=npc
    if type(q)~="number" then return end
    self.offers[q]=true;B.char.unavailable[q]=nil
    B.Knowledge:Offer(q,npc,s,B.Call(GetTitleText))
    self.revision=(self.revision or 0)+1;B.dirty=true
end
function V:Observe(kind,token)
    if token~=self.generation or not self.npc or Ding.Compat.NpcUnitID()~=self.npc then return end
    local s=B.State:Refresh(); local active,available
    if kind=="gossip" then active,available=Ding.Compat.GossipQuests()
    else
        -- Greeting titles are not unique IDs. Only accept a unique DB match.
        active,available=Ding.Compat.GreetingQuests()
        local names={}
        for _,q in ipairs(Ding.Data:NpcQuests(self.npc)) do local name=Ding.Data:QuestName(q); if names[name]~=nil then names[name]=false else names[name]=q end end
        for _,row in ipairs(available or {}) do row.questID=names[row.title] end
    end
    if not active or not available then return end -- unavailable API is not an empty NPC
    self.offers={}
    for _,row in ipairs(available) do if type(row.questID)=="number" then
        self.offers[row.questID]=true;B.char.unavailable[row.questID]=nil;B.Graph:Node(row.questID)
        B.Knowledge:Offer(row.questID,self.npc,s,row.title)
    end end
    if kind=="gossip" then
        for _,row in ipairs(active) do
            if type(row.questID)=="number" and s.log[row.questID] then B.Knowledge:Position(row.questID,"turnin",self.npc,s) end
        end
    end
    for _,q in ipairs(Ding.Data:NpcQuests(self.npc)) do
        if not self.offers[q] and not s.log[q] and not s:IsDone(q) then
            local n=B.Graph:Node(q)
            if n and n.min<=s.level then
                self.checks[q]=(self.checks[q] or 0)+1
                if self.checks[q]>=2 then B.Recovery:Block(q,self.npc,s,"notOffered") end
            end
        end
    end
    B.dirty=true
    self.revision=(self.revision or 0)+1
    if B.Update then
        B.Update()
        if not B.settings.paused then Ding.AutoQuest:OnQuestList(kind=="greeting") end
    end
end
function V:Open(kind)
    self:Close();self.npc=Ding.Compat.NpcUnitID();self.checks={}
    local token=self.generation
    C_Timer.After(0.25,function() B.Guard(function() V:Observe(kind,token) end) end)
    C_Timer.After(0.85,function() B.Guard(function() V:Observe(kind,token) end) end)
end
