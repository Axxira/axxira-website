// RaidPilot Team Relay candidate — deploy only after backing up the existing
// Cloudflare Worker and passing a two-peer live smoke test. This is NOT currently
// the production worker at tyronixstudio.workers.dev.
const MIME={"content-type":"application/json; charset=utf-8","cache-control":"no-store"};
const MAX_FRAME_BYTES=32768, MAX_ROOM_CLIENTS=12;
const TASK_ID=/^[0-9a-f]{24}$/i, STEP_ID=/^[0-9a-f]{24}:[0-9a-f]{24}$/i;
const ROOM_KEY=/^[A-Za-z0-9_-]{43}$/, MAP=/^[a-z0-9][a-z0-9_-]{0,63}$/i;
const MODE=new Set(["regular","pve","pvp-season"]);
const jsonResponse=(o,status=200)=>new Response(JSON.stringify(o),{status,headers:MIME});
function trimName(v){return String(v||"Joueur").replace(/[\u0000-\u001f\u007f]/g,"").slice(0,24)||"Joueur";}
export function cleanQuestPlan(data,now=Date.now()){
  if(!data||data.type!=="questplan"||!MODE.has(data.mode)
    ||!Number.isSafeInteger(data.at)||Math.abs(data.at-now)>120000
    ||!Array.isArray(data.quests)||!Array.isArray(data.done)
    ||data.quests.length>80||data.done.length>120)return null;
  if(data.quests.some(id=>typeof id!=="string"||!TASK_ID.test(id))
    ||data.done.some(id=>typeof id!=="string"||!STEP_ID.test(id)))return null;
  return {type:"questplan",mode:data.mode,quests:[...new Set(data.quests.map(x=>x.toLowerCase()))],
    done:[...new Set(data.done.map(x=>x.toLowerCase()))],at:now};
}
export function cleanPose(data,now=Date.now()){
  if(!data||data.type!=="pose"||typeof data.map!=="string"
    ||!MAP.test(data.map)||!data.position||typeof data.position!=="object"
    ||!["x","y","z"].every(x=>typeof data.position[x]==="number"
      &&Number.isFinite(data.position[x])&&Math.abs(data.position[x])<=20000)
    ||typeof data.rotation!=="number"||!Number.isFinite(data.rotation))return null;
  return {type:"pose",map:data.map.toLowerCase(),
    position:{x:data.position.x,y:data.position.y,z:data.position.z},
    rotation:((data.rotation%360)+360)%360,
    floor:typeof data.floor==="string"?data.floor.slice(0,32):null,at:now};
}
export function validTeamKey(value){return typeof value==="string"&&ROOM_KEY.test(value);}
export default {
  async fetch(request,env){
    const url=new URL(request.url);
    if(url.pathname==="/health")return jsonResponse({status:"ok",service:"RaidPilot Team Relay",
      protocol:2,features:{pose:true,questplan:true}});
    if(url.pathname!=="/ws")return jsonResponse({error:"not found"},404);
    const team=url.searchParams.get("team");
    const name=url.searchParams.get("name");
    if(!validTeamKey(team)||!name||name.length>80)return jsonResponse({error:"invalid team"},400);
    if((request.headers.get("Upgrade")||"").toLowerCase()!=="websocket")
      return jsonResponse({error:"websocket required"},426);
    if(!env.ROOM)return jsonResponse({error:"worker not configured"},503);
    const obj=env.ROOM.get(env.ROOM.idFromName(team));
    return obj.fetch(new Request("https://internal/ws?name="+encodeURIComponent(trimName(name)),request));
  }
};
export class TeamRoom {
  constructor(ctx,env){this.ctx=ctx;this.env=env;}
  async fetch(request){
    if(this.ctx.getWebSockets().length>=MAX_ROOM_CLIENTS)
      return jsonResponse({error:"room full"},429);
    const pair=new WebSocketPair(),client=pair[0],server=pair[1];
    const name=trimName(new URL(request.url).searchParams.get("name"));
    const id=crypto.randomUUID(),peers=[];
    for(const socket of this.ctx.getWebSockets()){
      const p=socket.deserializeAttachment();
      if(p?.id&&p.id!==id)peers.push(p.lastPose||{id:p.id,name:p.name});
    }
    this.ctx.acceptWebSocket(server);
    server.serializeAttachment({id,name,lastPose:null,lastPoseAt:0,lastQuestAt:0});
    server.send(JSON.stringify({type:"welcome",id,peers}));
    this.broadcast(server,{type:"peer_joined",id,name});
    return new Response(null,{status:101,webSocket:client});
  }
  broadcast(source,payload){
    const text=JSON.stringify(payload);
    for(const ws of this.ctx.getWebSockets()){
      if(ws===source)continue;
      try{ws.send(text);}catch(_){}
    }
  }
  webSocketMessage(ws,message){
    if(typeof message!=="string"||message.length>MAX_FRAME_BYTES)return;
    let data;
    try{data=JSON.parse(message);}catch{return;}
    const p=ws.deserializeAttachment();
    if(!p?.id)return;
    const now=Date.now();
    if(data?.type==="pose"){
      if(now-(p.lastPoseAt||0)<75)return;
      const clean=cleanPose(data,now);
      if(!clean)return;
      p.lastPoseAt=now;
      p.lastPose={...clean,id:p.id,name:p.name};
      ws.serializeAttachment(p);
      this.broadcast(ws,p.lastPose);
    } else if(data?.type==="questplan"){
      if(now-(p.lastQuestAt||0)<2000)return;
      const clean=cleanQuestPlan(data,now);
      if(!clean)return;
      p.lastQuestAt=now;
      ws.serializeAttachment(p);
      // No quest plan stored in attachments, disk or database; only relayed.
      this.broadcast(ws,{...clean,id:p.id,name:p.name});
    }
  }
  webSocketClose(ws,code,reason,wasClean){
    const p=ws.deserializeAttachment();
    if(p?.id)this.broadcast(ws,{type:"peer_left",id:p.id});
    try{ws.close(code,reason);}catch(_){}
  }
  webSocketError(ws){
    const p=ws.deserializeAttachment();
    if(p?.id)this.broadcast(ws,{type:"peer_left",id:p.id});
    try{ws.close(1011,"Transport error");}catch(_){}
  }
}
