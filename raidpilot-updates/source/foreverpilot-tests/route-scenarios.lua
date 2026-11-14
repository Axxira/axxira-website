local B=ForeverPilotLeveling
local R=B.SpeedRoute
local E=Ding.Engine
local s=TEST.state
local fallback={key="fallback",kind="complete",q=998,score=1}
assert(R:Enabled(s),"Route should be enabled on the free FastXP edition")
local a=R:Choose(s,fallback)
assert(TEST.resumes==1 and E.guide.id=="h33","Must choose compatible level-33 guide")
assert(a.routeStep and a.kind=="accept" and a.q==1110,"Initial guide pickup missing")
assert(a.point and a.point.x==63 and a.point.y==52,"Pickup must use its own NPC")
assert(a.routeIndex==1 and a.routeTotal==6,"Progress index should be visible")
E.index=2;s.log[1110]={level=33,ready=false,objectives={}}
a=R:Choose(s,fallback)
assert(a.kind=="complete" and a.q==1110,"Quest completion must follow accepted pickup")
assert(a.point and a.point.x==65,"Waypoint must target the chosen quest, not unrelated sticky points")
E.index=3
a=R:Choose(s,fallback)
assert(a.kind=="zone" and a.point.map==1441 and a.point.x==60,"Travel step is a genuine waypoint")
assert(a.routeVerb=="ÉTAPE DE TRAJET","Travel step must use a clear action verb")
E.index=4
a=R:Choose(s,fallback)
assert(a.kind=="grind" and a.point.x==54 and a.label=="Atteins le niveau 34",
 "Explicit guide level step must be retained")
E.index=5;B.Recovery.skipped[999]=true
a=R:Choose(s,fallback)
assert(a==fallback,"Skipped quest cannot lead to a misleading route waypoint")
E.index=6;s.log[339]={level=40,ready=false,objectives={{numRequired=4,numFulfilled=1}}}
a=R:Choose(s,fallback)
assert(a==fallback,"Level-40 chapter must not take over at player level 33")
E.index=2
local urgent={key="urgent",kind="turnin",q=97328,travel=8}
B.Planner.actions={urgent};s.log[97328]={ready=true}
a=R:Choose(s,fallback)
assert(a==urgent,"Nearby verified hand-in should interrupt the guide")
B.Planner.actions={}
s.log[97328]=nil
local priority={key="class",kind="complete",q=777,classPriority=true,travel=200}
assert(R:Choose(s,priority)==priority,"Class progression must keep priority")
a=R:Choose(s,fallback)
assert(a.routeStep and a.routeIndex==2,"Guide step must return after hand-in")
B.Recovery.Avoided=function(self,q,p,state,target)return q==1110 end
local blocked=R:Choose(s,fallback)
assert(blocked.kind=="recovery" and blocked.point==nil,
 "Exhausted quest spawn points cannot be replaced by unrelated waypoints")
B.Recovery.Avoided=nil
B.char={deferred={["route:h33:2:1110"]=150}}
time=function()return 100 end
assert(R:Choose(s,fallback)==fallback,"Temporarily deferred guide step must not override fallback")
B.char.deferred={}
a=R:Choose(s,fallback)
assert(R:SkipStep(a),"Player should be able to skip a route step explicitly")
assert(E.index==3 and TEST.replans==1,"Skipping should move the cursor and replan")
B.settings.speedRoute=false
assert(not R:Enabled(s) and R:Choose(s,fallback)==fallback,"Disabling route restores previous planner")
B.settings.speedRoute=true
B.mode="zone"
assert(R:Choose(s,fallback)==fallback,"Free zone mode remains unchanged")
B.mode="fast"
E.guide={id="different",pack="ding-zones",steps={}}
s.combat=true;s.now=120
assert(R:Choose(s,fallback)==fallback and TEST.resumes==1,"Do not switch guides in combat")
s.combat=false;s.now=200
a=R:Choose(s,fallback)
assert(E.guide.id=="h33" and TEST.resumes==2,"Resync to compatible guide after combat")
assert(type(R:Signature())=="string","Route signature should report the current step")
return "PASS SpeedRoute: level bracket, auto-resume, real quest coordinates, zone transfer, level step, blocked quests, close hand-in, class, manual skip, disabled mode, combat guard"
