import fs from 'node:fs';
import {PayPal} from '../src/paypal.mjs';
const env=JSON.parse(fs.readFileSync(new URL('../.dev.vars.sandbox.secret.json',import.meta.url)));
const access=JSON.parse(fs.readFileSync(new URL('../.e2e.secret.json',import.meta.url)));
if(env.PAYPAL_ENV!=='sandbox'||access.origin!=='https://foreverpilot-subscriptions-sandbox.tyronixstudio.workers.dev')throw Error('Sandbox only');
const status=await fetch(access.origin+'/api/status',{headers:{Authorization:'Bearer '+access.key}});console.log('service status',status.status,await status.json());
const session=await fetch(access.origin+'/api/session',{method:'POST',headers:{Authorization:'Bearer '+access.key}});const account=await session.json();
const pp=new PayPal(env,async(url,options)=>{const r=await fetch(url,options);console.log('PayPal',new URL(url).pathname,r.status,r.headers.get('paypal-debug-id'));if(!r.ok){const copy=await r.clone().json();console.log('error',copy.name,copy.details?.map(d=>({issue:d.issue,field:d.field})));}return r;});
await pp.validatePlan();
console.log({planVerified:true,accountFound:!!account.id}); // Read-only; checkout is an explicit separate script.
