const fs=require('node:fs'),path=require('node:path');
const {lua,lauxlib,lualib,to_luastring,to_jsstring}=require('fengari');
const root=path.resolve(process.argv[2]||path.join(__dirname,'../foreverpilot'));
const L=lauxlib.luaL_newstate();lualib.luaL_openlibs(L);
function run(src,name){
 if(lauxlib.luaL_loadbuffer(L,to_luastring(src),null,to_luastring(name))!==lua.LUA_OK||lua.lua_pcall(L,0,1,0)!==lua.LUA_OK)
  throw Error(name+': '+to_jsstring(lua.lua_tostring(L,-1)));
 const out=lua.lua_tostring(L,-1);lua.lua_pop(L,1);return out?to_jsstring(out):'';
}
run(String.raw`
TEST={distance=0,calls=0,checks=0}
local B={settings={enabled=true},mode="fast",Planner={}}
ForeverPilotLeveling=B
B.T=function(s)return s end
B.Mode=function()return B.mode end
B.LocalizeLabels=function()end
B.Call=function(fn,...)
 if type(fn)~="function" then return nil end
 local ok,a,b,c=pcall(fn,...);if not ok then error(a)end;return a,b,c
end
B.Trace=function()end
B.Now=function()return 100 end
local E={guide={id="leveling-h13-arathi-highlands",pack="ding"},index=29}
TEST.step={go={1458,47.9,76.5},text="Head out of the Apothecarium"}
function E:CurrentStep()return TEST.step end
function E:NearestPoint(points)return points[1],TEST.distance,TEST.other end
function E:OnPosition(distance,p)
 TEST.calls=TEST.calls+1
 assert(p.isGo and p.map==1458 and p.x==47.9 and p.y==76.5,"Must report real destination")
 if not self.hold and distance<=(TEST.step.radius or 6)then self.index=self.index+1 end
end
local C={
 IsDeadOrGhost=function()return TEST.dead end,OnTaxi=function()return TEST.taxi end,
 GetPlayerWorld=function()return 0,0,0 end,GetFacing=function()return 0 end,
 UserWaypoint=function()return nil end,SetUserWaypoint=function()return true end,
 MapName=function()return "Undercity" end
}
Ding={Engine=E,Compat=C,Data={available=true},SuggestGuide=function()end,
 Arrow={Tick=function()E:OnPosition(TEST.distance,{map=1458,x=47.9,y=76.5,isGo=true})end,
 MarkWorld=function()end,Bearing=function()return 0 end},
 Goals={Auto=function(self,step)return step.collect~=nil or step.talk~=nil end}}
B.char={}
B.Travel={Leg=function()return TEST.leg end,World=function(self,p)p.continent=0;p.wx=TEST.distance;p.wy=0 end}
TEST.s={level=34,dead=false,taxi=false}
function TEST.action()
 return {kind="zone",routeStep=true,routeSource="Ding",routeGuideId=E.guide.id,
 routeIndex=29,point={map=1458,x=47.9,y=76.5}}
end
function TEST.expect(ok,msg)TEST.checks=TEST.checks+1;assert(ok,msg)end
return true`,'fixture');
for(const f of ['SpeedRoute.lua','UI.lua'])run(fs.readFileSync(path.join(root,f),'utf8'),f);
run(fs.readFileSync(path.join(root,'Navigation.lua'),'utf8'),'Navigation.lua');
console.log(run(String.raw`
local B,E=ForeverPilotLeveling,Ding.Engine
local R,U=B.SpeedRoute,B.UI
local check=TEST.expect
B.Navigation:Install()
Ding.Arrow:Tick()
check(TEST.calls==0,"Hidden Ding arrow is suppressed")
U.frame={IsShown=function()return true end}
U.current=TEST.action();U.state=TEST.s;U.view={verb="go",detail="walk"}
U.arrow={Hide=function()end,Show=function()end,SetTexture=function()end,SetRotation=function()end}
U.distance={SetText=function(self,t)self.text=t end}
U.verb={SetText=function()end};U.progress={SetText=function()end}
U:Tick()
check(E.index==30,"Regression: walking onto the Apothecarium point must advance with Ding arrow disabled")
check(TEST.calls==1 and B.dirty,"Arrival must request plan refresh once")
U:Tick()
check(E.index==30 and TEST.calls==1,"Stale HUD action cannot complete next step")
local function reset()
 E.index=29;E.hold=false;B.settings.enabled=true;B.settings.paused=false
 B.settings.speedRoute=true;B.mode="fast";B.dirty=false
 TEST.calls=0;TEST.distance=0;TEST.other=false;TEST.dead=false;TEST.taxi=false;TEST.leg=nil
 TEST.step={go={1458,47.9,76.5}};TEST.s.dead=false;TEST.s.taxi=false
 U.current=TEST.action();return U.current
end
local function noAdvance(label,change)
 local a=reset();change(a);local moved=R:PollArrival(a,TEST.s)
 check(not moved and E.index==29 and TEST.calls==0,label)
end
noAdvance("far from destination",function()TEST.distance=89 end)
noAdvance("nearby is not arrived at default radius",function()TEST.distance=8 end)
noAdvance("unknown position",function()TEST.distance=nil end)
noAdvance("different continent",function()TEST.other=true end)
noAdvance("invalid negative distance",function()TEST.distance=-1 end)
noAdvance("NaN distance",function()TEST.distance=0/0 end)
noAdvance("infinite distance",function()TEST.distance=math.huge end)
noAdvance("paused",function()B.settings.paused=true end)
noAdvance("disabled",function()B.settings.enabled=false end)
noAdvance("route disabled",function()B.settings.speedRoute=false end)
noAdvance("zone mode",function()B.mode="zone" end)
noAdvance("dead live",function()TEST.dead=true end)
noAdvance("taxi live",function()TEST.taxi=true end)
noAdvance("dead snapshot",function()TEST.s.dead=true end)
noAdvance("taxi snapshot",function()TEST.s.taxi=true end)
noAdvance("manual previous hold",function()E.hold=true end)
noAdvance("wrong guide",function(a)a.routeGuideId="another" end)
noAdvance("wrong cursor",function(a)a.routeIndex=28 end)
noAdvance("lookahead preview",function(a)a.routePreview=true end)
noAdvance("embedded engine owns arrival",function(a)a.embedded=true end)
noAdvance("unrelated action",function(a)a.routeStep=false end)
noAdvance("quest action",function(a)a.q=513 end)
noAdvance("other route provider",function(a)a.routeSource="other" end)
noAdvance("retargeted HUD",function(a)a.point.x=52.15 end)
for _,key in ipairs({"accept","complete","turnin","level","train","vendor","repair","fly","hearth","sethearth","deathskip","patrol","collect","talk"})do
 noAdvance("arrival cannot perform "..key,function()TEST.step[key]=true end)
end
noAdvance("invalid radius",function()TEST.step.radius=math.huge end)
local a=reset();TEST.step.radius=10;TEST.distance=9
check(R:PollArrival(a,TEST.s) and E.index==30,"Explicit guide radius respected")
a=reset();TEST.distance=6
check(R:PollArrival(a,TEST.s) and E.index==30,"Default radius boundary advances")
a=reset();TEST.distance=8;U:Tick()
check(U.distance.text=="8 yd" and E.index==29,"HUD does not claim arrival before validation radius")
a=reset();TEST.distance=100;TEST.leg={point={map=1458,x=1,y=1},kind="passage"}
local world=B.Travel.World;B.Travel.World=function(self,p)world(self,p);p.wx=0 end
U:Tick()
check(E.index==29 and TEST.calls==0,"Nearby intermediate stop is not destination arrival")
return "PASS "..TEST.checks.." arrival integration and safety checks"
`,'arrival scenarios'));
