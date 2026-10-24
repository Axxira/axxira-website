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
console.log(JSON.stringify({status:"PASS",checks}));
