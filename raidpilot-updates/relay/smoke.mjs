// Live integration test: only fictitious positions and cryptographically random room codes.
// No Tarkov process or user positions are involved.
import assert from "node:assert/strict";
import { randomBytes } from "node:crypto";
const base = "https://raidpilot-team-relay.tyronixstudio.workers.dev";
const health = await fetch(base + "/health", { signal: AbortSignal.timeout(10000) });
assert.equal(health.status, 200, "Worker unreachable");
const body = await health.json();
assert.equal(body.status, "ok", "RaidPilot Worker protocol unavailable");
assert.equal(body.service, "RaidPilot Team Relay");
assert.ok(typeof WebSocket === "function", "Node 22+ WebSocket required");
const team = randomBytes(24).toString("base64url");
const other = randomBytes(24).toString("base64url");
const clients = [];
const wsUrl = (room,name) => base.replace("https:", "wss:") + "/ws?team=" + encodeURIComponent(room) + "&name=" + encodeURIComponent(name);
async function connect(room,name) {
  const ws = new WebSocket(wsUrl(room,name));
  const messages = [];
  const waiters = [];
  const holder = {ws,messages,waiters};
  clients.push(holder);
  ws.addEventListener("message", event => {
    let msg;
    try { msg = JSON.parse(String(event.data)); } catch { return; }
    const idx = waiters.findIndex(w => w.type === msg.type);
    if (idx >= 0) {const w = waiters.splice(idx,1)[0]; clearTimeout(w.timer); w.resolve(msg);}
    else messages.push(msg);
  });
  await new Promise((resolve,reject) => {
    const timer = setTimeout(() => reject(new Error("WebSocket timed out: " + name)),10000);
    ws.addEventListener("open",()=>{clearTimeout(timer);resolve();},{once:true});
    ws.addEventListener("error",()=>{clearTimeout(timer);reject(new Error("WebSocket refused: " + name));},{once:true});
  });
  return holder;
}
function waitFor(c,type,timeout=8000) {
  const idx=c.messages.findIndex(m=>m.type===type);
  if(idx>=0)return Promise.resolve(c.messages.splice(idx,1)[0]);
  return new Promise((resolve,reject)=>{
    const w={type,resolve,timer:null};
    w.timer=setTimeout(()=>{const idx=c.waiters.indexOf(w);if(idx>=0)c.waiters.splice(idx,1);reject(Error("Timed out waiting for "+type));},timeout);
    c.waiters.push(w);
  });
}
try {
  const a=await connect(team,"RaidPilotTestA");
  const aWelcome=await waitFor(a,"welcome"); assert.ok(aWelcome.id);
  const b=await connect(team,"RaidPilotTestB");
  const bWelcome=await waitFor(b,"welcome"); assert.ok(bWelcome.id);
  await waitFor(a,"peer_joined");
  const outsider=await connect(other,"IsolatedClient");
  await waitFor(outsider,"welcome");
  a.ws.send(JSON.stringify({type:"pose",map:"customs",position:{x:101,y:0,z:-25},rotation:270,floor:"BASE"}));
  const received=await waitFor(b,"pose");
  assert.equal(received.map,"customs");
  assert.equal(received.position.x,101);
  assert.equal(received.id,aWelcome.id);
  b.ws.send(JSON.stringify({type:"pose",map:"customs",position:{x:102,y:1,z:-26},rotation:90,floor:"BASE"}));
  const back=await waitFor(a,"pose");
  assert.equal(back.position.x,102);
  assert.equal(back.id,bWelcome.id);
  await new Promise(r=>setTimeout(r,300));
  assert.equal(outsider.messages.filter(m=>m.type==="pose").length,0,"Cross-room leak");
  console.log("PASS: Cloudflare Worker health, two-way WebSockets and isolated rooms (synthetic)");
} finally {for(const c of clients)try{c.ws.close();}catch{}}
