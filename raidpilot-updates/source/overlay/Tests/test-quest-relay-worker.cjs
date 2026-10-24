// Offline contract tests: never contact the deployed Cloudflare Worker.
const assert=require("node:assert/strict");
const {pathToFileURL}=require("node:url");
const path=require("node:path");
(async()=>{
  const mod=await import(pathToFileURL(path.join(__dirname,"../../../relay/worker-quest.mjs")));
  let n=0;
  const check=(ok,label)=>{assert.ok(ok,label);n++;};
  const team="q".repeat(43),a="a".repeat(24),b="b".repeat(24);
  check(mod.validTeamKey(team),"Accept derived 43-character room key");
  check(!mod.validTeamKey("RP-ABCD-EFGH-JKLM-NPQR"),"Raw invitation code must never travel as a room key");
  const now=Date.now();
  const clean=mod.cleanQuestPlan({type:"questplan",mode:"pve",quests:[a,a,b],
    done:[a+":"+b],at:now},now);
  check(clean?.quests.length===2&&clean.done.length===1,
    "Only de-duplicated public quest and step IDs should be forwarded");
  check(mod.cleanQuestPlan({type:"questplan",mode:"pvp",quests:[a],done:[],at:now},now)===null,
    "Unsupported game mode cannot contaminate another profile");
  check(mod.cleanQuestPlan({type:"questplan",mode:"pve",quests:[a],
    done:["secret"],at:now},now)===null,"No arbitrary text or player identity in quest payload");
  check(mod.cleanQuestPlan({type:"questplan",mode:"pve",quests:[a],
    done:[],at:now-300000},now)===null,"Replay outside a two-minute window rejected");
  check(mod.cleanQuestPlan({type:"questplan",mode:"pve",quests:Array(81).fill(a),
    done:[],at:now},now)===null,"Quest list bounded");
  check(mod.cleanQuestPlan({type:"questplan",mode:"pve",quests:[a],
    done:Array(121).fill(a+":"+b),at:now},now)===null,"Checked steps bounded");
  const validPose=mod.cleanPose({type:"pose",map:"customs",
    position:{x:1,y:0,z:2},rotation:375},now);
  check(validPose?.rotation===15&&validPose?.map==="customs","Legacy position behavior remains valid");
  check(mod.cleanPose({type:"pose",map:"customs",
    position:{x:"1",y:0,z:2},rotation:0},now)===null,"No string coordinates on network");
  const status=await mod.default.fetch(new Request("https://test/health"),{});
  const data=await status.json();
  check(status.status===200&&data.status==="ok"&&data.service==="RaidPilot Team Relay"
    &&data.features.questplan===true,"Feature advertisement requires a real server version");
  const denied=await mod.default.fetch(new Request("https://test/ws?team="+team),{});
  check(denied.status===400,"No anonymous socket without nickname");

  class MockSocket {
    constructor(id,name){this.info={id,name,lastPoseAt:0,lastQuestAt:0};this.messages=[];}
    deserializeAttachment(){return this.info;}
    serializeAttachment(s){this.info=s;}
    send(data){this.messages.push(JSON.parse(data));}
    close(){}
  }
  const alpha=new MockSocket("alpha","Alpha"),beta=new MockSocket("beta","Beta");
  const ctx={getWebSockets:()=>[alpha,beta]};
  const room=new mod.TeamRoom(ctx,{});
  room.webSocketMessage(alpha,JSON.stringify({type:"questplan",mode:"pve",
    quests:[a,b],done:[a+":"+b],at:Date.now()}));
  check(beta.messages.length===1&&beta.messages[0].id==="alpha"
    &&beta.messages[0].name==="Alpha"&&beta.messages[0].quests.length===2
    &&alpha.messages.length===0,"Two clients exchange a public quest plan only with the peer");
  room.webSocketMessage(alpha,JSON.stringify({type:"questplan",mode:"pve",
    quests:[a],done:[],at:Date.now()}));
  check(beta.messages.length===1,"Questplan send is limited to one every two seconds");
  room.webSocketMessage(beta,JSON.stringify({type:"pose",map:"customs",
    position:{x:2,y:0,z:1},rotation:90}));
  check(alpha.messages.some(m=>m.type==="pose"&&m.id==="beta"
    &&m.position.x===2),"Legacy position relay still works");
  const gamma=new MockSocket("gamma","Gamma");
  const otherRoom=new mod.TeamRoom({getWebSockets:()=>[gamma]},{});
  otherRoom.webSocketMessage(gamma,JSON.stringify({type:"questplan",mode:"pve",
    quests:[a],done:[],at:Date.now()}));
  check(alpha.messages.every(m=>m.id!=="gamma") && beta.messages.every(m=>m.id!=="gamma"),
    "Rooms have separate Durable Object instances");
  console.log(JSON.stringify({status:"PASS",checks:n}));
})().catch(e=>{console.error(e);process.exitCode=1;});
