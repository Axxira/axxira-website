// Synthetic capability probe: no real names, tokens, positions or quest progress.
// A deployed relay may intentionally support only pose. The result is informational,
// never publish "Quest sync works" without verifying a peer-to-peer round trip.
import { randomBytes } from "node:crypto";

const room=randomBytes(24).toString("base64url");
const base="wss://raidpilot-team-relay.tyronixstudio.workers.dev/ws";
const sockets=[];
const wait=async (ws,type,ms=2500)=>new Promise(resolve=>{
  let finished=false;
  const finish=(message)=>{
    if(finished)return;finished=true;clearTimeout(timeout);
    ws.removeEventListener("message",onMessage);resolve(message);
  };
  const onMessage=ev=>{try{const data=JSON.parse(String(ev.data));if(data.type===type)finish(data);}catch{}};
  const timeout=setTimeout(()=>finish(null),ms);
  ws.addEventListener("message",onMessage);
});
async function connect(name){
  const ws=new WebSocket(base+"?team="+encodeURIComponent(room)+"&name="+encodeURIComponent(name));
  sockets.push(ws);
  await new Promise((resolve,reject)=>{
    const timeout=setTimeout(()=>reject(Error("relay connect timeout")),8000);
    ws.addEventListener("open",()=>{clearTimeout(timeout);resolve();},{once:true});
    ws.addEventListener("error",()=>{clearTimeout(timeout);reject(Error("relay connection error"));},{once:true});
  });
  return ws;
}
try{
  const a=await connect("quest-probe-1"),b=await connect("quest-probe-2");
  const delivered=wait(b,"questplan");
  a.send(JSON.stringify({type:"questplan",mode:"pve",at:1792828800000,
    quests:["a12345678901234567890123"],done:[]}));
  const reply=await delivered;
  const valid=reply?.mode==="pve"&&Array.isArray(reply.quests)
    &&reply.quests[0]==="a12345678901234567890123";
  console.log("QUEST_RELAY_SUPPORT="+(valid?"yes":"no")+"; receivedType="+(reply?.type||"none"));
}catch(e){console.log("QUEST_RELAY_SUPPORT=unknown; reason="+String(e.message));}
finally{for(const s of sockets){try{s.close();}catch{}}}
