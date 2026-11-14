const fs=require('node:fs'),path=require('node:path');
const {lua,lauxlib,lualib,to_luastring,to_jsstring}=require('fengari');
const L=lauxlib.luaL_newstate();lualib.luaL_openlibs(L);
function run(src,name,execute=true){
 if(lauxlib.luaL_loadbuffer(L,to_luastring(src),null,to_luastring(name))!==lua.LUA_OK||
   (execute&&lua.lua_pcall(L,0,1,0)!==lua.LUA_OK))
  throw Error(name+': '+to_jsstring(lua.lua_tostring(L,-1)));
 if(!execute){lua.lua_pop(L,1);return ''}
 const result=lua.lua_tostring(L,-1);lua.lua_pop(L,1);
 return result?to_jsstring(result):'';
}
const addon=path.resolve(__dirname,'../foreverpilot');
const toc=fs.readFileSync(path.join(addon,'ForeverPilot_Leveling.toc'),'utf8').replace(/^\uFEFF/,'');
if(!toc.includes('## Version: 1.12.3')||!toc.includes('SpeedRoute.lua')||!toc.includes('SpeedRouteLocale.lua'))
 throw Error('Missing FastXP 1.12.3 route modules');
if(toc.indexOf('Planner.lua')>toc.indexOf('SpeedRoute.lua'))throw Error('Invalid route dependency order');
for(const rel of toc.split(/\r?\n/).map(x=>x.trim()).filter(x=>x.endsWith('.lua'))){
 const file=path.join(addon,rel);if(!fs.existsSync(file))throw Error('Missing module '+rel);
 run(fs.readFileSync(file,'utf8'),rel,false);
}
run(fs.readFileSync(path.join(__dirname,'route-fixture.lua'),'utf8'),'fixture');
run(fs.readFileSync(path.join(addon,'SpeedRoute.lua'),'utf8'),'SpeedRoute');
console.log(run(fs.readFileSync(path.join(__dirname,'route-scenarios.lua'),'utf8'),'route scenarios'));
