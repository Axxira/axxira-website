const fs=require('node:fs'),path=require('node:path');
const {lua,lauxlib,lualib,to_luastring,to_jsstring}=require('fengari');
const addon=path.resolve(__dirname,'../foreverpilot');
for(const language of ['frFR','enUS']){
 const L=lauxlib.luaL_newstate();lualib.luaL_openlibs(L);
 function run(src,name){
  if(lauxlib.luaL_loadbuffer(L,to_luastring(src),null,to_luastring(name))!==lua.LUA_OK||lua.lua_pcall(L,0,0,0)!==lua.LUA_OK)
   throw Error(name+': '+to_jsstring(lua.lua_tostring(L,-1)));
 }
 run(`TEST={clock=100,checks=0};Ding={db={},Compat={}};function GetTime()return TEST.clock end;function time()return TEST.clock end;function InCombatLockdown()return TEST.combat end;function GetLocale()return '${language}' end`,'host');
 run(fs.readFileSync(path.join(__dirname,'ui-runtime.lua'),'utf8'),'UI runtime');
 for(const file of ['Bootstrap.lua','Locale.lua','DualModeLocale.lua','AccessLocale.lua','Access.lua','Items.lua','Market.lua','Advisor.lua','UI.lua','Options.lua'])run(fs.readFileSync(path.join(addon,file),'utf8'),file);
 run(`
 local B=ForeverPilotLeveling;B.InitDB();B.distribution='subscriber';ForeverPilotAccess=nil
 local O=B.Options;B.UI:Create();local b=O.premiumButton
 local function check(ok,why)TEST.checks=TEST.checks+1;assert(ok,why)end
 check(b and b.parent==B.UI.frame,'CTA belongs to guide')
 check(b.state=='upgrade' and b.glow:IsShown(),'New user gets upgrade glow')
 check(b.label.text==B.T('VERSION PRO'),'Localized CTA')
 check(b.x>=0 and b.x+b.w<83 and b.y>=110 and b.y+b.h<140,'CTA fits left of separator above progress track')
 check(B.UI.arrow.y+B.UI.arrow.h<=B.UI.distance.y,'Arrow and distance do not overlap')
 check(B.UI.distance.y+B.UI.distance.h<b.y,'Distance and CTA do not overlap')
 check(b.y>31,'CTA cannot cover header Items or Options')
 local w,h=B.UI.frame.w,B.UI.frame.h;check(w==360 and h==176,'HUD size preserved')
 local count=#TEST.frames;O:SyncEntryPoints();O:SyncEntryPoints();check(#TEST.frames==count,'Repeated sync does not duplicate controls')
 B.Recovery={LastSkipped=function()end};b.scripts.OnClick()
 check(O.frame:IsShown() and O.tab=='subscription' and O.pages.subscription:IsShown(),'One click opens the real subscription page')
 check(O.accessLink:IsShown() and O.copyLink:IsShown(),'New visitor can reach the portal')
 check(O.accessLink.text==B.subscriptionURL and O.accessLink.text:find('https://foreverpilot-subscriptions.tyronixstudio.workers.dev/',1,true),'Uses official subscription portal')
 O.copyLink.scripts.OnClick();check(O.accessLink.focused and O.accessLink.highlighted,'Portal URL is selected for copying')
 check(O.accessCode:IsShown() and O.activateCode:IsShown(),'Code activation is available in addon')
 check(not O.accessCode.multiLine and O.accessCode.maxLetters==2048,'Long receipt stays in a bounded single-line field')
 check(O.accessCode.y+O.accessCode.h<O.activateCode.y and O.activateCode.y+O.activateCode.h<O.accessResult.y,'Code, action and result do not overlap')
 O.accessCode:SetText('not-a-code');O.activateCode.scripts.OnClick();check(O.accessResult.text==B.T('Code non reconnu. Copie le code complet commençant par FPA1, pas ta clé personnelle.'),'Invalid code gives an actionable localized message')
 check(not B.HasPremium(),'Invalid code never grants access')
 O.frame:Hide()
 for i=1,80 do b.scripts.OnUpdate(b,.05);check(b.glow.alpha>=.35 and b.glow.alpha<=.60,'Glow remains smooth and bounded') end
 TEST.combat=true;b.scripts.OnUpdate(b,.1);check(b.glow.alpha==.3,'No pulse in combat')
 TEST.combat=false;B.settings.paused=true;b.scripts.OnUpdate(b,.1);check(b.glow.alpha==.3,'No pulse during pause');B.settings.paused=false
 b.scripts.OnEnter();b.scripts.OnUpdate(b,.1);check(b.glow.alpha==.8 and b.hovered,'Steady hover highlight');b.scripts.OnLeave();check(not b.hovered,'Hover ends cleanly')
 ForeverPilotAccess={environment='live',expires=150,fastXP=true,classGuidance=true};b.scripts.OnUpdate(b,1.1)
 check(b.state=='active' and b.label.text==B.T('PRO actif') and not b.glow:IsShown(),'Active subscriber has status without upsell')
 TEST.clock=151;b.scripts.OnUpdate(b,1.1);check(b.state=='upgrade','Expiry refreshes while guide is visible')
 ForeverPilotAccess={environment='sandbox',expires=200,fastXP=true};O:SyncPremiumButton();check(b.state=='upgrade','Sandbox is not a paid live subscription')
 B.distribution='raidpilot-test';ForeverPilotAccess=nil;O:SyncPremiumButton()
 check(b.state=='included' and b.label.text==B.T('PRO inclus') and not b.glow:IsShown(),'Test edition remains fully included')
 b.scripts.OnClick();check(O.pages.subscription:IsShown() and not O.accessLink:IsShown() and not O.copyLink:IsShown(),'Test edition shows included access without checkout')
 check(B.HasPremium() and B.HasPremium('class'),'CTA never changes test access')
 check(B.settings.enabled and B.Mode()=='fast','CTA leaves guidance settings unchanged')
 b.scripts.OnHide();check(b.phase==0 and not b.hovered,'Hidden state clears motion')
 B.language='${language}'=='frFR' and 'enUS' or 'frFR';O:SyncPremiumButton();check(b.label.text==B.T('PRO inclus'),'Language refresh updates label')
 print('PASS '..TEST.checks..' Pro CTA checks (${language})')
 `,'Pro behavior');
}
