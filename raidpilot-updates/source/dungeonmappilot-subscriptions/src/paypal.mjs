export class ServiceError extends Error {
  constructor(code, status=503) { super(code); this.status=status; }
}
export class PayPal {
  constructor(env, transport=(...args)=>fetch(...args)) {
    if (!['sandbox','live'].includes(env.PAYPAL_ENV)) throw new ServiceError('CONFIGURATION');
    this.env=env; this.transport=transport;
    this.base=env.PAYPAL_ENV==='live'?'https://api-m.paypal.com':'https://api-m.sandbox.paypal.com';
  }
  async request(path, {method='GET', body, requestId}={}) {
    if (!this.token) {
      const r=await this.transport(this.base+'/v1/oauth2/token', {
        method:'POST', headers:{Authorization:'Basic '+btoa(this.env.PAYPAL_CLIENT_ID+':'+this.env.PAYPAL_CLIENT_SECRET),'Content-Type':'application/x-www-form-urlencoded'},
        body:'grant_type=client_credentials', signal:AbortSignal.timeout(15000)
      });
      if (!r.ok) throw new ServiceError('PAYPAL_UNAVAILABLE');
      this.token=(await r.json()).access_token;
      if (!this.token) throw new ServiceError('PAYPAL_UNAVAILABLE');
    }
    const r=await this.transport(this.base+path, {method, headers:{Authorization:'Bearer '+this.token,'Content-Type':'application/json',Prefer:'return=representation',...(requestId?{'PayPal-Request-Id':requestId}:{})},
      body:body?JSON.stringify(body):undefined,signal:AbortSignal.timeout(15000)});
    if (!r.ok) throw new ServiceError('PAYPAL_UNAVAILABLE'); // Never return provider responses, credentials or payer details.
    return r.status===204?{}:await r.json();
  }
  async validatePlan({allowDraft=false}={}) {
    const plan=await this.request('/v1/billing/plans/'+encodeURIComponent(this.env.PAYPAL_PLAN_ID));
    const cycle=plan.billing_cycles?.[0];
    if (plan.id!==this.env.PAYPAL_PLAN_ID || !(plan.status==='ACTIVE'||allowDraft&&plan.status==='CREATED') || plan.billing_cycles?.length!==1 ||
        cycle?.tenure_type!=='REGULAR' || cycle.frequency?.interval_unit!=='MONTH' || cycle.frequency?.interval_count!==1 ||
        cycle.total_cycles!==0 || cycle.pricing_scheme?.fixed_price?.currency_code!=='CAD' || cycle.pricing_scheme?.fixed_price?.value!=='5.99' ||
        Number(plan.payment_preferences?.setup_fee?.value||0)!==0 || Number(plan.taxes?.percentage||0)!==0 || plan.quantity_supported) {
      throw new ServiceError('PLAN_NOT_READY');
    }
    return plan;
  }
  async verifyWebhook(request, event) {
    const headers=['paypal-auth-algo','paypal-cert-url','paypal-transmission-id','paypal-transmission-sig','paypal-transmission-time'];
    if (headers.some(h=>!request.headers.get(h)) || !this.env.PAYPAL_WEBHOOK_ID) return false;
    const result=await this.request('/v1/notifications/verify-webhook-signature',{method:'POST',body:{
      auth_algo:request.headers.get(headers[0]),cert_url:request.headers.get(headers[1]),transmission_id:request.headers.get(headers[2]),
      transmission_sig:request.headers.get(headers[3]),transmission_time:request.headers.get(headers[4]),
      webhook_id:this.env.PAYPAL_WEBHOOK_ID,webhook_event:event
    }});
    return result.verification_status==='SUCCESS';
  }
}
export function monthAfter(time) {
  const date=new Date(time), day=date.getUTCDate(); date.setUTCDate(1);date.setUTCMonth(date.getUTCMonth()+1);
  const last=new Date(Date.UTC(date.getUTCFullYear(),date.getUTCMonth()+1,0)).getUTCDate();date.setUTCDate(Math.min(day,last));return +date;
}
export function paidUntil(subscription, transactions, revoked, now) {
  if (!['ACTIVE','CANCELLED','EXPIRED'].includes(subscription.status)) return 0;
  // Approval/activation alone is never proof of payment. No free trials in this plan.
  const sales=(transactions||[]).filter(t=>t.status==='COMPLETED' && !revoked.has(t.id) &&
    t.amount_with_breakdown?.gross_amount?.currency_code==='CAD' && t.amount_with_breakdown?.gross_amount?.value==='5.99' &&
    Number.isFinite(Date.parse(t.time)) && Date.parse(t.time)<=now);
  return sales.reduce((end,t)=>Math.max(end,monthAfter(Date.parse(t.time))),0);
}
