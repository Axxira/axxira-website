import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {JSDOM} from 'jsdom';
const html=readFileSync(new URL('../public/index.html',import.meta.url),'utf8');
const script=readFileSync(new URL('../public/app.js',import.meta.url),'utf8');
const flush=()=>new Promise(r=>setImmediate(r));
async function setup({blockedStorage=false,configFails=false,pendingReturn=false}={}){
 const dom=new JSDOM(html,{url:'https://billing.example.test'+(pendingReturn?'/retour':''),runScripts:'outside-only'}),w=dom.window;
 if(blockedStorage)Object.defineProperty(w,'localStorage',{get(){throw new w.DOMException('Blocked','SecurityError');}});
 let session=pendingReturn,restored=false,fail=configFails,paid=false,timerSequence=0;const timers=new Map();
 w.setTimeout=(fn,delay)=>{const id=++timerSequence;timers.set(id,{fn,delay});return id;};w.clearTimeout=id=>timers.delete(id);
 const calls=[];
 w.fetch=async(path,options)=>{
  calls.push(path);
  if(path==='/api/config'){if(fail)throw Error('network');return Response.json({ready:true,addonReady:true,environment:'live',price:'6.99',currency:'CAD'});}
  if(path==='/api/session'){session=true;return Response.json({id:'new',recoveryKey:'a'.repeat(43)});}
  if(path==='/api/restore'){session=true;restored=true;return Response.json({id:'restored'});}
  if(path==='/api/status')return session?Response.json({status:paid?'ACTIVE':pendingReturn?'APPROVAL_PENDING':'NEW',active:paid,autoRenew:paid,paidUntil:paid?1808000000000:0}):Response.json({error:'SIGN_IN_REQUIRED'},{status:401});
  throw Error(path);
 };
 try{w.eval(script);await flush();await flush();}catch(e){dom.window.close();throw e;}
 return {dom,w,$:id=>w.document.getElementById(id),calls,recoverNetwork(){fail=false;},restored:()=>restored,timers,confirmPayment(){paid=true;},async tick(){const [id,timer]=timers.entries().next().value||[];assert.ok(timer,'pending confirmation must schedule another check');timers.delete(id);timer.fn();await flush();await flush();}};
}
test('restricted browser storage cannot break subscription page startup',async()=>{
 const x=await setup({blockedStorage:true});assert.equal(x.$('create').disabled,false);assert.ok(x.calls.includes('/api/config'));x.dom.window.close();
});
test('recovering another access clears the obsolete personal key and save gate',async()=>{
 const x=await setup();x.$('create').click();await flush();await flush();assert.equal(x.$('recovery').hidden,false);assert.equal(x.$('checkout').disabled,true);
 x.$('restore-key').value='b'.repeat(43);x.$('restore-button').click();await flush();await flush();
 assert.equal(x.restored(),true);assert.equal(x.$('key').value,'');assert.equal(x.$('recovery').hidden,true);assert.equal(x.$('checkout').disabled,false);x.dom.window.close();
});
test('temporary configuration failure offers a working retry without reloading',async()=>{
 const x=await setup({configFails:true});assert.equal(x.$('refresh').hidden,false);assert.equal(x.$('refresh').disabled,false);
 x.recoverNetwork();x.$('refresh').click();await flush();await flush();assert.equal(x.$('create').disabled,false);assert.ok(x.calls.filter(x=>x==='/api/config').length>=2);x.dom.window.close();
});
test('PayPal return automatically detects delayed payment without another checkout',async()=>{
 const x=await setup({pendingReturn:true});assert.equal(x.$('checkout').hidden,true);assert.equal(x.timers.size,1);
 x.confirmPayment();await x.tick();assert.equal(x.$('addon-license').hidden,false);assert.equal(x.$('checkout').hidden,true);assert.equal(x.timers.size,0);x.dom.window.close();
});
test('payment confirmation retries are bounded and leave manual checking available',async()=>{
 const x=await setup({pendingReturn:true});for(let i=0;i<24;i++)await x.tick();assert.equal(x.timers.size,0);assert.equal(x.$('refresh').disabled,false);assert.equal(x.$('refresh').hidden,false);assert.equal(x.calls.filter(p=>p==='/api/status').length,25);x.dom.window.close();
});
