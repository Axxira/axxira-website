local B=ForeverPilotLeveling
B.InitDB();TEST.class='DRUID';TEST.race='Skyborne';TEST.level=20;TEST.map=1638;TEST.x=50;TEST.y=50;TEST.log={};TEST.done={}
B.char.completed={};B.char.deferred={};B.char.skipped={};B.char.unavailable={};B.char.flightDiscoveries={};B.State.knownDone={};B.char.trip=nil
B.Graph:Build()
local count=0
local function check(value,message) count=count+1;assert(value,message) end
local function state() B.Planner.signature=nil;return B.State:Refresh() end
local s=state()
check(#B.Graph:Node(98340).routes>0 and B.Graph.classQuests[98340], 'class guide must supply actual graph routes')
check(not B.Graph.classQuests[98405] and B.Graph.classQuests[98341], 'class guide race filter')
check(B.Graph:Node(98341).any[1]==98340 and B.Graph:Node(98404).any[1]==98341 and B.Graph:Node(98362).any[1]==98738,'explicit Forever class chain links')
check(#B.List((Ding.Data:QuestPrereqs(98341)))==0,'Ding data must remain unchanged')
local ok,why=B.Validator:Eligible(B.Graph:Node(98341),s,'accept')
check(not ok and why=='prerequisite','no premature class follow-up')
B.Validator.npc=1;B.Validator.offers={[98341]=true}
check(B.Validator:Eligible(B.Graph:Node(98341),s,'accept'),'real offer overrides inferred class prerequisite')
B.Validator.npc=nil;B.Validator.offers={}
TEST.level=16;s=state();check(not B.Validator:Eligible(B.Graph:Node(98340),s,'accept'),'class quest level requirement')
local bear=B.Graph:Node(94006)
check(B.Journal:Allows(bear,s) and B.Progression:Evaluate(bear,'accept',s).keep=='class','old unfinished class quest still allowed')
B.Travel:Prepare(s);local candidates=B.Planner:Candidates(s);local bearFound=false
for _,a in ipairs(candidates) do if a.q==94006 then bearFound=true end end
check(bearFound,'class quest beyond old five-level window must remain a candidate')
B.char.skipped[94006]={};check(not B.Planner:Action(bear,'accept',s),'skip still respected for class quests');B.char.skipped={}
B.char.unavailable[94006]={npc=1,level=s.level,signature=B.Recovery:Availability(94006,s)}
check(not B.Planner:Action(bear,'accept',s),'unavailable class quest must not trap arrow');B.char.unavailable={}
TEST.log[94006]={ready=true,objectives={}};s=state()
local report=B.Journal:Refresh(s,false);check(report.rows[1].status=='class' and B.Journal:Badge()=='1'..B.T(' DE CLASSE'),'class journal priority')
TEST.log={};s=state()
-- Exercise the real planner, batching, journey commitment and priority tier.
local candidateFn=B.Planner.Candidates
local function action(q,priority,score,x)
 local p={map=s.map,x=x,y=50}
 local n={id=q,name='Quest '..q,level=16,min=1,any={},all={},next={},routes={},ends={p},targets={},raw={requiredClasses=priority and 1024 or 0}}
 return {q=q,node=n,kind='turnin',key='turnin:'..q,classPriority=priority,score=score,xp=score*100,travel=5,work=15,point=p,batch={}}
end
local normal=action(990001,false,1000,51);local class=action(990002,true,0.001,52)
s.log={[normal.q]={ready=true,objectives={}},[class.q]={ready=true,objectives={}}};s.count=2
B.Planner.Candidates=function() return {normal,class} end
B.char.trip={ids={normal.q},accepted={[normal.q]=true},level=16};B.Planner.current=normal
local chosen=B.Planner:Plan(s)
check(chosen.q==class.q and chosen.classPriority,'class priority must override XP and committed ordinary outing')
B.UI:Create();B.UI:Render(chosen,s)
check(B.UI.tag.text==B.T('CLASSE PRIORITAIRE') and B.UI.frame.w<=360 and B.UI.frame.h<=176,'compact class UI')
B.Planner.Candidates=function() return {normal} end;s.now=s.now+6;B.Planner.signature=nil
check(B.Planner:Plan(s).q==normal.q,'ordinary quests resume after class candidate completes')
B.Planner.Candidates=candidateFn
-- Real service lookup remains faction-filtered; simulate server map knowledge.
local F=B.FlightPaths;local services=Ding.Services;local reach=B.Travel.reach
local master={npc=3615,name='Devrak',point={map=1413,x=50,y=50}}
Ding.Services={Nearest=function(_,kind) check(kind=='flight','flight service lookup');return {master} end}
TEST.map=1413;TEST.x=48;TEST.y=50;TEST.log={};s=state();s.flights={};B.Planner.current=nil;B.Travel.reach=nil
local quest={kind='complete',q=1,key='complete:1',point={map=1413,x=45,y=50},tripIDs={1},batch={}}
local unknown=true
C_TaxiMap={GetTaxiNodesForMap=function() return {{position={x=.5,y=.5},faction=1,isUndiscovered=unknown}} end}
local function choose() F.cache=nil;return F:Choose(s,quest) end
local flight=choose();check(flight.service=='flight' and flight.confidence=='LIVE / POSITION DB','nearby undiscovered flight precedes departure')
check(not B.char.flightDiscoveries[3615],'arrival must not count as learning')
B.UI:Render(flight,s);check(B.UI.tag.text==B.T('TRAJET AÉRIEN') and B.UI.verb.text==B.T('PARLE AU MAÎTRE DE VOL'),'same compact guidance UI for flight')
local leg=Ding.Travel.Leg;Ding.Travel.Leg=function() error('discovery must not take a flight') end;B.UI:Tick();Ding.Travel.Leg=leg
s.combat=true;check(choose()==quest,'no combat diversion');s.combat=false
s.taxi=true;check(choose()==quest,'no diversion during taxi');s.taxi=false
s.dead=true;check(choose()==quest,'no dead diversion');s.dead=false
IsInInstance=function() return true end;check(choose()==quest,'no instance diversion');IsInInstance=nil
B.char.deferred[flight.key]=time()+300;check(choose()==quest,'skip discovery respected');B.char.deferred={}
unknown=false;check(choose()==quest,'live known flight suppressed')
unknown=true;s.flights[3615]=true;check(choose()==quest,'Ding known flight suppressed');s.flights={}
TEST.x=20;s=state();s.flights={};check(choose()==quest,'no cross-zone flight chase')
TEST.x=50;s=state();s.flights={};TEST.npc=999;F:Event('TAXIMAP_OPENED');check(not B.char.flightDiscoveries[3615],'wrong NPC must not confirm discovery')
TEST.npc=3615;F:Event('TAXI_NODE_STATUS_CHANGED');check(not B.char.flightDiscoveries[3615],'generic node event is not proof of learning')
F:Event('TAXIMAP_OPENED');check(B.char.flightDiscoveries[3615] and choose()==quest,'opening nearby flight map confirms and resumes quests')
B.InitDB();check(B.char.flightDiscoveries[3615],'confirmation survives database reinit')
local save=ForeverPilotLevelingCharDB;ForeverPilotLevelingCharDB={};B.InitDB();check(not B.char.flightDiscoveries[3615],'flight discovery isolated by character');ForeverPilotLevelingCharDB=save;B.InitDB()
B.char.flightDiscoveries={};C_TaxiMap=nil;TEST.x=48;s=state();s.flights={};flight=choose()
check(flight.service=='flight' and flight.confidence=='DATABASE' and flight.label==B.T('Vérifie ce trajet aérien'),'missing API remains unverified')
local conversation={kind='turnin',point={map=1413,x=48,y=50}};check(F:Choose(s,conversation)==conversation,'finish conversation already in reach')
Ding.Services=services;B.Travel.reach=reach;F.cache=nil
print('PASS: '..count..' class priority and nearby flight discovery checks')


