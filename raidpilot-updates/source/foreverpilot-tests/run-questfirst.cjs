// Quest-first policy regression. Runs in Fengari without WoW.
const fs=require('node:fs'),path=require('node:path');
const {lua,lauxlib,lualib,to_luastring,to_jsstring}=require('fengari');
const L=lauxlib.luaL_newstate();lualib.luaL_openlibs(L);
const addon=process.env.FOREVERPILOT_TEST_ROOT||path.resolve(__dirname,'../foreverpilot');
function run(code,name,execute=true){
 if(lauxlib.luaL_loadbuffer(L,to_luastring(code),null,to_luastring(name))!==lua.LUA_OK||
    (execute&&lua.lua_pcall(L,0,0,0)!==lua.LUA_OK))
   throw Error(name+': '+to_jsstring(lua.lua_tostring(L,-1)));
 if(!execute)lua.lua_pop(L,1);
}
const toc=fs.readFileSync(path.join(addon,'ForeverPilot_Leveling.toc'),'utf8');
if(!toc.includes('## Version: 1.12.2')||!toc.includes('QuestFirstLocale.lua'))throw Error('TOC not 1.12.2 quest-first');
for(const rel of toc.split(/\r?\n/).map(s=>s.trim()).filter(s=>s.endsWith('.lua'))){
 const full=path.join(addon,rel);
 if(!fs.existsSync(full))throw Error('Missing TOC module '+rel);
 run(fs.readFileSync(full,'utf8'),rel,false);
}
run(fs.readFileSync(path.join(__dirname,'questfirst-fixture.lua'),'utf8'),'fixture');
for(const rel of ['Grind.lua','Journal.lua','Planner.lua'])run(fs.readFileSync(path.join(addon,rel),'utf8'),rel);
run(fs.readFileSync(path.join(__dirname,'questfirst-scenarios.lua'),'utf8'),'scenarios');
const b=fs.readFileSync(path.join(addon,'Bootstrap.lua'),'utf8');
if(!b.includes('version = "1.12.2"'))throw Error('Missing expected bootstrap version');
console.log('PASS: Quest-first 1.12.2; all TOC modules parse; held quests, old quests, skip, grind, recovery and class priority.');
