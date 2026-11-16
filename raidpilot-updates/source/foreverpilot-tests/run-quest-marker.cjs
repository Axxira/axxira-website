const fs=require('fs'),path=require('path');
const {lua,lauxlib,lualib,to_luastring,to_jsstring} = require('fengari');
const L=lauxlib.luaL_newstate();lualib.luaL_openlibs(L);
function run(src,name){if(lauxlib.luaL_loadbuffer(L,to_luastring(src),null,to_luastring(name))!==lua.LUA_OK||lua.lua_pcall(L,0,1,0)!==lua.LUA_OK) throw Error(name+': '+to_jsstring(lua.lua_tostring(L,-1)));let x=lua.lua_tostring(L,-1);lua.lua_pop(L,1);return x?to_jsstring(x):''}
run(String.raw`local B={settings={enabled=true},char={},calls=0}
ForeverPilotLeveling=B
function B.Public(x)return x end
function B.Call(fn,...)
 if type(fn)~="function" then return nil end
 local ok,a,b,c=pcall(fn,...)
 if ok then return a,b,c end
end
function B.T(s)return s end
B.Now=function()return 100 end
TEST={next={1413,.52,.29},shown={},q=100,clock=100,turns=0,rotations=0}
GetTime=function()return TEST.clock end
Ding={Compat={IsInLog=function(q)return q==100 end,
 MapToWorld=function(map,x,y)return 1,x*10,y*10 end,
 GetPlayerWorld=function()return 1,500,290 end,
 GetFacing=function()return 0 end}}
Ding.Arrow={Bearing=function(x,y)return math.atan(y,x) end}
C_QuestLog={
 GetNextWaypoint=function(q)if q==100 and TEST.next then return table.unpack(TEST.next) end end,
 GetNextWaypointForMap=function(q,map)if q==100 and TEST.onMap then return table.unpack(TEST.onMap) end end,
 GetQuestsOnMap=function()return TEST.rows end}
C_Map={GetBestMapForUnit=function()return 1413 end}
return true`,'fixture');
run(fs.readFileSync(path.resolve(__dirname,'../foreverpilot/QuestMarker.lua'),'utf8'),'QuestMarker');
console.log(run(String.raw`local M=ForeverPilotLeveling.QuestMarker
local s={log={[100]={ready=false}}}
local a={q=100,kind="complete",point={map=1413,x=10,y=10}}
local p=M:Point(a,s,100)
assert(p and p.map==1413 and math.abs(p.x-52)<0.0001 and math.abs(p.y-29)<0.0001 and p.blizzardQuestMarker,"Uses exact game pin: "..tostring(p and p.map)..":"..tostring(p and p.x)..":"..tostring(p and p.y))
local d,angle=M:Direction(p)
assert(d==20 and math.abs(angle)<0.001,"Uses native quest position for bearing")
assert(M:Point({q=101,kind="complete",point=a.point},s,100)==nil,"Wrong quest id never matched")
assert(M:Point({q=100,kind="accept",point=a.point},s,100)==nil,"Do not redirect before pickup")
assert(M:Point({q=100,kind="turnin",point=a.point},{log={}},100)==nil,"Not in quest log")
TEST.clock=103
TEST.next=nil
TEST.rows={{questID=101,mapID=1413,x=.8,y=.2},{questID=100,mapID=1413,x=.48,y=.30,isQuestStart=false}}
M:Reset()
p=M:Point(a,s,103)
assert(p and p.x==48 and p.y==30,"Uses matching map pin only, not unrelated quest")
TEST.rows[3]={questID=100,mapID=1413,x=.49,y=.33}
M:Reset()
assert(M:Point(a,s,106)==nil,"Ambiguous two pins must never invent one")
TEST.rows=nil
TEST.onMap={.45,.31}
M:Reset()
p=M:Point(a,s,110)
assert(p and p.x==45 and p.y==31,"Uses next waypoint map fallback")
TEST.onMap=nil
TEST.next={1413,25,30}
M:Reset()
assert(M:Point(a,s,114)==nil,"Out of normalized range must be rejected")
TEST.next={1413,.52,.29}
M:Reset()
local ui={Symbol=function(self,t)TEST.symbol=t end,
 arrow={SetRotation=function(self,angle)TEST.angle=angle end},
 distance={SetText=function(self,t)TEST.distance=t end}}
assert(M:Tick(ui,a,s,nil,120)==true and TEST.distance=="20 yd","Native marker controls arrow")
assert(not M:Tick(ui,a,s,{Nav={travel={Active=function()return true end,
 CurrentLeg=function()return {kind="fly"} end}}},121),"Transport leg must win")
return "PASS: map pin only for current quest; normalized source, no ambiguous targets, no false quest pickup, native bearing, transport preserved"`,'scenarios'));
