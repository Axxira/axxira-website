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
assert(B.Journey:Choose(hub,{hub,field},s)==hub,'full journal trapped in field')
B.char.trip={ids={field.q},accepted={[field.q]=true}};B.Planner.current=field
assert(B.Journey:Choose(hub,{hub,field},s)==hub,'full journal ignored a ready quest outside the saved outing')
print('PASS outings: field work beyond 250 yards, refresh saved outing, full journal escape')

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
assert(not B.Graph:LocalOpportunity(n,s),'long local detour admitted')
n.targets[1].points={{map=1421,x=52,y=50}};n.profession=true
assert(not B.Graph:LocalOpportunity(n,s),'profession admitted');n.profession=nil
n.manual=true;assert(not B.Graph:LocalOpportunity(n,s),'manual quest auto-added');n.manual=nil
n.routes={{kind='complete',step={elite=true}}}
assert(not B.Graph:LocalOpportunity(n,s),'group quest auto-added');n.routes={}
n.level=14;assert(not B.Graph:LocalOpportunity(n,s),'obsolete quest admitted');n.level=18
s.log[n.id].objectives={{objectiveType=14,finished=false}}
assert(not B.Graph:LocalOpportunity(n,s),'live profession requirement ignored')
s.log[n.id].objectives={};n.ends={}
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
