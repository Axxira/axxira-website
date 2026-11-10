local B=ForeverPilotLeveling
B.InitDB();TEST.class='DRUID';TEST.race='Troll';TEST.level=23;TEST.map=1424;TEST.x=64.05;TEST.y=53.84
TEST.log={};TEST.done={};B.Graph:Build();B.Travel.reach=nil
local checks=0
local function check(value,message) checks=checks+1;assert(value,message) end
local s=B.State:Refresh();s.done[98094]=true
local a=B.Planner:Action(B.Graph:Node(98095),'accept',s)
local p=B.Loop:Project(a,s);local work=0
for _,t in ipairs(p.tasks) do work=work+t.work end
check(work==335,'unmapped Masoj objective disappeared from estimated work')
check(a.uncertainWork,'unknown route presented without uncertainty')
check(#p.tasks==2,'invented objective waypoint')
B.char.learning[98095]={samples=1,duration=720}
p=B.Loop:Project(a,s);work=0;for _,t in ipairs(p.tasks) do work=work+t.work end
check(work==755,'learned duration ignored for unmapped work')
B.char.learning[98095]=nil
TEST.log[98094]={ready=true,objectives={}};s=B.State:Refresh()
a=B.Planner:Action(B.Graph:Node(98094),'turnin',s);p=B.Loop:Project(a,s)
check(#p.tasks==1 and p.tasks[1].work==15,'real ready delivery penalized as unknown work')
local unknown=B.Planner:Action(B.Graph:Node(98095),'accept',s)
if not unknown then
 s.done[98094]=true;s.log[98094]=nil;unknown=B.Planner:Action(B.Graph:Node(98095),'accept',s)
end
check(not B.Journey:LocalPickup(unknown,{hub=unknown.point,zone=s.map},s),'unmapped quest silently added to local batch')

-- Useful immediate follow-ups earn credit only after the projected parent.
local function point(x,y) return {map=1424,x=x,y=y or 50} end
local function node(id)
 local n={id=id,name='Fixture '..id,level=23,min=20,any={},all={},exclusive={},next={},routes={},startItems={},fast=true,raw={},
  starts={point(50)},ends={point(50)},targets={{kind='kill',points={point(55)}}}}
 B.Graph.nodes[id]=n;return n
end
local parent,child=node(991100),node(991101);parent.next[child.id]=true;child.all={parent.id}
local open=Ding.Data.QuestOpenToPlayer;Ding.Data.QuestOpenToPlayer=function(_,q) return q==child.id or open(Ding.Data,q) end
TEST.log={};s=B.State:Refresh();s.done={};s.log={}
local function bonus() s.chainCounts=nil;return B.Progression:ChainCount(parent,s) end
check(bonus()==1,'useful unlocked follow-up lost')
check(not s:IsDone(parent.id) and not s.log[parent.id],'projection changed actual completion')
child.min=24;check(bonus()==0,'unavailable level received chain bonus');child.min=20
child.level=30;check(bonus()==0,'far future chain received current XP bonus');child.level=23
B.char.skipped[child.id]={};check(bonus()==0,'skipped chain rewarded');B.char.skipped[child.id]=nil
s.done[child.id]=true;check(bonus()==0,'completed follow-up rewarded');s.done[child.id]=nil
s.log[child.id]={};check(bonus()==0,'already accepted child rewarded');s.log[child.id]=nil
child.all={parent.id,991199};check(bonus()==0,'other missing parent ignored');child.all={parent.id}
child.all={-parent.id};check(bonus()==0,'active-parent dependency treated as completed-parent unlock')
child.all={};child.any={parent.id,991199};s.done[991199]=true
check(bonus()==0,'satisfied OR branch rewarded');s.done[991199]=nil;child.any={};child.all={parent.id}
child.exclusive={parent.id};check(bonus()==0,'parent excludes child but received bonus');child.exclusive={}
child.starts={point(90)};check(bonus()==0,'remote pickup chain bonus');child.starts={point(50)}
child.routes={{kind='complete',step={elite=true}}};check(bonus()==0,'solo planner rewarded group chain');child.routes={}
child.targets={};check(bonus()==0,'unknown continuation rewarded');child.targets={{kind='kill',points={point(55)}}}
Ding.Data.QuestOpenToPlayer=function() return false end;check(bonus()==0,'wrong class/race follow-up rewarded');Ding.Data.QuestOpenToPlayer=open

-- Multi-start closed outings never cost more than the old nearest-first route.
s={map=1424,x=50,y=50,level=23,log={}}
local trip={hub=point(10)}
local function action(id,x,y)
 local pt=point(x,y);return {q=id,key='complete:'..id,point=pt,kind='complete',travel=B.Travel:Distance(pt,s)/7}
end
local function cost(first,list)
 local used={[first]=true};local at=first;local total=first.travel
 for _=2,#list do
  local best,d
  for _,v in ipairs(list) do if not used[v] then
   local x=B.Travel:Distance(at.point,v.point)/7
   if not d or x<d or (x==d and v.key<best.key) then best,d=v,x end
  end end
  used[best]=true;total=total+d;at=best
 end
 return total+B.Travel:Distance(at.point,trip.hub)/7
end
local list={action(1,45),action(2,60),action(3,75)};B.Planner.current=nil
local selected=B.Journey:OrderWork(list,trip,s)
local before,after=cost(list[1],list),cost(selected,list)
check(after<before-40,'closed outing did not improve backtracking fixture')
print(string.format('SIMULATED route only: nearest %.1fs / selected %.1fs (not measured in game)',before,after))
local seed=17
local function rand() seed=(seed*1103515245+12345)%2147483647;return 15+seed%70 end
for sample=1,50 do
 list={};for id=1,6 do list[id]=action(id,rand(),rand()) end
 local nearest=list[1];for _,v in ipairs(list) do if v.travel<nearest.travel then nearest=v end end
 selected=B.Journey:OrderWork(list,trip,s)
 check(cost(selected,list)<=cost(nearest,list)+0.001,'closed route worse than nearest baseline')
 check(B.Journey:OrderWork(list,trip,s)==selected,'unstable tie/order result')
end
list={action(1,45),action(2,60),action(3,75)}
B.Planner.current=list[1];s.combat=true;check(B.Journey:OrderWork(list,trip,s)==list[1],'direction changed mid-combat');s.combat=false
list[1].point=point(49.5);list[1].travel=B.Travel:Distance(list[1].point,s)/7
check(B.Journey:OrderWork(list,trip,s)==list[1],'nearly reached objective abandoned')
list[3].classPriority=true;check(B.Journey:OrderWork(list,trip,s)==list[3],'class priority lost')
list[3].classPriority=nil;trip.hub={map=9999,x=20,y=20}
check(B.Journey:OrderWork(list,trip,s)==list[1],'invented cross-continent optimization')
B.Planner.current=nil

-- Real session denominator includes travel/death, but excludes paused/AFK gaps.
local playerXP=Ding.Compat.PlayerXP;local afk=UnitIsAFK;local speed=GetUnitSpeed
local xp,maxXP=100,10000
Ding.Compat.PlayerXP=function() return xp,maxXP,0 end
UnitIsAFK=function() return TEST.afk end;GetUnitSpeed=function() return TEST.speed or 0 end
B.settings.enabled=true;B.settings.paused=false;B.State.current=nil;TEST.clock=1000;TEST.level=23;TEST.dead=false;TEST.combat=false;TEST.afk=false;TEST.speed=7
B.State:Refresh()
for _=1,24 do TEST.clock=TEST.clock+3;xp=xp+10;B.State:Refresh() end
local rate=B.State:Pace();check(math.abs(rate-12000)<0.01,'observed XP/hour denominator incorrect')
check(B.State.elapsed==72 and B.State.travelSeconds==72,'travel missing from session cost')
B.settings.paused=true;TEST.clock=TEST.clock+3;xp=xp+500;B.State:Refresh()
TEST.clock=TEST.clock+600;xp=xp+1000;B.State:Refresh();B.settings.paused=false;TEST.clock=TEST.clock+3;B.State:Refresh()
check(B.State.elapsed==72 and B.State.gained==240,'pause leaked XP or time')
TEST.afk=true;TEST.clock=TEST.clock+3;xp=xp+100;B.State:Refresh();TEST.afk=false;TEST.clock=TEST.clock+3;B.State:Refresh()
check(B.State.gained==240,'AFK leaked XP')
TEST.clock=TEST.clock+100;xp=xp+100;B.State:Refresh();check(B.State.elapsed==72 and B.State.gained==240,'unobserved gap included')
TEST.dead=true;TEST.speed=0;TEST.clock=TEST.clock+3;B.State:Refresh();TEST.clock=TEST.clock+3;B.State:Refresh()
check(B.State.deadSeconds==3 and B.State.elapsed==78,'death hidden from real efficiency')
TEST.dead=false;xp=9990;TEST.clock=TEST.clock+100;B.State:Refresh();TEST.level=24;xp=30;TEST.clock=TEST.clock+3;B.State:Refresh()
check(B.State.gained==280,'level-up XP difference incorrect')
Ding.Compat.PlayerXP=playerXP;UnitIsAFK=afk;GetUnitSpeed=speed
print('PASS efficiency: '..checks..' checks; unmapped work, useful chains, bounded outings, real session pace')
