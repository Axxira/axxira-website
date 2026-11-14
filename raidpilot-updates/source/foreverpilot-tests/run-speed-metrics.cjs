const fs=require('node:fs'),path=require('node:path');
const {lua,lauxlib,lualib,to_luastring,to_jsstring}=require('fengari');
const addon=path.resolve(__dirname,'../foreverpilot');
const toc=fs.readFileSync(path.join(addon,'ForeverPilot_Leveling.toc'),'utf8').replace(/^\uFEFF/,'');
const L=lauxlib.luaL_newstate();lualib.luaL_openlibs(L);
function run(code,name,execute=true){
 if(lauxlib.luaL_loadbuffer(L,to_luastring(code),null,to_luastring(name))!==lua.LUA_OK||
    (execute&&lua.lua_pcall(L,0,1,0)!==lua.LUA_OK))
  throw Error(name+': '+to_jsstring(lua.lua_tostring(L,-1)));
 if(!execute){lua.lua_pop(L,1);return ''}
 const result=lua.lua_tostring(L,-1);lua.lua_pop(L,1);
 return result?to_jsstring(result):'';
}
for(const rel of toc.split(/\r?\n/).map(x=>x.trim()).filter(x=>x.endsWith('.lua'))){
 const p=path.join(addon,rel);if(!fs.existsSync(p))throw Error('Missing module '+rel);
 run(fs.readFileSync(p,'utf8'),rel,false);
}
for(const name of ['SpeedRoute.lua','SpeedMetrics.lua','SpeedMetricsLocale.lua'])
 if(!toc.includes(name))throw Error('Module not loaded '+name);
run(String.raw`TEST={prints={}}
ForeverPilotLeveling={
 char={},settings={enabled=true},mode="fast",
 Mode=function()return "fast" end,T=function(x)return x end,
 Print=function(x)TEST.prints[#TEST.prints+1]=x end}
return true`,'fixture');
run(fs.readFileSync(path.join(addon,'SpeedMetrics.lua'),'utf8'),'SpeedMetrics');
console.log(run(fs.readFileSync(path.join(__dirname,'speed-metrics-scenarios.lua'),'utf8'),'metrics scenarios'));
