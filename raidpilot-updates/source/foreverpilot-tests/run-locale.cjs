const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {lua,lauxlib,lualib,to_luastring,to_jsstring}=require('fengari');
const addon=path.resolve(__dirname,'../foreverpilot');
let checks=0;
function context(locale,saved){
 const L=lauxlib.luaL_newstate();lualib.luaL_openlibs(L);
 function run(src,name='test'){
  if(lauxlib.luaL_loadbuffer(L,to_luastring(src),null,to_luastring(name))!==lua.LUA_OK||lua.lua_pcall(L,0,0,0)!==lua.LUA_OK)
   throw Error(name+': '+to_jsstring(lua.lua_tostring(L,-1)));
 }
 function file(f){run(fs.readFileSync(path.join(addon,f),'utf8'),f);}
 run(`function GetLocale() return ${JSON.stringify(locale)} end
 function GetTime() return 100 end
 function time() return 10000 end
 TEST={timers={}};Ding={db={},Compat={PlayerClass=function()return "DRUID"end}}
 C_Timer={After=function(_,f) TEST.timers[#TEST.timers+1]=f end}
 function InCombatLockdown() return TEST.combat end
 function ReloadUI() TEST.reloads=(TEST.reloads or 0)+1 end
 ForeverPilotLevelingDB=${saved}
 `);
 file('Bootstrap.lua');file('Locale.lua');
 return {run,file};
}
for(const [locale,saved,want] of [
 ['frFR','nil','fr'],['enUS','nil','en'],['enGB','nil','en'],['deDE','nil','en'],
 ['enUS','{brain={enabled=true}}','fr'],['enUS','{brain={language="fr"}}','fr'],
 ['frFR','{brain={language="en"}}','en'],['frFR','{brain={language="auto"}}','fr'],
 ['enUS','{brain={language="auto"}}','en'],['frFR','{brain={language="unknown"}}','fr']]){
 const c=context(locale,saved);c.run(`assert(ForeverPilotLeveling.language=="${want}"); assert(ForeverPilotLeveling.T("An actual quest name")=="An actual quest name")`);checks+=2;
}
for(const language of ['en','fr']){
 const c=context('enUS',`{brain={language="${language}"}}`);
 c.run(fs.readFileSync(path.join(__dirname,'ui-runtime.lua'),'utf8'),'UI fixture');
 for(const f of ['Items.lua','Market.lua','AuctionUI.lua','Advisor.lua','UI.lua'])c.file(f);
 const english=language==='en';
 c.run(`local B=ForeverPilotLeveling
 B.InitDB();B.Market:Init()
 assert(B.Items.classes.DRUID[2][2]=="feral")
 local p,key=B.Items:ResolveProfile("DRUID","spec:DRUID:2",false)
 assert(key=="feral" and p.agi==2 and p.int==.2)
 assert(B.Items.classes.DRUID[2][1]==${JSON.stringify(english?'Feral':'Farouche')})
 local verdict,details=B.Advisor:UpgradeSummary({status="lower",deltas={{value=3,text="+3 "..B.T("Intelligence")},{value=-1,text="-1 "..B.T("Force")}}})
 assert(verdict==${JSON.stringify(english?'Upgrade: NO':'Upgrade : NON')})
 assert(details[1]==${JSON.stringify(english?'Gains: +3 Intellect':'Gains : +3 Intelligence')})
 assert(details[2]==${JSON.stringify(english?'Losses: -1 Strength':'Pertes : -1 Force')})
 B.Recovery={};B.Journal={Badge=function()end,IsClass=function()return false end}
 local v=B.UI:View({kind="complete",node={name="Centaur Bracers"},objective={text="0/15 Centaur Bracers"},confidence="LIVE"},{log={}})
 assert(v.title=="Centaur Bracers" and v.detail=="0/15 Centaur Bracers")
 assert(v.verb==${JSON.stringify(english?'COMPLETE':'TERMINE')})
 assert(v.footer==${JSON.stringify(english?'STAY OUT FOR NOW':'NE RENTRE PAS ENCORE')})
 assert(B.Market:Money(20405):find("GoldIcon",1,true) and B.Market:Money(20405):find("CopperIcon",1,true))
 B.Advisor:Toggle("addons")
 assert(B.Advisor.languageButtons[1].button:IsShown())
 local click=B.Advisor.languageButtons[2].button.scripts.OnClick
 click();assert(B.settings.language=="en" and TEST.reloads==1)
 TEST.combat=true;B.Advisor.languageButtons[1].button.scripts.OnClick()
 assert(B.settings.language=="fr" and TEST.reloads==1)
 assert(B.SetLanguage("bogus")==false and B.settings.language=="fr")
 B.Advisor:Toggle("gear");assert(not B.Advisor.languageButtons[1].button:IsShown())
 `,'localized behavior '+language);checks+=16;
}
// Format arguments must retain their order and types in all translated messages.
const source=fs.readFileSync(path.join(addon,'Locale.lua'),'utf8');
for(const line of source.split('\n')){
 const m=line.match(/^\s*\[("(?:\\.|[^"\\])*")\]=("(?:\\.|[^"\\])*"),$/);
 if(!m)continue;
 const fr=JSON.parse(m[1]),en=JSON.parse(m[2]);
 const formats=s=>[...s.matchAll(/%[-+ #0]*\d*(?:\.\d+)?[cdiouxXeEfgGqs%]/g)].map(m=>m[0]);
 assert.deepEqual(formats(en),formats(fr),'Format mismatch: '+fr);
 assert.equal((en.match(/\n/g)||[]).length,(fr.match(/\n/g)||[]).length,'Line break mismatch: '+fr);
 checks+=2;
}
console.log(`PASS localization: ${checks} checks; fresh/legacy/auto languages, gear, navigation, names, coins and language controls.`);
