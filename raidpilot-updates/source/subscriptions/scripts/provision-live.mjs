import fs from 'node:fs';
import {generateKeyPairSync} from 'node:crypto';
import {PayPal} from '../src/paypal.mjs';
const file=new URL('../.dev.vars.live.secret.json',import.meta.url),env=JSON.parse(fs.readFileSync(file));
if(env.PAYPAL_ENV!=='live'||env.PUBLIC_ORIGIN!=='https://foreverpilot-subscriptions.tyronixstudio.workers.dev')throw Error('Wrong production environment');
const save=()=>fs.writeFileSync(file,JSON.stringify(env,null,2));
const pp=new PayPal(env);const plan=await pp.validatePlan({allowDraft:true});
if(process.argv.includes('--activate')){
  const proof=JSON.parse(fs.readFileSync(new URL('../../../../../../outputs/ForeverPilot-finalisation-20261108/Activation-sandbox-verifiee.json',import.meta.url)));
  const cancel=JSON.parse(fs.readFileSync(new URL('../../../../../../outputs/ForeverPilot-finalisation-20261108/Annulation-sandbox-verifiee.json',import.meta.url)));
  if(!proof.verified||proof.environment!=='sandbox'||!cancel.verified||cancel.status!=='CANCELLED')throw Error('Missing end-to-end sandbox evidence');
  if(plan.status==='CREATED')await pp.request('/v1/billing/plans/'+env.PAYPAL_PLAN_ID+'/activate',{method:'POST'});
  await pp.validatePlan();
}
if(!env.PAYPAL_WEBHOOK_ID){
  const url=env.PUBLIC_ORIGIN+'/api/webhook',list=await pp.request('/v1/notifications/webhooks');
  const hook=list.webhooks?.find(h=>h.url===url)||await pp.request('/v1/notifications/webhooks',{method:'POST',body:{url,event_types:[
    'BILLING.SUBSCRIPTION.CREATED','BILLING.SUBSCRIPTION.ACTIVATED','BILLING.SUBSCRIPTION.UPDATED','BILLING.SUBSCRIPTION.CANCELLED','BILLING.SUBSCRIPTION.SUSPENDED','BILLING.SUBSCRIPTION.EXPIRED','BILLING.SUBSCRIPTION.PAYMENT.FAILED','PAYMENT.SALE.COMPLETED','PAYMENT.SALE.REFUNDED','PAYMENT.SALE.REVERSED'
  ].map(name=>({name}))}});env.PAYPAL_WEBHOOK_ID=hook.id;save();
}
if(!env.SIGNING_JWK){const pair=generateKeyPairSync('rsa',{modulusLength:2048});env.SIGNING_JWK=JSON.stringify({...pair.privateKey.export({format:'jwk'}),kid:'fp-live-v1'});save();}
console.log({environment:'live',planDefinitionVerified:true,planActivated:process.argv.includes('--activate'),webhookConfigured:true,checkoutEnabled:false});
