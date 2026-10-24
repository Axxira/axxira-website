const assert=require("node:assert/strict");
const QuestPilot=require("../LocalMap/quest-pilot.js");
let checks=0;
function test(label,fn){fn();checks++;console.log("OK "+label);}
const translated={"quest-name":"Trois balises", "mark-first":"Installer la première balise",
 "mark-second":"Installer la deuxième balise","mark-third":"Installer la troisième balise"};
const objective=(id,description,map,x,z,extras={})=>({
  id,type:"plantItem",description,markerItem:"beacon-item",
  zones:[{map:{id:map},position:{x,y:1,z}}],...extras,
});
const task={id:"quest-a",name:"quest-name",objectives:[
  objective("first","mark-first","customs",10,10),
  objective("second","mark-second","customs",50,15),
  objective("third","mark-third","customs",90,75),
]};
const optionsTask={id:"quest-b",name:"Optional quest",objectives:[{
  id:"one",type:"findItem",description:"Search item",possibleLocations:[
    {map:{id:"customs"},positions:[{x:12,z:15},{x:15,z:18}]}
  ]
}]};
const remoteTask={id:"quest-c",name:"Other maps",objectives:[
  objective("one","On woods","woods",10,10),
  objective("two","On customs","customs",30,40),
  {id:"unk",type:"kill",description:"kill 10 scavs",zones:[]}
]};
const memory=new Map();
const storage={
 getItem:k=>memory.get(k)||null,
 setItem:(k,v)=>memory.set(k,String(v)),
};
const planner=new QuestPilot.Planner(storage,"pve");
planner.setCatalog([task,optionsTask,remoteTask],translated,
 {"beacon-item":{name:"Beacon"}},{"Beacon":"Balise"});
const map=new Set(["customs"]);
test("trois balises distinctes portent exactement 1/2/3",()=>{
 const p=planner.taskFor("quest-a");
 assert.deepEqual(p.objectives.map(s=>s.number),[1,2,3]);
 assert.deepEqual(p.objectives.map(s=>s.name),
 ["Installer la première balise","Installer la deuxième balise","Installer la troisième balise"]);
 assert.equal(p.objectives[0].objectiveCount,3);
});
test("chacune des balises conserve la même couleur que sa quête",()=>{
 planner.toggle("quest-a",true);
 const p=planner.taskFor("quest-a"),colors=new Set();
 for(const objective of task.objectives){
  const marker={meta:{questId:task.id,objectiveId:objective.id}};
  const style=planner.markerVisual(marker,map);
  assert.equal(style.number,task.objectives.indexOf(objective)+1);
  assert.equal(style.color,p.color);
  colors.add(style.color);
 }
 assert.equal(colors.size,1);
 assert.match(p.color,/^#[0-9a-f]{6}$/i);
});
test("couleur stable lorsque le catalogue et les raids changent",()=>{
 const original=planner.taskFor("quest-a").color;
 const newPlanner=new QuestPilot.Planner(storage,"pve");
 newPlanner.setCatalog([remoteTask,task,optionsTask],translated);
 assert.equal(newPlanner.taskFor("quest-a").color,original);
 assert.equal(newPlanner.isActive("quest-a"),true);
});
test("trois positions possibles d'une SEULE étape ne deviennent pas 1/2/3",()=>{
 planner.toggle("quest-b",true);
 const p=planner.taskFor("quest-b");
 assert.equal(p.objectives.length,1);
 assert.equal(p.objectives[0].locations.length,2);
 assert.equal(p.objectives[0].number,1);
 assert.equal(planner.markerVisual({meta:{questId:"quest-b",objectiveId:"one",possible:true}},map).possible,true);
});
test("étape géolocalisée sur autre carte conserve l'ordre global",()=>{
 planner.toggle("quest-c",true);
 const local=planner.stepsForMap(map).find(s=>s.questId==="quest-c");
 assert.equal(local.number,2);
 assert.equal(local.locations[0].map,"customs");
 const woods=planner.stepsForMap(new Set(["woods"])).find(s=>s.questId==="quest-c");
 assert.equal(woods.number,1);
});
test("l'ajout OCR n'efface pas les quêtes choisies",()=>{
 assert.equal(planner.recognize(["quest-c"]),false);
 assert.equal(planner.isActive("quest-a"),true);
});
test("l'exclusion manuelle n'est pas annulée au prochain OCR",()=>{
 planner.toggle("quest-b",false);
 planner.recognize(["quest-b","quest-a"]);
 assert.equal(planner.isActive("quest-b"),false);
 assert.equal(planner.isActive("quest-a"),true);
});
test("cocher à nouveau enlève l'exclusion d'une quête",()=>{
 planner.toggle("quest-b",true);
 assert.equal(planner.isActive("quest-b"),true);
 assert.ok(!planner.state.excluded.includes("quest-b"));
});
test("une étape déclarée terminée diminue les objectifs restant sans prétendre confirmer EFT",()=>{
 const step=planner.taskFor("quest-a").objectives[0];
 assert.equal(planner.toggleDone(step.id,true),true);
 assert.equal(planner.isDone(step.id),true);
 assert.equal(planner.suggested(map,{position:{x:0,z:0}}).some(s=>s.id===step.id),false);
});
test("sélection de l'étape cible survit à un redémarrage",()=>{
 planner.focus("quest-a:third");
 const another=new QuestPilot.Planner(storage,"pve");
 another.setCatalog([task,optionsTask,remoteTask],translated);
 assert.equal(another.state.focused,"quest-a:third");
 assert.equal(another.suggested(map,{position:{x:0,z:0}})[0].id,"quest-a:third");
});
test("préparation de balise conserve les cases localement",()=>{
 const step=planner.taskFor("quest-a").objectives[0];
 assert.ok(step.needs.some(s=>s.includes("Balise")));
 const label=step.questId+":"+step.needs[0];
 planner.togglePacked(label,true);
 const next=new QuestPilot.Planner(storage,"pve");
 assert.equal(next.isPacked(label),true);
});
test("profils PvE et PvP indépendants",()=>{
 planner.setMode("regular");
 assert.equal(planner.isActive("quest-a"),false);
 planner.toggle("quest-a",true);
 planner.toggleDone("quest-a:second",true);
 planner.setMode("pve");
 assert.equal(planner.isActive("quest-a"),true);
 assert.equal(planner.isDone("quest-a:second"),false);
});
test("la route proposée est indicative et ignore les étapes cochées",()=>{
 planner.setCatalog([task,optionsTask,remoteTask],translated);
 planner.toggle("quest-c",false);planner.toggle("quest-b",false);
 planner.state.focused="";planner.save();
 const ordered=planner.suggested(map,{position:{x:0,z:0}});
 assert.equal(ordered[0].number,2);
 assert.ok(ordered.every(s=>s.done===false));
 assert.ok(ordered.every(s=>s.nearest.position.x!==undefined));
});
test("une coordonnée absente ou infinie ne devient jamais un pion",()=>{
 const v=QuestPilot.objectiveLocations({zones:[
  {map:{id:"customs"},position:{x:"bad",z:0}},
  {map:{id:"customs"},position:{x:Infinity,z:0}},
  {map:{id:"customs"},position:{x:14,z:25}},
  {map:{id:"customs"},position:{x:14,z:25}} ]});
 assert.equal(v.length,1);
});
test("les données corrompues ou anciennes reviennent à un état sûr",()=>{
 memory.set("raidpilot-questpilot-v1:pve","{invalid");
 const clean=new QuestPilot.Planner(storage,"pve");
 assert.equal(clean.state.active.length,0);
 assert.equal(QuestPilot.normalizeState({schemaVersion:3,active:["quest-a"]}).active.length,0);
});
test("les quêtes choisies restent listées même hors de la carte actuelle",()=>{
 planner.setMode("regular");planner.setCatalog([task,remoteTask],translated);
 planner.toggle("quest-c",true);
 assert.ok(planner.visibleTasks(new Set(["factory"])).some(x=>x.id==="quest-c"));
});
test("journal EFT : remise valide confirmée et conservée",()=>{
 const id="aaaaaaaaaaaaaaaaaaaaaaaa";
 const mission={id,name:"task-log",objectives:[objective("step","mark-first","customs",24,24)]};
 planner.setMode("pve");
 planner.setCatalog([mission],{"task-log":"Quête du journal",...translated});
 assert.equal(planner.recordEvidence({mode:"pve",taskId:id,status:"completed",
   source:"eft-local-log",at:1792828800}),true);
 assert.equal(planner.isQuestCompleted(id),true);
 assert.equal(planner.stageStatus(id+":step",id).source,"eft-local-log");
 const restarted=new QuestPilot.Planner(storage,"pve");
 assert.equal(restarted.isQuestCompleted(id),true);
});
test("journal EFT : impossible de confondre les profils PvE/PvP",()=>{
 const id="aaaaaaaaaaaaaaaaaaaaaaaa";
 assert.equal(planner.recordEvidence({mode:"regular",taskId:id,status:"failed",
   source:"eft-local-log",at:1792828810}),false);
 assert.equal(planner.isQuestCompleted(id),true);
});
test("journal EFT : ancien événement ne remplace pas une remise",()=>{
 const id="aaaaaaaaaaaaaaaaaaaaaaaa";
 assert.equal(planner.recordEvidence({mode:"pve",taskId:id,status:"active",
   source:"eft-local-log",at:1792828799}),true);
 assert.equal(planner.statusFor(id).status,"completed");
});
test("journal EFT : ancien événement inconnu ou horodaté faussement est rejeté",()=>{
 const id="aaaaaaaaaaaaaaaaaaaaaaaa";
 assert.equal(planner.recordEvidence({mode:"pve",taskId:id,status:"completed",
   source:"browser",at:1792828900}),false);
 assert.equal(planner.recordEvidence({mode:"pve",taskId:id,status:"completed",
   source:"eft-local-log",at:0}),false);
 assert.equal(planner.recordEvidence({mode:"pve",taskId:"not-an-id",status:"completed",
   source:"eft-local-log",at:1792828900}),false);
});
test("journal EFT : exclusion manuelle domine un nouveau démarrage reconnu",()=>{
 const id="bbbbbbbbbbbbbbbbbbbbbbbb";
 planner.toggle(id,false);
 assert.equal(planner.recordEvidence({mode:"pve",taskId:id,status:"active",
   source:"eft-local-log",at:1792828800}),true);
 assert.equal(planner.isActive(id),false);
});
test("journal EFT : démarrage identifié peut sélectionner la quête du bon profil",()=>{
 const id="cccccccccccccccccccccccc";
 assert.equal(planner.recordEvidence({mode:"pve",taskId:id,status:"active",
   source:"eft-local-log",at:1792828800}),true);
 assert.equal(planner.isActive(id),true);
});
test("partage manuel : code public sans journal ni identité",()=>{
 const code=planner.exportShare();
 assert.ok(code.startsWith("RPQ1:"));
 const shared=JSON.parse(decodeURIComponent(code.slice(5)));
 assert.equal(shared.mode,"pve");
 assert.ok(!JSON.stringify(shared).includes("eft-local-log"));
 assert.ok(!JSON.stringify(shared).includes("RoomCode"));
});
test("partage manuel : plan ami distinct de mes propres quêtes",()=>{
 const friend=new QuestPilot.Planner(null,"pve");
 friend.setCatalog([task,optionsTask,remoteTask],translated);
 friend.toggle("quest-a",true);friend.toggleDone("quest-a:first",true);
 const before=[...planner.state.active];
 assert.equal(planner.importShare(friend.exportShare(),"Pierre"),true);
 assert.deepEqual(planner.state.active,before);
 assert.deepEqual(planner.memberNamesFor("quest-a"),["Pierre"]);
 assert.equal(planner.state.sharedPlans.Pierre.done.includes("quest-a:first"),true);
});
test("partage manuel : marqueur ami différent des marqueurs personnels",()=>{
 planner.setCatalog([task,optionsTask,remoteTask],translated);
 planner.toggle("quest-a",false);
 assert.equal(planner.isActive("quest-a"),false);
 const visual=planner.markerVisual({meta:{questId:"quest-a",objectiveId:"second"}},map);
 assert.equal(visual.teamOnly,true);
 assert.deepEqual(visual.teamNames,["Pierre"]);
 assert.equal(visual.number,2);
});
test("partage manuel : mode différent et code malveillant rejetés",()=>{
 const friend=new QuestPilot.Planner(null,"regular");
 assert.equal(planner.importShare(friend.exportShare(),"Ami"),false);
 assert.equal(planner.importShare("RPQ1:%7Bnot-json","Ami"),false);
 assert.equal(planner.importShare("RPQ1:"+encodeURIComponent('{"version":1,"mode":"pve","active":{},"done":[]}'),"Ami"),false);
});
test("partage manuel : profils sauvegardés et effacement individuel",()=>{
 const restored=new QuestPilot.Planner(storage,"pve");
 assert.equal(restored.memberNamesFor("quest-a").includes("Pierre"),true);
 assert.equal(restored.removeShare("Pierre"),true);
 assert.equal(restored.isTeamActive("quest-a"),false);
 assert.equal(restored.removeShare("Pierre"),false);
});
test("partage en direct limite le réseau aux identifiants publics de ses quêtes",()=>{
  const a="a".repeat(24),b="b".repeat(24);
  const p=new QuestPilot.Planner(storage,"pve");
  p.toggle("fake-local-id",true);
  p.toggle(a,true);
  p.toggleDone(a+":"+b,true);
  p.toggleDone("unrelated:secret",true);
  const snapshot=p.liveSnapshot();
  assert.equal(snapshot.mode,"pve");
  assert.deepEqual(snapshot.quests,[a]);
  assert.deepEqual(snapshot.done,[a+":"+b]);
  assert.equal(JSON.stringify(snapshot).includes("evidence"),false);
});
test("plan reçu ne modifie jamais mes propres cases",()=>{
  const a="a".repeat(24),b="b".repeat(24);
  const p=new QuestPilot.Planner(null,"pve");
  const now=Date.now();
  assert.equal(p.setLivePlan({id:"peer-1",name:"Ami",mode:"pve",
    quests:[a],done:[a+":"+b],at:now}),true);
  assert.equal(p.isTeamActive(a),true);
  assert.equal(p.isActive(a),false);
  assert.equal(p.isDone(a+":"+b),false);
  assert.equal(p.memberNamesFor(a).includes("Ami (direct)"),true);
});
test("un plan d'escouade en mode PvE ne fuit jamais en PvP",()=>{
  const a="a".repeat(24),p=new QuestPilot.Planner(null,"pve");
  assert.equal(p.setLivePlan({id:"peer-2",name:"Ami",mode:"pve",
    quests:[a],done:[],at:Date.now()}),true);
  p.setMode("regular");
  assert.equal(p.isTeamActive(a),false);
  p.setMode("pve");
  assert.equal(p.isTeamActive(a),true);
});
test("les quêtes en direct n'entrent pas dans les préférences persistées",()=>{
  const a="a".repeat(24),p=new QuestPilot.Planner(storage,"pve");
  assert.equal(p.setLivePlan({id:"peer-3",name:"Ami",mode:"pve",
    quests:[a],done:[],at:Date.now()}),true);
  p.save();
  const restored=new QuestPilot.Planner(storage,"pve");
  assert.equal(restored.isTeamActive(a),false);
});
test("une fausse mise à jour expirée ou rejouée ne remplace pas la précédente",()=>{
  const a="a".repeat(24),p=new QuestPilot.Planner(null,"pve"),now=Date.now();
  const msg={id:"peer-4",name:"Ami",mode:"pve",quests:[a],done:[],at:now};
  assert.equal(p.setLivePlan(msg),true);
  assert.equal(p.setLivePlan({...msg,quests:[],at:now}),false);
  assert.equal(p.setLivePlan({...msg,quests:[],at:now-250000}),false);
  assert.equal(p.isTeamActive(a),true);
  assert.equal(p.setLivePlan({...msg,done:["profile:secret"],at:now+5}),false);
});
test("les plans en direct expirent et disparaissent sans supprimer les miens",()=>{
  const a="a".repeat(24),p=new QuestPilot.Planner(null,"pve"),now=Date.now();
  p.toggle(a,true);
  p.setLivePlan({id:"peer-5",name:"Ami",mode:"pve",
    quests:[a],done:[],at:now});
  assert.equal(p.pruneLivePlans(now+91000),true);
  assert.equal(Object.keys(p.livePlans).length,0);
  assert.equal(p.isActive(a),true);
});
console.log(JSON.stringify({status:"PASS",checks}));
