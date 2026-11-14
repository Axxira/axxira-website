const fs=require('node:fs'),path=require('node:path');
const {lua,lauxlib,lualib,to_luastring,to_jsstring}=require('fengari');
const addon=path.resolve(__dirname,'../foreverpilot');
const L=lauxlib.luaL_newstate();lualib.luaL_openlibs(L);
function run(code,name){
 if(lauxlib.luaL_loadbuffer(L,to_luastring(code),null,to_luastring(name))!==lua.LUA_OK ||
    lua.lua_pcall(L,0,1,0)!==lua.LUA_OK)throw Error(name+': '+to_jsstring(lua.lua_tostring(L,-1)));
 const v=lua.lua_tostring(L,-1);lua.lua_pop(L,1);return v?to_jsstring(v):'';
}
run(String.raw`ForeverPilotLeveling={locales={}}
for _,key in ipairs({"enUS","frFR","deDE","esES","esMX","itIT","ptBR","ruRU","koKR","zhCN","zhTW"}) do
  ForeverPilotLeveling.locales[key]={}
end
return true`,'stub');
run(fs.readFileSync(path.join(addon,'SpeedRouteLocale.lua'),'utf8'),'locale');
console.log(run(String.raw`local L=ForeverPilotLeveling.locales
local langs={"enUS","frFR","deDE","esES","esMX","itIT","ptBR","ruRU","koKR","zhCN","zhTW"}
local keys={
 "Quête de l'itinéraire sans repère vérifié. Vérifie le journal et la carte.",
 "Atteins le niveau ","OBJECTIF DE NIVEAU","Étape de niveau : combats adaptés sur ton itinéraire.",
 "Entraîneur de classe","Maître de vol","Réparation","Vendeur","ÉTAPE DE SERVICE",
 "Pierre de foyer","ÉTAPE DE TRAJET","Continue l'itinéraire",
 "ITINÉRAIRE · ÉTAPE %d/%d","PARCOURS RAPIDE","Guide : %s · Étape %d/%d",
 "Itinéraire rapide : actif","Itinéraire rapide : désactivé"}
local total=0
local function formats(s)
 local xs={}
 for spec in s:gmatch("%%[sd]") do xs[#xs+1]=spec end
 return table.concat(xs,",")
end
for _,language in ipairs(langs) do
 for _,key in ipairs(keys) do
  local got=L[language][key]
  assert(type(got)=="string" and #got>0,language.." missing "..key)
  assert(formats(key)==formats(got),language.." format mismatch: "..key)
  total=total+1
 end
end
return "PASS route localization: "..total.." translations with format placeholders unchanged"`,'locale-tests'));
