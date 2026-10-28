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
  const p=new QuestPilot.Planner(null,"pve");
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

test("recherche globale : mission d'une autre carte disponible sans changer de carte",()=>{
 const p=new QuestPilot.Planner(null,"pve");
 const target={id:"remote-quest",name:"Livraison aux Bois",objectives:[
   {id:"wood",type:"visit",description:"Déposer une BALISE",zones:[
     {map:{id:"woods"},position:{x:1,z:2}}]}]};
 p.setCatalog([task,target],translated);
 const found=p.searchTasks(new Set(["customs"]),"bois");
 assert.ok(found.some(q=>q.id==="remote-quest"));
 const description=p.searchTasks(new Set(["customs"]),"balise");
 assert.ok(description.some(q=>q.id==="remote-quest"));
 assert.ok(!p.visibleTasks(new Set(["customs"])).some(q=>q.id==="remote-quest"));
});
test("les objectifs sans coordonnées restent enregistrés comme étapes réelles",()=>{
 const p=new QuestPilot.Planner(null,"pve");
 const q={id:"mixed",name:"Missions mixtes",objectives:[
   {id:"kill",type:"kill",description:"Éliminer un ennemi",markerItem:"signal-item"},
   objective("a","Balise A","customs",1,1),
   {id:"hand",type:"giveItem",description:"Remettre l'objet",zones:[]},
   objective("b","Balise B","customs",10,10)
 ]};
 p.setCatalog([q],{"signal-item":"Signaux"});
 const t=p.taskFor("mixed");
 assert.equal(t.objectives.length,4);
 assert.equal(t.mappedCount,2);
 assert.equal(t.unmappedCount,2);
 assert.deepEqual(t.objectives.map(s=>s.number),[null,1,null,2]);
 assert.deepEqual(t.objectives.map(s=>s.allNumber),[1,2,3,4]);
 p.toggle("mixed",true);
 const shown=p.stepsForMap(new Set(["customs"]));
 assert.deepEqual(shown.map(s=>s.number),[1,2]);
 assert.equal(p.unmappedSteps().length,2);
 assert.ok(p.preparationForMap(new Set(["customs"])).some(s=>s.need.includes("Balise")));
 assert.equal(p.markerVisual({meta:{questId:"mixed",objectiveId:"kill"}},
     new Set(["customs"])),null);
});
test("le choix manuel d'une étape sans carte survit au redémarrage",()=>{
 const storageMap=new Map();
 const store={getItem:k=>storageMap.get(k),setItem:(k,v)=>storageMap.set(k,v)};
 const p=new QuestPilot.Planner(store,"pve");
 p.setCatalog([{id:"nogeo",name:"Sans carte",objectives:[{
   id:"search",type:"findItem",description:"Chercher un objet",zones:[]}]}]);
 p.toggle("nogeo",true);
 assert.equal(p.unmappedSteps().length,1);
 p.toggleDone("nogeo:search",true);
 const restored=new QuestPilot.Planner(store,"pve");
 restored.setCatalog(p.tasks.map(q=>({
   id:q.id,name:q.name,objectives:[{id:"search",description:"Chercher un objet"}]
 })));
 assert.equal(restored.unmappedSteps()[0].done,true);
 assert.equal(restored.searchTasks(new Set(["customs"]),"sans carte")[0].id,"nogeo");
});
test("le HUD choisit une seule étape sans recalculer toute la tournée",()=>{
 const p=new QuestPilot.Planner(null,"pve");p.setCatalog([task],translated);
 p.toggle("quest-a",true);
 const loc={position:{x:1,z:1}};
 assert.equal(p.nextForMap(new Set(["customs"]),loc).id,"quest-a:first");
 p.focus("quest-a:third");
 assert.equal(p.nextForMap(new Set(["customs"]),loc).id,"quest-a:third");
 p.toggleDone("quest-a:third",true);
 assert.equal(p.nextForMap(new Set(["customs"]),loc).id,"quest-a:first");
 p.toggleDone("quest-a:first",true);p.toggleDone("quest-a:second",true);
 assert.equal(p.nextForMap(new Set(["customs"]),loc),null);
});
test("escouade : statut individuel de chaque balise, sans modifier mes coches",()=>{
 const p=new QuestPilot.Planner(null,"pve");p.setCatalog([task],translated);
 const friend=new QuestPilot.Planner(null,"pve");friend.setCatalog([task],translated);
 friend.toggle("quest-a",true);friend.toggleDone("quest-a:first",true);
 assert.equal(p.importShare(friend.exportShare(),"Pierre"),true);
 const steps=p.sharedStepsForMap(new Set(["customs"]));
 assert.equal(steps.length,3);
 assert.deepEqual(steps.map(s=>s.number),[1,2,3]);
 assert.equal(steps[0].members[0].name,"Pierre");
 assert.equal(steps[0].members[0].done,true);
 assert.equal(steps[1].members[0].done,false);
 assert.equal(steps[0].mine,false);
 assert.equal(p.isDone("quest-a:first"),false);
});
test("préparation : exclut quêtes remises, conserve exigences sans GPS",()=>{
 const p=new QuestPilot.Planner(null,"pve");
 const id="eeeeeeeeeeeeeeeeeeeeeeee";
 p.setCatalog([{id,name:"Équipement obligatoire",objectives:[
   {id:"unmapped",type:"wearing",description:"Équipement",markerItem:"signal-item"},
   objective("mapped","Placer la balise","customs",1,1)]}]);
 p.toggle(id,true);
 const before=p.preparationForMap(new Set(["customs"]));
 assert.ok(before.length>=1);
 assert.equal(p.recordEvidence({taskId:id,mode:"pve",status:"completed",
   source:"eft-local-log",at:1792828800}),true);
 assert.equal(p.preparationForMap(new Set(["customs"])).length,0);
 assert.equal(p.unmappedSteps()[0].doneSource,"eft-local-log");
});



test("une dependance explicite bloque une etape avant la premiere",()=>{
 const p=new QuestPilot.Planner(null,"pve");
 const a=objective("first","first","customs",100,100);
 const b=objective("second","second","customs",1,1,{requiredPreviousObjectives:["first"]});
 p.setCatalog([{id:"dependency-quest",name:"Dependency",objectives:[a,b]}]);
 p.toggle("dependency-quest",true);
 const pos={position:{x:0,z:0}};
 assert.equal(p.nextForMap(new Set(["customs"]),pos).objectiveId,"first");
 p.toggleDone("dependency-quest:first",true);
 assert.equal(p.nextForMap(new Set(["customs"]),pos).objectiveId,"second");
});
test("numero de balise ne suppose pas un ordre obligatoire",()=>{
 const p=new QuestPilot.Planner(null,"pve");
 p.setCatalog([{id:"independent-quest",name:"Independent",objectives:[
  objective("first","first","customs",100,100),
  objective("second","second","customs",1,1)
 ]}]);
 p.toggle("independent-quest",true);
 assert.equal(p.nextForMap(new Set(["customs"]),{position:{x:0,z:0}}).objectiveId,"second");
});
test("une position incertaine conserve la derniere cible sans distance inventee",()=>{
 const p=new QuestPilot.Planner(null,"pve");
 p.setCatalog([{id:"location-quest",name:"Locations",objectives:[
  objective("first","first","customs",3,3),
  objective("second","second","customs",100,100)
 ]}]);
 p.toggle("location-quest",true);
 const maps=new Set(["customs"]);
 const first=p.nextForMap(maps,{position:{x:0,z:0},positionConfidence:0.95});
 assert.equal(first.objectiveId,"first");
 assert.equal(first.locationUncertain,false);
 const uncertain=p.nextForMap(maps,{position:{x:99,z:99},positionConfidence:0.2});
 assert.equal(uncertain.objectiveId,"first");
 assert.equal(uncertain.locationUncertain,true);
 assert.equal(uncertain.distance,null);
 const lost=p.nextForMap(maps,{locationUncertain:true});
 assert.equal(lost.objectiveId,"first");
});

test("deux étapes terminées masquent leurs marqueurs mais la troisième reste visible",()=>{
 const p=new QuestPilot.Planner(null,"pve");
 p.setCatalog([task],translated);
 p.toggle("quest-a",true);
 p.toggleDone("quest-a:first",true);
 p.toggleDone("quest-a:second",true);
 const states=task.objectives.map(o=>p.markerVisual(
  {meta:{questId:"quest-a",objectiveId:o.id}},new Set(["customs"])));
 assert.deepEqual(states.map(x=>x.done),[true,true,false]);
 assert.deepEqual(p.stepsForMap(new Set(["customs"])).filter(s=>!s.done)
  .map(s=>s.objectiveId),["third"]);
 p.toggleDone("quest-a:second",false);
 assert.deepEqual(task.objectives.map(o=>p.markerVisual(
  {meta:{questId:"quest-a",objectiveId:o.id}},new Set(["customs"])).done),
  [true,false,false]);
});
test("lieux alternatifs d'une seule étape ont toujours le même statut",()=>{
 const p=new QuestPilot.Planner(null,"pve");
 p.setCatalog([optionsTask],translated);p.toggle("quest-b",true);
 const before=p.markerVisual({meta:{questId:"quest-b",objectiveId:"one",
  possible:true}},new Set(["customs"]));
 assert.equal(before.done,false);
 p.toggleDone("quest-b:one",true);
 const after=p.markerVisual({meta:{questId:"quest-b",objectiveId:"one",
  possible:true}},new Set(["customs"]));
 assert.equal(after.done,true);
 assert.equal(after.number,1);
});

test("la priorité manuelle reste devant une ancienne cible quand la position est perdue",()=>{
 const p=new QuestPilot.Planner(null,"pve");
 p.setCatalog([task],translated);
 p.toggle("quest-a",true);
 const map=new Set(["customs"]);
 p.nextForMap(map,{position:{x:0,z:0},positionReliable:true});
 p.focus("quest-a:third");
 const next=p.nextForMap(map,{position:{x:0,z:0},positionReliable:false});
 assert.equal(next.id,"quest-a:third");
 assert.equal(next.distance,null);
 assert.equal(next.locationUncertain,true);
});
test("une priorité valide est mémorisée et ne prétend pas être incertaine",()=>{
 const p=new QuestPilot.Planner(null,"pve");
 p.setCatalog([task],translated);
 p.toggle("quest-a",true);
 p.focus("quest-a:third");
 const next=p.nextForMap(new Set(["customs"]),{position:{x:0,z:0},positionReliable:true});
 assert.equal(next.id,"quest-a:third");
 assert.equal(next.locationUncertain,false);
});
test("le plan n'affiche aucune distance issue d'une position incertaine",()=>{
 const p=new QuestPilot.Planner(null,"pve");
 p.setCatalog([task],translated);
 p.toggle("quest-a",true);
 assert.ok(p.suggested(new Set(["customs"]),{positionReliable:false})
   .every(step=>step.distance===null && step.locationUncertain===true));
});

test("progression QuestPilot sauvegardée entre plusieurs raids et redémarrages",()=>{
 const memory=new Map();
 const store={getItem:k=>memory.get(k)||null,setItem:(k,v)=>memory.set(k,v)};
 const a=new QuestPilot.Planner(store,"pve");
 a.setCatalog([task],translated);
 a.recognize(["quest-a"]);
 a.toggleDone("quest-a:first",true);
 a.focus("quest-a:second");
 const b=new QuestPilot.Planner(store,"pve");
 b.setCatalog([task],translated);
 assert.equal(b.isActive("quest-a"),true);
 assert.equal(b.isDone("quest-a:first"),true);
 assert.equal(b.state.focused,"quest-a:second");
 b.recognize(["quest-a"]);
 const c=new QuestPilot.Planner(store,"pve");
 c.setCatalog([task],translated);
 assert.equal(c.isDone("quest-a:first"),true);
 assert.equal(c.isDone("quest-a:second"),false);
});
test("quête entière marquée faite manuellement, réactivable sans perdre les étapes",()=>{
 const memory=new Map(),store={
  getItem:k=>memory.get(k)||null,setItem:(k,v)=>memory.set(k,v)};
 const p=new QuestPilot.Planner(store,"pve");
 p.setCatalog([task],translated);p.toggle("quest-a",true);
 p.toggleDone("quest-a:first",true);
 assert.equal(p.toggleQuestCompleted("quest-a",true),true);
 assert.equal(p.isQuestCompleted("quest-a"),false);
 assert.equal(p.isQuestMarkedDone("quest-a"),true);
 assert.equal(p.stepsForMap(map).every(s=>s.done),true);
 assert.equal(p.nextForMap(map,{position:{x:0,y:1,z:0}}),null);
 const restored=new QuestPilot.Planner(store,"pve");
 restored.setCatalog([task],translated);
 assert.equal(restored.isQuestMarkedDone("quest-a"),true);
 assert.equal(restored.toggleQuestCompleted("quest-a",false),false);
 assert.deepEqual(restored.stepsForMap(map).map(s=>s.done),[true,false,false]);
 assert.equal(restored.isDone("quest-a:first"),true);
});
test("mission confirmée EFT ne peut pas être annulée manuellement",()=>{
 const id="a".repeat(24);
 const p=new QuestPilot.Planner(null,"pve");
 p.setCatalog([{id,name:"Confirmée",objectives:[
  objective("step","end","customs",10,10)]}]);
 p.toggle(id,true);
 p.recordEvidence({mode:"pve",taskId:id,status:"completed",
  source:"eft-local-log",at:1792828800});
 assert.equal(p.isQuestCompleted(id),true);
 assert.equal(p.toggleQuestCompleted(id,false),false);
 assert.equal(p.stepsForMap(map)[0].doneSource,"eft-local-log");
});
test("HUD zone seulement à moins de 30 mètres avec GPS fiable",()=>{
 const p=new QuestPilot.Planner(null,"pve");
 p.setCatalog([task],translated);p.toggle("quest-a",true);
 const pos=(x,uncertainty=1)=>({
  position:{x,y:1,z:10},uncertainty,positionReliable:true});
 assert.equal(p.nearbyForMap(map,pos(12))?.id,"quest-a:first");
 assert.equal(p.nearbyForMap(map,pos(180)),null);
 assert.equal(p.nearbyForMap(map,pos(12,7)),null);
 assert.equal(p.nearbyForMap(map,{...pos(12),positionReliable:false}),null);
});
test("HUD choisit le bon objectif voisin, pas une priorité lointaine",()=>{
 const p=new QuestPilot.Planner(null,"pve");
 p.setCatalog([task],translated);p.toggle("quest-a",true);
 p.focus("quest-a:third");
 const v=p.nearbyForMap(map,{
  position:{x:10,y:1,z:10},uncertainty:1,positionReliable:true});
 assert.equal(v.id,"quest-a:first");
 p.toggleDone("quest-a:first",true);
 assert.equal(p.nearbyForMap(map,{
  position:{x:10,y:1,z:10},uncertainty:1,positionReliable:true}),null);
});
test("HUD retient l'objectif dans la bande 30–45m puis le masque",()=>{
 const p=new QuestPilot.Planner(null,"pve");
 p.setCatalog([task],translated);p.toggle("quest-a",true);
 p.toggleDone("quest-a:second",true);p.toggleDone("quest-a:third",true);
 const pos=x=>({position:{x,y:1,z:10},uncertainty:1,positionReliable:true});
 assert.equal(p.nearbyForMap(map,pos(10))?.id,"quest-a:first");
 assert.equal(p.nearbyForMap(map,pos(50)),null);
 assert.equal(p.nearbyForMap(map,pos(50),"quest-a:first")?.id,"quest-a:first");
 assert.equal(p.nearbyForMap(map,pos(70),"quest-a:first"),null);
});
test("HUD ne confond pas deux étages et ne fabrique pas une altitude",()=>{
 const p=new QuestPilot.Planner(null,"pve");
 p.setCatalog([task],translated);p.toggle("quest-a",true);
 const near=(y)=>({position:{x:10,y,z:10},uncertainty:1,positionReliable:true});
 assert.equal(p.nearbyForMap(map,near(13)),null);
 assert.equal(p.nearbyForMap(map,near(undefined)),null);
 assert.equal(p.nearbyForMap(map,near(1))?.objectiveId,"first");
});
test("zone polygonale remplace la distance trompeuse au centre",()=>{
 const zone={id:"poly",name:"Polygon",objectives:[{
  id:"inside",description:"Place",type:"visit",zones:[{
   map:{id:"customs"},position:{x:300,y:1,z:300},
   outline:[{x:-5,z:-5},{x:5,z:-5},{x:5,z:5},{x:-5,z:5}]
  }]
 }]};
 const p=new QuestPilot.Planner(null,"pve");p.setCatalog([zone]);
 p.toggle("poly",true);
 assert.equal(p.nearbyForMap(map,{
  position:{x:0,y:1,z:0},uncertainty:1,positionReliable:true})?.objectiveId,"inside");
 assert.equal(p.nearbyForMap(map,{
  position:{x:300,y:1,z:300},uncertainty:1,positionReliable:true}),null);
});
test("la proximité n'est jamais une preuve de mission accomplie",()=>{
 const p=new QuestPilot.Planner(null,"pve");p.setCatalog([task],translated);
 p.toggle("quest-a",true);
 for(let i=0;i<5;i++)p.nearbyForMap(map,{
  position:{x:10,y:1,z:10},uncertainty:0.5,positionReliable:true});
 assert.equal(p.isDone("quest-a:first"),false);
 assert.equal(p.isQuestMarkedDone("quest-a"),false);
});
test("les quêtes sauvegardées restent séparées entre PvE et PvP",()=>{
 const data=new Map(),store={
  getItem:k=>data.get(k)||null,setItem:(k,v)=>data.set(k,v)};
 const p=new QuestPilot.Planner(store,"pve");
 p.setCatalog([task],translated);p.toggle("quest-a",true);
 p.toggleQuestCompleted("quest-a",true);
 p.setMode("regular");p.setCatalog([task],translated);
 assert.equal(p.isActive("quest-a"),false);
 assert.equal(p.isQuestMarkedDone("quest-a"),false);
 p.setMode("pve");p.setCatalog([task],translated);
 assert.equal(p.isQuestMarkedDone("quest-a"),true);
});
test("intégration locale : OCR ne réinitialise jamais les coches sauvegardées",()=>{
 const fs=require("node:fs");
 const source=fs.readFileSync(require("node:path").join(__dirname,
  "../LocalMap/raidpilot-local-map.js"),"utf8");
 const ocr=source.slice(source.indexOf("function setRaidQuests(p){"),
  source.indexOf("function setLearnedRoute(p){"));
 assert.match(ocr,/questPilot\.recognize\(\[\.\.\.ids\]\)/);
 assert.doesNotMatch(ocr,/state\.done\s*=\s*\[\]/);
 assert.doesNotMatch(ocr,/state\.active\s*=\s*\[\]/);
 assert.match(source,/function nearbyQuestStep\(\)/);
 assert.match(source,/planner\.nearbyForMap/);
 assert.match(source,/questPilotContextProgress/);
});


test("CORTEX zone : ne s'active qu'après deux observations GPS distinctes",()=>{
 const gate=new QuestPilot.ZoneGate();
 const step={id:"q:step"};
 assert.equal(gate.observe(step,1000),null);
 assert.equal(gate.observe(step,1000),null);
 assert.equal(gate.observe(step,1120),null);
 assert.equal(gate.observe(step,1210)?.id,step.id);
 assert.equal(gate.observe(step,1210)?.id,step.id);
});
test("CORTEX zone : disparaît instantanément si GPS absent ou cible terminée",()=>{
 const gate=new QuestPilot.ZoneGate();
 const step={id:"q:step"};
 gate.observe(step,1000);gate.observe(step,1200);
 assert.equal(gate.observe(null,1300),null);
 assert.equal(gate.observe(step,1350),null);
 assert.equal(gate.observe(step,1560)?.id,step.id);
});
test("CORTEX zone : jamais de confirmation réutilisée sur une autre étape",()=>{
 const gate=new QuestPilot.ZoneGate();
 const a={id:"q:one"},b={id:"q:two"};
 gate.observe(a,1000);gate.observe(a,1250);
 assert.equal(gate.observe(b,1400),null);
 assert.equal(gate.observe(a,1450),null);
 assert.equal(gate.observe(a,1600),null);
 assert.equal(gate.observe(a,1660)?.id,a.id);
});
test("CORTEX zone : une observation GPS hors ordre réinitialise l'indice",()=>{
 const gate=new QuestPilot.ZoneGate();
 const a={id:"q:step"};
 gate.observe(a,1000);
 assert.equal(gate.observe(a,900),null);
 assert.equal(gate.observe(a,1100),null);
 assert.equal(gate.observe(a,1350)?.id,a.id);
});

test("interface : carte simple avec commandes essentielles et réglages avancés",()=>{
 const fs=require("node:fs"),path=require("node:path");
 const html=fs.readFileSync(path.join(__dirname,"../LocalMap/index.html"),"utf8");
 for(const id of ["layers-btn","quests-btn","floor","zoom-in","zoom-out",
  "center-player","map-style","profile","label-mode","fit","calibrate",
  "questpilot-panel","questpilot-hud"]){
  assert.equal((html.match(new RegExp('id="'+id+'"',"g"))||[]).length,1,
   "Commande manquante ou dupliquée : "+id);
 }
 assert.match(html,/<details id="toolbar-more"/);
 assert.match(html,/<summary[^>]+>⚙ Options<\/summary>/);
 assert.match(html,/<aside id="questpilot-panel" class="hidden"/);
 assert.match(html,/<section id="questpilot-hud"[^>]+hidden>/);
});
test("interface : une grille de quêtes sobre avec boutons faits accessibles",()=>{
 const fs=require("node:fs"),path=require("node:path");
 const css=fs.readFileSync(path.join(__dirname,
  "../LocalMap/raidpilot-local-map.css"),"utf8");
 assert.match(css,/#questpilot-list \.qp-task \.qp-quest-finish/);
 assert.match(css,/focus-visible/);
 assert.match(css,/@media\(max-width:600px\)/);
 assert.match(css,/#questpilot-hud\[hidden\]\s*\{display:none\}/);
});
console.log(JSON.stringify({status:"PASS",checks}));
