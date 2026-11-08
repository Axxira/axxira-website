import fs from 'node:fs';
import {generateKeyPairSync} from 'node:crypto';
import {PayPal} from '../src/paypal.mjs';
const file=new URL('../.dev.vars.sandbox.secret.json',import.meta.url);
const env=JSON.parse(fs.readFileSync(file,'utf8'));
env.PAYPAL_ENV='sandbox';
env.PUBLIC_ORIGIN='https://foreverpilot-subscriptions-sandbox.tyronixstudio.workers.dev';
const save=()=>fs.writeFileSync(file,JSON.stringify(env,null,2));
const pp=new PayPal(env);
if(!env.PAYPAL_PRODUCT_ID){
  const product=await pp.request('/v1/catalogs/products',{method:'POST',requestId:'foreverpilot-sandbox-product-v1',body:{name:'ForeverPilot — Fast XP + Class',description:'ForeverPilot monthly access — sandbox tests only',type:'SERVICE',category:'SOFTWARE'}});
  env.PAYPAL_PRODUCT_ID=product.id;save();
}
if(!env.PAYPAL_PLAN_ID){
  const plan=await pp.request('/v1/billing/plans',{method:'POST',requestId:'foreverpilot-sandbox-plan-v1',body:{product_id:env.PAYPAL_PRODUCT_ID,name:'ForeverPilot Premium — CAD 9.99 / month',status:'ACTIVE',
    billing_cycles:[{frequency:{interval_unit:'MONTH',interval_count:1},tenure_type:'REGULAR',sequence:1,total_cycles:0,pricing_scheme:{fixed_price:{value:'9.99',currency_code:'CAD'}}}],
    payment_preferences:{auto_bill_outstanding:false,setup_fee:{value:'0',currency_code:'CAD'},setup_fee_failure_action:'CANCEL',payment_failure_threshold:1}}});
  env.PAYPAL_PLAN_ID=plan.id;save();
}
await pp.validatePlan();
if(!env.PAYPAL_WEBHOOK_ID){
  const url=env.PUBLIC_ORIGIN+'/api/webhook';
  const existing=await pp.request('/v1/notifications/webhooks');
  let hook=existing.webhooks?.find(h=>h.url===url);
  if(!hook)hook=await pp.request('/v1/notifications/webhooks',{method:'POST',body:{url,event_types:[
    'BILLING.SUBSCRIPTION.CREATED','BILLING.SUBSCRIPTION.ACTIVATED','BILLING.SUBSCRIPTION.UPDATED','BILLING.SUBSCRIPTION.CANCELLED','BILLING.SUBSCRIPTION.SUSPENDED','BILLING.SUBSCRIPTION.EXPIRED','BILLING.SUBSCRIPTION.PAYMENT.FAILED','PAYMENT.SALE.COMPLETED','PAYMENT.SALE.REFUNDED','PAYMENT.SALE.REVERSED'
  ].map(name=>({name}))}});
  env.PAYPAL_WEBHOOK_ID=hook.id;save();
}
if(!env.SIGNING_JWK){const pair=generateKeyPairSync('rsa',{modulusLength:2048});env.SIGNING_JWK=JSON.stringify({...pair.privateKey.export({format:'jwk'}),kid:'fp-sandbox-v1'});save();}
env.BILLING_ENABLED='true';env.LIVE_VERIFIED='false';save();
console.log(JSON.stringify({environment:'sandbox',planVerified:true,webhookConfigured:true,credentialsPrinted:false}));
