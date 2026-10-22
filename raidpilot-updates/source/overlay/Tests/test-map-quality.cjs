const fs=require('fs'),path=require('path'),crypto=require('crypto'),assert=require('assert/strict');
const root=path.resolve(__dirname,'../LocalMap'),cal=require(path.join(root,'map-calibration.js')),display=require(path.join(root,'map-display.js'));
let checks=0;const check=(ok,label)=>{assert.ok(ok,label);checks++;};
const pts=[[0,0],[500,0],[0,200],[400,300]].map(([x,z])=>({x,z,u:-.001*x+.0002*z+.8,v:.0003*x+.002*z+.1}));
const fit=cal.fit(pts);check(cal.valid(fit),'Valid affine fit');check(fit.maxMeters<1e-8,'Exact known geometry recovered');
for(const invalid of [[],pts.slice(0,2),[{x:0,z:0,u:0,v:0},{x:1,z:1,u:1,v:1},{x:2,z:2,u:2,v:2}],pts.map(p=>({...p,v:NaN}))]){assert.throws(()=>cal.fit(invalid));checks++;}
check(!cal.valid({affine:[0,0,0,0,0,0],width:1,height:1}),'Singular affine rejected');
check(Math.abs(cal.heading({x:0,z:0},90,p=>({x:-p.x,y:p.z}))+90)<1e-10,'East points left on reversed illustration');
check(Math.abs(Math.abs(cal.heading({x:0,z:0},0,p=>({x:-p.x,y:p.z})))-180)<1e-10,'North follows projected axes');
const points=Array.from({length:30},(_,i)=>({id:String(i),name:'Extraction voisine '+i,label:true,x:80+(i%6)*21,y:80+Math.floor(i/6)*21,priority:1}));
const view={width:450,height:350},labels=display.layoutLabels(points,view);
check(labels.length>0&&labels.length<points.length,'Dense names reduced');
for(let i=0;i<labels.length;i++)for(let j=i+1;j<labels.length;j++)check(!display.intersects(labels[i],labels[j]),'Labels do not overlap');
check(display.layoutLabels(points,view,'hover').length===0,'Hover-only mode');
const manifest=JSON.parse(fs.readFileSync(path.join(root,'assets/forge-manifest.json')));
check(Object.keys(manifest.maps).length===13,'All thirteen maps included');
for(const [id,m]of Object.entries(manifest.maps)){
 const bytes=fs.readFileSync(path.join(root,m.path));check(crypto.createHash('sha256').update(bytes).digest('hex')===m.sha256,'Original image hash '+id);
 check(m.author&&m.sourceUrl.startsWith('https://'),'Attribution '+id);
 if(m.calibration){check(cal.valid(m.calibration),'Finite calibration '+id);check(m.calibration.validatedInRaid===false&&m.calibration.leaveOneOutRmsMeters>=0,'Calibration limits disclosed '+id);}
}
check(manifest.maps.customs.calibration.leaveOneOutRmsMeters<12,'Customs cross-validation within initial tolerance');
check(manifest.maps.terminal.calibration===null,'Do not fabricate missing calibration');
console.log(JSON.stringify({status:'PASS',checks}));
