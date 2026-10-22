const assert = require("assert");
const p = require("../LocalMap/projection.js");

function approx(a,b,eps=1e-9){ assert.ok(Math.abs(a-b)<=eps, `${a} != ${b}`); }

let r = p.rotateXZ(10,0,90);
approx(r.x,0,1e-8);
approx(r.z,10,1e-8);

const map = {rotation:180,bounds:[[100,-50],[-100,50]]};
let c = p.projectNormalized({x:0,z:0},map);
approx(c.u,0.5,1e-8);
approx(c.v,0.5,1e-8);

let px = p.toPixels({x:0,z:0},map,1000,500);
approx(px.x,500,1e-8);
approx(px.y,250,1e-8);

const floorMeta = {floors:[{name:"2nd Floor",extents:[{height:[3,6]}]}]};
assert.strictEqual(p.markerOnFloor({position:{x:0,y:4,z:0}},floorMeta,"2nd Floor"),true);
assert.strictEqual(p.markerOnFloor({position:{x:0,y:1,z:0}},floorMeta,"2nd Floor"),false);

console.log("projection self-test: OK");
