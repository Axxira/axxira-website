local B=ForeverPilotLeveling
local M=B.SpeedMetrics
local route1={routeStep=true,routeGuideId="h33",routeIndex=4,kind="complete",q=1110}
local route2={routeStep=true,routeGuideId="h33",routeIndex=5,kind="turnin",q=1110}
local function state(t,xp,level,extras)
    local s={now=t,xp=xp,level=level or 33,maxXP=4000,
        paceEligible=true,moving=false,combat=false,taxi=false,dead=false}
    for k,v in pairs(extras or {}) do s[k]=v end
    return s
end
local initial=state(100,250,33)
M:Observe(initial,route1)
for i=1,30 do
    M:Observe(state(100+i*3,250+i*15,33,{moving=i<10}),route1)
end
local d=M:Data()
assert(d.seconds==90 and d.xp==450,"XP/time must reflect actual active ticks")
assert(d.travel>=27 and d.travel<=30,"Travel percentage must be measured from movement")
M:Observe(state(193,715,33),route2)
assert(#d.splits==1 and d.splits[1].step==4,"Finishing the first step records a split")
assert(d.splits[1].xp==465 and d.splits[1].seconds==93,"Split must include last interval")
M:Observe(state(196,745,33,{paceEligible=false}),route2)
M:Observe(state(230,755,33,{paceEligible=true}),route2)
assert(d.seconds==93,"AFK and long gaps must never inflate pacing")
M:Observe(state(233,770,33),route2)
assert(d.seconds==96 and d.xp==480,"Recording resumes after a valid tick")
M:Observe(state(500,180,34),route2)
assert(d.seconds==96,"Disconnected time cannot count")
assert(M:XP({level=33,xp=3980,maxXP=4000},{level=34,xp=100})==120,
 "Level-up XP must not go negative")
assert(M:XP({level=33,xp=3980,maxXP=4000},{level=35,xp=100})==0,
 "Multi-level jumps must remain unknown")
assert(M:XP({level=33,xp=100,maxXP=4000},{level=33,xp=50})==0,
 "Negative XP must be ignored")
assert(M:Summary().sufficient==false,"Short sample must not be benchmark-certified")
M:Report()
assert(#TEST.prints>0,"Report must be visible to player")
M:Flush()
assert(#d.splits==1,"Partial short step should not pollute slow-step stats")
local first=d.splits[1]
assert(M:SlowSteps(2)[1]==nil,"Only steps exceeding 120s are labeled slow")
M:Reset()
local empty=M:Summary()
assert(empty.seconds==0 and empty.xp==0 and #B.char.speedMetrics.splits==0,
 "Reset clears only local performance counters")
-- Stress the bounded stats store without leaking identifiers or player names.
for i=1,175 do
    local start=1000+i*40
    local action={routeStep=true,routeGuideId="h33",routeIndex=i}
    M:Observe(state(start,100,33),action)
    for j=1,6 do M:Observe(state(start+j*3,100+j*5,33),action) end
    M:Observe(state(start+18,130,33),
        {routeStep=true,routeGuideId="h33",routeIndex=i+1})
end
M:Flush()
assert(#M:Data().splits==160,"Recorder must keep the latest 160 valid splits")
assert(M:Data().splits[1].step>=16,"Oldest records must be evicted")
return "PASS speed metrics: XP, travel, AFK, level-up, long gaps, report, local reset, bounded storage"