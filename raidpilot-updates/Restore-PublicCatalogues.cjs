'use strict';
const fs=require('fs'),path=require('path'),crypto=require('crypto');
const canonical=x=>Array.isArray(x)?x.map(canonical):x&&typeof x==='object'?Object.fromEntries(Object.keys(x).sort().map(k=>[k,canonical(x[k])])):x;
const hash=x=>crypto.createHash('sha256').update(typeof x==='string'?x:JSON.stringify(canonical(x))).digest('hex');
const root=path.resolve(process.argv[2]||path.join(__dirname,'source/overlay'));
const cache=path.join(root,'LocalMap/cache');
const specification=JSON.parse(fs.readFileSync(path.join(__dirname,'public-catalogues.json'),'utf8'));
const records=[];
(async()=>{
 for(const spec of specification.downloads){
  const url='https://api.tarkov-stammtisch.de/api/v2/'+spec.endpoint;
  const response=await fetch(url,{signal:AbortSignal.timeout(120000)});
  if(!response.ok)throw Error('Public catalogue '+spec.endpoint+': HTTP '+response.status);
  const data=await response.json();
  // Prices and public goon reports can change. Pin the actual map geometry,
  // quest objectives, translations and item identities used by this release.
  let checked=spec.table?data.data?.[spec.table]:data;
  if(spec.omitAchievementStatistics){
   checked=structuredClone(checked);
   for(const achievement of Object.values(checked.data?.achievements||{})){
    delete achievement.playersCompletedPercent;
    delete achievement.adjustedPlayersCompletedPercent;
   }
  }
  if(spec.identities)checked=Object.values(checked).map(x=>({id:x.id,name:x.name,normalizedName:x.normalizedName})).sort((a,b)=>a.id.localeCompare(b.id));
  if(hash(checked)!==spec.checkedSha256)throw Error('Public game data changed since validation: '+spec.endpoint);
  const text=JSON.stringify(data),target=path.join(cache,spec.file);
  fs.mkdirSync(path.dirname(target),{recursive:true});fs.writeFileSync(target,text);
  records.push({file:spec.file,source:url,sha256:hash(text),bytes:Buffer.byteLength(text)});
  console.log('Verified public catalogue: '+spec.endpoint);
 }
 for(const spec of specification.translations){
  const target=path.join(cache,spec.file),bytes=fs.readFileSync(target),data=JSON.parse(bytes);
  if(hash(bytes.toString('utf8'))!==spec.sha256||Object.values(data.data).some(v=>typeof v!=='string'))throw Error('Game translation dictionary mismatch: '+spec.file);
  records.push({...spec,source:'Public game translation dictionary bundled as readable JSON'});
 }
 fs.writeFileSync(path.join(cache,'manifest.json'),JSON.stringify({status:'READY',updatedAt:new Date().toISOString(),sources:records.length}));
 fs.writeFileSync(path.join(cache,'catalogue-sources.json'),JSON.stringify({revision:8,createdAt:new Date().toISOString(),modes:['regular','pve'],unavailableModes:['pvp-season'],records},null,2));
})().catch(error=>{console.error(error.message);process.exitCode=1;});
