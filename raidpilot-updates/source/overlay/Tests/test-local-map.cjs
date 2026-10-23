const fs=require('fs'),path=require('path'),vm=require('vm'),assert=require('assert/strict');
const root=path.resolve(__dirname,'../LocalMap');
// Minimal DOM model: runs the delivered renderer and real cached geometry
// without a browser, network, WebView2, or game process.
class Element {
 constructor(tag){this.tagName=tag;this.children=[];this.attrs={};this.dataset={};this.style={};this.listeners={};this.value='';this.textContent='';this.className='';this.classList={toggle:()=>{},add:()=>{},remove:()=>{}};}
 append(...items){this.children.push(...items);} appendChild(item){this.children.push(item);return item;}
 replaceChildren(...items){this.children=items;}
 setAttribute(k,v){this.attrs[k]=String(v);if(k==='class')this.className=v;}
 getAttribute(k){return this.attrs[k]??null;} removeAttribute(k){delete this.attrs[k];}
 addEventListener(name,fn){(this.listeners[name]??=[]).push(fn);}
 dispatchEvent(e){for(const fn of this.listeners[e.type]??[])fn(e);}
 getBoundingClientRect(){return {width:1200,height:700,left:0,top:0};}
 setPointerCapture(){} get options(){return this.children;}
}
const ids={};for(const id of ['viewport','stage','base-map','overlay','panel','filters','layers-btn','search','profile','floor','zoom-in','zoom-out','fit','center-player','zoom-value','map-name','marker-count','health','details','missing-asset','label-mode','map-style','map-credit','calibrate','calibration-panel','calibration-map','calibration-status','calibration-landmark','calibration-reference','calibration-position','calibration-offset','calibration-save','calibration-reset','calibration-close','tactical-warning'])ids[id]=new Element('div');
const listeners={}, messages=[], storage=new Map();
const context={console,URLSearchParams,Event:class{constructor(type){this.type=type;}},setTimeout,clearTimeout,
 document:{getElementById:id=>ids[id]??null,querySelector:s=>ids[s.slice(1)]??null,createElement:t=>new Element(t),createElementNS:(_,t)=>new Element(t)},
 location:{search:'?map=customs'},localStorage:{
   getItem:key=>storage.has(key)?storage.get(key):null,
   setItem:(key,value)=>storage.set(key,String(value)),
   removeItem:key=>storage.delete(key)
 },
 fetch:async url=>{try {const text=fs.readFileSync(path.join(root,url),'utf8');return {ok:true,json:async()=>JSON.parse(text.replace(/^\uFEFF/,''))};}catch{return {ok:false,status:404};}},
 addEventListener:(name,fn)=>(listeners[name]??=[]).push(fn),
 dispatchEvent:e=>{for(const fn of listeners[e.type]??[])fn(e);},
 chrome:{webview:{postMessage:m=>messages.push(m),addEventListener:()=>{}}}
};context.window=context;context.globalThis=context;vm.createContext(context);
let checks=0;function check(ok,label){assert.ok(ok,label);checks++;}
function marker(id){return ids.overlay.children.find(e=>e.dataset.markerId===id);}
const ready=new Promise(resolve=>context.addEventListener('raidpilot-local-initialized',resolve));
for(const f of ['projection.js','map-calibration.js','map-display.js','raid-tactics.js','automatic-floors.js','raidpilot-local-map.js','map-features.js','legacy-bridge.js'])vm.runInContext(fs.readFileSync(path.join(root,f),'utf8'),context,{filename:f});
context.__raidPilotSetLocalPose({generation:4,map:'customs',x:100,y:2,z:0,yaw:90});
(async()=>{
 await Promise.race([ready,new Promise((_,reject)=>setTimeout(()=>reject(Error('Renderer did not initialize')),5000))]);
 const api=context.RaidPilotLocalMap;
 check(api.health().markerCount>100,'Real cached POI missing');
 check(ids.overlay.children.filter(e=>e.dataset.markerId).length===1,'Illustrated default duplicates baked POI');
 check(!!marker('__player'),'Pose received before data readiness was lost');
 const first=marker('__player').getAttribute('transform');
 context.__raidPilotSetLocalPose({generation:4,map:'customs',x:160,y:2,z:40,yaw:180});
 const moved=marker('__player').getAttribute('transform');
 check(moved!==first,'Player did not move');check(moved.includes('rotate(') && moved!==first,'Native projected yaw ignored');
 context.__raidPilotSetLocalPose({generation:3,map:'customs',x:-100,y:2,z:-20,yaw:0});
 check(marker('__player').getAttribute('transform')===moved,'Stale raid overwrote current position');
 context.__raidPilotSetSquadPose({memberId:'test',name:'Team test',map:'customs',x:180,y:2,z:60,yaw:20});
 check(!!marker('__team_test'),'Native teammate not rendered');
 context.__raidPilotRemoveSquadMember('test');check(!marker('__team_test'),'Teammate not removed');
 const newer={memberId:'timestamp',name:'Current',map:'customs',x:180,y:2,z:60,yaw:20,sentAtUnixMs:2000};
 context.__raidPilotSetSquadPose(newer);
 check(!!marker('__team_timestamp'),'Stamped teammate not rendered');
 const latestTransform=marker('__team_timestamp').getAttribute('transform');
 context.__raidPilotSetSquadPose({...newer,x:220,yaw:180,sentAtUnixMs:1999});
 check(marker('__team_timestamp').getAttribute('transform')===latestTransform,'Out-of-order teammate displaced current marker');
 context.__raidPilotRemoveSquadMember('timestamp',2000);
 check(!marker('__team_timestamp'),'Map-change removal failed');
 context.__raidPilotSetSquadPose({...newer,sentAtUnixMs:2000});
 check(!marker('__team_timestamp'),'Removed teammate resurrected by an old pose');
 context.__raidPilotSetSquadPose({...newer,sentAtUnixMs:2001});
 check(!!marker('__team_timestamp'),'New teammate pose could not recover after removal');
 context.__raidPilotRemoveSquadMember('timestamp',2000);
 check(!!marker('__team_timestamp'),'Stale removal erased newer pose');
 context.__raidPilotRemoveSquadMember('timestamp',2001);
 check(!marker('__team_timestamp'),'Current removal did not clear teammate');
 context.RaidPilotLocalMap.setMap('woods');
 context.__raidPilotSetSquadPose({memberId:'woodsMate',name:'Woods',map:'woods',x:0,y:2,z:0,yaw:30,sentAtUnixMs:2100});
 check(api.health().teammateCount===1,'Teammate did not follow map change');
 context.RaidPilotLocalMap.setMap('customs');
 context.__raidPilotSetSquadPose({memberId:'customsMate',name:'Customs',map:'customs',x:180,y:2,z:60,yaw:20,sentAtUnixMs:2200});
 check(api.health().teammateCount===1,'Old-map teammate leaked through soft map switch');
 context.__raidPilotClearSquad();
 check(api.health().teammateCount===0,'Squad clear retained teammate');
 check(api.canonicalMapKey('labs')==='the-lab','Local map alias missing');
 context.__raidPilotSetLocalPose({generation:4,map:'customs',x:160,y:2,z:40,yaw:180});
 check(!!marker('__player'),'Local marker did not recover after soft map switch');
 ids.search.value='no-such-marker';ids.search.dispatchEvent(new context.Event('input'));check(!!marker('__player'),'Search hid player');
 ids.search.value='';ids.search.dispatchEvent(new context.Event('input'));
 context.__raidPilotClearLocalPose(4);check(!marker('__player'),'Clear left stale player');
 context.__raidPilotSetLocalPose({generation:5,map:'woods',x:0,y:2,z:0,yaw:30});
 check(api.health().mapKey==='woods','Map transition not applied');check(api.health().markerCount>100,'Woods POI missing');
 context.__raidPilotSetLocalPose({generation:6,map:'customs',x:160,y:2,z:40,yaw:180});
 check(api.health().mapKey==='customs'&&api.health().position==='TRACKING','Map/player not restored');
 check(!context.__raidPilotSetLocalPose({generation:6,map:'customs',x:null,z:null}),'Invalid position accepted');
 check(messages.some(m=>m.type==='raidpilot-local-ready'),'Ready handshake absent');
 // A real zoom preference is global across map changes and not reset by
 // loading an image with different natural dimensions.
 const image=ids['base-map'], viewport=ids.viewport;
 image.naturalWidth=12500;image.naturalHeight=8000;
 image.dispatchEvent(new context.Event('load'));
 const initialFactor=api.health().zoomFactor;
 check(Math.abs(initialFactor-1)<1e-9,'No custom zoom should start at fit');
 const wheel={type:'wheel',deltaY:-100,clientX:600,clientY:350,preventDefault(){}};
 viewport.dispatchEvent(wheel);
 const preferred=api.health().zoomFactor;
 check(preferred>1.05&&preferred<2,'Mouse wheel did not change global zoom factor');
 const stored=JSON.parse(storage.get('raidpilot-forge-map-v6'));
 check(Math.abs(stored.zoomFactor-preferred)<1e-7,'Zoom setting was not saved in map preferences');
 check(Array.isArray(stored.enabled)&&stored.mapStyle==='forge',
   'Saving zoom erased the layer/style preferences');
 api.setMap('woods');image.naturalWidth=9200;image.naturalHeight=6900;
 image.dispatchEvent(new context.Event('load'));
 check(Math.abs(api.health().zoomFactor-preferred)<1e-7,
   'Changing map discarded user zoom');
 const zoomAtWoods=api.health().zoom;
 ids.fit.dispatchEvent(new context.Event('click'));
 const fitAtWoods=api.health().zoom;
 check(Math.abs(zoomAtWoods/fitAtWoods-preferred)<0.02,
   'Relative zoom on Woods differs from the user-selected level');
 check(Math.abs(api.health().zoomFactor-1)<1e-9,
   'Fit-to-map button did not reset the saved zoom preference');
 // A native preference can be re-synchronized after navigation/Launcher update.
 context.__raidPilotHostZoomFactor=2.25;
 context.dispatchEvent(new context.Event('raidpilot-zoom-sync'));
 check(Math.abs(api.health().zoomFactor-2.25)<1e-9,
   'Native durable zoom preference was not applied to the local map');
 api.setMap('customs');image.naturalWidth=12500;image.naturalHeight=8000;
 image.dispatchEvent(new context.Event('load'));
 check(Math.abs(api.health().zoomFactor-2.25)<1e-9,
   'The restored preference did not survive another map change');
 await new Promise(resolve=>setTimeout(resolve,210));
 check(messages.some(m=>m.type==='raidpilot-map-zoom-preference'
   && Number.isFinite(m.payload?.factor)),
   'Local map never sent a zoom preference to the native host');
 console.log(JSON.stringify({status:'PASS',checks,customsPOI:api.health().markerCount,visibleMarkers:ids.overlay.children.filter(e=>e.dataset.markerId).length,playerTransform:marker('__player').getAttribute('transform'),health:ids.health.textContent}));
})().catch(e=>{console.error(e);process.exitCode=1;});
