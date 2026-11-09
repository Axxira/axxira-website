local B=ForeverPilotLeveling
B.InitDB()
local s={level=18,map=1421,x=50,y=50,count=2,limit=20,log={},IsDone=function() return false end}
local function action(q,kind,x)
 local p={map=1421,x=x,y=50}
 return {q=q,key=kind..':'..q,kind=kind,point=p,travel=B.Travel:Seconds(p,s),work=120,xp=1000,score=5,
   node={id=q,name='Quest '..q,level=18,min=1,routes={},next={},targets={},ends={p}},levelFit={factor=1}}
end
local hub,field=action(90001,'turnin',10),action(90002,'complete',60)
s.log[hub.q]={ready=true,objectives={}};s.log[field.q]={objectives={}}
B.Planner:Batch({hub,field},s)
assert(hub.returnPenalty<1,'premature return: useful field work 400 yards away ignored')
B.char.trip={ids={hub.q},accepted={[hub.q]=true}};B.Planner.current=hub
local chosen=B.Journey:Choose(hub,{hub,field},s)
assert(chosen==field,'saved trip returned to hub despite nearby held work')
s.count=20;B.Planner:Batch({hub,field},s)
assert(hub.returnPenalty==1,'full journal prevented return')
assert(B.Journey:Choose(hub,{hub,field},s)==field,'full journal sent player home before finishing zone work')
B.char.trip={ids={field.q},accepted={[field.q]=true}};B.Planner.current=field
assert(B.Journey:Choose(hub,{hub,field},s)==field,'ready quest outside the outing interrupted full-journal field work')
print('PASS outings: field work beyond 250 yards, refresh saved outing, full journal does not force backtracking')

s.count=2
local original=B.Copy(field.point)
field.point={map=1421,x=1,y=50};field.travel=280
assert(not B.Planner:BeforeReturn(hub,field,s),'distant work blocked return')
field.point=original;field.travel=B.Travel:Seconds(field.point,s)
field.work=1200;assert(not B.Planner:BeforeReturn(hub,field,s),'long work blocked return');field.work=120
field.node.ends={};assert(not B.Planner:BeforeReturn(hub,field,s),'unknown final destination guessed')
field.node.ends={{map=9999,x=50,y=50}};assert(not B.Planner:BeforeReturn(hub,field,s),'unknown continent transfer counted as saving')
field.node.ends={hub.point};hub.travel=4
assert(not B.Planner:BeforeReturn(hub,field,s),'NPC beside player ignored');hub.travel=200
print('PASS travel guards: distant/long work, missing destination, unknown transfer, NPC already beside player')

local n=field.node;n.level=18;n.min=1;n.targets={{kind='kill',points={{map=1421,x=52,y=50}}}}
n.ends={{map=1421,x=50,y=50}};s.log={};B.char.opportunities=nil
B.Validator.npc=nil;B.Validator.offers={}
assert(not B.Graph:LocalOpportunity(n,s),'unoffered catalog quest treated as available')
B.Validator.npc=123;B.Validator.offers[n.id]=true
assert(B.Graph:LocalOpportunity(n,s),'short local live quest excluded')
assert(not B.char.opportunities,'offer mistaken for pickup')
s.log[n.id]={objectives={}}
assert(B.Graph:LocalOpportunity(n,s) and B.char.opportunities[n.id],'actual pickup not retained')
B.Validator:Close();n.targets[1].points={{map=1421,x=90,y=50}}
assert(B.Graph:LocalOpportunity(n,s),'held opportunity lost simply by walking away')
B.char.opportunities=nil
assert(B.Graph:LocalOpportunity(n,s),'zone work excluded by the old four-minute walk limit')
n.targets[1].points={{map=1421,x=52,y=50}};n.profession=true
assert(not B.Graph:LocalOpportunity(n,s),'profession admitted');n.profession=nil
n.manual=true;assert(not B.Graph:LocalOpportunity(n,s),'manual quest auto-added');n.manual=nil
n.routes={{kind='complete',step={elite=true}}}
assert(not B.Graph:LocalOpportunity(n,s),'group quest auto-added');n.routes={}
n.level=14;assert(not B.Graph:LocalOpportunity(n,s),'obsolete quest admitted');n.level=18
s.log[n.id].objectives={{objectiveType=14,finished=false}}
assert(not B.Graph:LocalOpportunity(n,s),'live profession requirement ignored')
s.log[n.id].objectives={};n.ends={};B.char.opportunities=nil
assert(not B.Graph:LocalOpportunity(n,s),'unknown turn-in admitted')
print('PASS local quests: live proof, actual pickup, durable character selection, level/solo/profession/travel guards')
local class=action(90003,'accept',20);class.node.raw={requiredClasses=1024};class.travel=1800
s.log={};B.Validator:Close()
assert(not B.Planner:ClassPriorityHere(class,s),'distant unconfirmed class pickup forced a detour')
class.travel=100;assert(B.Planner:ClassPriorityHere(class,s),'nearby class quest lost priority')
class.travel=1800;B.Validator.npc=1;B.Validator.offers[class.q]=true
assert(B.Planner:ClassPriorityHere(class,s),'real class offer lost priority')
B.Validator:Close();s.log[class.q]={objectives={}}
assert(B.Planner:ClassPriorityHere(class,s),'held class chain lost priority')
print('PASS class routes: nearby/held/live priority retained, distant unconfirmed pickup does not interrupt local work')
s.log={[field.q]={objectives={}}};B.char.trip={ids={class.q},accepted={}};B.Planner.current=class
field.node.ends={hub.point}
assert(B.Journey:Choose(field,{field,class},s)==field,'old unconfirmed distant pickup locked the outing')

-- Player scenario: collect a whole village before leaving, finish the zone,
-- then hand in together. Eight quests exceeds both previous outing caps.
B.Journey:Reset();B.Planner.current=nil;s.log={};s.count=0
local zone={}
for i=1,8 do
 local a=action(91000+i,'accept',50+(i-1)*2)
 a.node.starts={a.point};a.node.targets={{kind='kill',points={{map=1421,x=85,y=50}}}}
 zone[i]=a
end
local first=B.Journey:Choose(zone[1],zone,s)
assert(first.collection.total==8 and first.collection.confirmed==0,'not all village quests included before departure')
B.Planner.current=first;zone[8].score=9999
assert(B.Journey:Choose(zone[8],zone,s).q==first.q,'pickup not stable before log confirmation')
for i=1,8 do
 local q=91000+i;s.log[q]={objectives={}};s.count=i
 local held=action(q,'complete',85);held.node=zone[i].node;zone[i]=held
 local a=B.Journey:Choose(held,zone,s)
 if i<8 then assert(a.kind=='accept' and a.collection.confirmed==i,'departed before all pickups confirmed')
 else assert(a.kind=='complete' and a.outing.remaining==8,'did not start the full zone after collection') end
 B.Planner.current=a
end
local savedTrip=B.Copy(B.char.trip);B.char.trip=B.Copy(savedTrip)
for i=1,7 do
 s.log[91000+i].ready=true;zone[i]=action(91000+i,'turnin',10)
end
s.count=20;zone[8].travel=400
local a=B.Journey:Choose(zone[1],zone,s)
assert(a.q==91008 and a.kind=='complete' and a.outing.remaining==1,'returned with distant zone work unfinished after reload/full journal')
local remote=action(91999,'accept',50);remote.node.targets={{points={{map=1421,x=85,y=50}}}};remote.travel=300
-- Recalculate the player location after departure; the village is no longer beside us.
s.x=85;B.Planner.current=a;zone[#zone+1]=remote
a=B.Journey:Choose(remote,zone,s)
assert(a.q==91008,'left field work for a new pickup back in the village')
s.log[91008].ready=true;zone[8]=action(91008,'turnin',10)
a=B.Journey:Choose(zone[1],zone,s)
assert(a.kind=='turnin' and a.outing.phase=='return' and a.outing.ready==8,'did not return after all zone objectives finished')
assert(B.UI:View(a,s).footer==string.format(B.T('RETOUR HUB · %d à rendre'),8),'return phase/count missing from compact UI')
-- An unavailable action is removed; completed and excluded quests cannot trap it.
B.Journey:Reset();s.x=50;s.count=0;s.log={};B.Planner.current=nil
local one,two=action(92001,'accept',50),action(92002,'accept',51)
one.node.targets={{points={{map=1421,x=70,y=50}}}};two.node.targets=one.node.targets
B.Journey:Choose(one,{one,two},s)
s.log[one.q]={objectives={}};one.kind='complete';one.key='complete:'..one.q;one.point={map=1421,x=70,y=50};s.count=1
a=B.Journey:Choose(one,{one},s)
assert(a==one and #a.tripIDs==1,'unavailable companion prevented departure')
local nearby=action(92003,'turnin',50);nearby.travel=2;s.log[nearby.q]={ready=true,objectives={}}
a=B.Journey:Choose(nearby,{one,nearby},s)
assert(a==nearby and a.outing.nearby and a.outing.remaining==1,'nearby hand-in reset the unfinished outing')
assert(B.UI:View(a,s).footer==B.T('REMETS ICI · PUIS ON CONTINUE'),'nearby hand-in misleadingly requested hub return')
assert(B.Journey:Choose(one,{one},s)==one,'did not resume field work after nearby hand-in')
-- Journal capacity, foreign-zone errands and the top-60 search window.
B.Journey:Reset();B.Planner.current=nil;s.log={};s.count=18
local picks={};for i=1,8 do picks[i]=action(93000+i,'accept',50+i);picks[i].node.targets={{points={{map=1421,x=70,y=50}}}} end
a=B.Journey:Choose(picks[1],picks,s)
assert(#a.tripIDs==2,'pickup reservation exceeds remaining journal capacity')
B.Journey:Reset();s.count=1;s.log={[one.q]={objectives={}}};one.travel=300
local village={one};for _,p in ipairs(picks) do village[#village+1]=p end
a=B.Journey:Choose(one,village,s)
assert(a.kind=='accept' and a.collection.total==9,'existing field quest prevented collecting the whole village before departure')
-- Return round: hand-ins across the same village finish before newly acquired work.
B.Journey:Reset();s.log={[one.q]={objectives={}},[nearby.q]={ready=true,objectives={}}};s.count=2
nearby.travel=40;nearby.point={map=1421,x=57,y=50}
B.char.trip={schema=2,zone=1421,hub={map=1421,x=50,y=50},phase='return',ids={nearby.q},accepted={[nearby.q]=true}}
a=B.Journey:Choose(one,{one,nearby},s)
assert(a==nearby,'departed return hub before grouped hand-ins were finished')
B.Journey:Reset();s.count=0;picks[2].node.targets={{points={{map=9999,x=50,y=50}}}}
s.log={}
a=B.Journey:Choose(picks[1],picks,s)
for _,q in ipairs(a.tripIDs) do assert(q~=picks[2].q,'foreign-zone errand added to local pickup batch') end
local many={};for i=1,70 do many[i]=action(94000+i,'complete',70) end
assert(#B.Planner:Batch(many,s)==70,'quest list truncated to beam search window')
-- Zone catalog suggestions remain unconfirmed until the server offers them.
local catalog=action(95001,'accept',50).node
catalog.starts={{map=1421,x=50,y=50}};catalog.ends=catalog.starts
catalog.targets={{points={{map=1421,x=90,y=50}}}}
catalog.routes={{kind='accept',guide={pack='ding-zones'},step={}}}
B.Validator.offers={};B.Validator.npc=nil;B.char.opportunities=nil;s.log={}
assert(B.Graph:LocalOpportunity(catalog,s),'known solo zone catalog quest requires prior NPC visit')
assert(not B.char.opportunities and not s.log[catalog.id],'catalog suggestion fabricated pickup')
catalog.targets={{points={{map=9999,x=90,y=50}}}}
assert(not B.Graph:LocalOpportunity(catalog,s),'zone catalog imported unknown foreign destination')
print('PASS zone batch: eight pickups, real confirmation, full journal, reload, distant last objective, late pickup, grouped return, unavailable quest, nearby hand-in, capacity, foreign zone, 70 candidates, catalog confidence, compact UI')

-- Actual Items:Compare path: no mock verdicts, no class-only reward weights.
TEST.class='DRUID';TEST.level=18;TEST.combat=false
B.Items:SetProfile('feral');B.Knowledge.db.quests={}
local records={
 ['item:70001']={agi=4,armor=31,quality=2},
 ['item:70002']={agi=10,armor=40,quality=3},
 ['item:70003']={int=3,armor=16,quality=3},
}
C_Item={GetItemInfo=function(link)
 local r=records[link];if not r or r.unloaded then return end
 return 'Test item',link,r.quality,18,1,'Armor','Leather',1,'INVTYPE_WAIST',1,1,4,2,2,0,0
end,GetItemStats=function(link)
 local r=records[link];if not r then return end
 return {ITEM_MOD_AGILITY_SHORT=r.agi,ITEM_MOD_INTELLECT_SHORT=r.int,RESISTANCE0_NAME=r.armor}
end,GetItemSpell=function(link) return records[link] and records[link].spell end,
GetItemUniquenessByID=function() return false end}
C_PlayerInfo={CanUseItem=function() return true end}
GetInventoryItemLink=function(_,slot) if slot==6 then return 'item:70001' end end
GetInventoryItemTexture=function(_,slot) if slot==6 then return 1 end end
InCombatLockdown=function() return TEST.combat end
local dialog=90004;Ding.Compat.DialogQuestID=function() return dialog end
GetNumQuestRewards=function() return 0 end;GetNumQuestChoices=function() return 2 end
GetQuestItemLink=function(kind,i) return i==1 and 'item:70002' or 'item:70003' end
B.Knowledge:ReadRewards(true)
local reward=B.Items:QuestReward(dialog)
assert(reward and reward.link=='item:70002' and reward.bonus<=.25,'useful blue feral reward not selected / uncapped bonus')
assert(B.Items:Compare('item:70002').status=='compare','rare effect uncertainty hidden')
records['item:70002'].spell='Unknown proc'
assert(not B.Items:QuestReward(dialog),'proc or intellect-only blue preferred for feral')
B.Items:SetProfile('caster')
assert(B.Items:QuestReward(dialog).link=='item:70003','reward not recalculated for specialization')
records['item:70003'].unloaded=true
assert(not B.Items:QuestReward(dialog),'uncached reward invented');records['item:70003'].unloaded=nil
C_PlayerInfo.CanUseItem=function() return false end
assert(not B.Items:QuestReward(dialog),'unusable reward boosted route')
C_PlayerInfo.CanUseItem=function() return true end
TEST.combat=true;assert(not B.Items:QuestReward(dialog),'protected combat comparison used');TEST.combat=false
local at=B.Knowledge.db.quests[dialog].rewardsAt
B.Knowledge.db.quests[dialog].rewardsAt=at-31*86400
assert(not B.Items:QuestReward(dialog),'stale rewards used across changes')
B.Knowledge.db.quests[dialog].rewardsAt=at
GetQuestItemLink=function() return nil end;B.Knowledge:ReadRewards(true)
assert(#B.Knowledge:Rewards(dialog)==2,'partial loading erased known reward data')
dialog=nil;C_QuestLog={GetSelectedQuest=function() return 90005 end}
GetQuestItemLink=function() return 'item:70003' end;B.Knowledge:ReadRewards(true)
assert(not B.Knowledge.db.quests[90005],'dialog reward assigned to unrelated journal quest')
GetNumQuestLogRewards=function() return 1 end;GetNumQuestLogChoices=function() return 0 end
GetQuestLogItemLink=function() return 'item:70003' end
B.Knowledge:ReadRewards(false)
assert(B.Knowledge:Rewards(90005)[1].link=='item:70003','selected journal reward not captured')
print('PASS rare rewards: observed links, class/spec stats, bounded bonus, no guessed availability, unknown/proc/unusable/combat/stale safeguards')
