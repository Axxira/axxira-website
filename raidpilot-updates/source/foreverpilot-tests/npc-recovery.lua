local B=ForeverPilotLeveling
B.InitDB();TEST.level=23;TEST.class='DRUID';TEST.race='Troll';TEST.map=1424;TEST.x=64.05;TEST.y=53.84
B.Graph:Build();local checks=0
local function check(ok,msg) assert(ok,msg);checks=checks+1 end
local s={now=1000,level=23,faction='Horde',map=1424,x=64.05,y=53.84,log={},count=0,limit=20,targetNpc=270438,targetNear=true}
local done={};s.IsDone=function(_,q) return done[q]==true end
local n=B.Graph:Node(98095);local p=B.Graph:Node(98094)
check(n and p,'real Ding quest data missing')
check(B.Recovery:Prerequisite(n,s)==98094,'REGRESSION: Masoj pickup offered before Scout Support')
check(B.Validator:Eligible(n,s,'accept')==false,'missing prerequisite eligible')
s.log[98094]={ready=true,objectives={}}
check(B.Recovery:Prerequisite(n,s)==98094,'active prerequisite confused with completed')
local candidates=B.Planner:Candidates(s);local parent,child
for _,a in ipairs(candidates) do if a.q==98094 and a.kind=='turnin' then parent=a end;if a.q==98095 then child=a end end
check(parent and not child,'ready parent not recovered before child')
s.log={};done[98094]=true
check(not B.Recovery:Prerequisite(n,s),'completed prerequisite blocked child')
check(B.Validator:Eligible(n,s,'accept'),'child still unavailable after parent')
done[98094]=nil
B.Validator:Detail(98095,270438,s)
check(B.Validator:Eligible(n,s,'accept'),'actual offer failed to override database')
check(not s.log[98095] and not done[98095],'offer treated as pickup/completion')
B.Validator:Close()
local a={q=98095,key='accept:98095',kind='accept',node=n,point={map=s.map,x=s.x,y=s.y,npc=270438}}
local function reset()
 B.Validator:Close();B.char.unavailable={};B.char.skipped={};B.Recovery:Replan();B.settings.paused=false
 s.now=1000;s.combat=false;s.moving=false;s.afk=false;s.dead=false;s.taxi=false;s.targetNpc=270438;s.targetNear=true;s.count=0;s.limit=20;s.log={};done={};a.itemStart=nil
end
local function ticks(amount)
 B.Recovery:TickPickup(s,a)
 for i=1,amount do s.now=s.now+3;B.Recovery:TickPickup(s,a) end
end
reset();ticks(14);check(not B.Recovery:IsBlocked(a.q,s),'pickup timed out before 45 seconds')
ticks(1);check(B.Recovery:IsBlocked(a.q,s),'non-interactive selected nearby NPC never recovered')
check(B.char.unavailable[a.q].reason=='pickupUnconfirmed','timeout mislabeled as verified absence')
check(not B.char.skipped[a.q] and not s.log[a.q],'timeout altered log or permanent skip')
check(not B.Recovery.pickupWatch and B.Planner.current==nil,'stale outing retained after timeout')
check(B.char.unavailable[a.q].expires==time()+1800,'timeout has no bounded expiry')
for _,field in ipairs({'combat','moving','afk','dead','taxi'}) do
 reset();s[field]=true;ticks(20);check(not B.Recovery:IsBlocked(a.q,s),'timeout during '..field)
end
reset();B.settings.paused=true;ticks(20);check(not B.Recovery:IsBlocked(a.q,s),'timeout while paused')
reset();s.targetNpc=999;ticks(20);check(not B.Recovery:IsBlocked(a.q,s),'other target blocked expected quest')
reset();s.targetNear=nil;ticks(20);check(not B.Recovery:IsBlocked(a.q,s),'unknown interaction range used as proof')
reset();s.targetNear=false;ticks(20);check(not B.Recovery:IsBlocked(a.q,s),'out of range target timed out')
reset();a.itemStart=123;ticks(20);check(not B.Recovery:IsBlocked(a.q,s),'item-start pickup timed out')
reset();s.count=20;ticks(20);check(not B.Recovery:IsBlocked(a.q,s),'full journal became unavailable NPC')
reset();s.log[a.q]={objectives={}};ticks(20);check(not B.Recovery:IsBlocked(a.q,s),'confirmed pickup timed out')
reset();done[a.q]=true;ticks(20);check(not B.Recovery:IsBlocked(a.q,s),'completed quest timed out')
reset();B.Validator:Detail(a.q,270438,s);ticks(20);check(not B.Recovery:IsBlocked(a.q,s),'player reading quest timed out')
check(not B.Recovery:CanReportUnavailable(a,s),'report button replaces visible real offer')
reset();B.Validator.npc=123;ticks(20);check(not B.Recovery:IsBlocked(a.q,s),'another dialog timed out')
reset();B.Validator:Detail(nil,nil,s);ticks(20);check(not B.Recovery:IsBlocked(a.q,s),'unknown quest dialog timed out')
reset();ticks(10);s.now=s.now+100;B.Recovery:TickPickup(s,a);ticks(5)
check(not B.Recovery:IsBlocked(a.q,s),'long inactive gap counted as progress')
reset();ticks(10);s.combat=true;B.Recovery:TickPickup(s,a);s.combat=false;ticks(6)
check(not B.Recovery:IsBlocked(a.q,s),'combat interruption retained pickup timer')
reset();check(B.Recovery:Unavailable(a,s),'manual NPC report failed')
B.Recovery:Invalidate(999);check(B.Recovery:IsBlocked(a.q,s),'unrelated quest erased unavailable evidence')
done[98094]=true;check(not B.Recovery:IsBlocked(a.q,s),'prerequisite change did not release unavailable quest')
reset();B.Recovery:Unavailable(a,s);B.Validator:Detail(a.q,270438,s)
check(not B.char.unavailable[a.q] and B.Validator:Eligible(n,s,'accept'),'real direct offer did not recover missing quest')
reset();B.Recovery:Unavailable(a,s);B.char.unavailable[a.q].expires=time()-1
check(not B.Recovery:IsBlocked(a.q,s),'expired unconfirmed pickup stayed blocked')
reset();local savedPoint=a.point;a.point={map=s.map,x=s.x+10,y=s.y,npc=270438};check(not B.Recovery:Unavailable(a,s),'remote NPC report accepted');a.point=savedPoint
reset();B.Recovery:Defer(a);B.Validator:Detail(a.q,270438,s)
check(B.Recovery:IsSkipped(a.q),'live offer undid explicit permanent skip')
reset();s.log[111]={objectives={}};B.Recovery:Unavailable(a,s)
check(s.log[111] and not s.log[a.q],'report changed another quest')
reset();check(p.next[98095],'corrected chain not linked into graph')
check(p.fastPrerequisite,'corrected parent not protected as useful prerequisite')
if TEST.frames then
 B.UI:Create();a.confidence='DATABASE';a.batch={};B.UI:Render(a,s)
 check(B.UI.reportUnavailable and B.UI.skipLabel.text==B.T('Absent ?'),'nearby pickup recovery button missing')
 local reports,skips=0,0
 B.ReportUnavailable=function() reports=reports+1 end;B.SkipCurrent=function() skips=skips+1 end
 B.UI.skip.scripts.OnClick(B.UI.skip,'LeftButton');B.UI.skip.scripts.OnClick(B.UI.skip,'RightButton')
 check(reports==1 and skips==1,'recovery button actions reversed')
 B.Validator:Detail(a.q,270438,s);B.UI:Render(a,s)
 check(not B.UI.reportUnavailable and B.UI.skipLabel.text==B.T('Passer'),'real offer still labeled absent')
 B.Validator:Close();s.log[a.q]={objectives={}};a.kind='complete';B.UI:Render(a,s)
 check(not B.UI.reportUnavailable,'completion action shows absent-NPC button')
end
print('PASS NPC recovery: '..checks..' checks; real level-23 Masoj chain, direct offers, timeout guards, expiry and manual recovery')
