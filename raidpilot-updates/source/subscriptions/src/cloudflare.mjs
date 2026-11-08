import worker from './worker.mjs';
import schema from '../migrations/0001.sql';
// Isolated SQLite Durable Object. Does not touch the RaidPilot relay or require a shared database.
export class BillingStore {
  constructor(ctx,env) {
    const sql=ctx.storage.sql;
    for(const statement of schema.split(';').filter(s=>s.trim())) sql.exec(statement);
    const columns=new Set(sql.exec('PRAGMA table_info(accounts)').toArray().map(c=>c.name));
    if(!columns.has('checkout_id'))sql.exec('ALTER TABLE accounts ADD COLUMN checkout_id TEXT');
    if(!columns.has('last_webhook_at'))sql.exec('ALTER TABLE accounts ADD COLUMN last_webhook_at INTEGER NOT NULL DEFAULT 0');
    this.env={...env,DB:{prepare(query){let args=[];return {
      bind(...values){args=values;return this;},
      async first(){return sql.exec(query,...args).toArray()[0]||null;},
      async all(){return {results:sql.exec(query,...args).toArray()};},
      async run(){const result=sql.exec(query,...args);return {meta:{changes:result.rowsWritten}};}
    };}}};
  }
  async fetch(request) {
    if(new URL(request.url).hostname==='internal') {await worker.scheduled({},this.env);return new Response('ok');}
    return worker.fetch(request,this.env);
  }
}
export default {
  async fetch(request,env) {
    const path=new URL(request.url).pathname;
    if(path.startsWith('/api/')||path==='/health') return env.BILLING.get(env.BILLING.idFromName('subscriptions-v1')).fetch(request);
    return worker.fetch(request,env);
  },
  async scheduled(_event,env) {await env.BILLING.get(env.BILLING.idFromName('subscriptions-v1')).fetch('https://internal/reconcile');}
};
