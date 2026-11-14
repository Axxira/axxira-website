const fs=require('node:fs'),path=require('node:path');
const {lua,lauxlib,lualib,to_luastring,to_jsstring}=require('fengari');
const L=lauxlib.luaL_newstate();lualib.luaL_openlibs(L);
function run(code,name){
 if(lauxlib.luaL_loadbuffer(L,to_luastring(code),null,to_luastring(name))!==lua.LUA_OK||
 lua.lua_pcall(L,0,1,0)!==lua.LUA_OK)throw Error(name+': '+to_jsstring(lua.lua_tostring(L,-1)));
 const text=lua.lua_tostring(L,-1);lua.lua_pop(L,1);
 return text?to_jsstring(text):'';
}
const addon=path.resolve(__dirname,'../foreverpilot');
run('ForeverPilotLeveling={locales={}}; for _,x in ipairs({"enUS","frFR","deDE","esES","esMX","itIT","ptBR","ruRU","koKR","zhCN","zhTW"}) do ForeverPilotLeveling.locales[x]={} end; return true','fixture');
run(fs.readFileSync(path.join(addon,'SpeedMetricsLocale.lua'),'utf8'),'SpeedMetricsLocale');
console.log(run(fs.readFileSync(path.join(__dirname,'speed-metrics-locales-scenarios.lua'),'utf8'),'locale scenarios'));
