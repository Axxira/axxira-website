// Explicitly authorized one-time migration of an existing FastXp plan to CAD 6.99.
// Nothing runs until --execute is supplied. Never create a second subscription plan.
import fs from 'node:fs';
import {PayPal,MONTHLY_PRICE_CAD} from '../src/paypal.mjs';

const file=new URL('../.dev.vars.live.secret.json',import.meta.url);
if(!fs.existsSync(file)) throw new Error('LIVE_CREDENTIALS_UNAVAILABLE');
const env=JSON.parse(fs.readFileSync(file,'utf8'));
if(env.PAYPAL_ENV!=='live'||
   env.PUBLIC_ORIGIN!=='https://foreverpilot-subscriptions.tyronixstudio.workers.dev'||
   !/^P-[A-Z0-9]+$/.test(env.PAYPAL_PLAN_ID||'')||
   !env.PAYPAL_CLIENT_ID||!env.PAYPAL_CLIENT_SECRET) throw new Error('INVALID_LIVE_ENVIRONMENT');
const api=new PayPal(env);
const endpoint='/v1/billing/plans/'+encodeURIComponent(env.PAYPAL_PLAN_ID);
async function get() {
  const plan=await api.request(endpoint);
  const cycle=plan.billing_cycles?.[0];
  if(plan.id!==env.PAYPAL_PLAN_ID||plan.status!=='ACTIVE'||plan.billing_cycles?.length!==1||
     cycle?.tenure_type!=='REGULAR'||cycle.frequency?.interval_unit!=='MONTH'||
     cycle.frequency?.interval_count!==1||cycle.total_cycles!==0||
     cycle.pricing_scheme?.fixed_price?.currency_code!=='CAD'||
     Number(plan.payment_preferences?.setup_fee?.value||0)!==0||
     Number(plan.taxes?.percentage||0)!==0||plan.quantity_supported) throw new Error('UNEXPECTED_PAYPAL_PLAN');
  return {plan,cycle,amount:cycle.pricing_scheme.fixed_price.value};
}
const before=await get();
if(before.amount===MONTHLY_PRICE_CAD){
 console.log('FASTXP_LIVE_PLAN_PRICE_ALREADY_6_99_VERIFIED');
 process.exit(0);
}
if(before.amount!=='9.99') throw new Error('UNEXPECTED_EXISTING_PLAN_PRICE');
if(!process.argv.includes('--execute')){
 console.log('DRY_RUN: existing FastXp live monthly plan verified at CAD 9.99; target CAD 6.99. Nothing changed.');
 process.exit(0);
}
await api.request(endpoint+'/update-pricing-schemes',{method:'POST',body:{
 pricing_schemes:[{billing_cycle_sequence:before.cycle.sequence,
   pricing_scheme:{fixed_price:{value:MONTHLY_PRICE_CAD,currency_code:'CAD'}}}]
}});
const after=await get();
if(after.amount!==MONTHLY_PRICE_CAD) throw new Error('PRICE_UPDATE_NOT_YET_VERIFIED');
console.log('SUCCESS: EXISTING_FASTXP_PAYPAL_PLAN_PRICE_CAD_6_99; NO_NEW_PLAN_CREATED');
