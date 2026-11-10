const fs=require('node:fs'),path=require('node:path');
const {lua,lauxlib,lualib,to_luastring,to_jsstring}=require('fengari');
const L=lauxlib.luaL_newstate();lualib.luaL_openlibs(L);
function run(code,name){if(lauxlib.luaL_loadbuffer(L,to_luastring(code),null,to_luastring(name))!==lua.LUA_OK||lua.lua_pcall(L,0,0,0)!==lua.LUA_OK)throw new Error(name+': '+to_jsstring(lua.lua_tostring(L,-1)));}
function file(p){return fs.readFileSync(p,'utf8');}
const ding=process.env.DING_ADDON||path.resolve(__dirname,'../ding'),addon=path.resolve(__dirname,'../foreverpilot');
run(file(path.join(__dirname,'graph-fixture.lua')),'graph fixture');
run('ForeverPilotLevelingDB={brain={language="'+(process.argv.includes('--en')?'en':'fr')+'"}}','test locale');
function loadD(p){run('local function chunk(...)\n'+file(path.join(ding,p))+'\nend\nchunk("Ding",Ding)','Ding/'+p);}
for(const p of ['DB/DB.lua','DB/Quests.lua','DB/Npcs.lua','DB/Objects.lua','DB/Items.lua','DB/Zones.lua','Core/Data.lua','Core/DingDB.lua','Core/Registry.lua'])loadD(p);
function walk(p){return fs.readdirSync(p,{withFileTypes:true}).flatMap(e=>e.isDirectory()?walk(path.join(p,e.name)):[path.join(p,e.name)]);}
for(const p of walk(path.join(ding,'Guides')).filter(p=>p.endsWith('.lua')&&!p.includes('Dungeon')))loadD(path.relative(ding,p));
run('Ding.Data:Init()','data');
run(file(path.join(__dirname,'ui-runtime.lua')),'UI');
for(const p of file(path.join(addon,'ForeverPilot_Leveling.toc')).split(/\r?\n/).filter(p=>p.endsWith('.lua')&&p!=='Main.lua'))run(file(path.join(addon,p)),p);
run(file(path.join(__dirname,'npc-recovery.lua')),'npc-recovery');
