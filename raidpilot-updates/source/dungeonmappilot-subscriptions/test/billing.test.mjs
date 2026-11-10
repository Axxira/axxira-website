import {test} from 'node:test';
import assert from 'node:assert/strict';
import {DatabaseSync} from 'node:sqlite';
import {readFileSync} from 'node:fs';
import {generateKeyPairSync,createPublicKey,verify} from 'node:crypto';
import {handle,reconcile,configured} from '../src/worker.mjs';
import {PayPal,paidUntil,monthAfter} from '../src/paypal.mjs';
const now=Date.parse('2026-11-08T16:00:00Z'),origin='https://billing.example.test';
const pair=generateKeyPairSync('rsa',{modulusLength:2048});
const privateJwk=pair.privateKey.export({format:'jwk'});
const sale={id:'SALE1',status:'COMPLETED',time:'2026-11-08T15:00:00Z',amount_with_breakdown:{gross_amount:{currency_code:'CAD',value:'5.99'}}};
function setup() {
  const db=new DatabaseSync(':memory:');db.exec(readFileSync(new URL('../migrations/0001.sql',import.meta.url),'utf8'));
  const env={PUBLIC_ORIGIN:origin,PAYPAL_ENV:'sandbox',BILLING_ENABLED:'true',PAYPAL_CLIENT_ID:'test',PAYPAL_CLIENT_SECRET:'test',PAYPAL_PLAN_ID:'P-TEST',PAYPAL_WEBHOOK_ID:'HOOK',SIGNING_JWK:JSON.stringify(privateJwk),
    DB:{prepare(sql){let values=[];return {bind(...args){values=args;return this;},async first(){return db.prepare(sql).get(...values)||null;},async all(){return {results:db.prepare(sql).all(...values)};},async run(){return {meta:db.prepare(sql).run(...values)};}}}}};
  let sub={id:'I-TEST',plan_id:'P-TEST',status:'ACTIVE',custom_id:''},sales=[structuredClone(sale)],signature=true,calls=[];
  const provider={async validatePlan(){},async verifyWebhook(){return signature;},async request(path,options={}){
    calls.push({path,options});
    if(path==='/v1/billing/subscriptions'){sub.custom_id=options.body.custom_id;sub.status='APPROVAL_PENDING';return {...sub,links:[{rel:'approve',href:'https://www.sandbox.paypal.com/webapps/billing/subscriptions?ba_token=TEST'}]};}
    if(path.includes('/transactions?'))return {transactions:sales};
    if(path.endsWith('/cancel')){sub.status='CANCELLED';return {};}
    return structuredClone(sub);
  }};
  let key;
  const request=async(path,method='GET',data,headers={})=>handle(new Request(origin+path,{method,headers:{Origin:origin,'Content-Type':'application/json',...(key?{Authorization:'Bearer '+key}:{}),...headers},body:data===undefined?undefined:JSON.stringify(data)}),env,{provider,now});
  const start=async()=>{const response=await request('/api/session','POST');const data=await response.json();key=data.recoveryKey;sub.custom_id=data.id;return data;};
  const enroll=async()=>{await start();await request('/api/checkout','POST');sub.status='ACTIVE';};
  return {env,db,provider,request,start,enroll,sub,calls,setSales(v){sales=v;},setSignature(v){signature=v;}};
}
test('live billing cannot start without explicit validation; missing secrets fail closed',()=>{
  const {env}=setup();assert.equal(configured(env),true);env.PAYPAL_ENV='live';assert.equal(configured(env),false);env.LIVE_VERIFIED='true';assert.equal(configured(env),false);env.COMPANION_URL='https://github.com/Axxira/axxira-website/releases/download/test/DungeonMapPilot-Companion-v1.0.0-Windows-x64.zip';assert.equal(configured(env),true);delete env.SIGNING_JWK;assert.equal(configured(env),false);
});
test('calendar months clamp month end and retain UTC time',()=>assert.equal(new Date(monthAfter(Date.parse('2027-01-31T14:15:00Z'))).toISOString(),'2027-02-28T14:15:00.000Z'));

test('Maps uses its own product, audience, price and account store',async()=>{
  const x=setup();await x.enroll();
  const config=await(await x.request('/api/config')).json();assert.equal(config.product,'dungeon_maps');assert.equal(config.price,'5.99');
  const {token}=await(await x.request('/api/license','POST')).json();const claims=JSON.parse(Buffer.from(token.split('.')[1],'base64url'));
  assert.equal(claims.aud,'dungeonmappilot');assert.equal(claims.product,'dungeon_maps');assert.deepEqual(claims.features,['dungeon_maps']);
  x.sub.plan_id='P-LEVELING';await assert.rejects(x.request('/api/license','POST'),/SUBSCRIPTION_MISMATCH/);
  assert.equal(paidUntil({status:'ACTIVE'},[{...sale,amount_with_breakdown:{gross_amount:{currency_code:'CAD',value:'9.99'}}}],new Set(),now),0);
  const other=setup();const session=await setup().start();
  await assert.rejects(other.request('/api/restore','POST',{key:session.recoveryKey}),/INVALID_KEY/);
});

test('new-checkout maintenance does not block existing access, cancellation or verified webhooks',async()=>{
  const x=setup();await x.enroll();x.env.BILLING_ENABLED='false';
  await assert.rejects(x.request('/api/checkout','POST'),/SERVICE_NOT_READY/);
  assert.equal((await x.request('/api/license','POST')).status,200);
  await x.request('/api/webhook','POST',{id:'MAINT1',event_type:'PAYMENT.SALE.COMPLETED',resource:{billing_agreement_id:'I-TEST'}});
  assert.ok(x.db.prepare('SELECT last_webhook_at FROM accounts').get().last_webhook_at>0);
  assert.equal((await(await x.request('/api/cancel','POST',{confirm:true})).json()).status,'CANCELLED');
});

test('only a new completed monthly payment extends access after an expired period',()=>{
  const later=Date.parse('2026-12-09T16:00:00Z');
  assert.ok(paidUntil({status:'ACTIVE'},[sale],new Set(),later)<later);
  const renewal={...sale,id:'SALE2',time:'2026-12-08T15:00:00Z'};
  assert.equal(paidUntil({status:'ACTIVE'},[sale,renewal],new Set(),later),Date.parse('2027-01-08T15:00:00Z'));
  assert.ok(paidUntil({status:'ACTIVE'},[sale,{...renewal,status:'FAILED'}],new Set(),later)<later);
});

test('companion download only redirects to the published owner repository',async()=>{
  const x=setup();x.env.COMPANION_URL='https://evil.test/companion.zip';await assert.rejects(x.request('/download'),/DOWNLOAD_NOT_READY/);
  x.env.COMPANION_URL='https://github.com/Axxira/axxira-website/releases/download/test/DungeonMapPilot-Companion-v1.0.0-Windows-x64.zip';
  const response=await x.request('/download');assert.equal(response.status,302);assert.equal(response.headers.get('location'),x.env.COMPANION_URL);
});
test('approval alone, failed payments, wrong currencies and revoked payments grant nothing',()=>{
  assert.equal(paidUntil({status:'APPROVAL_PENDING'},[sale],new Set(),now),0);
  for(const changed of [{status:'PENDING'},{amount_with_breakdown:{gross_amount:{currency_code:'USD',value:'5.99'}}},{time:'2030-01-01T00:00:00Z'}])assert.equal(paidUntil({status:'ACTIVE'},[{...sale,...changed}],new Set(),now),0);
  assert.equal(paidUntil({status:'ACTIVE'},[sale],new Set(['SALE1']),now),0);
});
test('session secrets are hashed, cookies are private, recovery restores the same account',async()=>{
  const x=setup(),session=await x.start();const row=x.db.prepare('SELECT * FROM accounts').get();assert.notEqual(row.token_hash,session.recoveryKey);assert.equal(row.token_hash.length,64);
  const result=await x.request('/api/restore','POST',{key:session.recoveryKey});assert.match(result.headers.get('set-cookie'),/HttpOnly; Secure; SameSite=Lax/);assert.equal((await result.json()).id,session.id);
});
test('cross-origin session creation and unauthenticated license access are rejected',async()=>{
  const x=setup();await assert.rejects(x.request('/api/session','POST',undefined,{Origin:'https://evil.test'}),/WRONG_ORIGIN/);await assert.rejects(x.request('/api/license','POST'),/SIGN_IN_REQUIRED/);
});
test('one session cannot access another subscription by supplying a PayPal id',async()=>{
  const x=setup();await x.enroll();x.sub.custom_id='someone-else';await assert.rejects(x.request('/api/license','POST',{subscriptionId:'I-OTHER'}),/SUBSCRIPTION_MISMATCH/);
});
test('checkout retries reuse pending approval; paid accounts cannot be billed twice',async()=>{
  const x=setup();await x.start();const first=await(await x.request('/api/checkout','POST')).json(),second=await(await x.request('/api/checkout','POST')).json();assert.deepEqual(second,first);assert.equal(x.calls.filter(c=>c.path==='/v1/billing/subscriptions').length,1);
  x.sub.status='ACTIVE';await assert.rejects(x.request('/api/checkout','POST'),/SUBSCRIPTION_EXISTS/);
});
test('pending approval never queries transactions, which PayPal rejects before activation',async()=>{
  const x=setup();await x.start();await x.request('/api/checkout','POST');x.calls.length=0;await x.request('/api/status');
  assert.equal(x.calls.some(c=>c.path.includes('/transactions?')),false);
});
test('resubscription after an ended period uses a fresh PayPal idempotency key',async()=>{
  const x=setup();await x.enroll();x.sub.status='CANCELLED';x.setSales([]);await x.request('/api/checkout','POST');
  const creations=x.calls.filter(c=>c.path==='/v1/billing/subscriptions');assert.equal(creations.length,2);assert.notEqual(creations[0].options.requestId,creations[1].options.requestId);
});
test('a paid license is RSA signed, environment-bound and limited to seven days',async()=>{
  const x=setup();await x.enroll();const value=await(await x.request('/api/license','POST')).json();const [header,body,sig]=value.token.split('.');
  assert.equal(verify('RSA-SHA256',Buffer.from(header+'.'+body),createPublicKey(pair.privateKey),Buffer.from(sig,'base64url')),true);
  const claims=JSON.parse(Buffer.from(body,'base64url'));assert.equal(claims.environment,'sandbox');assert.equal(claims.exp,now/1000+7*86400);assert.deepEqual(claims.features,['dungeon_maps']);
});
test('cancellation needs an explicit confirmation and retains already paid time',async()=>{
  const x=setup();await x.enroll();await assert.rejects(x.request('/api/cancel','POST',{}),/CONFIRM_REQUIRED/);
  const result=await(await x.request('/api/cancel','POST',{confirm:true})).json();assert.equal(result.status,'CANCELLED');assert.ok(result.paidUntil>now);
  assert.equal((await x.request('/api/license','POST')).status,200);
});
test('suspended, unpaid and expired periods do not issue licenses',async()=>{
  for(const variant of ['SUSPENDED','UNPAID','OLD']){const x=setup();await x.enroll();if(variant==='SUSPENDED')x.sub.status='SUSPENDED';if(variant==='UNPAID')x.setSales([]);if(variant==='OLD')x.setSales([{...sale,time:'2026-09-01T00:00:00Z'}]);await assert.rejects(x.request('/api/license','POST'),/NO_ACTIVE_ACCESS/);}
});
test('forged webhook cannot alter the database',async()=>{
  const x=setup();await x.enroll();x.setSignature(false);await assert.rejects(x.request('/api/webhook','POST',{id:'EV1',event_type:'BILLING.SUBSCRIPTION.ACTIVATED',resource:{id:'I-TEST'}}),/INVALID_SIGNATURE/);assert.equal(x.db.prepare('SELECT COUNT(*) AS n FROM events').get().n,0);
});
test('verified webhook retries are idempotent; out-of-order activation reads current state',async()=>{
  const x=setup();await x.enroll();x.sub.status='SUSPENDED';const event={id:'EV1',event_type:'BILLING.SUBSCRIPTION.ACTIVATED',resource:{id:'I-TEST'}};
  await x.request('/api/webhook','POST',event);const calls=x.calls.length;await x.request('/api/webhook','POST',event);assert.equal(x.calls.length,calls);assert.equal(x.db.prepare('SELECT status FROM accounts').get().status,'SUSPENDED');
});
test('refund without billing agreement id resolves the recorded sale and revokes access',async()=>{
  const x=setup();await x.enroll();await x.request('/api/license','POST');
  await x.request('/api/webhook','POST',{id:'REF1',event_type:'PAYMENT.SALE.REFUNDED',resource:{id:'REFUND1',sale_id:'SALE1'}});
  await assert.rejects(x.request('/api/license','POST'),/NO_ACTIVE_ACCESS/);
});
test('refund arriving before the payment is recorded still prevents future activation',async()=>{
  const x=setup();await x.start();await x.request('/api/webhook','POST',{id:'REF1',event_type:'PAYMENT.SALE.REFUNDED',resource:{id:'REFUND1',sale_id:'SALE1'}});
  await x.request('/api/checkout','POST');x.sub.status='ACTIVE';await assert.rejects(x.request('/api/license','POST'),/NO_ACTIVE_ACCESS/);
});
test('an old reconciliation cannot overwrite a newer revocation',async()=>{
  const x=setup();await x.enroll();const old=x.db.prepare('SELECT * FROM accounts').get();x.db.exec('UPDATE accounts SET revision=revision+1,paid_until=0');
  const row=await reconcile(old,x.env,x.provider,now);assert.equal(row.paid_until,0);
});
test('rate limits prevent unlimited account creation',async()=>{
  const x=setup();for(let i=0;i<40;i++)await handle(new Request(origin+'/api/session',{method:'POST',headers:{Origin:origin}}),x.env,{provider:x.provider,now});
  await assert.rejects(handle(new Request(origin+'/api/session',{method:'POST',headers:{Origin:origin}}),x.env,{provider:x.provider,now}),/PLEASE_WAIT/);
});
test('PayPal adapter rejects wrong price, trial periods and unsigned notifications',async()=>{
  const plan={id:'P-TEST',status:'ACTIVE',billing_cycles:[{tenure_type:'REGULAR',total_cycles:0,frequency:{interval_unit:'MONTH',interval_count:1},pricing_scheme:{fixed_price:{currency_code:'CAD',value:'5.99'}}}]};
  let reply=plan;const pp=new PayPal(setup().env,async url=>Response.json(url.endsWith('/token')?{access_token:'TEST'}:reply));await pp.validatePlan();
  reply={...plan,billing_cycles:[...plan.billing_cycles,{tenure_type:'TRIAL'}]};await assert.rejects(pp.validatePlan(),/PLAN_NOT_READY/);
  assert.equal(await pp.verifyWebhook(new Request(origin),{}),false);
});
