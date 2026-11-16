const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {lua,lauxlib,lualib,to_luastring,to_jsstring}=require('fengari');
const L=lauxlib.luaL_newstate();lualib.luaL_openlibs(L);
function script(src,name,vars=false){
 if(lauxlib.luaL_loadbuffer(L,to_luastring(src),null,to_luastring(name))!==lua.LUA_OK)
  throw Error(name+' syntax: '+to_jsstring(lua.lua_tostring(L,-1)));
 if(vars){lua.lua_pushstring(L,to_luastring('ForeverPilot_Engine'));lua.lua_getglobal(L,to_luastring('NS'));}
 if(lua.lua_pcall(L,vars?2:0,1,0)!==lua.LUA_OK)throw Error(name+': '+to_jsstring(lua.lua_tostring(L,-1)));
 const result=lua.lua_tostring(L,-1);lua.lua_pop(L,1);return result?to_jsstring(result):'';
}
const src=path.resolve(__dirname,'../foreverpilot');
const engine=path.resolve(__dirname,'../launcher/Runtime/ForeverPilot_Engine');
script(String.raw`
TEST={sets=0,releases=0,routeCalls=0,step=7,guide="h47",manual=false,
 pin={map=1413,x=52,y=29,blizzardQuestMarker=true},checks=0}
function TEST.ok(x,msg)TEST.checks=TEST.checks+1;assert(x,msg)end
local E={}
function E.GetLiveStep()return TEST.step end
function E.GetGuide()return {id=TEST.guide} end
local Nav={native={map=1413,x=12,y=17}}
Nav.target=Nav.native
function Nav.GetTarget()return Nav.target end
function Nav.IsOverride()return Nav.target~=Nav.native end
function Nav.IsHeld()return TEST.held end
function Nav.SetTarget(map,x,y,label)
 TEST.sets=TEST.sets+1
 Nav.target={map=map,x=x,y=y,label=label}
 return Nav.target
end
function Nav.ReleaseTarget()
 TEST.releases=TEST.releases+1
 Nav.target=Nav.native
end
function Nav.GetRouteGeometry()
 return {legs={{kind="walk",pts={{1,1,1},{1,2,2}}},{kind="walk",pts={{1,2,2},{1,3,3}},straight=TEST.straight}}}
end
TEST.Nav=Nav
local core={Nav=Nav,Engine=E,settings={['nav.fastxpRouteOverlay']=false}}
function core.Get(key)return core.settings[key] end
function core.Set(key,value)core.settings[key]=value;TEST.routeCalls=TEST.routeCalls+1 end
TEST.core=core
ForeverPilotLeveling={
 settings={enabled=true,mapRoute=true},mode="fast",
 Now=function()return 100 end,
 Mode=function()return ForeverPilotLeveling.mode end,
 Call=function(fn,...) if type(fn)~="function" then return nil end; local ok,a,b,c=pcall(fn,...);if not ok then error(a) end;return a,b,c end,
 EmbeddedEngine={Runtime=function()return core end},
 QuestMarker={Point=function(self,a,s,now)
   if a.q==100 and s.log[100] and (a.kind=="complete" or a.kind=="turnin") then return TEST.pin end
 end},
 locales={enUS={},frFR={},deDE={},esES={},esMX={},itIT={},ptBR={},ruRU={},koKR={},zhCN={},zhTW={}}
}
return true
`,'setup');
script(fs.readFileSync(path.join(src,'MapRoute.lua'),'utf8'),'MapRoute.lua');
script(fs.readFileSync(path.join(src,'MapRouteLocale.lua'),'utf8'),'MapRouteLocale.lua');
console.log(script(String.raw`
local B,M,Nav=ForeverPilotLeveling,ForeverPilotLeveling.MapRoute,TEST.Nav
local action={embedded=true,q=100,kind="complete",routeIndex=7,routeGuideId="h47",label="Quest 100"}
local s={log={[100]={ready=false}},dead=false,taxi=false}
local check=TEST.ok
M:Sync(action,s)
check(TEST.core.Get("nav.fastxpRouteOverlay")==true,"Map line visible in FastXP mode")
check(TEST.sets==1 and Nav.target.x==52 and Nav.target.y==29,"Exact native pin becomes graph destination")
M:Sync(action,s)
check(TEST.sets==1 and TEST.releases==0,"No target churn on 10Hz HUD ticks")
local st=M:Status()
check(st.enabled and st.legs==2 and st.pinSynced,"Reports route geometry and synchronized pin")
TEST.straight=true;check(M:Status().estimated,"Recognizes fallback straight segments")
TEST.pin={map=1413,x=54,y=30,blizzardQuestMarker=true}
M:Sync(action,s)
check(TEST.sets==2 and TEST.releases==1 and Nav.target.x==54,"Changed pin refreshes route exactly")
TEST.pin=nil
M:Sync(action,s)
check(TEST.releases==2 and Nav.target==Nav.native,"Missing pin restores native guidance")
TEST.pin={map=1413,x=52,y=29,blizzardQuestMarker=true}
Nav.SetTarget(1413,66,77,"Manual")
local before=TEST.sets
M:Sync(action,s)
check(TEST.sets==before and Nav.target.x==66 and TEST.releases==2,"Manual target is never overwritten")
Nav.ReleaseTarget()
TEST.held=true;M:Sync(action,s)
check(TEST.sets==before,"Corpse/death hold is respected")
TEST.held=false;M:Sync(action,s)
check(TEST.sets==before+1,"Native target recovers when manual waypoint is released")
TEST.step=8;M:Sync(action,s)
check(TEST.releases==4 and Nav.target==Nav.native,"Stale guide step cannot hold the old quest target")
TEST.step=7;M:Sync(action,s)
B.settings.mapRoute=false;M:Sync(action,s)
check(TEST.core.Get("nav.fastxpRouteOverlay")==false and Nav.target==Nav.native,"Disabling route hides map and resets own target")
B.settings.mapRoute=true;B.mode="zone";M:Sync(action,s)
check(not M:Status().enabled,"Free Zone Explorer cannot accidentally switch to Pro map route")
B.mode="fast";M:Sync(action,s)
B.settings.paused=true;M:Sync(action,s)
check(not M:Status().enabled and Nav.target==Nav.native,"Pause hides route safely")
B.settings.paused=false;M:Sync(action,s)
s.dead=true;M:Sync(action,s)
check(not M:Status().enabled,"Dead state hides route")
for _,loc in ipairs({"enUS","frFR","deDE","esES","esMX","itIT","ptBR","ruRU","koKR","zhCN","zhTW"}) do
 local table=B.locales[loc]
 check(type(table["Tracé sur carte : actif"])=="string" and type(table["Tracé sur carte : désactivé"])=="string","Missing route translation "..loc)
end
return "PASS "..TEST.checks.." native quest pin, live graph route, manual protection, pause, locale tests"
`,'route scenarios'));

script(String.raw`
NS={hidden=true,enabled=false,handlers={},frames={}}
local ns=NS
function ns.Hidden()return ns.hidden end
function ns.Get(key)if key=="nav.fastxpRouteOverlay" then return ns.enabled end end
function ns.On(k,fn)ns.handlers[k]=fn end
function ns.Safe(fn)return fn end
function ns.CreateFrameUnder()
 local f={shown=nil,events={}}
 function f:SetShown(value)self.shown=value end
 function f:SetScript(k,fn)self.events[k]=fn end
 return f
end
return true
`,'canvas setup');
script(fs.readFileSync(path.join(engine,'Nav/MapCanvas.lua'),'utf8'),'MapCanvas.lua',true);
console.log(script(String.raw`
local ns=NS
local canvas={GetWidth=function()return 200 end,GetHeight=function()return 100 end}
local map={GetMapID=function()return 1413 end,GetCanvas=function()return canvas end}
local v=ns.MapCanvas.Watch(map,function()end)
assert(v and v.frame.shown==false,"Bundled map stays hidden without FastXP route")
ns.enabled=true;ns.handlers.SETTING_CHANGED("nav.fastxpRouteOverlay")
assert(v.frame.shown==true,"FastXP shows map route canvas while full engine hidden")
ns.enabled=false;ns.handlers.SETTING_CHANGED("nav.fastxpRouteOverlay")
assert(v.frame.shown==false,"Disabling FastXP immediately hides map route")
ns.hidden=false;ns.handlers.FASTXP_ENGINE_HIDDEN(false)
assert(v.frame.shown==true,"Normal engine map visibility unchanged")
ns.hidden=true;ns.handlers.FASTXP_ENGINE_HIDDEN(true)
assert(v.frame.shown==false,"Normal hide still works")
return "PASS 5 hidden-map-canvas visibility transitions"
`,'canvas scenarios'));
const lines=fs.readFileSync(path.join(engine,'Maps/RouteLines.lua'),'utf8');
assert(lines.includes('fastxpRouteOverlay')&&lines.includes('if ns.Hidden() then')&&
 lines.includes('return enabled, enabled')&&lines.includes('["nav.fastxpRouteOverlay"] = true'),
 'RouteLines must explicitly allow only the dedicated FastXP route surfaces');
script(String.raw`return true`,'done');
console.log('PASS RouteLines map+minimap visibility flags, no engine pins re-enabled');
