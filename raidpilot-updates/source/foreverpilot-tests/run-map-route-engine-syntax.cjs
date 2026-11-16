const fs=require('node:fs'),path=require('node:path');
const {lua,lauxlib,to_luastring,to_jsstring}=require('fengari');
const L=lauxlib.luaL_newstate();
const core=path.resolve(__dirname,'../launcher/Runtime/ForeverPilot_Engine');
for(const rel of ['Nav/MapCanvas.lua','Maps/RouteLines.lua','Maps/WorldMap.lua']){
 const src=fs.readFileSync(path.join(core,rel),'utf8');
 const code=lauxlib.luaL_loadbuffer(L,to_luastring(src),null,to_luastring(rel));
 if(code!==lua.LUA_OK)throw Error(rel+': '+to_jsstring(lua.lua_tostring(L,-1)));
 lua.lua_pop(L,1);
 console.log('PASS_ENGINE_LUA_SYNTAX',rel);
}
