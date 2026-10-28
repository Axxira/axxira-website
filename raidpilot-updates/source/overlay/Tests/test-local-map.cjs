const fs=require('fs'),path=require('path'),vm=require('vm'),assert=require('assert/strict');
const root=path.resolve(__dirname,'../LocalMap');
// Pure key/door data-model coverage is also part of this Windows suite.
require('./test-key-guide.cjs');
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
const ids={};for(const id of ['viewport','stage','base-map','overlay','panel','filters','layers-btn','search','profile','floor','zoom-in','zoom-out','fit','center-player','zoom-value','map-name','marker-count','health','details','missing-asset','label-mode','map-style','map-credit','calibrate','calibration-panel','calibration-map','calibration-status','calibration-landmark','calibration-reference','calibration-position','calibration-offset','calibration-save','calibration-reset','calibration-close','tactical-warning','position-stale',
  'quests-btn','questpilot-panel','questpilot-close','questpilot-search','questpilot-summary',
  'questpilot-list','questpilot-steps','questpilot-other','questpilot-shared-steps','questpilot-prep','questpilot-hud',
  'questpilot-hud-color','questpilot-hud-title','questpilot-hud-detail',
  'keys-btn','keyguide-panel','keyguide-close','keyguide-search',
  'keyguide-summary','keyguide-list','keyguide-access','keyguide-show-locks',
  'keyguide-map',
  'questpilot-share','questpilot-share-status','questpilot-share-output',
  'questpilot-peer-name','questpilot-peer-code','questpilot-import','questpilot-team-plans'])ids[id]=new Element('div');
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
for(const f of ['projection.js','map-calibration.js','map-display.js','raid-tactics.js','automatic-floors.js','quest-pilot.js','key-guide.js','raidpilot-local-map.js','map-features.js','legacy-bridge.js'])vm.runInContext(fs.readFileSync(path.join(root,f),'utf8'),context,{filename:f});
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
 // Maps without a verified affine fit must remain usable without inventing
 // a projected position on an uncalibrated HD illustration.
 for (const uncalibrated of ['terminal','icebreaker','the-labyrinth']) {
   api.setMap(uncalibrated);
   api.setPlayer({position:{x:0,y:0,z:0},rotation:25});
   check(api.health().mapStyle==='tactical',
     'Missing HD calibration did not switch to safe schematic: '+uncalibrated);
   check(!!marker('__player'),
     'Schematic fallback did not render native player: '+uncalibrated);
 }
 api.setMap('customs');
 check(api.health().mapStyle==='forge',
   'Previously selected HD map style did not restore on calibrated maps');
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
 api.setPlayer({position:{x:160,y:2,z:40},rotation:180});
 check(!!marker('__player'),'Player marker did not recover after zoom and map transitions');
 await new Promise(resolve=>setTimeout(resolve,210));
 check(messages.some(m=>m.type==='raidpilot-map-zoom-preference'
   && Number.isFinite(m.payload?.factor)),
   'Local map never sent a zoom preference to the native host');
 // The native pose may stop while the player marker still contains last-known
 // coordinates. Never report such a marker as a live tracked position.
 context.__poseTestNow = Date.now() + 4000;
 vm.runInContext('Date.now = () => __poseTestNow',context);
 check(api.health().position==='STALE' && api.health().positionStale,
   'A silent native position stream was incorrectly reported as live');
 check(api.health().positionAgeMs>3000,'Stale age not preserved in health state');
 api.render();
 check(ids['position-stale'].hidden===false,
   'GPS stale warning is not visible in the non-interactive overlay');
 check(ids.health.textContent.includes('Signal de position interrompu'),
   'Health text does not disclose interrupted coordinates');
 api.setPlayer({position:{x:160,y:2,z:40},rotation:180});
 check(api.health().position==='TRACKING' && !api.health().positionStale,
   'First new position did not immediately recover GPS status');
 check(ids['position-stale'].hidden===true,'Recovered GPS banner was not hidden');
 api.setPlayer(null);
 check(api.health().position==='WAIT' && api.health().positionAgeMs===null,
   'Cleared pose leaked its last freshness timestamp');
 api.setPlayer({position:{x:160,y:2,z:40},rotation:180});
 // QuestPilot: native OCR synchronization and local map share persistent user selection.
 api.setMap('customs');
 ids.fit.dispatchEvent(new context.Event('click'));
 const mapsDocument=JSON.parse(fs.readFileSync(path.join(root,'cache/maps.json'),'utf8'));
 const tasksDocument=JSON.parse(fs.readFileSync(path.join(root,'cache/tasks.json'),'utf8'));
 const mapValues=mapsDocument.data?.maps||mapsDocument.maps||[];
 const taskValues=tasksDocument.data?.tasks||tasksDocument.tasks||[];
 const realMaps=Array.isArray(mapValues)?mapValues:Object.values(mapValues);
 const realTasks=Array.isArray(taskValues)?taskValues:Object.values(taskValues);
 // Actual immutable tarkov.dev snapshot, not fabricated demo keys.
 const customsMap=realMaps.find(m=>m.normalizedName==='customs');
 const guide=api.keyGuideSnapshot();
 check(guide.map==='customs'&&guide.locks>0,
   'Real cached Customs locks not available in the key guide');
 check(guide.located>0&&guide.identified>0,
   'Real local key/door associations were not resolved');
 check(guide.keys>0,'No verified key references in cached Customs');
 check(api.keyGuideFind('porte').locks.length>0,
   'French door search did not include published locks');
 check(api.openKeyGuide()===true,'Guide does not open from the local map');
 check(ids['keyguide-list'].children.length>0,
   'Key guide did not render entries for the selected map');
 check(ids['keyguide-summary'].textContent.includes('serrures répertoriées'),
   'Key guide lacks a clear sourced map summary');
 ids['keyguide-show-locks'].dispatchEvent(new context.Event('click'));
 check(api.health().markerCount>0,
   'Displaying the locked-door layer broke the map');
 api.closeKeyGuide();
 const customsIds=new Set(realMaps.filter(m=>m.normalizedName==='customs').map(m=>m.id));
 const locatable=realTasks.filter(task=>(task.objectives||[]).some(o=>
   (o.zones||[]).some(z=>customsIds.has(z.map?.id||z.map)&&z.position) ||
   (o.possibleLocations||[]).some(z=>customsIds.has(z.map?.id||z.map)&&(z.positions||[]).length)));
 check(locatable.length>0,'Actual Tarkov quest catalogue contains no geolocated Customs tasks');
 const chosen=locatable[0];
 check(api.questPilotToggle(chosen.id,true),'QuestPilot failed to select a real task');
 const qp=api.questPilotSnapshot();
 check(qp.active.includes(chosen.id)&&qp.mapSteps.some(x=>x.questId===chosen.id),
   'QuestPilot did not place its active objectives on the local map');
 check(ids['questpilot-list'].children.length>0,
   'QuestPilot panel did not expose a manually selectable catalogue');
 const stage=qp.mapSteps.find(x=>x.questId===chosen.id);
 const expectedColor=context.RPQuestPilot.colorFor(chosen.id);
 check(qp.mapSteps.filter(x=>x.questId===chosen.id).every(x=>x.color===expectedColor),
   'Quest stages and legend use different colors');
 check(api.questPilotStepDone(stage.id,true),
   'QuestPilot refused to mark a stage as done manually');
 check(api.questPilotSnapshot().done.includes(stage.id),
   'Manually marked completion missing');
 check(JSON.parse(storage.get('raidpilot-questpilot-v1:regular')).done.includes(stage.id),
   'Manual progress was not retained in profile');
 api.questPilotToggle(chosen.id,false);
 context.__raidPilotSetRaidQuests({generation:6,map:'customs',gameMode:'regular',
   quests:[{id:chosen.id,name:chosen.name}]});
 check(!api.questPilotSnapshot().active.includes(chosen.id),
   'OCR unexpectedly restored a quest explicitly removed by player');

 // EFT log evidence crosses the native bridge and persists in the isolated profile.
 const logEvent={mode:'regular',taskId:chosen.id,status:'completed',
   at:1792828800,source:'eft-local-log'};
 check(context.__raidPilotApplyQuestLogEvents([logEvent])===1,
   'Native journal message was not acknowledged by QuestPilot');
 const afterLog=api.questPilotSnapshot();
 check(afterLog.evidence[chosen.id]?.status==='completed',
   'Quest hand-in was not recorded as EFT-log evidence');
 check(!afterLog.active.includes(chosen.id),
   'Native log reactivated a manually excluded quest');
 check(JSON.parse(storage.get('raidpilot-questpilot-v1:regular')).evidence[chosen.id].source==='eft-local-log',
   'Quest evidence did not persist locally');
 const pveLog={mode:'pve',taskId:'1234567890abcdef12345678',status:'active',
   at:1792828801,source:'eft-local-log'};
 check(context.__raidPilotApplyQuestLogEvents([pveLog])===1,
   'PvE event could not be stored while viewing PvP');
 check(!api.questPilotSnapshot().active.includes(pveLog.taskId)
   && JSON.parse(storage.get('raidpilot-questpilot-v1:pve')).active.includes(pveLog.taskId),
   'PvE journal event crossed into PvP profile');
 check(context.__raidPilotApplyQuestLogEvents([{...logEvent,source:'untrusted'}])===0,
   'Forged source was accepted as an EFT journal event');

 // Manually imported squad objectives remain distinct from my quest progress.
 const friend=new context.RPQuestPilot.Planner(null,'regular');
 friend.toggle(chosen.id,true);
 friend.toggleDone(stage.id,true);
 ids['questpilot-peer-name'].value='Pierre';
 ids['questpilot-peer-code'].value=friend.exportShare();
 ids['questpilot-import'].dispatchEvent(new context.Event('click'));
 const squadPlan=JSON.parse(storage.get('raidpilot-questpilot-v1:regular'));
 check(!!squadPlan.sharedPlans?.Pierre,
   'QuestPilot failed manual plan import: '+ids['questpilot-share-status'].textContent);
 check(squadPlan.sharedPlans.Pierre.active.includes(chosen.id),
   'QuestPilot did not import the manually shared teammate plan');
 check(!api.questPilotSnapshot().active.includes(chosen.id),
   'Importing teammate plan changed my own quest selection');
 check(ids['questpilot-team-plans'].children.length>0,
   'Imported teammate plan not visible in interactive panel');
 check(ids['questpilot-shared-steps'].children.length>0,
   'The combined team objectives section did not render');
 check(ids['questpilot-other'].children.length>0,
   'Objectives lacking public coordinates are not described');
 check(ids['questpilot-prep'].children.length>0,
   'The simple raid-preparation summary is missing');
 check(ids.overlay.children.some(node=>node.dataset.questId===chosen.id),
   'Teammate-only quest marker did not display on map');
 // Live squad plans are ephemeral, mode-scoped and never imported as personal progress.
 const second=locatable.find(q=>q.id!==chosen.id && /^[0-9a-f]{24}$/i.test(q.id));
 check(!!second,'Need a second real geolocated quest for the live team smoke');
 const live={id:'test-live-peer',name:'Amie',mode:'regular',
   quests:[second.id],done:[],at:Date.now()};
 check(context.__raidPilotSetTeamQuestPlan(live)===true,
   'Live squad plan did not enter the WebView2 bridge');
 check(ids.overlay.children.some(node=>node.dataset.questId===second.id),
   'A peer-only quest did not appear on the actual map');
 check(!api.questPilotSnapshot().active.includes(second.id),
   'Receiving a peer plan contaminated my own active tasks');
 check(api.questPilotLiveSnapshot().quests.every(id=>id!==second.id),
   'Peer quest leaked into my outgoing privacy-scoped plan');
 check(context.__raidPilotSetTeamQuestPlan({...live,quests:[],at:live.at-1})===false,
   'Out-of-order peer plan was accepted');
 check(context.__raidPilotClearTeamQuestPlan('test-live-peer')===true,
   'Peer removal failed to clear ephemeral plans');
 check(!ids.overlay.children.some(node=>node.dataset.questId===second.id),
   'A departed peer left a ghost quest on the map');
 console.log(JSON.stringify({status:'PASS',checks,customsPOI:api.health().markerCount,visibleMarkers:ids.overlay.children.filter(e=>e.dataset.markerId).length,playerTransform:marker('__player').getAttribute('transform'),health:ids.health.textContent}));
})().catch(e=>{console.error(e);process.exitCode=1;});
