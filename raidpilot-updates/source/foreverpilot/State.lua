local B=ForeverPilotLeveling
local S={}; B.State=S
function S:Objectives(q)
    local out=Ding.Compat.GetObjectives(q)
    local raw=B.Call(C_QuestLog and C_QuestLog.GetQuestObjectives,q)
    for i,o in ipairs(out) do
        local source=type(raw)=="table" and raw[i]
        if type(source)=="table" then o.objectiveType=B.Public(source.objectiveType) end
    end
    return out
end
function S:Refresh()
    local C=Ding.Compat
    self.knownDone=self.knownDone or {}
    local s={now=B.Now(),level=UnitLevel("player"),faction=UnitFactionGroup("player"),log={},done={},count=C.NumQuestsInLog()}
    s.xp,s.maxXP,s.rested=C.PlayerXP()
    s.map,s.x,s.y=C.GetPlayerMapPosition(); s.continent,s.wx,s.wy=C.GetPlayerWorld()
    s.dead=C.IsDeadOrGhost(); s.ghost=C.IsGhost(); s.combat=B.Call(UnitAffectingCombat,"player")
    s.afk=B.Call(UnitIsAFK,"player") or false
    s.targetNpc=B.Call(C.UnitNpcID,"target")
    s.targetNear=B.Call(CheckInteractDistance,"target",3)
    s.speed=B.Call(GetUnitSpeed,"player") or 0
    local health,maxHealth=B.Call(UnitHealth,"player"),B.Call(UnitHealthMax,"player")
    if health and maxHealth and maxHealth>0 then s.health=health/maxHealth end
    if B.Call(UnitPowerType,"player")==0 then
        local mana,maxMana=B.Call(UnitPower,"player",0),B.Call(UnitPowerMax,"player",0)
        if mana and maxMana and maxMana>0 then s.mana=mana/maxMana end
    end
    if not self.durabilityAt or s.now-self.durabilityAt>5 then
        self.durability=nil;self.durabilityAt=s.now
        for slot=1,19 do
            local now,max=B.Call(GetInventoryItemDurability,slot)
            if now and max and max>0 then self.durability=math.min(self.durability or 1,now/max) end
        end
    end
    s.durability=self.durability
    s.taxi=C.OnTaxi(); s.bags=C.FreeBagSlots(); s.hearth=C.BindLocation()
    s.flights=Ding.char and Ding.char.flights or {}
    s.limit=B.Call(C_QuestLog and C_QuestLog.GetMaxNumQuestsCanAccept) or MAX_QUESTLOG_QUESTS or 20
    for q in pairs(self.knownDone) do s.done[q]=true end
    for q in pairs(B.char.completed) do s.done[q]=true end
    for _,q in ipairs(C.LogQuests()) do
        local level=B.Call(C_QuestLog and C_QuestLog.GetQuestDifficultyLevel,q)
        s.log[q]={ready=C.IsQuestReady(q), objectives=self:Objectives(q),title=B.Call(C_QuestLog and C_QuestLog.GetTitleForQuestID,q),level=type(level)=="number" and level>0 and level or nil}
    end
    function s:IsDone(q)
        if self.done[q] then return true end
        if C.IsQuestCompleted(q) or (Ding.Engine.turnedIn and Ding.Engine.turnedIn[q]) then self.done[q]=true;S.knownDone[q]=true; return true end
        return false
    end
    local prev=self.current
    s.moving=s.speed>0 or (prev and prev.continent==s.continent and prev.wx and s.wx and ((s.wx-prev.wx)^2+(s.wy-prev.wy)^2)>4) or false
    s.paceEligible=B.settings.enabled and not B.settings.paused and not s.afk
    if not prev then self.elapsed=0;self.gained=0;self.travelSeconds=0;self.deadSeconds=0 end
    local dt=prev and s.now-prev.now or 0
    -- Pause/AFK transitions and long observation gaps count neither XP nor time.
    -- Death, taxi and stationary play remain real leveling costs, not hidden time.
    if prev and prev.paceEligible and s.paceEligible and dt>=0 and dt<=10 then
        self.elapsed=(self.elapsed or 0)+dt
        if s.dead and prev.dead then self.deadSeconds=(self.deadSeconds or 0)+dt
        elseif not s.combat and not prev.combat and (s.moving or s.taxi) and (prev.moving or prev.taxi) then
            self.travelSeconds=(self.travelSeconds or 0)+dt
        end
    end
    if prev and prev.paceEligible and s.paceEligible and dt>=0 and dt<=10 and s.xp and prev.xp then
        local delta=s.xp-prev.xp
        if s.level==prev.level+1 then delta=(prev.maxXP or 0)-prev.xp+s.xp end
        if delta>0 and s.level<=prev.level+1 then self.gained=(self.gained or 0)+delta end
    end
    self.started=self.started or s.now; self.current=s
    return s
end
function S:Pace()
    local s=self.current; local elapsed=self.elapsed or 0
    if elapsed<60 or not self.gained or self.gained<=0 then return nil end
    local rate=self.gained*3600/elapsed
    return rate, s.maxXP and s.xp and (s.maxXP-s.xp)/rate*3600
end
