-- FastXp measured grind recommendation. Never assumes unobserved XP or a kill.
local B=ForeverPilotLeveling
local G={samples=0,totalXP=0,totalSeconds=0};B.Grind=G
function G:QuestTurnIn(now)
 self.lastTurnIn=now or B.Now()
end
function G:Observe(s)
 if not s or B.Mode()~="fast" or type(s.now)~="number" then self.previous=nil;return end
 local last=self.previous
 self.previous={now=s.now,level=s.level,map=s.map,xp=s.xp,combat=s.combat,
    afk=s.afk,dead=s.dead,signature=B.Planner and B.Planner:Signature(s)}
 if not last or not s.xp or not last.xp or s.level~=last.level
    or s.map~=last.map or s.xp<=last.xp or s.dead or s.afk
    or not (last.combat or s.combat) then return end
 if self.lastTurnIn and s.now-self.lastTurnIn<8 then return end
 local dt=s.now-last.now
 if type(dt)~="number" or dt<1 or dt>8 then return end
 local amount=s.xp-last.xp
 -- Reject large quest rewards and anomalous short-duration XP spikes.
 local upper=math.max(500,math.floor(40+s.level*s.level*4))
 if amount<=0 or amount>upper then return end
 local bucket=B.char and B.char.grindTelemetry
 if type(bucket)~="table" or bucket.level~=s.level then
  bucket={level=s.level,samples=0,totalXP=0,totalSeconds=0,lastAt=s.now}
  if B.char then B.char.grindTelemetry=bucket end
 end
 bucket.samples=math.min(200,bucket.samples+1)
 bucket.totalXP=math.min(100000000,bucket.totalXP+amount)
 -- Count time conservatively; one awarded kill must never imply a 1-second kill rate.
 bucket.totalSeconds=math.min(200000, bucket.totalSeconds+math.max(12,dt))
 bucket.lastAt=s.now
end
function G:Rate(s)
 if not s or not B.char then return nil end
 local telemetry=B.char.grindTelemetry
 if type(telemetry)~="table" or telemetry.level~=s.level
    or type(telemetry.samples)~="number" or telemetry.samples<5
    or type(telemetry.lastAt)~="number" or s.now-telemetry.lastAt>1200
    or type(telemetry.totalXP)~="number" or type(telemetry.totalSeconds)~="number"
    or telemetry.totalSeconds<=0 then return nil end
 return telemetry.totalXP*3600/telemetry.totalSeconds
end
function G:QuestRate(winner)
 if not winner then return nil end
 if winner.plan and winner.plan.xp and winner.plan.seconds and winner.plan.seconds>0 then
  return winner.plan.xp*3600/math.max(90,winner.plan.seconds)
 end
 if winner.xp and winner.travel and winner.work then
  return winner.xp*3600/math.max(90,winner.travel+winner.work)
 end
end
function G:Choose(s,winner)
 if B.Mode()~="fast" or not s or not winner or not winner.q
    or winner.classPriority or winner.kind=="turnin" or s.combat or s.dead
    or s.taxi or s.afk or (s.health and s.health<.55)
    or (s.mana and s.mana<.35) or (s.durability and s.durability<.4) then
  return winner
 end
 if winner.kind=="complete" and winner.node and
    s.log[winner.q] and B.Progression:Progress(s.log[winner.q])>.6 then return winner end
 local measured=self:Rate(s)
 local quest=self:QuestRate(winner)
 if not measured or not quest or measured<=quest*1.3 then return winner end
 local candidate=B.Planner:Fallback(s)
 if not candidate or candidate.kind~="grind" or not candidate.point
    or candidate.point.map~=s.map or not B.Travel:Reachable(candidate.point,s) then
  return winner
 end
 local travel=B.Travel:Seconds(candidate.point,s)
 if type(travel)~="number" or travel>220 then return winner end
 -- Switch only when measured XP/hour after amortizing the travel is superior.
 local adjusted=measured*math.max(.45,300/(300+travel))
 if adjusted<=quest*1.25 then return winner end
 candidate.measuredRate=math.floor(adjusted+.5)
 candidate.questRate=math.floor(quest+.5)
 candidate.note=B.T("Grind conseillé selon l'XP observée, puis retour aux quêtes si le rendement baisse.")
 candidate.confidence="MEASURED"
 candidate.fastGrind=true
 return candidate
end
