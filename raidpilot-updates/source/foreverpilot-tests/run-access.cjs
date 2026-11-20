const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {generateKeyPairSync,sign}=require('node:crypto');
const {lua,lauxlib,lualib,to_luastring,to_jsstring}=require('fengari');
const addon=path.resolve(__dirname,'../foreverpilot');
const pair=generateKeyPairSync('rsa',{modulusLength:2048}),pub=pair.publicKey.export({format:'jwk'});
const L=lauxlib.luaL_newstate();lualib.luaL_openlibs(L);
function run(code,name){if(lauxlib.luaL_loadbuffer(L,to_luastring(code),null,to_luastring(name))!==lua.LUA_OK||lua.lua_pcall(L,0,0,0)!==lua.LUA_OK)throw Error(name+': '+to_jsstring(lua.lua_tostring(L,-1)));}
lua.lua_newtable(L);
for(const [name,fn] of Object.entries({band:(a,b)=>a&b,bxor:(a,b)=>a^b,bnot:a=>~a,rshift:(a,b)=>a>>>b,ror:(a,b)=>(a>>>b)|(a<<(32-b))})){
 lua.lua_pushjsfunction(L,s=>{const values=[];for(let i=1;i<=lua.lua_gettop(s);i++)values.push(lua.lua_tonumber(s,i));let value=fn(...values);if(name==='band'||name==='bxor')for(let i=2;i<values.length;i++)value=fn(value,values[i]);lua.lua_pushnumber(s,value>>>0);return 1;});lua.lua_setfield(L,-2,to_luastring(name));
}lua.lua_setglobal(L,to_luastring('bit'));
const now=1805472000;
run(`unpack=table.unpack; TEST={now=${now},combat=false};function GetServerTime()return TEST.now end;function time()return TEST.now end;function InCombatLockdown()return TEST.combat end`,'host');
run(fs.readFileSync(path.join(addon,'Bootstrap.lua'),'utf8'),'bootstrap');
let source=fs.readFileSync(path.join(addon,'Access.lua'),'utf8');
assert.match(source,/\["fp-live-v1"\]="[\w-]+"/);
source=source.replace(/(\["fp-live-v1"\]=")[\w-]+(")/,`$1${pub.n}$2`);
run(source,'access');run("B=ForeverPilotLeveling;B.db={};B.settings={enabled=true};B.distribution='public';A=B.Access",'init');
function code(fields={}){const x={kid:'fp-live-v1',env:'live',issuer:'https://foreverpilot-subscriptions.tyronixstudio.workers.dev',id:'12345678-1234-1234-1234-123456789abc',iat:now-60,exp:now+28*86400,features:'fast_xp,class_guidance',...fields};const payload=Object.values(x).join('|');const input='FPA1.'+Buffer.from(payload).toString('base64url');return input+'.'+sign('RSA-SHA256',Buffer.from(input),pair.privateKey).toString('base64url');}
const good=code();const q=JSON.stringify;let checks=0;
function check(src,label){run(src,label);checks++;}
check(`local ok= A:Import(${q(good)});assert(ok);assert(B.HasPremium() and B.HasPremium('class'));assert(B.db.addonAccessCode==${q(good)})`,'Valid RSA receipt activates without companion');
check(`A.checked=nil;A.access=nil;assert(B.HasPremium());assert(A:Current().expires==${now+28*86400})`,'Stored receipt reverified after reload');
check(`local old=B.db.addonAccessCode;assert(not A:Import(${q(code({exp:now+86400}))}));assert(B.db.addonAccessCode==old)`,'Older receipt cannot shorten paid access');
check(`TEST.combat=true;local ok,reason=A:Import(${q(good)});assert(not ok and reason=='combat');TEST.combat=false`,'Import deferred in combat');
const changes=[{env:'sandbox'},{issuer:'https://evil.test'},{kid:'unknown'},{exp:now-1},{iat:now+3600},{exp:now+33*86400},{features:'dungeon_maps'},{id:'not-an-account'}];
for(const fields of changes)check(`assert(not A:Verify(${q(code(fields))}))`,'Reject '+JSON.stringify(fields));
const parts=good.split('.');parts[1]=Buffer.from(Buffer.from(parts[1],'base64url').toString().replace(String(now+28*86400),String(now+29*86400))).toString('base64url');
check(`assert(not A:Verify(${q(parts.join('.'))}))`,'Signed expiry tampering rejected');
const sig=Buffer.from(good.split('.')[2],'base64url');sig[0]^=1;
check(`assert(not A:Verify(${q(good.split('.').slice(0,2).join('.')+'.'+sig.toString('base64url'))}))`,'Altered signature rejected');
for(const text of ['', 'personal_key_is_not_a_receipt', 'FPA1.abc.def',good+'x','x'.repeat(2049)])check(`assert(not A:Verify(${q(text)}))`,'Malformed input rejected');
check(`TEST.now=${now+29*86400};assert(not B.HasPremium());B.distribution='raidpilot-test';assert(B.HasPremium() and B.HasPremium('class'));B.distribution='public'`,'Expiry changes access; RaidPilot stays free');
check(`ForeverPilotAccess={environment='live',expires=TEST.now+86400,fastXP=true,classGuidance=true};assert(B.HasPremium() and B.HasPremium('class'))`,'Existing paid companion access preserved');
console.log(`PASS ${checks} offline access cases, RSA-2048 signatures and expiry; no payment or Internet from Lua.`);
