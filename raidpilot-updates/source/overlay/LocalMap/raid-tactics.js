(function(root,factory){const api=factory();if(typeof module!=="undefined"&&module.exports)module.exports=api;else root.RPRaidTactics=api;})(typeof globalThis!=="undefined"?globalThis:this,function(){
"use strict";
const label=value=>String(value??"").normalize("NFD").replace(/[\u0300-\u036f]/g,"").replace(/[^a-z0-9]+/gi," ").trim().toLowerCase();
const finite=v=>typeof v==="number"&&Number.isFinite(v);
function extractionEntries(entries){return (Array.isArray(entries)?entries:[]).map(e=>({id:String(e?.id??"").trim().toLowerCase(),labels:[...new Set((Array.isArray(e?.labels)?e.labels:[]).map(label).filter(Boolean))]})).filter(e=>e.id||e.labels.length);}
function matchesExtraction(marker,entries){
 if(!marker?.category?.startsWith("extract_")||marker.category==="extract_transit")return false;
 const id=String(marker.meta?.sourceId || marker.id?.replace(/^extract:/,"") || "").toLowerCase();
 const labels=[marker.name,...(marker.meta?.labels||[])].map(label).filter(Boolean);
 return entries.some(e=>(id&&id===e.id)||labels.some(l=>e.labels.includes(l)));
}
function zones(input){return (Array.isArray(input)?input:[]).map((z,i)=>{
 const p=z?.position||z||{},outline=(Array.isArray(z?.outline)?z.outline:[]).filter(p=>finite(p?.x)&&finite(p?.z)).map(p=>({x:p.x,z:p.z}));
 let top=finite(z?.top)?z.top:null,bottom=finite(z?.bottom)?z.bottom:null;if(top!==null&&bottom!==null&&top<bottom)[top,bottom]=[bottom,top];
 return {id:String(z?.id||i),x:p.x,y:p.y,z:p.z,outline,top,bottom};
}).filter(z=>finite(z.x)&&finite(z.z));}
function segmentDistance(p,a,b){const dx=b.x-a.x,dz=b.z-a.z,len=dx*dx+dz*dz,t=len?Math.max(0,Math.min(1,((p.x-a.x)*dx+(p.z-a.z)*dz)/len)):0;return Math.hypot(p.x-a.x-t*dx,p.z-a.z-t*dz);}
function distanceToZone(p,z){
 if(z.outline.length<3)return Math.hypot(p.x-z.x,p.z-z.z);
 let inside=false,best=Infinity;
 for(let i=0,j=z.outline.length-1;i<z.outline.length;j=i++){
  const a=z.outline[i],b=z.outline[j];
  if((a.z>p.z)!==(b.z>p.z)&&p.x<(b.x-a.x)*(p.z-a.z)/(b.z-a.z)+a.x)inside=!inside;
  best=Math.min(best,segmentDistance(p,a,b));
 }
 return inside?0:best;
}
// Same thresholds and hysteresis as the existing C# providers. This local
// fallback works while their network-backed cache is still loading.
function proximity(p,zoneList,previous={},kind="boundary"){
 const safe={warning:false,critical:false,distance:null,zoneId:null};
 if(!p||!finite(p.x)||!finite(p.z))return safe;
 let distance=Infinity,zoneId=null;
 for(const z of zoneList){
  if(finite(p.y)&&z.top!==null&&z.bottom!==null&&(p.y<z.bottom-3||p.y>z.top+3))continue;
  const d=distanceToZone(p,z);if(d<distance){distance=d;zoneId=z.id;}
 }
 if(!finite(distance))return safe;
 const uncertainty=finite(p.uncertainty)?Math.min(6,Math.max(0,p.uncertainty)):0,effective=Math.max(0,distance-uncertainty);
 const critical=effective<=(kind==="mine"?(previous.critical?16:12):(previous.critical?18:14));
 return {warning:kind==="mine"?critical:effective<=(previous.warning?36:30),critical,distance,zoneId};
}
function nativeOrLocal(p,local,kind="boundary"){
 const d=kind==="mine"?p?.mineDistance:p?.boundaryDistance;
 const critical=kind==="mine"?p?.mineAlert===true:p?.boundaryCritical===true;
 const warning=critical||(kind!=="mine"&&p?.boundaryWarning===true);
 if(!finite(d)&&!warning)return local;
 return {...local,warning,critical,distance:finite(d)?Math.max(0,d):null};
}
return {label,extractionEntries,matchesExtraction,zones,segmentDistance,distanceToZone,proximity,nativeOrLocal};
});
