import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {JSDOM} from 'jsdom';
const html=readFileSync(new URL('../public/index.html',import.meta.url),'utf8');
const script=readFileSync(new URL('../public/app.js',import.meta.url),'utf8');
const flush=()=>new Promise(r=>setImmediate(r));
async function setup({blockedStorage=false,configFails=false}={}){
 const dom=new JSDOM(html,{url:'https://billing.example.test',runScripts:'outside-only'}),w=dom.window;
 if(blockedStorage)Object.defineProperty(w,'localStorage',{get(){throw new w.DOMException('Blocked','SecurityError');}});
 let session=false,restored=false,fail=configFails;
 const calls=[];
 w.fetch=async(path,options)=>{
  calls.push(path);
  if(path==='/api/config'){if(fail)throw Error('network');return Response.json({ready:true,addonReady:true,environment:'live',price:'6.99',currency:'CAD'});}
  if(path==='/api/session'){session=true;return Response.json({id:'new',recoveryKey:'a'.repeat(43)});}
  if(path==='/api/restore'){session=true;restored=true;return Response.json({id:'restored'});}
  if(path==='/api/status')return session?Response.json({status:'NEW',active:false,autoRenew:false,paidUntil:0}):Response.json({error:'SIGN_IN_REQUIRED'},{status:401});
  throw Error(path);
 };
 try{w.eval(script);await flush();await flush();}catch(e){dom.window.close();throw e;}
 return {dom,w,$:id=>w.document.getElementById(id),calls,recoverNetwork(){fail=false;},restored:()=>restored};
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
