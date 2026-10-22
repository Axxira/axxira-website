(function(root,factory){const api=factory();if(typeof module!=="undefined"&&module.exports)module.exports=api;else root.RPMapCalibration=api;})(typeof globalThis!=="undefined"?globalThis:this,function(){
"use strict";
// Coordinates are normalized to the source image. A calibration never depends
// on viewport size, zoom or a resized preview. Reject collinear/duplicate data.
function fit(points){
  if(!Array.isArray(points)||points.length<3)throw Error("Il faut au moins trois repères.");
  if(points.some(p=>![p.x,p.z,p.u,p.v].every(Number.isFinite)))throw Error("Repère invalide.");
  const mx=points.reduce((s,p)=>s+p.x,0)/points.length, mz=points.reduce((s,p)=>s+p.z,0)/points.length;
  const scale=Math.max(...points.map(p=>Math.hypot(p.x-mx,p.z-mz)));
  if(scale<1)throw Error("Choisis des repères plus éloignés.");
  const a=Array.from({length:3},()=>[0,0,0,0,0]);
  for(const p of points){const q=[(p.x-mx)/scale,(p.z-mz)/scale,1];for(let i=0;i<3;i++){for(let j=0;j<3;j++)a[i][j]+=q[i]*q[j];a[i][3]+=q[i]*p.u;a[i][4]+=q[i]*p.v;}}
  for(let i=0;i<3;i++){
    let pivot=i;for(let j=i+1;j<3;j++)if(Math.abs(a[j][i])>Math.abs(a[pivot][i]))pivot=j;
    if(Math.abs(a[pivot][i])<1e-6)throw Error("Les repères sont alignés : choisis un triangle plus large.");
    [a[i],a[pivot]]=[a[pivot],a[i]];const d=a[i][i];for(let j=i;j<5;j++)a[i][j]/=d;
    for(let k=0;k<3;k++){if(k===i)continue;const f=a[k][i];for(let j=i;j<5;j++)a[k][j]-=f*a[i][j];}
  }
  const affine=[a[0][3]/scale,a[1][3]/scale,a[2][3]-(a[0][3]*mx+a[1][3]*mz)/scale,a[0][4]/scale,a[1][4]/scale,a[2][4]-(a[0][4]*mx+a[1][4]*mz)/scale];
  const det=affine[0]*affine[4]-affine[1]*affine[3];
  if(Math.abs(det)<1e-12)throw Error("Les points cliqués sont alignés : recommence.");
  const errors=points.map(p=>{const du=affine[0]*p.x+affine[1]*p.z+affine[2]-p.u,dv=affine[3]*p.x+affine[4]*p.z+affine[5]-p.v;return Math.hypot((affine[4]*du-affine[1]*dv)/det,(-affine[3]*du+affine[0]*dv)/det);});
  return {affine,width:1,height:1,controlPoints:points.length,rmsMeters:Math.sqrt(errors.reduce((s,e)=>s+e*e,0)/points.length),maxMeters:Math.max(...errors)};
}
function heading(position,yaw,project){
  const radians=Number(yaw||0)*Math.PI/180;
  const a=project(position),b=project({...position,x:position.x+Math.sin(radians)*4,z:position.z+Math.cos(radians)*4});
  return Math.atan2(b.x-a.x,-(b.y-a.y))*180/Math.PI;
}
function valid(c){return !!(c&&Array.isArray(c.affine)&&c.affine.length===6&&c.affine.every(Number.isFinite)&&Number.isFinite(c.width)&&c.width>0&&Number.isFinite(c.height)&&c.height>0&&Math.abs(c.affine[0]*c.affine[4]-c.affine[1]*c.affine[3])>1e-12);}
function containsHeight(c,y){
  if(!c?.worldHeight)return true;
  const h=c.worldHeight;
  return Array.isArray(h)&&h.length===2&&h.every(Number.isFinite)&&h[0]<h[1]
    && Number.isFinite(y)&&y>=h[0]&&y<h[1];
}
function builtIn(asset,position){
  const y=position?.y;
  if(Number.isFinite(y)){
    const floor=(asset?.floorCalibrations||[]).find(c=>valid(c)&&containsHeight(c,y));
    if(floor)return floor;
  }
  const base=asset?.calibration;
  if(!valid(base))return null;
  // Positions without altitude can still use an overview. Never infer an
  // upper or underground plane from an absent/invalid altitude.
  return y==null||containsHeight(base,y)?base:null;
}
return {fit,heading,valid,containsHeight,builtIn};
});
