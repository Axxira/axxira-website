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
 ForeverPilotLevelingDB=nil
 `);
 file('Bootstrap.lua');file('Locale.lua');
 return {run,file,restore:()=>run('ForeverPilotLevelingDB='+saved)};
}
for(const [locale,saved,want] of [
 ['frFR','nil','fr'],['enUS','nil','en'],['enGB','nil','en'],['deDE','nil','en'],
 ['enUS','{brain={enabled=true}}','fr'],['enUS','{brain={language="fr"}}','fr'],
 ['frFR','{brain={language="en"}}','en'],['frFR','{brain={language="auto"}}','fr'],
 ['enUS','{brain={language="auto"}}','en'],['frFR','{brain={language="unknown"}}','fr']]){
 const c=context(locale,saved);c.restore();c.run(`ForeverPilotLeveling.InitLocale(); assert(ForeverPilotLeveling.language=="${want}"); assert(ForeverPilotLeveling.T("An actual quest name")=="An actual quest name")`);checks+=2;
}
for(const language of ['en','fr']){
 const c=context('enUS',`{brain={language="${language}"}}`);
 c.run(fs.readFileSync(path.join(__dirname,'ui-runtime.lua'),'utf8'),'UI fixture');
 for(const f of ['Items.lua','Market.lua','AuctionUI.lua','Advisor.lua','UI.lua','Options.lua'])c.file(f);
 // Actual client order: Lua files first, saved variables second, PLAYER_LOGIN last.
 c.restore();
 const english=language==='en';
 c.run(`local B=ForeverPilotLeveling
 B.InitDB();B.Market:Init()
 assert(B.Items.classes.DRUID[2][2]=="feral")
 local p,key=B.Items:ResolveProfile("DRUID","spec:DRUID:2",false)
 assert(key=="feral" and p.agi==2 and p.int==.2)
 assert(B.Items.classes.DRUID[2][1]==${JSON.stringify(english?'Feral':'Farouche')})
 B.Advisor:ToggleProfile();B.Advisor.classChoice.scripts.OnClick()
 assert(B.Advisor.classChoice.enabled and #B.Items:ClassChoices()==9)
 assert(B.Advisor.choiceTitle.text==${JSON.stringify(english?'All classes':'Toutes les classes')})
 assert(B.Advisor.choiceButtons.WARRIOR.label.text==${JSON.stringify(english?'Warrior':'Guerrier')})
 B.Advisor.choiceButtons.WARRIOR.scripts.OnClick()
 assert(B.Advisor.profileMode=='preview' and B.Advisor.preview.class=='WARRIOR')
 B.Advisor.specChoice.scripts.OnClick()
 assert(B.Advisor.choiceButtons['spec:WARRIOR:2'].label.text==${JSON.stringify(english?'Fury':'Fureur')})
 B.Advisor.profileFrame:Hide()
 local verdict,details=B.Advisor:UpgradeSummary({status="lower",deltas={{value=3,text="+3 "..B.T("Intelligence")},{value=-1,text="-1 "..B.T("Force")}}})
 assert(verdict==${JSON.stringify(english?'Upgrade: NO':'Upgrade : NON')})
 assert(details[1]==${JSON.stringify(english?'Gains: +3 Intellect':'Gains : +3 Intelligence')})
 assert(details[2]==${JSON.stringify(english?'Losses: -1 Strength':'Pertes : -1 Force')})
 B.Recovery={LastSkipped=function()end};B.Journal={Badge=function()end,IsClass=function()return false end}
 local v=B.UI:View({kind="complete",node={name="Centaur Bracers"},objective={text="0/15 Centaur Bracers"},confidence="LIVE"},{log={}})
 assert(v.title=="Centaur Bracers" and v.detail=="0/15 Centaur Bracers")
 assert(v.verb==${JSON.stringify(english?'COMPLETE':'TERMINE')})
 assert(v.footer==${JSON.stringify(english?'STAY OUT FOR NOW':'NE RENTRE PAS ENCORE')})
 assert(B.Market:Money(20405):find("GoldIcon",1,true) and B.Market:Money(20405):find("CopperIcon",1,true))
 B.Options:SyncEntryPoints()
 assert(B.Options.shortcut:IsShown()) -- Before the guide or Advisor has initialized.
 B.UI:Create();B.Options:Toggle()
 local O=B.Options
 assert(O.headerButton and not O.shortcut:IsShown())
 local initMarket=B.Market.Init
 B.Market.Init=function() error('simulated auction failure') end
 assert(not pcall(B.Advisor.Init,B.Advisor))
 B.Market.Init=initMarket
 O.frame:Hide();O.headerButton.scripts.OnClick();assert(O.frame:IsShown())
 B.UI.frame:Hide();B.UI.frame.scripts.OnHide()
 assert(O.shortcut:IsShown())
 B.settings.enabled=false;O:SyncEntryPoints();O.frame:Hide()
 O.shortcut.scripts.OnClick();assert(O.frame:IsShown())
 B.settings.enabled=true
 assert(B.distribution=="raidpilot-test" and B.HasPremium() and B.HasPremium("class"))
 ForeverPilotAccess={environment="sandbox",expires=0};assert(B.HasPremium())
 B.distribution="public"
 assert(not B.HasPremium() and not B.HasPremium("class"))
 O:SyncEntryPoints();assert(O.shortcut:IsShown())
 O.frame:Hide();O.shortcut.scripts.OnClick();assert(O.frame:IsShown() and not B.HasPremium())
 ForeverPilotAccess={environment="sandbox",expires=time()+1000,fastXP=true,classGuidance=true};assert(not B.HasPremium())
 ForeverPilotAccess={environment="live",expires=time()+1000,fastXP=true,classGuidance=false};assert(B.HasPremium() and not B.HasPremium("class"))
 ForeverPilotAccess.expires=time()-1;assert(not B.HasPremium())
 ForeverPilotAccess.expires=0/0;assert(not B.HasPremium())
 B.distribution="raidpilot-test";ForeverPilotAccess=nil
 B.UI.frame:Show();B.UI.frame.scripts.OnShow();assert(not O.shortcut:IsShown())
 O.frame:Hide();B.char.welcomeSeen=nil;TEST.combat=true;O:MaybeWelcome();assert(not O.frame:IsShown() and not B.char.welcomeSeen)
 TEST.combat=false;O:MaybeWelcome();assert(O.frame:IsShown() and B.char.welcomeSeen)
 O.frame:Hide();O:MaybeWelcome();assert(not O.frame:IsShown())
 O:Toggle()
 assert(O.frame:IsShown() and O.pages.general:IsShown() and not O.pages.help:IsShown())
 assert(O.languages.auto.label.text==${JSON.stringify(english?'Auto · game language':'Auto · langue du jeu')})
 local other=B.language=="en" and "fr" or "en"
 O.languages[other].scripts.OnClick()
 assert(TEST.reloads==nil and B.settings.language==B.language and O.apply.enabled)
 TEST.combat=true;O.frame.scripts.OnEvent();O.apply.scripts.OnClick()
 assert(not O.apply.enabled and TEST.reloads==nil and B.settings.language==B.language)
 TEST.combat=false;O.frame.scripts.OnEvent();O.apply.scripts.OnClick()
 assert(B.settings.language==other and TEST.reloads==1)
 assert(B.SetLanguage("bogus")==false and B.settings.language==other)
 O.sizes[1.2].scripts.OnClick();assert(B.UI.frame.scale==1.2)
 O.lock.scripts.OnClick();assert(B.settings.locked)
 O.pause.scripts.OnClick();assert(B.settings.paused and B.dirty)
 O.tabs.help.scripts.OnClick();assert(O.pages.help:IsShown() and not O.pages.general:IsShown())
 assert(not O.resume.enabled)
 O:Toggle();assert(not O.frame:IsShown())
 O:Toggle();assert(O.tab=="general" and O.frame:IsShown())
 assert(not B.UI.tag:IsShown())
 `,'localized behavior '+language);checks+=42;
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
