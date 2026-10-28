'use strict';
const assert=require('node:assert/strict');
const Guide=require('../LocalMap/key-guide.js');
let checks=0;
function test(title,run){run();checks++;console.log('OK  '+title);}
const items={
 items:{
  a:{id:'a',name:'key-custom',shortName:'RB-PKPM',properties:{units:10},
    wikiLink:'https://escapefromtarkov.fandom.com/wiki/Example_key'},
  b:{id:'b',name:'key-second',shortName:'Room 315',properties:{units:1}},
  c:{id:'c',name:'key-access',shortName:'Lab pass'},
  d:{id:'d',name:'key-unrelated'}
 }
};
const tr={'key-custom':'Clé RB-PKPM','key-second':'Clé chambre 315',
 'key-access':"Carte d'accès au laboratoire"};
const fixtures={
 locks:[
  {lockType:'door',key:'a',position:{x:12,y:2,z:13}},
  {lockType:'door',key:{id:'a'},position:{x:14,y:2,z:15}},
  {lockType:'carTrunk',key:'b',position:{x:42,y:2,z:-5}},
  {lockType:'safe',key:null,position:{x:3,y:2,z:6}},
  {lockType:'door',key:'d',position:null}
 ],
 accessKeys:['a',{id:'c'}]
};
const meta={floors:[{name:'Rez-de-chaussée',extents:[{
 height:[0,4],bounds:[[[0,-10],[60,60]]]}]},{
 name:'Sous-sol',extents:[{height:[-5,-1]}]
}]};
const build=()=>Guide.build(fixtures,items,tr,meta);

test('deux portes peuvent partager la même clé réelle',()=>{
 const catalog=build();
 assert.equal(catalog.locks.filter(d=>d.keyId==='a').length,2);
 assert.equal(catalog.keys,3);
 assert.equal(catalog.locks.filter(d=>d.keyName==='Clé RB-PKPM').length,2);
});
test('les portes sont liées au bon identifiant de clé et à la carte',()=>{
 const catalog=build();
 assert.ok(catalog.locks.some(d=>d.keyId==='b'
    && d.kind==='Coffre de véhicule'
    && d.keyName==='Clé chambre 315'));
 assert.equal(catalog.locks.length,5);
});
test('une serrure sans clé ne reçoit pas de clé inventée',()=>{
 const catalog=build();
 const unknown=catalog.locks.find(d=>d.ordinal===4);
 assert.equal(unknown.identified,false);
 assert.equal(unknown.keyId,'');
 assert.match(unknown.note,/non indiquée/);
});
test('une clé avec porte sans position reste trouvable sans faux marqueur',()=>{
 const catalog=build(),other=catalog.locks.find(d=>d.keyId==='d');
 assert.equal(other.position,null);
 assert.equal(other.identified,false);
});
test('une clé d’accès n’est jamais présentée comme une porte',()=>{
 const catalog=build();
 assert.equal(catalog.access.length,1);
 assert.equal(catalog.access[0].id,'c');
 assert.equal(catalog.access[0].name,"Carte d'accès au laboratoire");
});
test('une clé ouvre deux portes sans être dupliquée dans les accès',()=>{
 const catalog=build();
 assert.equal(catalog.access.some(x=>x.id==='a'),false);
});
test('la recherche retrouve une clé par nom exact ou raccourci',()=>{
 const catalog=build();
 assert.equal(Guide.search(catalog,'RB-PKPM').locks.length,2);
 assert.equal(Guide.search(catalog,'chambre 315').locks.length,1);
 assert.equal(Guide.search(catalog,'coffre').locks.length,1);
});
test('recherche accent-insensible pour le mot clé',()=>{
 const catalog=build();
 assert.ok(Guide.search(catalog,'cle RB').locks.length>=2);
 assert.equal(Guide.search(catalog,'AUCUN-NOM').locks.length,0);
});
test('recherche d’accès de carte sans salle fabriquée',()=>{
 const catalog=build();
 assert.equal(Guide.search(catalog,'laboratoire').access.length,1);
});
test('position de porte contrôlée avant affichage sur la carte',()=>{
 assert.equal(Guide.positionOf({x:Infinity,z:0}),null);
 assert.equal(Guide.positionOf({x:'12',z:0}),null);
 assert.equal(Guide.positionOf({x:0,z:null}),null);
 assert.deepEqual(Guide.positionOf({x:1,y:2,z:3}),{x:1,y:2,z:3});
});
test('les étages ne sont nommés que si la hauteur correspond',()=>{
 const catalog=build();
 const door=catalog.locks.find(d=>d.keyId==='a');
 assert.equal(door.floor,'Rez-de-chaussée');
 assert.equal(Guide.floorOf({x:2,y:-2,z:4},meta),'Sous-sol');
 assert.equal(Guide.floorOf({x:2,y:50,z:4},meta),'');
 assert.equal(Guide.floorOf({x:80,y:2,z:4},meta),'');
});
test('deux étages qui se chevauchent ne permettent pas un étage inventé',()=>{
 const ambiguous={floors:[
  {name:'A',extents:[{height:[0,5]}]},
  {name:'B',extents:[{height:[1,3]}]}]};
 assert.equal(Guide.floorOf({x:1,y:2,z:3},ambiguous),'');
});
test('filtrage des liens de wiki externes non autorisés',()=>{
 const safe=Guide.itemFor({id:'a'},items,tr);
 assert.ok(safe.wiki.startsWith('https://escapefromtarkov.fandom.com'));
 assert.equal(Guide.itemFor({id:'z',name:'X',wikiLink:'javascript:alert(1)'},items,tr).wiki,'');
});
test('usages de clé affichés seulement si données numériques fiables',()=>{
 assert.equal(Guide.itemFor('a',items,tr).uses,10);
 assert.equal(Guide.itemFor('b',items,tr).uses,1);
 assert.equal(Guide.itemFor({id:'z',properties:{units:-10}},items,tr).uses,null);
});
test('informations de courant affichées sans prétendre un loot',()=>{
 const m={locks:[{key:'a',lockType:'door',needsPower:true,position:{x:1,y:1,z:1}}]};
 assert.equal(Guide.build(m,items,tr).locks[0].needsPower,true);
});
test('clés inconnues et cartes sans serrure ne cassent pas le guide',()=>{
 const m=Guide.build({locks:null,accessKeys:null},null,null);
 assert.equal(m.locks.length,0);
 assert.equal(m.access.length,0);
 assert.equal(m.keys,0);
});
test('limitation du volume empêche une liste illimitée',()=>{
 const m=Guide.build({locks:Array.from({length:1500},(_,i)=>({
  key:'a',position:{x:i,y:1,z:0}}))},items,tr);
 assert.equal(m.locks.length,1200);
});
console.log(JSON.stringify({keyGuideTests:'PASS',checks}));
