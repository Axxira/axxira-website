import fs from 'node:fs';
const file=new URL('../.e2e.secret.json',import.meta.url),access=JSON.parse(fs.readFileSync(file));
if(access.origin!=='https://foreverpilot-subscriptions-sandbox.tyronixstudio.workers.dev')throw Error('Sandbox only');
const r=await fetch(access.origin+'/api/checkout',{method:'POST',headers:{Authorization:'Bearer '+access.key,'Content-Type':'application/json'},body:'{}'});
const data=await r.json();if(data.approvalUrl){access.approvalUrl=data.approvalUrl;fs.writeFileSync(file,JSON.stringify(access));}
console.log({status:r.status,error:data.error,approvalHost:data.approvalUrl?new URL(data.approvalUrl).hostname:null});
