// Verify that the GPL Ding route data supplies an unbroken playable 1-60
// level bracket for both factions. This is an input-data audit, not a speed claim.
const fs=require('node:fs'),path=require('node:path');
const root=process.env.FASTXP_DING_LEVELING_DIR||path.resolve(__dirname,'../ding/Guides/Leveling');
const factions=['Horde','Alliance'];
const all=[];
for(const folder of [...factions,'Skyborne']){
 const dir=path.join(root,folder);
 if(!fs.existsSync(dir))throw Error('Missing route directory '+folder);
 for(const file of fs.readdirSync(dir).filter(f=>f.endsWith('.lua'))){
  const text=fs.readFileSync(path.join(dir,file),'utf8');
  const id=text.match(/\bid\s*=\s*"([^"]+)"/);
  const min=text.match(/\bminLevel\s*=\s*(\d+)/);
  const max=text.match(/\bmaxLevel\s*=\s*(\d+)/);
  const next=text.match(/\bnext\s*=\s*"([^"]+)"/);
  if(!id||!min||!max)throw Error('Invalid guide metadata '+folder+'/'+file);
  const low=Number(min[1]),high=Number(max[1]);
  if(low<1||high>60||low>high)throw Error('Bad level range '+id[1]);
  all.push({id:id[1],min:low,max:high,next:next?.[1],folder,file});
 }
}
const unique=new Map();
for(const x of all){
 if(unique.has(x.id))throw Error('Duplicate guide id '+x.id);
 unique.set(x.id,x);
}
const links=all.filter(x=>x.next&& !unique.has(x.next));
if(links.length>0)throw Error('Missing continuation guide: '+links.slice(0,6).map(x=>x.id+' -> '+x.next).join(', '));
const coverage={};
for(const faction of factions){
 const guides=all.filter(x=>x.folder===faction);
 if(guides.length<20)throw Error('Too few guides for '+faction);
 const levels=[];
 for(let level=1;level<=60;level++)
  if(!guides.some(x=>x.min<=level&&x.max>=level))levels.push(level);
 if(levels.length)throw Error(faction+' uncovered levels: '+levels.join(','));
 coverage[faction]={guideCount:guides.length,start:Math.min(...guides.map(x=>x.min)),
  end:Math.max(...guides.map(x=>x.max)),coveredLevels:60};
}
console.log('PASS 1-60 route coverage:',all.length+' sections',
 JSON.stringify(coverage),'missing links: 0');
