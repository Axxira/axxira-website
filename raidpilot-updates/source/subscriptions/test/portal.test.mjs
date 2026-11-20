import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {JSDOM} from 'jsdom';
const html=readFileSync(new URL('../public/index.html',import.meta.url),'utf8');
const script=readFileSync(new URL('../public/app.js',import.meta.url),'utf8');
const flush=()=>new Promise(r=>setImmediate(r));
async function page({language='fr',paid=true,addonReady=true,copyFails=false}={}){
 const dom=new JSDOM(html,{url:'https://billing.example.test',runScripts:'outside-only'}),w=dom.window;
 w.localStorage.setItem('fp-language',language);
 const requests=[];let clipboard='',active=paid;
 Object.defineProperty(w.navigator,'clipboard',{value:{async writeText(text){if(copyFails)throw Error('denied');clipboard=text;}}});
 const code='FPA1.'+Buffer.from('test-receipt').toString('base64url')+'.'+Buffer.alloc(256,123).toString('base64url');
 w.fetch=async(path,options)=>{requests.push({path,options});
  if(path==='/api/config')return Response.json({ready:true,addonReady,companionReady:true,environment:'live',price:'6.99',currency:'CAD'});
  if(path==='/api/status')return Response.json({status:active?'ACTIVE':'NEW',active,autoRenew:active,paidUntil:active?1808000000000:0});
  if(path==='/api/addon-license')return active?Response.json({code,expiresAt:1808000000,environment:'live'}):Response.json({error:'NO_ACTIVE_ACCESS'},{status:403});
  throw Error('Unexpected API call: '+path);
 };
 w.eval(script);await flush();await flush();
 return {w,dom,code,requests,$:id=>w.document.getElementById(id),clipboard:()=>clipboard,expire(){active=false;}};
}
for(const language of ['fr','en'])test('portal code activation flow '+language,async()=>{
 const x=await page({language});
 assert.equal(x.$('license').hidden,true);assert.equal(x.$('addon-license').hidden,false);
 assert.equal(x.$('addon-license').disabled,false);
 assert.equal(x.$('addon-license').textContent,language==='fr'?'Obtenir mon code pour WoW':'Get my code for WoW');
 x.$('addon-license').click();await flush();
 assert.equal(x.$('addon-code').value,x.code);assert.equal(x.$('addon-receipt').hidden,false);
 assert.match(x.$('addon-code-status').textContent,/Valable|Valid/);
 x.$('copy-addon-code').click();await flush();assert.equal(x.clipboard(),x.code);
 assert.match(x.$('addon-code-status').textContent,/WoW/);
 assert.match(x.$('addon-receipt').textContent,language==='fr'?/Après chaque renouvellement payé/:/After each paid renewal/);
 assert.equal(x.w.localStorage.length,1);assert.equal(x.w.localStorage.key(0),'fp-language');
 x.expire();x.$('refresh').click();await flush();assert.equal(x.$('addon-code').value,'');assert.equal(x.$('addon-receipt').hidden,true);
 assert.ok(x.requests.some(r=>r.path==='/api/addon-license'&&r.options.method==='POST'));
 x.dom.window.close();
});
test('unpaid visitors cannot request code; old activation remains compatible',async()=>{
 const unpaid=await page({paid:false});assert.equal(unpaid.$('addon-license').hidden,true);unpaid.dom.window.close();
 const legacy=await page({addonReady:false});assert.equal(legacy.$('license').hidden,false);assert.equal(legacy.$('addon-license').hidden,true);legacy.dom.window.close();
});
test('clipboard denial leaves code selected with manual copying instructions',async()=>{
 const x=await page({copyFails:true});x.$('addon-license').click();await flush();x.$('copy-addon-code').click();await flush();
 assert.match(x.$('addon-code-status').textContent,/Ctrl\+C/);assert.equal(x.$('addon-code').selectionStart,0);assert.equal(x.$('addon-code').selectionEnd,x.code.length);x.dom.window.close();
});
