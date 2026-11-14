import {PayPal, ServiceError, paidUntil, MONTHLY_PRICE_CAD} from './paypal.mjs';
const enc=new TextEncoder();
export const hash=async value=>Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',enc.encode(value))),n=>n.toString(16).padStart(2,'0')).join('');
const b64=value=>btoa(String.fromCharCode(...value)).replace(/=/g,'').replace(/\+/g,'-').replace(/\//g,'_');
const token=()=>b64(crypto.getRandomValues(new Uint8Array(32)));
const companionURL=env=>/^https:\/\/github\.com\/Axxira\/axxira-website\/releases\/download\/[A-Za-z0-9._-]+\/ForeverPilot-Companion-v[0-9.]+-Windows-x64\.zip$/.test(env.COMPANION_URL||'')?env.COMPANION_URL:null;
const json=(value,status=200,headers={})=>new Response(JSON.stringify(value),{status,headers:{'Content-Type':'application/json;charset=utf-8','Cache-Control':'no-store',...headers}});
function connected(env) {
  return ['sandbox','live'].includes(env.PAYPAL_ENV) &&
    !!(env.DB&&env.PAYPAL_CLIENT_ID&&env.PAYPAL_CLIENT_SECRET&&env.PAYPAL_PLAN_ID&&env.PAYPAL_WEBHOOK_ID&&env.SIGNING_JWK);
}
export function configured(env) {
  return connected(env) && env.BILLING_ENABLED==='true' &&
    (env.PAYPAL_ENV==='sandbox'||env.LIVE_VERIFIED==='true'&&!!companionURL(env));
}
async function body(request) {
  if (!request.headers.get('content-type')?.startsWith('application/json')) throw new ServiceError('JSON_REQUIRED',415);
  const raw=await request.text();if (raw.length>65536) throw new ServiceError('TOO_LARGE',413);
  try { return JSON.parse(raw); } catch { throw new ServiceError('INVALID_JSON',400); }
}
function credential(request) {
  const value=request.headers.get('authorization')?.replace(/^Bearer /,'')||request.headers.get('cookie')?.match(/(?:^|;\s*)fp_session=([^;]+)/)?.[1];
  return /^[A-Za-z0-9_-]{43}$/.test(value||'')?value:null;
}
async function account(request,env) {
  const key=credential(request);if (!key) throw new ServiceError('SIGN_IN_REQUIRED',401);
  const row=await env.DB.prepare('SELECT * FROM accounts WHERE token_hash=?').bind(await hash(key)).first();
  if (!row) throw new ServiceError('SIGN_IN_REQUIRED',401);return row;
}
function cookie(key) { return `fp_session=${key}; HttpOnly; Secure; SameSite=Lax; Path=/; Max-Age=31536000`; }
async function rate(request,env,now) {
  // The platform supplies this header. Store a daily hash, never the raw address.
  const key=await hash((request.headers.get('CF-Connecting-IP')||'local')+':'+Math.floor(now/86400000));
  const row=await env.DB.prepare('INSERT INTO limits(key,window,count) VALUES(?,?,1) ON CONFLICT(key,window) DO UPDATE SET count=count+1 RETURNING count')
    .bind(key,Math.floor(now/60000)).first();
  if (row.count>40) throw new ServiceError('PLEASE_WAIT',429);
}
export async function reconcile(row,env,provider,now=Date.now()) {
  if (!row.subscription_id) return row;
  const sub=await provider.request('/v1/billing/subscriptions/'+encodeURIComponent(row.subscription_id));
  if (sub.id!==row.subscription_id || sub.plan_id!==env.PAYPAL_PLAN_ID || sub.custom_id!==row.id) throw new ServiceError('SUBSCRIPTION_MISMATCH');
  const start=new Date(now-35*86400000).toISOString(),end=new Date(now).toISOString();
  const tx=['ACTIVE','CANCELLED','EXPIRED'].includes(sub.status)
    ?await provider.request(`/v1/billing/subscriptions/${encodeURIComponent(row.subscription_id)}/transactions?start_time=${encodeURIComponent(start)}&end_time=${encodeURIComponent(end)}`)
    :{transactions:[]}; // PayPal rejects transaction queries before buyer approval.
  for (const sale of tx.transactions||[]) if (typeof sale.id==='string') await env.DB.prepare('INSERT OR IGNORE INTO sales(sale_id,subscription_id) VALUES(?,?)').bind(sale.id,row.subscription_id).run();
  const revoked=await env.DB.prepare("SELECT sale_id FROM revoked_sales WHERE subscription_id=? OR subscription_id=''").bind(row.subscription_id).all();
  const until=paidUntil(sub,tx.transactions,new Set(revoked.results.map(t=>t.sale_id)),now);
  await env.DB.prepare('UPDATE accounts SET status=?,paid_until=?,checked_at=?,revision=revision+1 WHERE id=? AND checked_at<=? AND revision=?')
    .bind(sub.status,until,now,row.id,now,row.revision).run();
  return await env.DB.prepare('SELECT * FROM accounts WHERE id=?').bind(row.id).first();
}
async function license(row,env,now) {
  if (row.paid_until<=now || !['ACTIVE','CANCELLED','EXPIRED'].includes(row.status)) throw new ServiceError('NO_ACTIVE_ACCESS',403);
  const jwk=JSON.parse(env.SIGNING_JWK);
  const key=await crypto.subtle.importKey('jwk',jwk,{name:'RSASSA-PKCS1-v1_5',hash:'SHA-256'},false,['sign']);
  const exp=Math.floor(Math.min(row.paid_until,now+7*86400000)/1000);
  const claims={iss:env.PUBLIC_ORIGIN,aud:'foreverpilot',sub:row.id,iat:Math.floor(now/1000),exp,
    environment:env.PAYPAL_ENV,features:['fast_xp','class_guidance'],paidUntil:Math.floor(row.paid_until/1000)};
  const input=b64(enc.encode(JSON.stringify({alg:'RS256',typ:'JWT',kid:jwk.kid||'fp-v1'})))+'.'+b64(enc.encode(JSON.stringify(claims)));
  return {token:input+'.'+b64(new Uint8Array(await crypto.subtle.sign('RSASSA-PKCS1-v1_5',key,enc.encode(input)))),expiresAt:exp};
}
async function webhook(request,env,provider,now) {
  const event=await body(request);
  if (!event.id || typeof event.id!=='string' || event.id.length>128 || !await provider.verifyWebhook(request,event)) throw new ServiceError('INVALID_SIGNATURE',401);
  if (await env.DB.prepare('SELECT id FROM events WHERE id=?').bind(event.id).first()) return json({received:true});
  const resource=event.resource||{};
  let id=event.event_type?.startsWith('BILLING.SUBSCRIPTION.')?resource.id:resource.billing_agreement_id;
  if (['PAYMENT.SALE.REFUNDED','PAYMENT.SALE.REVERSED'].includes(event.event_type)) {
    const saleLink=resource.links?.find(l=>l.rel==='sale')?.href;
    let linkedSale;try { linkedSale=new URL(saleLink).pathname.match(/^\/v1\/payments\/sale\/([A-Za-z0-9]+)$/)?.[1]; } catch {}
    const saleId=resource.sale_id||linkedSale||(event.event_type==='PAYMENT.SALE.REVERSED'?resource.id:null);
    if (typeof saleId!=='string') throw new ServiceError('INVALID_EVENT',400);
    id ||= (await env.DB.prepare('SELECT subscription_id FROM sales WHERE sale_id=?').bind(saleId).first())?.subscription_id;
    await env.DB.prepare('INSERT OR IGNORE INTO revoked_sales(sale_id,subscription_id,received_at) VALUES(?,?,?)').bind(saleId,id||'',now).run();
    if (id) await env.DB.prepare('UPDATE accounts SET paid_until=0,checked_at=0,revision=revision+1 WHERE subscription_id=?').bind(id).run();
  }
  const row=id?await env.DB.prepare('SELECT * FROM accounts WHERE subscription_id=?').bind(id).first():null;
  if (row) {
    // Re-read PayPal: delayed or out-of-order events cannot re-enable an old subscription.
    await reconcile(row,env,provider,now);
    await env.DB.prepare('UPDATE accounts SET last_webhook_at=MAX(last_webhook_at,?) WHERE id=?').bind(now,row.id).run();
  }
  await env.DB.prepare('INSERT OR IGNORE INTO events(id,received_at) VALUES(?,?)').bind(event.id,now).run();
  return json({received:true});
}
export async function handle(request,env,{provider,now=Date.now()}={}) {
  const url=new URL(request.url),path=url.pathname;
  const origin=env.PUBLIC_ORIGIN||url.origin;
  if(path==='/download'&&request.method==='GET') {
    const destination=companionURL(env);if(!destination)throw new ServiceError('DOWNLOAD_NOT_READY');
    return Response.redirect(destination,302);
  }
  if(path==='/api/public-key' && request.method==='GET') {
    if(!env.SIGNING_JWK) throw new ServiceError('SERVICE_NOT_READY');
    const {n,e,kid}=JSON.parse(env.SIGNING_JWK);return json({kty:'RSA',alg:'RS256',use:'sig',kid:kid||'fp-v1',n,e});
  }
  if (path==='/api/config' && request.method==='GET') {
    const serverReady=configured(env);
    let priceVerified=false;
    if (serverReady) {
      try { await (provider||new PayPal(env)).validatePlan();priceVerified=true; }
      catch { /* Reject stale provider prices or provider unavailability. */ }
    }
    return json({ready:serverReady&&priceVerified,companionReady:!!companionURL(env),
      environment:env.PAYPAL_ENV||'sandbox',price:priceVerified?MONTHLY_PRICE_CAD:null,
      currency:'CAD',interval:'MONTH'});
  }
  if (path==='/health') return json({service:'foreverpilot-subscriptions',billingReady:configured(env)});
  if (!path.startsWith('/api/')) return env.ASSETS.fetch(request);
  if (!env.DB) throw new ServiceError('SERVICE_NOT_READY');
  if (url.origin!==origin) throw new ServiceError('WRONG_ORIGIN',403);
  if (path==='/api/webhook' && request.method==='POST') {
    if (!connected(env)) throw new ServiceError('SERVICE_NOT_READY');
    return webhook(request,env,provider||new PayPal(env),now);
  }
  // Browser writes require an exact Origin; native clients must authenticate with a bearer token.
  if (request.method!=='GET' && request.headers.get('origin')!==origin && !request.headers.get('authorization')) throw new ServiceError('WRONG_ORIGIN',403);
  await rate(request,env,now);
  if (path==='/api/session' && request.method==='POST') {
    if (credential(request)) { const row=await account(request,env);return json({id:row.id}); }
    const key=token(),id=crypto.randomUUID();
    await env.DB.prepare('INSERT INTO accounts(id,token_hash,created_at) VALUES(?,?,?)').bind(id,await hash(key),now).run();
    return json({id,recoveryKey:key},201,{'Set-Cookie':cookie(key)});
  }
  if (path==='/api/restore' && request.method==='POST') {
    const data=await body(request);
    if (!/^[A-Za-z0-9_-]{43}$/.test(data.key||'')) throw new ServiceError('INVALID_KEY',401);
    const row=await env.DB.prepare('SELECT id FROM accounts WHERE token_hash=?').bind(await hash(data.key)).first();
    if (!row) throw new ServiceError('INVALID_KEY',401);
    return json({id:row.id},200,{'Set-Cookie':cookie(data.key)});
  }
  let row=await account(request,env);
  if (path==='/api/status' && request.method==='GET') {
    if (row.subscription_id && now-row.checked_at>60000 && connected(env)) row=await reconcile(row,env,provider||new PayPal(env),now);
    return json({status:row.status,active:row.paid_until>now && ['ACTIVE','CANCELLED','EXPIRED'].includes(row.status),paidUntil:row.paid_until,
      autoRenew:row.status==='ACTIVE',environment:env.PAYPAL_ENV,lastWebhookAt:row.last_webhook_at});
  }
  if (!connected(env)) throw new ServiceError('SERVICE_NOT_READY');
  provider ||= new PayPal(env);
  if (path==='/api/checkout' && request.method==='POST') {
    if (!configured(env)) throw new ServiceError('SERVICE_NOT_READY');
    await provider.validatePlan();
    if (row.subscription_id) {
      row=await reconcile(row,env,provider,now);
      if (row.status==='APPROVAL_PENDING' && row.approval_url) return json({approvalUrl:row.approval_url});
      // Allow a new purchase only after a cancelled/expired paid period has ended.
      if(!['CANCELLED','EXPIRED'].includes(row.status)||row.paid_until>now)throw new ServiceError('SUBSCRIPTION_EXISTS',409);
      await env.DB.prepare("UPDATE accounts SET subscription_id=NULL,approval_url=NULL,status='NEW',checkout_started=?,checkout_id=?,revision=revision+1 WHERE id=? AND revision=?")
        .bind(now,crypto.randomUUID(),row.id,row.revision).run();
      row=await env.DB.prepare('SELECT * FROM accounts WHERE id=?').bind(row.id).first();
      if(row.subscription_id)throw new ServiceError('SUBSCRIPTION_EXISTS',409);
    }
    await env.DB.prepare('UPDATE accounts SET checkout_started=? WHERE id=? AND checkout_started=0').bind(now,row.id).run();
    row=await env.DB.prepare('SELECT * FROM accounts WHERE id=?').bind(row.id).first();
    if (now-row.checkout_started>71*3600000) throw new ServiceError('CHECKOUT_EXPIRED',409);
    const sub=await provider.request('/v1/billing/subscriptions',{method:'POST',requestId:row.checkout_id||row.id,body:{plan_id:env.PAYPAL_PLAN_ID,custom_id:row.id,
      application_context:{brand_name:'ForeverPilot',shipping_preference:'NO_SHIPPING',user_action:'SUBSCRIBE_NOW',
        return_url:origin+'/retour',cancel_url:origin+'/annule'}}});
    const approval=sub.links?.find(l=>l.rel==='approve')?.href;
    const target=approval?new URL(approval):null;
    const expected=env.PAYPAL_ENV==='live'?'www.paypal.com':'www.sandbox.paypal.com';
    if (!/^I-[A-Z0-9]+$/.test(sub.id||'') || target?.protocol!=='https:' || target?.hostname!==expected) throw new ServiceError('INVALID_CHECKOUT');
    await env.DB.prepare('UPDATE accounts SET subscription_id=?,approval_url=?,status=? WHERE id=? AND subscription_id IS NULL')
      .bind(sub.id,approval,'APPROVAL_PENDING',row.id).run();
    return json({approvalUrl:approval});
  }
  if (path==='/api/license' && request.method==='POST') {
    row=await reconcile(row,env,provider,now);return json(await license(row,env,now));
  }
  if (path==='/api/cancel' && request.method==='POST') {
    const data=await body(request);if (data.confirm!==true) throw new ServiceError('CONFIRM_REQUIRED',400);
    row=await reconcile(row,env,provider,now);
    if (['ACTIVE','SUSPENDED','APPROVED'].includes(row.status)) {
      await provider.request('/v1/billing/subscriptions/'+encodeURIComponent(row.subscription_id)+'/cancel',{method:'POST',body:{reason:'Cancelled by the subscriber in the ForeverPilot account portal.'},requestId:'cancel-'+row.id});
      row=await reconcile(row,env,provider,now);
    }
    return json({status:row.status,paidUntil:row.paid_until});
  }
  throw new ServiceError('NOT_FOUND',404);
}
export default {
  async fetch(request,env) {
    let response;
    try { response=await handle(request,env); }
    catch(e) { response=json({error:e instanceof ServiceError?e.message:'SERVICE_UNAVAILABLE'},e instanceof ServiceError?e.status:503); }
    const headers=new Headers(response.headers);
    headers.set('X-Content-Type-Options','nosniff');headers.set('Referrer-Policy','no-referrer');headers.set('X-Frame-Options','DENY');
    headers.set('Content-Security-Policy',"default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'");
    return new Response(response.body,{status:response.status,headers});
  },
  async scheduled(_event,env) {
    if (!connected(env)) return;
    const rows=await env.DB.prepare("SELECT * FROM accounts WHERE subscription_id IS NOT NULL AND status NOT IN ('EXPIRED','CANCELLED') ORDER BY checked_at LIMIT 50").all();
    for (const row of rows.results) { try { await reconcile(row,env,new PayPal(env)); } catch { /* Leave the row due for a retry; never extend access on failure. */ } }
    await env.DB.prepare('DELETE FROM limits WHERE window<?').bind(Math.floor(Date.now()/60000)-60).run();
  }
};
