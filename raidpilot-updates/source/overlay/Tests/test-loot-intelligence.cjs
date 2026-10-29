'use strict';
const assert=require('node:assert/strict');
const {Planner,extractHideout,extractTaskRequirements,cleanState}
 =require('../LocalMap/loot-intelligence.js');
let checks=0;
const test=(title,fn)=>{fn();checks++;console.log('OK '+title);};
const storage=new Map();
const local={
  getItem:k=>storage.get(k)||null,
  setItem:(k,v)=>storage.set(k,String(v))
};
const items={data:{items:{
  bolts:{id:'bolts',name:'bolt name',shortName:'Bolt',basePrice:18000},
  wire:{id:'wire',name:'wire name',shortName:'Wire'},
  med:{id:'med',name:'med name'},
  rare:{id:'rare',name:'rare name'}
}}};
const fr={'bolt name':'Boulons','wire name':'Fil électrique',
  'med name':'Médicament','rare name':'Objet rare'};
const tasks={data:{tasks:[
  {id:'quest-A',name:'Quest A',objectives:[
    {id:'deliver',type:'giveItem',item:'bolts',count:3,foundInRaid:true},
    {id:'optional',type:'findItem',item:'rare',count:1,optional:true},
    {id:'alternatives',type:'giveItem',items:['bolts','wire'],count:5},
    {id:'visit',type:'visitLocation',item:'wire',count:50}
  ]},
  {id:'quest-B',name:'Quest B',objectives:[
    {id:'deliver',type:'giveItem',item:'wire',count:2}
  ]}
]}};
const hideout={data:{hideoutStations:[
  {id:'generator',name:'Generator',levels:[
    {level:1,itemRequirements:[{item:{id:'bolts'},count:2}]},
    {level:2,itemRequirements:[{item:{id:'wire'},count:4}]}
  ]},
  {id:'workbench',name:'Workbench',levels:[
    {level:1,itemRequirements:[{item:{id:'wire'},count:3}]}
  ]}
]}};
const make=(mode='pve')=>{
  const p=new Planner(local,mode);
  p.setData({items,tasks,hideout,itemTranslations:fr,
    activeQuests:['quest-A'],completedQuests:[],completedObjectives:[]});
  return p;
};
test('catalogue items et stations disponible après un vrai chargement',()=>{
 const p=make();
 assert.equal(p.items.size,4);
 assert.equal(p.stations.length,2);
 assert.equal(p.hideoutLoaded,true);
 assert.equal(p.summary().trackedQuests,1);
});
test('somme sûre des besoins de quête et des modules de planque',()=>{
 const p=make();
 const b=p.requirements().find(x=>x.id==='bolts');
 assert.equal(b.quantity,5);
 assert.equal(b.questQuantity,3);
 assert.equal(b.hideoutQuantity,2);
 assert.equal(b.firQuantity,3);
 assert.match(b.recommendation,/quête FIR/);
});
test('les objectifs optionnels ou à choix ne deviennent pas obligatoires',()=>{
 const p=make();
 assert.equal(p.requirements().some(x=>x.id==='rare'),false);
 assert.equal(p.requirements().find(x=>x.id==='wire').quantity,3);
});
test('quêtes non suivies ignorées, mais consultables explicitement',()=>{
 const p=make();
 assert.equal(p.requirements().find(x=>x.id==='wire').quantity,3);
 assert.equal(p.requirements({allQuests:true}).find(x=>x.id==='wire').quantity,5);
});
test('étape cochée ou quête terminée ne demande plus ses objets',()=>{
 const p=make();
 p.setProgress({activeQuests:['quest-A'],
  completedObjectives:['quest-A:deliver']});
 assert.equal(p.requirements().find(x=>x.id==='bolts').quantity,2);
 p.setProgress({activeQuests:['quest-A'],
  completedQuests:['quest-A']});
 assert.equal(p.requirements().find(x=>x.id==='bolts').quantity,2);
});
test('la planque ne réclame que le prochain niveau non accompli',()=>{
 const p=make();
 assert.equal(p.setStationLevel('generator',1),true);
 const requirements=p.requirements();
 assert.equal(requirements.find(x=>x.id==='bolts').quantity,3);
 assert.equal(requirements.find(x=>x.id==='wire').quantity,7);
 assert.equal(p.nextLevel(p.stations.find(x=>x.id==='generator')).level,2);
});
test('on peut également demander tous les futurs niveaux',()=>{
 const p=make();
 assert.equal(p.requirements({allHideout:true}).find(x=>x.id==='wire').quantity,7);
});
test('quantités réellement possédées manuelles, sans inventer la vente',()=>{
 const p=make();
 assert.equal(p.setOwned('bolts',2),true);
 const row=p.requirements().find(x=>x.id==='bolts');
 assert.equal(row.owned,2);
 assert.equal(row.missing,3);
 const unknown=p.itemInfo('rare');
 assert.match(unknown.assessment,/vendre reste à vérifier/);
});
test('un objet absent des données ne peut pas être enregistré comme connu',()=>{
 const p=make();
 assert.equal(p.setOwned('made-up',100),false);
 assert.equal(p.setStationLevel('imaginary-station',1),false);
 assert.equal(p.setOwned('bolts',-1),false);
 assert.equal(p.setStationLevel('generator',999),false);
});
test('progression planque/inventaire conservée entre raids et redémarrages',()=>{
 const one=make();
 one.setOwned('bolts',3);
 one.setStationLevel('generator',1);
 const two=make();
 assert.equal(two.state.owned.bolts,3);
 assert.equal(two.state.stationLevels.generator,1);
});
test('PvE et PvP ont des progressions séparées',()=>{
 const p=make('pve');
 p.setOwned('rare',2);
 p.setMode('regular');
 assert.equal(p.state.owned.rare||0,0);
 p.setMode('pve');
 assert.equal(p.state.owned.rare,2);
});
test('cache de planque absent ne fabrique aucun besoin de planque',()=>{
 const p=new Planner(null,'pve');
 p.setData({items,tasks,hideout:null,activeQuests:['quest-A']});
 assert.equal(p.hideoutLoaded,false);
 assert.equal(p.requirements().find(x=>x.id==='bolts').quantity,3);
});
test('translittération française et recherche par noms',()=>{
 const p=make();
 assert.equal(p.search('BOULONS').length,1);
 assert.equal(p.search('boulons').length,1);
 assert.equal(p.search('QUETE').length>=1,true);
});
test('import de données non fiables borné et nettoyé',()=>{
 const result=cleanState({owned:{bolts:3,invalid:-1},stationLevels:{
  generator:1,impossible:99}});
 assert.equal(result.owned.bolts,3);
 assert.equal(result.owned.invalid,undefined);
 assert.equal(result.stationLevels.impossible,undefined);
});
test('catalogue de planque ne déduit pas une porte ou un prix',()=>{
 const p=make();
 const station=p.stations.find(x=>x.id==='generator');
 assert.equal(station.levels[0].ingredients[0].name,'Boulons');
 assert.equal(Object.prototype.hasOwnProperty.call(station,'door'),false);
});
test('quantités FIR distinctes et besoins couverts',()=>{
 const p=make();
 p.setOwned('bolts',9);
 assert.equal(p.requirements().find(x=>x.id==='bolts').missing,0);
});
console.log(JSON.stringify({lootIntelligence:'PASS',checks}));
