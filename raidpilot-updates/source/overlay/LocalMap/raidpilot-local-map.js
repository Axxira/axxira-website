(() => {
"use strict";

const $ = (s) => document.querySelector(s);
const state = {
  meta: null,
  catalog: null,
  assets: { maps: {} },
  cache: {},
  mapKey: null,
  mapMeta: null,
  apiMap: null,
  markers: [],
  player: null,
  lastPoseReceivedAt: null,
  teammates: [],
  floor: "BASE",
  zoom: 1,
  zoomFactor: 1,
  panX: 0,
  panY: 0,
  stageW: 1600,
  stageH: 900,
  dragging: false,
  dragStart: null,
  search: "",
  enabled: new Set(),
  selectedId: null,
  labelMode: "auto",
  runtimeMarkers: [],
  cacheStatus: "MISSING",
  beaconStatus: "LOCAL",
  calibration: null
};
state.mapStyle = "forge";
state.preferredMapStyle = "forge";
state.uncalibratedFallback = false;
state.followPlayer = true;
state.calibrationPoints = [];
state.pendingCalibration = null;
state.raidExtractions = null;
state.boundaryPayload = null;
state.minePayload = null;
state.cachedBoundaries = [];
state.cachedMines = [];
state.boundaryStatus = {};
state.mineStatus = {};
state.guidanceTarget = null;
state.initialized = false;
state.gameMode = "regular";
state.catalogues = {};
state.raidQuests = null;
state.questPilot = null;
state.questHudTarget = "";
state.questMapIds = new Set();
state.questSearch = "";
state.learnedRoute = null;
state.guidance = null;
state.automaticFloor = null;
const floorTracker = new RaidPilotFloors.Tracker();

const refs = {};
let viewportSize = null;
const SVG_NS = "http://www.w3.org/2000/svg";
const mapZoomMinFactor = 0.20;
const mapZoomMaxFactor = 32;
// Map health is observational: never modify measured coordinates or yaw.
const poseStaleAfterMs = 3000;
let mapZoomSyncTimer = null;
const validMapZoomFactor = value => {
  if (value === null || value === undefined || value === "") return null;
  const factor = Number(value);
  return Number.isFinite(factor) && factor >= mapZoomMinFactor && factor <= mapZoomMaxFactor
    ? factor : null;
};
const clampMapZoomFactor = factor =>
  Math.max(mapZoomMinFactor, Math.min(mapZoomMaxFactor, factor));

const normalize = s => String(s ?? "").trim().toLowerCase().replace(/[\s_]+/g, "-");
const finite = (v, f = 0) => Number.isFinite(Number(v)) ? Number(v) : f;
const interactive = () => document.documentElement?.classList?.contains("raidpilot-interactive") === true;
const followingPlayer = () => !state.pendingCalibration && (!interactive() || state.followPlayer);

function escapeText(v) { return String(v ?? ""); }
function unwrap(v) { return v && typeof v === "object" && v.data !== undefined ? v.data : v; }

async function fetchJsonSafe(url) {
  try {
    const r = await fetch(url, { cache: "no-store" });
    if (!r.ok) throw new Error(`${r.status} ${r.statusText}`);
    return await r.json();
  } catch (e) {
    return null;
  }
}

function translated(value, dict) {
  if (value == null) return "";
  const key = String(value);
  return dict && Object.prototype.hasOwnProperty.call(dict, key) ? String(dict[key]) : key;
}

function postHost(message) {
  try {
    if (window.chrome?.webview?.postMessage) window.chrome.webview.postMessage(message);
  } catch (_) {}
}

function loadPreferences() {
  let storedZoomFactor = null;
  try {
    const raw = localStorage.getItem("raidpilot-forge-map-v6");
    if (raw) {
      const p = JSON.parse(raw);
      if (Array.isArray(p.enabled)) state.enabled = new Set(p.enabled);
      if (["auto","hover"].includes(p.labelMode)) state.labelMode = p.labelMode;
      if (["forge","tactical"].includes(p.mapStyle)) state.mapStyle = p.mapStyle;
      storedZoomFactor = validMapZoomFactor(p.zoomFactor);
      if(p.tacticsVersion!==7){state.enabled.add("boundary");state.enabled.add("hazard_minefield");}
    }
  } catch (_) {}
  // Native %LocalAppData%\RaidPilot preference survives launcher replacements;
  // the old WebView localStorage preference works if native storage is absent.
  state.zoomFactor = validMapZoomFactor(window.__raidPilotHostZoomFactor)
    ?? storedZoomFactor ?? 1;
  state.preferredMapStyle = state.mapStyle;
}

function savePreferences() {
  try {
    localStorage.setItem("raidpilot-forge-map-v6", JSON.stringify({
      enabled: [...state.enabled], labelMode:state.labelMode,
      mapStyle:state.preferredMapStyle, tacticsVersion:7,
      zoomFactor:state.zoomFactor
    }));
  } catch (_) {}
}

function saveZoomPreferenceFromGesture() {
  const fit = calculateFitZoom();
  if (!Number.isFinite(fit) || fit <= 0) return;
  const next = clampMapZoomFactor(state.zoom / fit);
  state.zoomFactor = next;
  if (refs.zoomValue) refs.zoomValue.textContent = `${Math.round(next * 100)}%`;
  savePreferences();
  // Do not write an AppData JSON file on each high-frequency wheel event.
  // The per-page localStorage save above is immediate.
  if (mapZoomSyncTimer !== null) clearTimeout(mapZoomSyncTimer);
  mapZoomSyncTimer = setTimeout(() => {
    mapZoomSyncTimer = null;
    postHost({type:"raidpilot-map-zoom-preference",payload:{factor:state.zoomFactor}});
  }, 160);
}

function defaultEnabled() {
  // The illustrated maps already contain labels, exits and hazard symbols.
  // Keep the old v1 preferences intact and start this view with only the team.
  state.enabled = new Set(["player-position","teammate","boundary","hazard_minefield"]);
}

function layerKnown(id) {
  return state.catalog.groups.some(g => (g.layers || []).some(l => l.id === id || l.linked?.includes(id)));
}

function inferGroup(category) {
  if (category === "player-position" || category === "teammate" || category === "boundary") return "system";
  if (category.startsWith("extract_")) return "extracts";
  if (category.startsWith("quest_")) return "tasks";
  if (category.startsWith("container_")) return "lootable";
  if (category === "loose_loot" || category.startsWith("loose_")) return "loose";
  if (category.startsWith("spawn_")) return "spawns";
  if (category.startsWith("hazard")) return "hazards";
  if (["lock","switch","stationarygun"].includes(category)) return "usable";
  if (["place-names","btr-stop"].includes(category)) return "landmarks";
  return "other";
}

function groupById(id) { return state.catalog.groups.find(g => g.id === id); }

function isEnabled(marker) {
  const cats = [marker.category, ...(marker.extraLayers || [])].filter(Boolean);
  if (cats.includes("player-position")) return true;
  if (cats.some(c => state.enabled.has(c))) return true;
  if (state.catalog.groups.some(g => g.layers?.some(l => state.enabled.has(l.id) && l.linked?.some(c=>cats.includes(c))))) return true;
  return false;
}

function searchMatches(marker) {
  if (!state.search) return true;
  const hay = `${marker.name || ""} ${(marker.search || []).join(" ")} ${marker.category || ""}`.toLowerCase();
  return state.search.split(",").map(x => x.trim()).filter(Boolean).every(term => hay.includes(term));
}

function extentContainsPoint(pos, b) {
  if (!Array.isArray(b) || !Array.isArray(b[0]) || !Array.isArray(b[1])) return true;
  const minX = Math.min(finite(b[0][0]), finite(b[1][0]));
  const maxX = Math.max(finite(b[0][0]), finite(b[1][0]));
  const minZ = Math.min(finite(b[0][1]), finite(b[1][1]));
  const maxZ = Math.max(finite(b[0][1]), finite(b[1][1]));
  return finite(pos?.x) >= minX && finite(pos?.x) <= maxX && finite(pos?.z) >= minZ && finite(pos?.z) <= maxZ;
}

function markerOnFloor(marker) {
  if(state.floor === "AUTO" && state.automaticFloor?.filter){
    const f=floorTracker.catalog?.levels.find(l=>l.id===state.automaticFloor.selection);
    if(f && Number.isFinite(marker.position?.y))return f.regions.some(r=>RaidPilotFloors.inside(r,marker.position,false));
  }
  if (!state.mapMeta || !state.floor || state.floor === "BASE" || state.floor === "AUTO") return true;
  const floor = (state.mapMeta.floors || []).find(f => normalize(f.name) === normalize(state.floor));
  if (!floor) return true;
  const y = Number(marker.position?.y);
  if (!Number.isFinite(y) && marker.top == null && marker.bottom == null) return true;
  const top = marker.top == null ? y : finite(marker.top, y);
  const bottom = marker.bottom == null ? y : finite(marker.bottom, y);
  return (floor.extents || []).some(ext => {
    if (!Array.isArray(ext.height)) return false;
    const lo = Math.min(finite(ext.height[0], -1e9), finite(ext.height[1], 1e9));
    const hi = Math.max(finite(ext.height[0], -1e9), finite(ext.height[1], 1e9));
    const overlaps = top >= lo && bottom <= hi;
    if (!overlaps) return false;
    return !Array.isArray(ext.bounds) || !ext.bounds.length || ext.bounds.some(b => extentContainsPoint(marker.position, b));
  });
}

function project(pos) {
  const calibration = activeCalibration(pos);
  if (state.mapStyle === "forge" && !calibration) return {x:NaN,y:NaN,inBounds:false};
  return RPProjection.toPixels(pos || {x:0,z:0}, state.mapMeta || {}, state.stageW, state.stageH, calibration);
}

function assetInfo() { return state.assets.maps[state.mapKey] || {}; }
function legacyCalibrationKey() { return `raidpilot-calibration-v6:${state.mapKey}:${state.mapStyle}:${assetInfo().sha256 || "legacy"}:${state.floor==="AUTO"?"BASE":state.floor}`; }
function calibrationKey(pos=state.player?.position) {
  const key=legacyCalibrationKey(),asset=assetInfo();
  const c=state.mapStyle==="forge"&&asset.floorCalibrations?.length?RPMapCalibration.builtIn(asset,pos):null;
  return c?`${key}:plane:${c.scope}`:key;
}
function loadCalibration() {
  state.calibration=null;
  try {
    let raw=localStorage.getItem(calibrationKey());
    // Legacy Factory personal fits belong to the previously supported base
    // plane; do not apply those offsets to the separately drawn tunnels.
    if(!raw&&RPMapCalibration.builtIn(assetInfo(),state.player?.position)===assetInfo().calibration)raw=localStorage.getItem(legacyCalibrationKey());
    const c=JSON.parse(raw);if(RPMapCalibration.valid(c))state.calibration=c;
  } catch(_){}
}
function activeCalibration(pos) {
  if(RPMapCalibration.valid(state.calibration)){
    const c=state.calibration,asset=assetInfo();
    const legacyPlane=!asset.floorCalibrations?.length||c.worldHeight||RPMapCalibration.builtIn(asset,pos)===asset.calibration;
    if(legacyPlane&&(pos?.y==null||RPMapCalibration.containsHeight(c,pos.y)))return c;
  }
  if(state.mapStyle!=="forge")return null;
  return RPMapCalibration.builtIn(assetInfo(),pos);
}

function svgEl(name, attrs = {}) {
  const el = document.createElementNS(SVG_NS, name);
  for (const [k,v] of Object.entries(attrs)) el.setAttribute(k, String(v));
  return el;
}

function categoryClass(cat) {
  if (cat === "player-position") return "player";
  if (cat === "teammate") return "teammate";
  if (cat.startsWith("extract_")) return "extract";
  if (cat.startsWith("hazard")) return "hazard";
  if (cat.startsWith("quest_")) return "quest";
  if (cat.startsWith("container_")) return "container";
  if (cat.startsWith("spawn_")) return "spawn";
  if (cat === "lock" || cat === "switch" || cat === "stationarygun") return "usable";
  if (cat === "btr-stop") return "landmark";
  if (cat.startsWith("loose_") || cat === "loose_loot") return "loot";
  return "generic";
}

function makeOutline(marker) {
  if (!Array.isArray(marker.outline) || marker.outline.length < 2) return null;
  if (!marker.category.startsWith("hazard") && marker.id !== state.selectedId) return null;
  const pts = marker.outline.map(v => project(v)).map(p => `${p.x.toFixed(1)},${p.y.toFixed(1)}`).join(" ");
  if(pts.includes("NaN"))return null;
  const poly = svgEl("polygon", { points: pts, class: `rp-outline ${categoryClass(marker.category)}` });
  poly.dataset.markerId = marker.id;
  return poly;
}

function markerRadius(marker) {
  if (marker.category === "player-position") return 10;
  if (marker.category === "teammate") return 8;
  if (marker.category.startsWith("extract_")) return 7;
  return 4;
}

function renderMarker(marker, view) {
  const p = project(marker.position);
  if(!Number.isFinite(p.x)||!Number.isFinite(p.y))return;
  if (!p.inBounds && marker.category !== "player-position" && marker.category !== "teammate") return;
  const screen = RPMapDisplay.screenPoint(p,view);
  if (!RPMapDisplay.inViewport(screen,view)) return;
  const cls = categoryClass(marker.category);
  const questStyle = cls === "quest"
    ? state.questPilot?.markerVisual(marker, state.questMapIds) : null;
  const inverse = (1/state.zoom).toFixed(6);
  const confirmed=isRaidExtraction(marker),target=confirmed&&isGuidanceTarget(marker);
  const g = svgEl("g", { class: `rp-marker ${cls}${marker.id === state.selectedId ? " selected" : ""}${confirmed?" raid-confirmed":""}${target?" guidance-target":""}${questStyle?" quest-pilot-step"+(questStyle.done?" done":"")+(questStyle.focused?" focused":"")+(questStyle.possible?" possible":"")+(questStyle.teamOnly?" team-only":""):""}`, transform: `translate(${p.x.toFixed(2)} ${p.y.toFixed(2)}) scale(${inverse})` });
  g.dataset.markerId = marker.id;
  g.dataset.category = marker.category;
  if(isRaidQuest(marker))g.classList.add("quest-confirmed");
  if (questStyle) {
    g.dataset.questId = String(marker.meta.questId);
    g.dataset.objectiveId = String(marker.meta.objectiveId);
    g.dataset.questStep = String(questStyle.number);
    g.dataset.questColor = questStyle.color;
  }
  if(confirmed){
    syncAnimation(g,target?1.4:1.8);
    const ring=svgEl("circle",{r:14,class:"rp-extraction-ring"});syncAnimation(ring,target?1.4:1.8);g.appendChild(ring);
  }

  if (marker.category === "player-position" || marker.category === "teammate") {
    const heading = RPMapCalibration.heading(marker.position,marker.rotation ?? marker.heading,project);
    g.setAttribute("transform", `translate(${p.x.toFixed(2)} ${p.y.toFixed(2)}) rotate(${heading.toFixed(2)}) scale(${inverse})`);
    g.appendChild(svgEl("circle", {r:15,class:"rp-player-halo"}));
    g.appendChild(svgEl("path", { d: "M 0 -11 L 7 8 L 0 4 L -7 8 Z", class: "rp-arrow" }));
  } else if (questStyle) {
    if(questStyle.focused)g.appendChild(svgEl("circle",{r:16,fill:"none",
      stroke:questStyle.color,"stroke-width":2,"stroke-dasharray":"3 2"}));
    g.appendChild(svgEl("circle",{r:12,fill:questStyle.color,class:"rp-step-ring"}));
    const number=svgEl("text",{x:0,y:1,class:"rp-step-number"});
    number.textContent=questStyle.done?"✓":questStyle.label;
    g.appendChild(number);
  } else if (cls === "hazard") {
    g.appendChild(svgEl("path", {d:"M 0 -7 L 7 6 L -7 6 Z",class:"rp-hazard-icon"}));
    g.appendChild(svgEl("path", {d:"M 0 -3 V 1 M 0 3 V 4",class:"rp-icon-line"}));
  } else {
    g.appendChild(svgEl("circle", { cx: 0, cy: 0, r: markerRadius(marker), class: "rp-dot" }));
    if (marker.category.startsWith("extract_")) {
      g.appendChild(svgEl("path", {d:marker.category === "extract_transit" ? "M -4 -2 H 4 L 2 -4 M 4 2 H -4 L -2 4" : "M -2 -4 H -4 V 4 H -2 M -1 0 H 4 M 1 -3 L 4 0 L 1 3",class:"rp-exit-icon"}));
    }
  }
  g.appendChild(svgEl("circle", {r:11,class:"rp-hit-target"}));

  const title = svgEl("title");
  title.textContent = questStyle
    ? questStyle.name + " · Étape " + questStyle.number + " : " + questStyle.description
      + (questStyle.possible ? " · zone à confirmer" : "")
      + (questStyle.done ? (questStyle.doneSource==="eft-local-log"?" · remis (journal EFT)":" · coché manuellement") : "")
      + (questStyle.teamOnly ? " · plan ami : "+questStyle.teamNames.join(", ") : "")
    : marker.name || marker.category;
  g.appendChild(title);
  g.addEventListener("click", ev => {
    ev.stopPropagation();
    if(questStyle && !questStyle.teamOnly) {
      state.questPilot.focus(questStyle.id);renderQuestPilotPanel();
    }
    selectMarker(marker);
  });
  g.addEventListener("pointerdown", ev => ev.stopPropagation());
  g.addEventListener("mouseenter", () => showMarkerDetails(marker));
  g.addEventListener("mouseleave", () => {
    const selected = visibleMarkers().find(m => m.id === state.selectedId);
    if (selected) showMarkerDetails(selected); else refs.details.hidden = true;
  });
  refs.overlay.appendChild(g);
  return {...screen,id:marker.id,name:marker.name,
    label:marker.category.startsWith("extract_") || marker.category === "teammate" || marker.category === "place-names" || marker.id === state.selectedId,
    priority:marker.id === state.selectedId ? 100 : marker.category === "teammate" ? 90 : confirmed ? 80 : marker.category === "extract_pmc" ? 30 : marker.category === "extract_shared" ? 25 : 10};
}

function renderLabels(points, view) {
  for (const label of RPMapDisplay.layoutLabels(points,view,state.labelMode)) {
    const x = (label.x-state.panX)/state.zoom, y = (label.y-state.panY)/state.zoom;
    const group = svgEl("g",{class:"rp-map-label",transform:`translate(${x} ${y}) scale(${1/state.zoom})`});
    group.appendChild(svgEl("rect",{x:0,y:0,width:label.width,height:label.height,rx:4}));
    const text = svgEl("text",{x:6,y:15}); text.textContent=label.text; group.appendChild(text);
    refs.overlay.appendChild(group);
  }
}

function renderBoundary() {
  // Real sniper polygons replace the old rectangular image extent.
  renderHazardZones("boundary",state.boundaryStatus);
  renderHazardZones("mine",state.mineStatus);
}

function syncAnimation(element,seconds){
  // The pose redraws at 10 Hz. Preserve animation phase across recreated SVG
  // nodes, otherwise every pose would restart the flash before it became dim.
  const now=typeof performance!=="undefined"?performance.now():Date.now();
  element.style.animationDelay=`-${(now/1000)%seconds}s`;
}
function isRaidExtraction(marker){return state.raidExtractions?.map===state.mapKey&&RPRaidTactics.matchesExtraction(marker,state.raidExtractions.entries);}
function isGuidanceTarget(marker){return state.guidanceTarget?.map===state.mapKey&&RPRaidTactics.matchesExtraction(marker,[state.guidanceTarget.entry]);}
function currentHazards(kind){
  const payload=kind==="mine"?state.minePayload:state.boundaryPayload;
  return payload?.map===state.mapKey?payload.zones:kind==="mine"?state.cachedMines:state.cachedBoundaries;
}
function updateTacticalProximity(){
  const p=state.player?{...state.player,...state.player.position}:null;
  for(const kind of ["boundary","mine"]){
    const key=kind==="mine"?"mineStatus":"boundaryStatus",cached=kind==="mine"?state.cachedMines:state.cachedBoundaries;
    const geometry=cached.length?cached:currentHazards(kind);
    const local=RPRaidTactics.proximity(p,geometry,state[key],kind);
    state[key]=RPRaidTactics.nativeOrLocal(p,local,kind);
  }
}
function renderHazardZones(kind,status){
  if(!state.enabled.has(kind==="mine"?"hazard_minefield":"boundary")&&!status.warning)return;
  const severity=status.critical?" critical":status.warning?" warning":"";
  const group=svgEl("g",{class:`rp-tactical-zones ${kind}${severity}`});
  for(const zone of currentHazards(kind)){
    if(zone.outline.length<3)continue;
    const points=zone.outline.map(p=>project({...p,y:zone.y}));
    if(points.some(p=>!Number.isFinite(p.x)||!Number.isFinite(p.y)))continue;
    const poly=svgEl("polygon",{class:"rp-tactical-zone",points:points.map(p=>`${p.x.toFixed(1)},${p.y.toFixed(1)}`).join(" ")});
    poly.dataset.hazardId=zone.id;
    if(status.warning||kind==="mine")syncAnimation(poly,status.critical ? .9 : 1.8);
    group.appendChild(poly);
  }
  refs.overlay.appendChild(group);
}
function renderTacticalWarning(){
  const messages=[],b=state.boundaryStatus,m=state.mineStatus;
  const dist=v=>Number.isFinite(v)?` · ≈${Math.round(v)} m`:"";
  if(b.warning)messages.push(b.critical?"LIMITE / SNIPERS — DEMI-TOUR":"LIMITE / SNIPERS"+dist(b.distance));
  if(m.warning)messages.push("MINES — DEMI-TOUR"+dist(m.distance));
  refs.tacticalWarning.hidden=!messages.length;
  refs.tacticalWarning.textContent=messages.join(" · ");
  refs.tacticalWarning.className=(b.critical||m.critical)?"critical":"warning";
}
function setRaidExtractions(p){
  state.raidExtractions={map:canonicalMapKey(p.map||state.mapKey),entries:RPRaidTactics.extractionEntries(p.extractions)};
  render();return applyRaidExtractions();
}
function applyRaidExtractions(){return state.markers.filter(isRaidExtraction).length;}
function clearRaidExtractions(){state.raidExtractions=null;render();}
function setHazards(p,kind){state[kind==="mine"?"minePayload":"boundaryPayload"]={map:canonicalMapKey(p.map),zones:RPRaidTactics.zones(p.zones),cleared:false};updateTacticalProximity();render();return true;}
// A native clear discards its prior-map overlay. Keep the current map's bundled
// polygons available while the native provider is fetching the next catalog.
function clearHazards(kind){state[kind==="mine"?"minePayload":"boundaryPayload"]=null;state[kind==="mine"?"mineStatus":"boundaryStatus"]={};render();return true;}
function setGuidanceTarget(p){state.guidance=p;state.guidanceTarget={map:canonicalMapKey(p.map||state.mapKey),entry:RPRaidTactics.extractionEntries([{id:p.id,labels:[p.name,...(p.aliases||[])]}])[0]||{id:"",labels:[]}};render();return state.markers.filter(isGuidanceTarget).length;}
function clearGuidanceTarget(){state.guidance=null;state.guidanceTarget=null;render();}
function resetRaid(){clearRaidQuests();state.questHudTarget="";state.learnedRoute=null;state.guidance=null;state.raidExtractions=null;state.guidanceTarget=null;state.boundaryPayload=null;state.mineStatus={};state.boundaryStatus={};state.player=null;state.lastPoseReceivedAt=null;state.teammates=[];clearFloorPose();window.RaidPilotFeatures?.reset();render();}

function visibleMarkers() {
  const base = [...state.markers, ...state.runtimeMarkers];
  if (state.player) base.push({ id:"__player", category:"player-position", name:"Vous", position:state.player.position || state.player, rotation:state.player.rotation ?? state.player.heading });
  for (const t of state.teammates || []) {
    base.push({ id:`__team_${t.id || t.name || Math.random()}`, category:"teammate", name:t.name || "Coéquipier", position:t.position || t, rotation:t.rotation ?? t.heading });
  }
  return base.filter(m => {
    if(!m.position)return false;
    if(m.meta?.questId) {
      if(state.questPilot) {
        if(!markerOnFloor(m))return false;
        const style=state.questPilot.markerVisual(m,state.questMapIds);
        if(!style)return false;
        // Hidden only for this player's completed objectives. A completed
        // objective on the same mission must not hide remaining objective pins.
        // Team-only markers remain personal to the teammate.
        return style.teamOnly || !style.done;
      }
      return isRaidQuest(m) && markerOnFloor(m);
    }
    if(m.category==="player-position"||isRaidExtraction(m))return true;
    if(m.category==="hazard_sniper"||m.category==="hazard_minefield")return false;
    if(state.raidExtractions?.map===state.mapKey&&m.category.startsWith("extract_")&&m.category!=="extract_transit")return false;
    return isEnabled(m)&&searchMatches(m)&&markerOnFloor(m);
  });
}

function render() {
  if (!state.mapMeta || !refs.overlay) return;
  refs.overlay.replaceChildren();
  refs.overlay.setAttribute("viewBox", `0 0 ${state.stageW} ${state.stageH}`);
  renderBoundary();
  renderLearnedRoute();
  const r = refs.viewport.getBoundingClientRect();
  const view = {panX:state.panX,panY:state.panY,zoom:state.zoom,width:r.width,height:r.height};
  const markers = visibleMarkers();
  const points = [];
  for (const marker of markers) {
    const out = makeOutline(marker);
    if (out) refs.overlay.appendChild(out);
  }
  // Player and squad are appended last, above every ordinary marker.
  markers.sort((a,b) => Number(["player-position","teammate"].includes(a.category))-Number(["player-position","teammate"].includes(b.category)));
  for (const marker of markers) { const p=renderMarker(marker,view); if(p) points.push(p); }
  renderLabels(points,view);
  for(const p of state.calibrationPoints){const g=svgEl("g",{transform:`translate(${p.u*state.stageW} ${p.v*state.stageH}) scale(${1/state.zoom})`});g.appendChild(svgEl("circle",{r:5,class:"rp-calibration-point"}));refs.overlay.appendChild(g);}
  updateCounts();
  updateHealth();
  renderQuestPilotHud();
  renderTacticalWarning();
  window.RaidPilotFeatures?.renderGuidance(state.guidance);
}

function updateCounts() {
  const total = state.markers.length + state.runtimeMarkers.length;
  const visible = visibleMarkers().length;
  refs.markerCount.textContent = `${visible}/${total}`;
}

function selectMarker(marker) {
  state.selectedId = marker.id;
  showMarkerDetails(marker);
  render();
}

function showMarkerDetails(marker) {
  refs.details.hidden = false;
  refs.details.innerHTML = "";
  const title = document.createElement("strong");
  title.textContent = marker.name || marker.category;
  const sub = document.createElement("div");
  sub.textContent = groupById(inferGroup(marker.category))?.name || (marker.category === "player-position" ? "Votre position" : "Point d'intérêt");
  refs.details.append(title, sub);
  if (marker.description) {
    const d = document.createElement("div");
    d.textContent = marker.description;
    refs.details.appendChild(d);
  }
}

function applyTransform() {
  state.zoom = Math.max(0.005, Math.min(8, state.zoom));
  // Resize the SVG image at its displayed resolution instead of magnifying
  // a small compositor texture. Symbols cancel this scale independently.
  refs.stage.style.width = `${state.stageW*state.zoom}px`;
  refs.stage.style.height = `${state.stageH*state.zoom}px`;
  refs.stage.style.transform = `translate(${state.panX}px, ${state.panY}px)`;
  // Display the user's relative zoom, consistent on large and small maps.
  refs.zoomValue.textContent = `${Math.round(state.zoomFactor * 100)}%`;
  render();
}

function calculateFitZoom() {
  const r = refs.viewport.getBoundingClientRect();
  if (!r.width || !r.height) return null;
  const f = state.mapStyle === "forge"
    ? (assetInfo().focus || [0,0,1,1]) : [0,0,1,1];
  const sx = r.width / Math.max(1, state.stageW * (f[2] - f[0]));
  const sy = r.height / Math.max(1, state.stageH * (f[3] - f[1]));
  return Math.max(0.005, Math.min(8, Math.min(sx,sy) * 0.96));
}

function fitStage({ resetPreference = false } = {}) {
  const r = refs.viewport.getBoundingClientRect();
  if (!r.width || !r.height) return;
  const fit = calculateFitZoom();
  if (fit === null) return;
  viewportSize = {width:r.width,height:r.height};
  if (resetPreference) {
    state.zoomFactor = 1;
    savePreferences();
    if (mapZoomSyncTimer !== null) clearTimeout(mapZoomSyncTimer);
    mapZoomSyncTimer = setTimeout(() => {
      mapZoomSyncTimer = null;
      postHost({type:"raidpilot-map-zoom-preference",payload:{factor:1}});
    }, 160);
  }
  // The same relative user zoom works on different image resolutions/maps.
  // Loading a map must NEVER overwrite the player's selected factor.
  state.zoom = Math.max(0.005, Math.min(8, fit * state.zoomFactor));
  const f = state.mapStyle === "forge"
    ? (assetInfo().focus || [0,0,1,1]) : [0,0,1,1];
  state.panX = r.width/2 - state.stageW*(f[0]+f[2])/2*state.zoom;
  state.panY = r.height/2 - state.stageH*(f[1]+f[3])/2*state.zoom;
  applyTransform();
}

function resizeViewport() {
  const r = refs.viewport.getBoundingClientRect();
  if (!r.width || !r.height) return;
  if (!viewportSize) { fitStage(); return; }
  // Opening the controls changes the map area. Keep its world center and
  // chosen zoom instead of fitting the whole map on every mode switch.
  state.panX += (r.width - viewportSize.width) / 2;
  state.panY += (r.height - viewportSize.height) / 2;
  viewportSize = {width:r.width,height:r.height};
  if(followingPlayer()&&state.player){centerPlayer();return;}
  applyTransform();
}

function centerPlayer() {
  if (!state.player?.position) return;
  const p = project(state.player.position);
  if(!Number.isFinite(p.x)||!Number.isFinite(p.y))return;
  const r = refs.viewport.getBoundingClientRect();
  state.panX = r.width/2 - p.x * state.zoom;
  state.panY = r.height/2 - p.y * state.zoom;
  applyTransform();
}

function setBaseAsset(url) {
  state.baseAsset = url || "";
  refs.baseMap.removeAttribute("src");
  refs.missingAsset.hidden = !!url;
  if (!url) {
    state.stageW = 1600; state.stageH = 900;
    refs.stage.style.width = `${state.stageW}px`;
    refs.stage.style.height = `${state.stageH}px`;
    refs.overlay.setAttribute("width", state.stageW);
    refs.overlay.setAttribute("height", state.stageH);
    fitStage(); render(); return;
  }
  refs.baseMap.src = url;
}

function canonicalMapKey(key) {
  const n = normalize(key);
  if (state.meta?.maps?.[n]) return n;
  for (const [id,m] of Object.entries(state.meta?.maps || {})) {
    if ((m.aliases || []).some(a => normalize(a) === n)) return id;
  }
  return n;
}

function rawMapsRoot() { return unwrap((state.catalogues[state.gameMode] || state.cache).maps) || {}; }
function rawItemsRoot() { return unwrap(state.cache.items) || {}; }
function rawTasksRoot() { return unwrap(state.catalogues[state.gameMode]?.tasks) || {}; }
function mapTranslations() { return unwrap(state.cache.mapsFr) || {}; }
function itemTranslations() { return unwrap(state.cache.itemsFr) || {}; }
function taskTranslations() { return unwrap(state.catalogues[state.gameMode]?.tasksFr) || {}; }

function translateFrom(dict, v) {
  return translated(v, dict);
}

function arrayOf(v) { return Array.isArray(v) ? v : v && typeof v === "object" ? Object.values(v) : []; }

function findApiMap(key) {
  const root = rawMapsRoot();
  const maps = arrayOf(root.maps);
  const meta = state.meta.maps[key];
  const candidates = new Set([normalize(key), ...(meta?.aliases || []).map(normalize)]);
  return maps.find(m => candidates.has(normalize(m.normalizedName)) || candidates.has(normalize(m.name)) || candidates.has(normalize(m.key))) || null;
}

function markerId(prefix, idx, obj) {
  return `${prefix}:${obj?.id || obj?.zoneName || idx}`;
}

function buildMarkersForMap(key) {
  const root = rawMapsRoot();
  const itemsRoot = rawItemsRoot();
  const tasksRoot = rawTasksRoot();
  const trM = mapTranslations(), trI = itemTranslations(), trT = taskTranslations();
  const m = findApiMap(key);
  state.apiMap = m;
  const out = [];
  let seq = 0;
  const add = (category, name, position, extra = {}) => {
    if (!position || position.x == null || position.z == null) return;
    out.push({
      id: extra.id || `${category}:${seq++}`,
      category,
      name: name || category,
      position: { x: finite(position.x), y: position.y == null ? undefined : finite(position.y), z: finite(position.z) },
      top: extra.top ?? position.top,
      bottom: extra.bottom ?? position.bottom,
      outline: extra.outline || null,
      description: extra.description || "",
      search: extra.search || [],
      extraLayers: extra.extraLayers || [],
      meta: extra.meta || {}
    });
  };
  if (!m) return out;

  for (const [i,e] of arrayOf(m.extracts).entries()) {
    const faction = normalize(e.faction || "shared").replace(/-/g,"_");
    add(`extract_${faction}`, translateFrom(trM,e.name), e.position, {id:markerId("extract",i,e),top:e.top,bottom:e.bottom,outline:e.outline,meta:{sourceId:e.id,labels:[e.name,translateFrom(trM,e.name)]}});
  }
  for (const [i,e] of arrayOf(m.transits).entries()) {
    add("extract_transit", translateFrom(trM,e.description || e.name || "Transit"), e.position, {id:markerId("transit",i,e),top:e.top,bottom:e.bottom,outline:e.outline});
  }

  for (const [i,s] of arrayOf(m.spawns).entries()) {
    const cats = (s.categories || []).map(normalize), sides = (s.sides || []).map(normalize);
    let type = "spawn_scav";
    if (cats.includes("boss")) type = "spawn_boss";
    else if ((cats.includes("player") || cats.includes("all") || cats.includes("botpmc")) && (sides.includes("pmc") || sides.includes("all"))) type = "spawn_pmc";
    else if (cats.includes("sniper") || (cats.includes("bot") && /snipe/i.test(s.zoneName||""))) type = "spawn_sniper_scav";
    add(type, type.replace("spawn_","").replaceAll("_"," "), s.position, {id:markerId("spawn",i,s),search:[s.zoneName || ""]});
  }
  for(const boss of arrayOf(m.bosses)){
    const def=root.mobs?.[boss.mob]||{},name=translateFrom(trM,def.name||boss.mob);
    const norm=normalize(def.normalizedName||boss.mob);
    const category=/cultist|sectant/.test(norm)?"spawn_cultist-priest":/rogue|exusec/.test(norm)?"spawn_rogue":/bloodhound/.test(norm)?"spawn_bloodhound":/black-div/.test(norm)?"spawn_black-div":/arena-fighter|^af$/.test(norm)?"spawn_af":"spawn_boss";
    for(const loc of arrayOf(boss.spawnLocations)){
      const positions=arrayOf(loc.positions);
      // A published spawn zone may reference known spawn points instead of coordinates.
      const points=positions.length?positions:arrayOf(m.spawns).filter(s=>s.zoneName===(loc.spawnKey||loc.name)).map(s=>s.position);
      for(const [i,p] of points.entries())add(category,name,p,{id:`boss:${boss.mob}:${loc.name}:${i}`,description:"Zone d'apparition possible, présence non confirmée.",search:[name,loc.name]});
    }
  }

  const globalContainers = root.lootContainers || {};
  for (const [i,c] of arrayOf(m.lootContainers).entries()) {
    const def = globalContainers[c.lootContainer] || c.lootContainer || {};
    const norm = normalize(def.normalizedName || def.name || c.lootContainer || "container");
    const cat = `container_${norm==="civilian-body"?"dead-civilian":norm}`;
    add(cat, translateFrom(trM,def.name || c.name || norm), c.position, {id:markerId("container",i,c)});
  }

  for (const [i,h] of arrayOf(m.hazards).entries()) {
    const type = normalize(h.hazardType || "hazard");
    const cat = type === "minefield" ? "hazard_minefield" : type === "sniper" ? "hazard_sniper" : `hazard_${type}`;
    add(cat, translateFrom(trM,h.name || type), h.position, {id:markerId("hazard",i,h),top:h.top,bottom:h.bottom,outline:h.outline});
  }
  for (const [i,h] of arrayOf(m.artillery?.zones).entries()) {
    add("hazard_mortar", "Mortier / artillerie", h.position, {id:markerId("mortar",i,h),top:h.top,bottom:h.bottom??h.botom,outline:h.outline});
  }

  for (const [i,sw] of arrayOf(m.switches).entries()) add("switch", translateFrom(trM,sw.name || "Interrupteur"), sw.position, {id:markerId("switch",i,sw)});
  for (const [i,l] of arrayOf(m.locks).entries()) add("lock", "Serrure / porte verrouillée", l.position, {id:markerId("lock",i,l)});
  const globalWeapons = root.stationaryWeapons || {};
  for (const [i,w] of arrayOf(m.stationaryWeapons).entries()) {
    const def = globalWeapons[w.stationaryWeapon] || {};
    add("stationarygun", translateFrom(trM,def.name || "Arme stationnaire"), w.position, {id:markerId("weapon",i,w)});
  }
  for (const [i,b] of arrayOf(m.btrStops).entries()) add("btr-stop", translateFrom(trM,b.name || "Arrêt BTR"), b, {id:markerId("btr",i,b)});

  const items = itemsRoot.items || {};
  const hb = itemsRoot.handbookCategories || {};
  for (const [i,l] of arrayOf(m.lootLoose).entries()) {
    const itemNames = [], layers = [];
    for (const id of l.items || []) {
      const item = items[id] || {};
      itemNames.push(translateFrom(trI,item.name || "Objet non répertorié"));
      const catIds = item.handbookCategories || [];
      for (const cid of catIds) {
        const c = hb[cid] || {};
        if (c.normalizedName) layers.push(`loose_${normalize(c.normalizedName)}`);
      }
    }
    add("loose_loot", itemNames.slice(0,3).join(", ") || "Loot au sol", l.position, {id:markerId("loose",i,l),extraLayers:[...new Set(layers)],search:itemNames});
  }

  const tasks = arrayOf(tasksRoot.tasks);
  const qItems = tasksRoot.questItems || {};
  const mapIds = new Set(arrayOf(root.maps).filter(x=>canonicalMapKey(x.normalizedName)===key).map(x=>x.id));
  for (const task of tasks) {
    const taskName = translateFrom(trT,task.name || task.id || "Quête");
    for (const obj of arrayOf(task.objectives)) {
      for (const loc of arrayOf(obj.possibleLocations)) {
        if (!mapIds.has(loc.map?.id||loc.map)) continue;
        for (const p of arrayOf(loc.positions)) {
          const qi = qItems[obj.questItem] || {};
          const itemName = translateFrom(trT,qi.name || obj.questItem || "Objet de quête");
          add("quest_item", `${taskName} — ${itemName}`, p, {id:`quest-item:${task.id}:${obj.id}:${seq++}`,meta:{questId:task.id,objectiveId:obj.id},description:translateFrom(trT,obj.description||""),search:[taskName,itemName,translateFrom(trT,obj.description || "")]});
        }
      }
      for (const z of arrayOf(obj.zones)) {
        if (!mapIds.has(z.map?.id||z.map)) continue;
        add("quest_objective", `${taskName} — ${translateFrom(trT,obj.description || "Objectif")}`, z.position, {id:`quest-zone:${task.id}:${obj.id}:${z.id || seq++}`,meta:{questId:task.id,objectiveId:obj.id},description:translateFrom(trT,obj.description||""),top:z.top,bottom:z.bottom,outline:z.outline,search:[taskName,translateFrom(trT,obj.description || "")]});
      }
    }
  }
  const seenQuestPoints=new Set();
  return out.filter(marker=>{
    if(!marker.meta.questId)return true;
    const id=JSON.stringify([marker.category,marker.meta.questId,marker.meta.objectiveId,marker.position]);
    if(seenQuestPoints.has(id))return false;seenQuestPoints.add(id);return true;
  });
}

function dynamicLayerNames(markers) {
  const names = new Map();
  for (const m of markers) {
    for (const c of [m.category, ...(m.extraLayers || [])]) {
      if (c && !layerKnown(c)) {
        const handbook=Object.values(rawItemsRoot().handbookCategories||{}).find(h=>`loose_${normalize(h.normalizedName)}`===c);
        names.set(c,handbook?translateFrom(itemTranslations(),handbook.name):m.name||c.replaceAll("_"," "));
      }
    }
  }
  return names;
}

function renderFilterPanel() {
  refs.filters.replaceChildren();
  const dynamic = dynamicLayerNames(state.markers);
  for (const group of state.catalog.groups) {
    const box = document.createElement("section");
    box.className = "filter-group";
    const h = document.createElement("button");
    h.type = "button"; h.className = "filter-group-title"; h.textContent = group.name;
    const body = document.createElement("div"); body.className = "filter-group-body";
    const layers = [...(group.layers || [])];
    for (const [id,name] of dynamic) if (inferGroup(id) === group.id) layers.push({id,name,default:group.default});
    const seen = new Set();
    for (const layer of layers) {
      if (seen.has(layer.id)) continue; seen.add(layer.id);
      const label = document.createElement("label"); label.className = "filter-row";
      const input = document.createElement("input"); input.type = "checkbox";
      input.dataset.layer = layer.id;
      const linked = Array.isArray(layer.linked) ? layer.linked : [];
      input.checked = state.enabled.has(layer.id) || linked.some(id => state.enabled.has(id));
      input.addEventListener("change", () => {
        const ids = [layer.id, ...linked];
        for (const id of ids) {
          if (input.checked) state.enabled.add(id); else state.enabled.delete(id);
        }
        savePreferences(); render(); refs.profile.value = "custom";
      });
      const span = document.createElement("span"); span.textContent = layer.name || layer.id;
      const count=layerCount(layer),badge=document.createElement("small");badge.className="layer-count";
      badge.textContent=layer.id==="player-position"||layer.id==="teammate"?"direct":layer.id==="place-names"?"fond HD":String(count);
      label.title=layer.id==="place-names"?"Noms imprimés dans l’image : affichage lié au fond HD.":count?`${count} points disponibles sur cette carte, même lorsque la case est décochée.`:"Aucun point géolocalisé dans le catalogue pour cette carte.";
      if(layer.always || layer.id==="place-names"){input.disabled=true;input.checked=layer.always||state.mapStyle==="forge";}
      label.append(input, span, badge); body.appendChild(label);
    }
    h.addEventListener("click", () => body.classList.toggle("collapsed"));
    box.append(h, body); refs.filters.appendChild(box);
  }
}

function layerCount(layer){
  if(layer.id==="boundary"||layer.id==="hazard_sniper")return state.cachedBoundaries.length;
  if(layer.id==="hazard_minefield")return state.cachedMines.length;
  const ids=[layer.id,...(layer.linked||[])];
  return state.markers.filter(m=>[m.category,...m.extraLayers].some(id=>ids.includes(id))).length;
}
function catalogueStatus(){
  return {maps:arrayOf(rawMapsRoot().maps).length>0,tasks:arrayOf(rawTasksRoot().tasks).length>0,items:arrayOf(rawItemsRoot().items).length>0};
}
function updateCatalogueStatus(){
  const c=catalogueStatus();state.cacheStatus=c.maps?(c.tasks&&c.items?"READY":"PARTIAL"):"MISSING";
  const el=$("#catalogue-status");if(el)el.textContent=`${state.gameMode==="pve"?"PvE":state.gameMode==="pvp-season"?"PvP saison":"PvP"} · ${c.tasks?arrayOf(rawTasksRoot().tasks).length+" quêtes locales":"Catalogue des quêtes indisponible"} · ${c.items?arrayOf(rawItemsRoot().items).length+" objets":"Objets indisponibles"}. Les objectifs sans lieu précis n’ont pas de marqueur. Les noms non traduits restent en anglais.`;
}
function setGameMode(mode){
  if(!["regular","pve","pvp-season"].includes(mode))return false;
  if(state.gameMode!==mode){state.gameMode=mode;state.raidQuests=null;state.questHudTarget="";state.learnedRoute=null;
    if(state.questPilot){ state.questPilot.setMode(mode); refreshQuestPilotCatalogue(); }
    window.RaidPilotFeatures?.clearQuest();if(state.mapKey){
    state.markers=buildMarkersForMap(state.mapKey);
    state.questMapIds=questMapIdsFor(state.mapKey);
    const hazards=arrayOf(state.apiMap?.hazards);
    state.cachedBoundaries=RPRaidTactics.zones(hazards.filter(h=>normalize(h.hazardType)==="sniper"));
    state.cachedMines=RPRaidTactics.zones(hazards.filter(h=>normalize(h.hazardType)==="minefield"));
    renderFilterPanel();updateTacticalProximity();
  }}
  try{localStorage.setItem("gameMode",JSON.stringify(mode));}catch(_){}
  const el=$("#game-mode");if(el)el.value=mode;
  updateCatalogueStatus();renderQuestPilotPanel();render();return !!state.catalogues[mode]?.tasks;
}
function questMapIdsFor(mapKey) {
  const ids=new Set([mapKey]);
  for(const m of arrayOf(rawMapsRoot().maps)){
    if(canonicalMapKey(m.normalizedName)===mapKey||canonicalMapKey(m.name)===mapKey){
      if(typeof m.id==="string")ids.add(m.id);
      if(typeof m.normalizedName==="string")ids.add(m.normalizedName);
    }
  }
  return ids;
}
function refreshQuestPilotCatalogue(){
  const planner=state.questPilot;
  if(!planner)return;
  const root=rawTasksRoot();
  const itemRoot=rawItemsRoot();
  planner.setCatalog(arrayOf(root.tasks),taskTranslations(),
    root.questItems || itemRoot.items || {},state.cache.itemsFr ? unwrap(state.cache.itemsFr) : {});
  renderQuestPilotPanel();
}
function qpText(parent,tag,text,className){
  const el=document.createElement(tag);
  if(className)el.className=className;
  el.textContent=String(text||"");
  parent.appendChild(el);return el;
}
function qpEmpty(parent,message){qpText(parent,"div",message,"qp-empty");}
function renderQuestPilotPanel(){
  const planner=state.questPilot;
  if(!planner||!refs.questList||!refs.questSteps||!refs.questPrep)return;
  refs.questList.replaceChildren();
  refs.questSteps.replaceChildren();
  refs.questPrep.replaceChildren();
  const scanned=state.raidQuests?.ids||new Set();
  const active=[...scanned].filter(id=>planner.isActive(id)).length;
  const persistent=planner.state.active.length;
  const manualCompleted=planner.state.completedQuests.length;
  const confirmed=Object.values(planner.state.evidence||{})
    .filter(e=>e.status==="completed").length;
  refs.questSummary.textContent=persistent+" quête(s) sauvegardée(s) · "
    +active+" retrouvée(s) dans le scan du raid · "
    +manualCompleted+" marquée(s) faite(s) · "
    +confirmed+" confirmée(s) par le journal EFT";
  const search=state.questSearch;
  const showAllButton=document.getElementById("questpilot-show-all");
  if(showAllButton){
    showAllButton.textContent=state.questShowAll
      ?"Afficher seulement mes quêtes":"Voir toutes les quêtes disponibles";
    showAllButton.setAttribute("aria-pressed",state.questShowAll?"true":"false");
    showAllButton.onclick=()=>{
      state.questShowAll=!state.questShowAll;
      renderQuestPilotPanel();
    };
  }
  // Default to selected missions; explicitly searching or toggling discovery
  // reveals the wider catalogue without overwhelming the player.
  const candidates=(state.questShowAll&&!search
    ?planner.tasks.slice():planner.searchTasks(state.questMapIds,search))
    .filter(q=>!!search||state.questShowAll
      ||scanned.has(String(q.id).toLowerCase())||planner.isActive(q.id));
  const visible=candidates.slice(0,search?80:65);
  if(!visible.length)qpEmpty(refs.questList,
    search?"Aucune quête ne correspond à ta recherche."
      :"Aucune quête suivie. Active une quête ou lance TAB → TÂCHES → F8.");
  for(const quest of visible){
    const selected=planner.isActive(quest.id);
    const row=document.createElement("div");
    row.className="qp-task"+(selected?"":" inactive")
      +(planner.isQuestMarkedDone(quest.id)?" completed":"");
    const tick=document.createElement("input");tick.type="checkbox";tick.checked=selected;
    tick.setAttribute("aria-label","Activer "+quest.name);
    tick.addEventListener("change",()=>{
      planner.toggle(quest.id,tick.checked);
      renderQuestPilotPanel();render();
    });
    row.appendChild(tick);
    const color=document.createElement("span");color.className="qp-swatch";color.style.backgroundColor=quest.color;
    row.appendChild(color);
    const name=document.createElement("span");name.className="qp-name";name.textContent=quest.name;
    row.appendChild(name);
    const count=quest.objectives.length;
    const logStatus=planner.statusFor(quest.id);
    const statusLabel=logStatus?.status==="completed"?"✓ Journal EFT"
      :logStatus?.status==="failed"?"Échec · journal"
      :logStatus?.status==="active"?"Acceptée · journal":null;
    const countLabel=count?count+" étape(s) · "+(quest.mappedCount||0)+" repère(s)"
      :"aucun objectif public";
    qpText(row,"small",planner.isQuestMarkedDone(quest.id)
      ?"✓ Faite selon toi":statusLabel||countLabel,"qp-count");
    // Full-quest completion is ALWAYS a manual decision, distinct from
    // an unverified location/position and from EFT's authoritative log.
    const finish=document.createElement("button");finish.type="button";
    finish.className="qp-quest-finish";
    const isDone=planner.isQuestMarkedDone(quest.id);
    finish.textContent=logStatus?.status==="completed"?"✓ Journal EFT"
      :isDone?"↶ Réactiver":"✓ Fait";
    finish.disabled=logStatus?.status==="completed";
    finish.setAttribute("aria-label",(isDone?"Réactiver ":"Marquer la quête faite : ")+quest.name);
    finish.setAttribute("aria-pressed",isDone?"true":"false");
    finish.addEventListener("click",()=>{
      planner.toggleQuestCompleted(quest.id,!planner.isQuestMarkedDone(quest.id));
      state.questHudTarget="";
      renderQuestPilotPanel();render();
    });
    row.appendChild(finish);
    refs.questList.appendChild(row);
  }
  if(candidates.length>visible.length)
    qpText(refs.questList,"small",String(candidates.length-visible.length)+" autres quêtes : affine ta recherche.","qp-empty");

  const completedSteps=planner.stepsForMap(state.questMapIds).filter(step=>step.done);
  if(refs.questDoneList){
    refs.questDoneList.replaceChildren();
    if(!completedSteps.length)qpEmpty(refs.questDoneList,"Aucune étape terminée sur cette carte.");
    for(const step of completedSteps.slice(0,40)){
      const row=document.createElement("div");row.className="qp-step done";
      qpText(row,"strong",step.questName+" · "+step.number+" — "+step.name);
      const doneFromEft=step.doneSource==="eft-local-log";
      const undo=document.createElement("button");undo.type="button";
      undo.textContent=doneFromEft?"✓ Confirmé par EFT":"↶ Réactiver ce point";
      undo.disabled=doneFromEft;
      undo.addEventListener("click",()=>{
        planner.toggleDone(step.id,false);renderQuestPilotPanel();render();
      });
      row.appendChild(undo);refs.questDoneList.appendChild(row);
    }
  }
  const selectedSteps=planner.suggested(state.questMapIds,state.player);
  if(selectedSteps.length){
    const step=selectedSteps[0];
    const quick=document.createElement("div");quick.className="qp-step qp-quick-next";
    qpText(quick,"strong","➡️ PROCHAIN OBJECTIF · "+step.questName);
    qpText(quick,"span",step.name);
    const controls=document.createElement("div");controls.className="qp-quick-controls";
    const target=document.createElement("button");target.type="button";
    target.textContent="🎯 Suivre sur la carte";
    target.addEventListener("click",()=>{planner.focus(step.id);renderQuestPilotPanel();render();});
    controls.appendChild(target);
    const complete=document.createElement("button");complete.type="button";
    complete.textContent="✓ Fait";
    complete.addEventListener("click",()=>{
      planner.toggleDone(step.id,true);renderQuestPilotPanel();render();
    });
    controls.appendChild(complete);
    quick.appendChild(controls);refs.questSteps.appendChild(quick);
  }
  if(!selectedSteps.length)qpEmpty(refs.questSteps,
    "Aucune étape active localisée ici. Les quêtes sans point public restent dans la liste.");
  for(const step of selectedSteps.slice(0,10)){
    const row=document.createElement("div");row.className="qp-step"+(step.done?" done":"");
    const tick=document.createElement("input");tick.type="checkbox";tick.checked=step.done;
    tick.disabled=step.doneSource==="eft-local-log";
    tick.setAttribute("aria-label","Étape "+step.number+" terminée selon moi");
    tick.addEventListener("change",()=>{planner.toggleDone(step.id,tick.checked);renderQuestPilotPanel();render();});
    row.appendChild(tick);
    const badge=document.createElement("span");badge.className="qp-step-badge";
    badge.textContent=String(step.number);badge.style.backgroundColor=step.color;row.appendChild(badge);
    const button=document.createElement("button");button.type="button";
    button.className=planner.state.focused===step.id?"selected":"";
    const title=qpText(button,"strong",step.questName+" · Étape "+step.number+"/"+step.objectiveCount);
    qpText(button,"span",step.name);
    const place=step.alternative?" · emplacements possibles, étape unique":"";
    qpText(button,"small",(step.optional?"Facultatif · ":"")
      +(step.done?(step.doneSource==="eft-local-log"?"Quête remise · journal EFT · ":"Faite selon toi · "):"")
      +step.needs.join(" · ")+place);
    button.addEventListener("click",()=>{planner.focus(step.id);renderQuestPilotPanel();render();});
    row.appendChild(button);
    const complete=document.createElement("button");complete.type="button";
    complete.className="qp-done-action";complete.textContent="✓ Fait";
    complete.setAttribute("aria-label","Marquer l'étape "+step.number+" comme faite");
    complete.addEventListener("click",()=>{planner.toggleDone(step.id,true);renderQuestPilotPanel();render();});
    row.appendChild(complete);
    refs.questSteps.appendChild(row);
  }
  if(selectedSteps.length>10)
    qpText(refs.questSteps,"small",String(selectedSteps.length-10)+" autres objectifs : décoche les étapes terminées ou sélectionne la priorité.","qp-empty");
  renderQuestPilotUnmapped(planner);
  renderQuestPilotPreparation(planner);
  renderQuestPilotSharedSteps(planner);
  if(refs.questTeamPlans){
    refs.questTeamPlans.replaceChildren();
    const teams=Object.entries(planner.state.sharedPlans||{});
    if(!teams.length)qpEmpty(refs.questTeamPlans,"Aucun plan ami importé.");
    for(const [name,plan] of teams){
      const row=document.createElement("div");row.className="qp-team-item";
      qpText(row,"strong",name+" · "+plan.active.length+" quête(s) · "+plan.done.length+" étape(s) cochée(s) (importé)");
      const remove=document.createElement("button");remove.type="button";
      remove.textContent="Retirer";
      remove.addEventListener("click",()=>{planner.removeShare(name);renderQuestPilotPanel();render();});
      row.appendChild(remove);refs.questTeamPlans.appendChild(row);
    }
    const peers=Object.values(planner.livePlans||{}).filter(p=>p.mode===planner.mode
      && Date.now()-p.receivedAt<90000);
    if(peers.length){
      qpText(refs.questTeamPlans,"small","Escouade en direct · progression individuelle, non confirmée par ton jeu.","qp-subtle");
      for(const peer of peers){
        const row=document.createElement("div");row.className="qp-team-item";
        qpText(row,"strong",peer.name+" (direct) · "+peer.quests.length+
          " quête(s) · "+peer.done.length+" étape(s) cochée(s)");
        refs.questTeamPlans.appendChild(row);
      }
    }
  }
}
// Keep optional advanced details inside 'Mes quêtes': the combat overlay stays compact.
function renderQuestPilotUnmapped(planner){
  const root=refs.questOther;if(!root)return;root.replaceChildren();
  const steps=planner.unmappedSteps();
  if(!steps.length)qpEmpty(root,"Aucun autre objectif sans position publique.");
  for(const step of steps.slice(0,30)){
    const row=document.createElement("label");row.className="qp-step qp-unmapped"+(step.done?" done":"");
    const checkbox=document.createElement("input");checkbox.type="checkbox";
    checkbox.checked=step.done;checkbox.disabled=step.doneSource==="eft-local-log";
    checkbox.setAttribute("aria-label","Objectif sans point "+step.name+" · fait selon moi");
    checkbox.addEventListener("change",()=>{
      planner.toggleDone(step.id,checkbox.checked);renderQuestPilotPanel();render();
    });
    row.appendChild(checkbox);
    const badge=document.createElement("span");badge.className="qp-unmapped-tag";
    badge.textContent="—";row.appendChild(badge);
    const info=document.createElement("div");
    qpText(info,"strong",step.questName+" · Étape "+step.allNumber+"/"+step.allObjectiveCount);
    qpText(info,"span",step.name);
    qpText(info,"small",step.doneSource==="eft-local-log"?"Quête remise · journal EFT":
      step.done?"Coché manuellement":"Pas de point géolocalisé public");
    row.appendChild(info);root.appendChild(row);
  }
  if(steps.length>30)qpText(root,"small",String(steps.length-30)+" étapes supplémentaires","qp-empty");
}
function renderQuestPilotPreparation(planner){
  const root=refs.questPrep;if(!root)return;root.replaceChildren();
  const requirements=planner.preparationForMap(state.questMapIds);
  const missing=requirements.filter(item=>!item.packed).length;
  qpText(root,"small",requirements.length
    ?missing+" condition(s) à vérifier avant de partir · inventaire non consulté"
    :"Aucune condition publiée pour cette carte.","qp-subtle");
  for(const requirement of requirements.slice(0,35)){
    const row=document.createElement("label");row.className="qp-prep";
    const checkbox=document.createElement("input");checkbox.type="checkbox";
    checkbox.checked=requirement.packed;
    checkbox.addEventListener("change",()=>{
      planner.togglePacked(requirement.id,checkbox.checked);
      renderQuestPilotPanel();
    });
    row.appendChild(checkbox);
    qpText(row,"span",requirement.questName+" : "+requirement.need);
    root.appendChild(row);
  }
  if(requirements.length>35)qpText(root,"small",
    String(requirements.length-35)+" autres préparations","qp-empty");
}
function renderQuestPilotSharedSteps(planner){
  const root=refs.questSharedSteps;if(!root)return;root.replaceChildren();
  const steps=planner.sharedStepsForMap(state.questMapIds);
  if(!steps.length)qpEmpty(root,"Aucun plan d'ami sur cette carte. Ta progression reste personnelle.");
  for(const step of steps.slice(0,18)){
    const row=document.createElement("div");row.className="qp-team-step";
    const dot=document.createElement("span");dot.className="qp-swatch";
    dot.style.backgroundColor=step.color;row.appendChild(dot);
    const info=document.createElement("div");
    qpText(info,"strong",step.questName+" · "+step.number+"/"+step.objectiveCount
      +(step.mine?" · aussi ta quête":" · équipe seulement"));
    const statuses=step.members.map(m=>m.name+(m.done?" ✓ coché":" · à faire"));
    qpText(info,"small",statuses.join(" · ")||"Aucune progression de coéquipier disponible");
    row.appendChild(info);root.appendChild(row);
  }
  if(steps.length>18)qpText(root,"small",
    String(steps.length-18)+" autres étapes d'escouade","qp-empty");
}
function nearbyQuestStep() {
  const planner=state.questPilot;
  const fresh=state.player&&Number.isFinite(state.lastPoseReceivedAt)
    && Date.now()-state.lastPoseReceivedAt<=poseStaleAfterMs;
  const uncertainty=Number(state.player?.uncertainty);
  const reliable=!!fresh && Number.isFinite(uncertainty)
    && uncertainty>=0 && uncertainty<=6;
  if(!planner||!reliable)return null;
  return planner.nearbyForMap(state.questMapIds,{
    ...state.player,positionReliable:true
  },state.questHudTarget);
}
function renderQuestPilotHud(){
  if(!refs.questHud)return;
  // QuestPilot is contextual: show ONLY once the player's fresh, reliable
  // position is within a published objective zone (or its small perimeter).
  const next=nearbyQuestStep();
  if(!next){state.questHudTarget="";refs.questHud.hidden=true;return;}
  state.questHudTarget=next.id;
  refs.questHud.hidden=false;
  refs.questHudColor.style.backgroundColor=next.color;
  refs.questHudTitle.textContent=next.questName+" · Étape "
    +next.number+"/"+next.objectiveCount;
  const estimate=" ≈"+(Math.round(next.distance/5)*5)+" m de la zone";
  refs.questHudDetail.textContent=next.name
    +(next.alternative?" · emplacement possible":" · "+estimate)
    +(next.optional?" · facultatif":"");
}

function questPilotClosePanel(){
  if(!refs.questPanel)return false;
  refs.questPanel.classList.add("hidden");
  refs.questsBtn?.setAttribute("aria-expanded","false");
  if(refs.questsBtn)refs.questsBtn.textContent="🎯 Afficher QuestPilot";
  return true;
}
function questPilotOpenPanel(){
  if(!refs.questPanel)return false;
  refs.questPanel.classList.remove("hidden");
  refs.questsBtn?.setAttribute("aria-expanded","true");
  if(refs.questsBtn)refs.questsBtn.textContent="🎯 Masquer QuestPilot";
  renderQuestPilotPanel();
  return true;
}
function questPilotCompleteFocused(){
  const planner=state.questPilot;
  const next=nearbyQuestStep();
  // No blind completion of a far-away quest from the HUD shortcut.
  // Being nearby alone never marks anything done.
  if(!planner||!next||next.done||next.doneSource==="eft-local-log")return false;
  planner.toggleDone(next.id,true);
  state.questHudTarget="";
  renderQuestPilotPanel();render();
  return true;
}

function setLiveTeamQuestPlan(payload){
  if(!state.questPilot)return false;
  if(!state.questPilot.setLivePlan(payload))return false;
  renderQuestPilotPanel();render();return true;
}
function clearLiveTeamQuestPlan(id){
  if(!state.questPilot || !state.questPilot.clearLivePlan(id))return false;
  renderQuestPilotPanel();render();return true;
}
function clearAllLiveTeamQuestPlans(){
  if(!state.questPilot)return false;
  state.questPilot.clearLivePlans();
  renderQuestPilotPanel();render();return true;
}
function isRaidQuest(marker){return state.raidQuests?.map===state.mapKey&&state.raidQuests.ids.has(String(marker.meta?.questId||"").toLowerCase());}
function applyRaidQuests(){return new Set(state.markers.filter(isRaidQuest).map(m=>m.meta.questId)).size;}
function applyQuestLogEvents(events){
  if(!Array.isArray(events)||events.length>64)return 0;
  let accepted=0,changed=false;
  for(const event of events){
    if(!["regular","pve","pvp-season"].includes(event?.mode))continue;
    const same=event.mode===state.gameMode;
    const planner=same?state.questPilot:new RPQuestPilot.Planner(window.localStorage,event.mode);
    if(!planner)continue;
    if(planner.recordEvidence(event)){
      accepted++;
      if(same)changed=true;
    }
  }
  if(changed){renderQuestPilotPanel();render();}
  return accepted;
}
function setRaidQuests(p){
  if(!p||!Array.isArray(p.quests))return 0;
  if(p.gameMode)setGameMode(p.gameMode);
  const recognized=p.quests.map(q=>String(q.id||"").trim().toLowerCase()).filter(Boolean);
  const scannedMap=canonicalMapKey(p.map||state.mapKey);
  const ids=new Set(recognized);
  state.raidQuests={map:scannedMap,ids};
  if(state.questPilot){
    // OCR is discovery, NOT a new quest profile. Never reset manually
    // completed steps, completed quests or previously selected missions.
    state.questPilot.recognize([...ids]);
  }
  renderQuestPilotPanel();render();return applyRaidQuests();
}
function clearRaidQuests(){
  // Raid lifecycle is transient; persistent planning and completion are not.
  state.raidQuests=null;
  state.questHudTarget="";
  renderQuestPilotPanel();render();
}

function setLearnedRoute(p){
  if(p.kind!=="learned"||!["high","medium"].includes(p.confidence)||!Array.isArray(p.points)||p.points.length<2||p.points.length>4096||p.points.some(q=>![q?.x,q?.y,q?.z].every(n=>typeof n==="number"&&Number.isFinite(n)&&Math.abs(n)<=20000)))return false;
  const next=p.nextIndex??0;if(!Number.isInteger(next)||next<0||next>=p.points.length)return false;
  state.learnedRoute={...p,nextIndex:next,map:state.mapKey};render();return true;
}
function clearLearnedRoute(){state.learnedRoute=null;render();}
function renderLearnedRoute(){
  const r=state.learnedRoute;if(!r||r.map!==state.mapKey)return;
  const points=r.points.map(project);if(points.some(p=>!Number.isFinite(p.x)||!Number.isFinite(p.y)))return;
  const path=points.map(p=>`${p.x},${p.y}`).join(" ");
  refs.overlay.appendChild(svgEl("polyline",{points:path,class:`rp-learned-route ${r.confidence}`}));
  const ahead=points.slice(Math.max(0,r.nextIndex-1)).map(p=>`${p.x},${p.y}`).join(" ");
  refs.overlay.appendChild(svgEl("polyline",{points:ahead,class:"rp-learned-route ahead"}));
}
function initializeFloorCatalogue(){
  floorTracker.setCatalog({map:state.mapKey,base:RaidPilotFloors.regions([{height:state.mapMeta.baseHeight||[-1000,1000]}]),levels:(state.mapMeta.floors||[]).map(f=>({id:f.name,name:f.name,regions:RaidPilotFloors.regions(f.extents)}))});
  state.automaticFloor=null;state.manualFloor=false;
}
function setFloorPose(p){
  if(canonicalMapKey(p.map)!==state.mapKey)return {status:"loading"};
  const result=floorTracker.update({...p,map:state.mapKey});state.automaticFloor=result;
  if(p.enabled&&!state.manualFloor&&floorCatalogCoverage(state.mapMeta).total>0
    &&state.floor!=="AUTO")setFloor("AUTO",true);
  render();return result;
}
function clearFloorPose(){state.automaticFloor=null;if(state.mapMeta)initializeFloorCatalogue();render();}

function applyProfile(id) {
  const p = state.catalog.profiles[id];
  if (!p) return;
  state.enabled.clear();
  if (p.all) {
    for (const g of state.catalog.groups) for (const l of g.layers || []) state.enabled.add(l.id);
    for (const id2 of dynamicLayerNames(state.markers).keys()) state.enabled.add(id2);
  } else {
    for (const x of p.enabled || []) state.enabled.add(x);
  }
  savePreferences(); renderFilterPanel(); render();
}

function floorCatalogCoverage(meta) {
  const floors=Array.isArray(meta?.floors)?meta.floors:[];
  const valid=floors.filter(f=>typeof f.name==="string"
    && RaidPilotFloors.regions(f.extents).length>0);
  const local=valid.filter(f=>RaidPilotFloors.regions(f.extents)
    .some(region=>Array.isArray(region.boxes)&&region.boxes.length>0));
  return {total:valid.length,localized:local.length,
    uncovered:valid.length===0,
    description:valid.length===0
      ?"Aucun étage documenté : affichage BASE, sans position verticale inventée."
      :valid.length+" niveaux documentés · "+local.length
        +" avec limites de bâtiment · sélection manuelle disponible"};
}
function updateFloorOptions() {
  refs.floor.replaceChildren();
  const coverage=floorCatalogCoverage(state.mapMeta);
  const options=[["BASE","Base / RDC"]];
  if(!coverage.uncovered)options.push(["AUTO","AUTO (hauteur vérifiée)"]);
  for(const [value,label] of options){
    const o=document.createElement("option");o.value=value;o.textContent=label;
    refs.floor.appendChild(o);
  }
  for(const f of state.mapMeta?.floors||[]){
    if(!RaidPilotFloors.regions(f.extents).length)continue;
    const o=document.createElement("option");o.value=f.name;o.textContent=f.name;
    refs.floor.appendChild(o);
  }
  refs.floor.value=[...refs.floor.options].some(o=>o.value===state.floor)?state.floor:"BASE";
  if(refs.floorCoverage){
    refs.floorCoverage.textContent=coverage.description;
    refs.floorCoverage.title=coverage.description;
  }
}

function resolveAssetForMap(key) {
  const v = state.assets?.maps?.[key];
  if (!v) return "";
  return typeof v === "string" ? v : (state.mapStyle === "tactical" ? v.tacticalPath : v.path) || "";
}

function setMap(key, { allowUncalibratedForge = false } = {}) {
  const k = canonicalMapKey(key);
  if (!state.meta?.maps?.[k]) {
    refs.mapName.textContent = `Carte inconnue: ${key}`;
    return false;
  }
  if (state.mapKey !== k) { state.player = null; state.lastPoseReceivedAt = null; state.teammates = []; state.runtimeMarkers = []; state.selectedId = null; refs.details.hidden = true; }
  if(state.mapKey!==k){
    state.questHudTarget="";
    if(state.raidQuests?.map!==k)state.raidQuests=null;state.learnedRoute=null;state.guidance=null;window.RaidPilotFeatures?.reset();
    if(state.raidExtractions?.map!==k)state.raidExtractions=null;
    if(state.guidanceTarget?.map!==k)state.guidanceTarget=null;
    if(state.boundaryPayload?.map!==k)state.boundaryPayload=null;
    if(state.minePayload?.map!==k)state.minePayload=null;
    state.boundaryStatus={};state.mineStatus={};
  }
  state.mapKey = k; state.mapMeta = state.meta.maps[k]; state.floor = "BASE";
  state.questMapIds = questMapIdsFor(k);
  state.mapStyle = state.preferredMapStyle;
  state.uncalibratedFallback = false;
  initializeFloorCatalogue();
  cancelCalibration();state.calibrationPoints=[];refs.calibrationPanel.hidden=true;loadCalibration();
  // HD illustrations without an affine fit do not support reliable marker
  // projection. Show the schematic/tactical coordinates instead of hiding
  // the player. The explicit manual HD option remains available for calage.
  if (state.mapStyle === "forge" && !allowUncalibratedForge
      && !activeCalibration(state.player?.position)) {
    state.mapStyle = "tactical";
    state.uncalibratedFallback = true;
  }
  if (refs.mapStyle) refs.mapStyle.value = state.mapStyle;
  refs.mapName.textContent = k;
  refs.credit.textContent=state.mapStyle==="forge"?`${assetInfo().author || ""} · TarkovForge`:'tarkov.dev · carte locale';
  state.markers = buildMarkersForMap(k);
  const hazards=arrayOf(state.apiMap?.hazards);
  state.cachedBoundaries=RPRaidTactics.zones(hazards.filter(h=>normalize(h.hazardType)==="sniper"));
  state.cachedMines=RPRaidTactics.zones(hazards.filter(h=>normalize(h.hazardType)==="minefield"));
  updateTacticalProximity();
  updateFloorOptions(); renderFilterPanel();renderQuestPilotPanel();
  setBaseAsset(resolveAssetForMap(k));
  render();
  return true;
}

function setPlayer(p) {
  if (!p) { state.player = null; state.lastPoseReceivedAt = null; updateTacticalProximity();render(); return; }
  const pos = p.position || p;
  if (pos.x == null || pos.z == null || !Number.isFinite(Number(pos.x)) || !Number.isFinite(Number(pos.z))) return false;
  if (p.map && canonicalMapKey(p.map) !== state.mapKey) setMap(p.map);
  const previousCalibrationKey=calibrationKey();
  state.player = { map: state.mapKey, position: {x:Number(pos.x),y:pos.y,z:Number(pos.z)}, rotation:p.rotation ?? p.yaw ?? p.heading,
    uncertainty:p.uncertainty,boundaryWarning:p.boundaryWarning,boundaryCritical:p.boundaryCritical,boundaryDistance:p.boundaryDistance,mineAlert:p.mineAlert,mineDistance:p.mineDistance };
  state.lastPoseReceivedAt = Date.now();
  if(calibrationKey()!==previousCalibrationKey)loadCalibration();
  updateTacticalProximity();
  if(followingPlayer())centerPlayer();
  render();
  return true;
}

function setTeam(team) { state.teammates = Array.isArray(team) ? team : []; state.beaconStatus = team ? "READY" : "LOCAL"; render(); }
function setFloor(floor,automatic=false) { state.floor = floor || "BASE";if(!automatic)state.manualFloor=state.floor!=="AUTO"; cancelCalibration();state.calibrationPoints=[];loadCalibration();if ([...refs.floor.options].some(o=>o.value===state.floor)) refs.floor.value=state.floor; render(); }
function setRuntimeMarkers(markers) { state.runtimeMarkers = Array.isArray(markers) ? markers : []; renderFilterPanel(); render(); }
function setCalibration(c) { state.calibration = RPMapCalibration.valid(c) ? c : null; render(); }

function applySnapshot(s) {
  if (!s || typeof s !== "object") return;
  if (s.map || s.mapKey) setMap(s.map || s.mapKey);
  if (s.baseAsset) setBaseAsset(s.baseAsset);
  if (s.calibration) setCalibration(s.calibration);
  if (s.player || s.position) setPlayer(s.player || {position:s.position,rotation:s.rotation});
  if (s.teammates || s.team) setTeam(s.teammates || s.team);
  if (s.markers) setRuntimeMarkers(s.markers);
  if (s.floor || s.level) setFloor(s.floor || s.level);
}

function health() {
  const positionAgeMs = state.player && Number.isFinite(state.lastPoseReceivedAt)
    ? Math.max(0, Date.now() - state.lastPoseReceivedAt) : null;
  const positionStale = positionAgeMs !== null && positionAgeMs > poseStaleAfterMs;
  return {
    version:"0.14.2-localfirst-hotfix11",
    map: state.mapKey ? "READY" : "WAIT",
    data: state.cacheStatus,
    overlay: "READY",
    position: !state.player ? "WAIT" : (positionStale ? "STALE" : "TRACKING"),
    positionStale, positionAgeMs,
    beacon: state.beaconStatus,
    teammateCount: state.teammates.length,
    markerCount: state.markers.length,
    floor: state.floor,
    mapKey: state.mapKey,
    visibleCount: visibleMarkers().length,
    initialized: state.initialized
    ,gameMode:state.gameMode,questCount:state.markers.filter(m=>m.meta.questId).length,confirmedQuestCount:applyRaidQuests(),catalogues:catalogueStatus(),floorStatus:state.automaticFloor?.status||"waiting_anchor"
    ,mapStyle:state.mapStyle,zoom:state.zoom,zoomFactor:state.zoomFactor,calibration:state.mapStyle!=="forge"?"TACTICAL":state.calibration&&activeCalibration(state.player?.position)===state.calibration?"PERSONAL":activeCalibration(state.player?.position)?"INITIAL_ESTIMATE":"REQUIRED"
    ,calibrationPlane:activeCalibration(state.player?.position)?.scope||null
    ,raidExtractionCount:applyRaidExtractions(),boundaryWarning:state.boundaryStatus.warning===true,boundaryCritical:state.boundaryStatus.critical===true,camera:followingPlayer()?"FOLLOW":"EXPLORE"
  };
}

function updateHealth() {
  const h = health();
  const cal=h.calibration==="REQUIRED"?" · Caler le pion sur cette vue":h.calibration==="INITIAL_ESTIMATE"?" · Position approximative : calage à affiner":"";
  const positionLabel = h.position === "STALE"
    ? `Signal de position interrompu depuis ${Math.ceil(h.positionAgeMs / 1000)} s`
    : h.position === "TRACKING" ? "Position reçue" : "En attente de la position EFT";
  refs.overlay.classList.toggle("rp-pose-stale", h.position === "STALE");
  const staleBanner = $("#position-stale");
  if (staleBanner) staleBanner.hidden = h.position !== "STALE";
  refs.health.textContent = state.pendingCalibration ? "Clique sur la carte à l'emplacement choisi. Échap pour annuler." : `HOTFIX11 · ${positionLabel}${cal}${h.data === "MISSING" ? " · Données manquantes" : ""}`;
  if(h.data==="PARTIAL")refs.health.textContent+=" · Catalogue partiel";
  if(state.uncalibratedFallback)refs.health.textContent+=" · Fond HD non calibré : vue schématique";
  if(assetInfo().floorCalibrations?.length&&h.calibrationPlane)refs.health.textContent+=` · Plan : ${h.calibrationPlane}`;
  if(state.floor==="AUTO"&&state.automaticFloor)refs.health.textContent+=` · ${state.automaticFloor.name} (${state.automaticFloor.detail})`;
  refs.health.className = h.map === "READY" && h.data !== "MISSING" && h.position === "TRACKING" && !["REQUIRED","INITIAL_ESTIMATE"].includes(h.calibration) ? "health ok" : "health warn";
  refs.credit.textContent=(state.mapStyle==="forge"?`${assetInfo().author || ""} · TarkovForge`:'tarkov.dev · carte locale')+(h.calibration==="REQUIRED" && h.position==="TRACKING"?" · Pion à caler":"")+(assetInfo().floorCalibrations?.length&&h.calibrationPlane?` · ${h.calibrationPlane} (estimé)`:"");
}

function cancelCalibration() {
  state.pendingCalibration=null;
  refs.viewport?.classList.remove("calibrating");
}
function showCalibration() {
  cancelCalibration();refs.calibrationPanel.hidden=false;refs.panel.classList.add("hidden");
  refs.calibrationMap.textContent=`${state.mapKey} · ${state.floor}`;
  refs.calibrationLandmark.replaceChildren();
  for(const m of state.markers.filter(m=>m.category.startsWith("extract_")).sort((a,b)=>a.name.localeCompare(b.name))){const o=document.createElement("option");o.value=m.id;o.textContent=m.name;refs.calibrationLandmark.appendChild(o);}
  if(!refs.calibrationLandmark.value && refs.calibrationLandmark.options.length)refs.calibrationLandmark.value=refs.calibrationLandmark.options[0].value;
  refs.calibrationStatus.textContent=`${state.calibrationPoints.length} repère(s). Pour un nouveau calage : pointe au moins 3 lieux éloignés et non alignés, sur le même étage. « Décaler » corrige seulement le décalage à ta position actuelle.`;
  refs.calibrationSave.disabled=state.calibrationPoints.length<3;
  refs.calibrationSave.textContent=`Enregistrer ${state.calibrationPoints.length} repères`;
  refs.calibrationOffset.disabled=!state.player || !activeCalibration(state.player.position);
  refs.calibrationPosition.disabled=!state.player;
  refs.calibrationReference.disabled=!refs.calibrationLandmark.options.length;
}
function startCalibrationPoint(kind) {
  let pos;
  if(kind==="reference")pos=state.markers.find(m=>m.id===refs.calibrationLandmark.value)?.position;
  else pos=state.player?.position;
  if(!pos){refs.calibrationStatus.textContent="Aucune position reçue pour le moment.";return;}
  // Freeze the pose now: subsequent GPS updates cannot move the reference
  // while the user is selecting the corresponding image point.
  state.pendingCalibration={kind,position:{...pos},key:calibrationKey(pos)};
  state.followPlayer=false;refs.center.classList.remove("following");
  refs.calibrationPanel.hidden=true;refs.viewport.classList.add("calibrating");updateHealth();
}
function acceptCalibrationPoint(e) {
  const pending=state.pendingCalibration;if(!pending)return;
  const r=refs.viewport.getBoundingClientRect();
  const u=(e.clientX-r.left-state.panX)/state.zoom/state.stageW,v=(e.clientY-r.top-state.panY)/state.zoom/state.stageH;
  if(u<0||u>1||v<0||v>1)return;
  if(pending.key!==calibrationKey(pending.position)){cancelCalibration();return;}
  if(pending.kind==="offset"){
    const current=activeCalibration(pending.position);if(!current){cancelCalibration();return;}
    const c={...current,affine:[...current.affine],status:"personal-offset",validatedInRaid:false};
    const p=RPProjection.projectNormalized(pending.position,state.mapMeta,c);
    c.affine[2]+=(u-p.u)*c.width;c.affine[5]+=(v-p.v)*c.height;
    try{localStorage.setItem(pending.key,JSON.stringify(c));loadCalibration();showCalibration();refs.calibrationStatus.textContent="Décalage enregistré pour cette carte et cet étage.";}catch(_){cancelCalibration();refs.calibrationPanel.hidden=false;refs.calibrationStatus.textContent="Impossible d'enregistrer ce calage.";}
  }else{
    if(state.calibrationPoints.some(p=>Math.hypot(p.x-pending.position.x,p.z-pending.position.z)<2)){showCalibration();refs.calibrationStatus.textContent="Ce repère est trop proche d'un point déjà choisi. Choisis un autre lieu.";}
    else if(state.calibrationPoints.length&&state.calibrationPoints[0].key!==pending.key){showCalibration();refs.calibrationStatus.textContent="Ces repères appartiennent à des plans différents. Choisis le même étage.";}
    else{state.calibrationPoints.push({x:pending.position.x,y:pending.position.y,z:pending.position.z,u,v,key:pending.key});showCalibration();}
  }
  render();
}
function saveCalibrationPoints() {
  try{
    const c={...RPMapCalibration.fit(state.calibrationPoints),status:"personal-landmarks",validatedInRaid:false};
    const first=state.calibrationPoints[0],plane=RPMapCalibration.builtIn(assetInfo(),first);
    if(assetInfo().floorCalibrations?.length&&plane){c.worldHeight=[...plane.worldHeight];c.scope=plane.scope;}
    if(c.maxMeters>25)throw Error("Les repères ne concordent pas. Recommence avec des lieux précis du même étage.");
    localStorage.setItem(first.key||calibrationKey(),JSON.stringify(c));loadCalibration();state.calibrationPoints=[];showCalibration();refs.calibrationStatus.textContent="Calage personnel enregistré. Le pion suit désormais ce fond.";render();
  }catch(e){refs.calibrationStatus.textContent=e.message;}
}
function changeMapStyle(value) {
  if(!["forge","tactical"].includes(value))return;
  const player=state.player,team=state.teammates;
  state.preferredMapStyle=value;
  state.mapStyle=value;savePreferences();
  setMap(state.mapKey,{allowUncalibratedForge:value==="forge"});
  state.player=player;state.teammates=team;render();
}

function handleHostMessage(raw) {
  let m = raw;
  if (typeof m === "string") { try { m = JSON.parse(m); } catch (_) { return; } }
  if (!m || typeof m !== "object") return;
  const type = normalize(m.type || m.kind || "");
  const p = m.payload ?? m.data ?? m;
  if (type === "snapshot" || type === "state" || type === "raid-state") applySnapshot(p);
  else if (type === "map" || type === "set-map") setMap(p.map || p.mapKey || p.key || p);
  else if (type === "player" || type === "position" || type === "player-position") setPlayer(p);
  else if (type === "team" || type === "teammates" || type === "beacon") setTeam(p.team || p.teammates || p);
  else if (type === "markers" || type === "pois") setRuntimeMarkers(p.markers || p);
  else if (type === "floor" || type === "level") setFloor(p.floor || p.level || p);
  else if (type === "calibration") setCalibration(p);
  else if (type === "health" || type === "health-request") postHost({type:"raidpilot-local-health",payload:health()});
  else if (m.map || m.player || m.position || m.markers) applySnapshot(m);
}

async function loadCache() {
  const names = ["maps","maps_fr","items","items_fr","tasks","tasks_fr"];
  let found = 0;
  for (const name of names) {
    const v = await fetchJsonSafe(`cache/${name}.json`);
    const key = name.replace("_fr","Fr");
    state.cache[key] = v;
    if (v) found++;
  }
  state.cacheStatus = arrayOf(rawMapsRoot().maps).length > 0 ? (found === names.length ? "READY" : "PARTIAL") : "MISSING";
  state.catalogues.regular=state.cache;
  state.catalogues.pve={};
  for(const name of ["maps","tasks","tasks_fr"]){state.catalogues.pve[name.replace("_fr","Fr")]=await fetchJsonSafe(`cache/pve/${name}.json`);}
  let saved;try{saved=JSON.parse(localStorage.getItem("gameMode")||'"regular"');}catch(_){}
  if(["regular","pve","pvp-season"].includes(saved))state.gameMode=saved;
  updateCatalogueStatus();
}

function wireUI() {
  $("#game-mode")?.addEventListener("change",e=>setGameMode(e.target.value));
  let lastInteractive=interactive();
  const syncMode=()=>{
    const current=interactive();if(current===lastInteractive)return;lastInteractive=current;
    state.followPlayer=!current;refs.center.classList.toggle("following",!current);
    if(!current){cancelCalibration();refs.calibrationPanel.hidden=true;centerPlayer();}
  };
  if(typeof MutationObserver==="function")new MutationObserver(syncMode).observe(document.documentElement,{attributes:true,attributeFilter:["class"]});
  refs.viewport.addEventListener("wheel", e => {
    e.preventDefault();
    const rect = refs.viewport.getBoundingClientRect();
    const mx = e.clientX - rect.left, my = e.clientY - rect.top;
    const beforeX = (mx - state.panX) / state.zoom, beforeY = (my - state.panY) / state.zoom;
    const factor = e.deltaY < 0 ? 1.12 : 1/1.12;
    state.zoom = Math.max(0.005, Math.min(8, state.zoom * factor));
    state.panX = mx - beforeX * state.zoom; state.panY = my - beforeY * state.zoom;
    applyTransform();
    saveZoomPreferenceFromGesture();
  }, {passive:false});
  refs.viewport.addEventListener("pointerdown", e => {
    if (e.button !== 0) return;
    if(!interactive())return;
    if(e.target?.closest?.("#calibration-panel"))return;
    if(state.pendingCalibration){acceptCalibrationPoint(e);return;}
    state.followPlayer=false;refs.center.classList.remove("following");
    state.dragging = true; refs.viewport.setPointerCapture(e.pointerId);
    state.dragStart = {x:e.clientX,y:e.clientY,panX:state.panX,panY:state.panY};
  });
  refs.viewport.addEventListener("pointermove", e => {
    if (!state.dragging) return;
    state.panX = state.dragStart.panX + e.clientX - state.dragStart.x;
    state.panY = state.dragStart.panY + e.clientY - state.dragStart.y;
    applyTransform();
  });
  refs.viewport.addEventListener("pointerup", () => state.dragging = false);
  refs.viewport.addEventListener("pointercancel", () => state.dragging = false);

  function zoomAtCenter(factor) {
    const r=refs.viewport.getBoundingClientRect(), old=state.zoom;
    state.zoom=Math.max(.005,Math.min(8,old*factor));
    state.panX=r.width/2-(r.width/2-state.panX)*state.zoom/old;
    state.panY=r.height/2-(r.height/2-state.panY)*state.zoom/old;
    applyTransform();
    saveZoomPreferenceFromGesture();
  }
  refs.zoomIn.addEventListener("click",()=>zoomAtCenter(1.2));
  refs.zoomOut.addEventListener("click",()=>zoomAtCenter(1/1.2));
  refs.fit.addEventListener("click",()=>{state.followPlayer=false;refs.center.classList.remove("following");fitStage({resetPreference:true});if(followingPlayer())centerPlayer();});
  refs.center.classList.add("following");
  refs.center.addEventListener("click",()=>{state.followPlayer=true;refs.center.classList.add("following");centerPlayer();});
  refs.mapStyle.value=state.mapStyle;
  refs.mapStyle.addEventListener("change",()=>changeMapStyle(refs.mapStyle.value));
  refs.calibrate.addEventListener("click",showCalibration);
  refs.calibrationReference.addEventListener("click",()=>startCalibrationPoint("reference"));
  refs.calibrationPosition.addEventListener("click",()=>startCalibrationPoint("position"));
  refs.calibrationOffset.addEventListener("click",()=>startCalibrationPoint("offset"));
  refs.calibrationSave.addEventListener("click",saveCalibrationPoints);
  refs.calibrationReset.addEventListener("click",()=>{try{localStorage.removeItem(calibrationKey());state.calibrationPoints=[];loadCalibration();showCalibration();render();}catch(_){refs.calibrationStatus.textContent="Impossible d'effacer le calage de cette vue.";}});
  refs.calibrationClose.addEventListener("click",()=>{cancelCalibration();refs.calibrationPanel.hidden=true;});
  window.addEventListener("keydown",e=>{if(e.key==="Escape" && state.pendingCalibration)showCalibration();});
  refs.layersBtn.addEventListener("click",()=>{
    refs.panel.classList.toggle("hidden");
    if(!refs.panel.className.includes("hidden"))refs.questPanel?.classList.add("hidden");
  });
  const toggleQuestPanel=force=>{
    if(!refs.questPanel)return;
    const show=typeof force==="boolean"?force:refs.questPanel.classList.contains("hidden");
    refs.questPanel.classList.toggle("hidden",!show);
    refs.questsBtn?.setAttribute("aria-expanded",show?"true":"false");
    refs.questsBtn.textContent=show?"🎯 Masquer QuestPilot":"🎯 Afficher QuestPilot";
    if(show){refs.panel.classList.add("hidden");renderQuestPilotPanel();}
  };
  refs.questsBtn?.addEventListener("click",()=>toggleQuestPanel());
  refs.questClose?.addEventListener("click",()=>toggleQuestPanel(false));
  refs.questSearch?.addEventListener("input",()=>{
    state.questSearch=refs.questSearch.value.trim().toLowerCase();
    renderQuestPilotPanel();
  });
  refs.questShare?.addEventListener("click",async()=>{
    const code=state.questPilot?.exportShare()||"";
    if(!code)return;
    refs.questShareOutput.hidden=false;refs.questShareOutput.value=code;
    let copied=false;
    try{
      if(window.navigator?.clipboard?.writeText){
        await window.navigator.clipboard.writeText(code);copied=true;
      }
    }catch(_){}
    refs.questShareStatus.textContent=copied
      ?"Plan copié. Envoie ce code à ton ami."
      :"Sélectionne et copie le code ci-dessous (Ctrl+C), puis envoie-le à ton ami.";
  });
  refs.questImport?.addEventListener("click",()=>{
    const planner=state.questPilot;
    const ok=planner?.importShare(refs.questPeerCode.value,refs.questPeerName.value);
    refs.questShareStatus.textContent=ok
      ?"Plan ami importé, sans changer tes quêtes ni tes cases cochées."
      :"Code RPQ1 invalide ou incompatible avec le mode PvE/PvP choisi.";
    if(ok){refs.questPeerCode.value="";renderQuestPilotPanel();render();}
  });
  refs.labels.value=state.labelMode;
  refs.labels.addEventListener("change",()=>{state.labelMode=refs.labels.value;savePreferences();render();});
  refs.search.addEventListener("input",()=>{state.search=refs.search.value.trim().toLowerCase();render();});
  refs.profile.addEventListener("change",()=>{ if(refs.profile.value!=="custom") applyProfile(refs.profile.value);});
  refs.floor.addEventListener("change",()=>setFloor(refs.floor.value));
  refs.baseMap.addEventListener("load",()=>{
    state.stageW = Math.max(1, refs.baseMap.naturalWidth || 1600);
    state.stageH = Math.max(1, refs.baseMap.naturalHeight || 900);
    refs.stage.style.width=`${state.stageW}px`;refs.stage.style.height=`${state.stageH}px`;
    refs.overlay.setAttribute("width",state.stageW);refs.overlay.setAttribute("height",state.stageH);
    refs.missingAsset.hidden=true;fitStage();if(followingPlayer())centerPlayer();render();
  });
  refs.baseMap.addEventListener("error",()=>{refs.missingAsset.hidden=false;});
  if (typeof ResizeObserver === "function") {
    new ResizeObserver(resizeViewport).observe(refs.viewport);
  }
  window.addEventListener("resize",resizeViewport);
}

async function init() {
  Object.assign(refs, {
    viewport:$("#viewport"),stage:$("#stage"),baseMap:$("#base-map"),overlay:$("#overlay"),
    panel:$("#panel"),filters:$("#filters"),layersBtn:$("#layers-btn"),search:$("#search"),
    questPanel:$("#questpilot-panel"),questsBtn:$("#quests-btn"),questClose:$("#questpilot-close"),
    questSearch:$("#questpilot-search"),questList:$("#questpilot-list"),questSteps:$("#questpilot-steps"),
    questOther:$("#questpilot-other"),questDoneList:$("#questpilot-done"),questSharedSteps:$("#questpilot-shared-steps"),
    questPrep:$("#questpilot-prep"),questSummary:$("#questpilot-summary"),
    questShare:$("#questpilot-share"),questShareStatus:$("#questpilot-share-status"),
    questShareOutput:$("#questpilot-share-output"),questPeerName:$("#questpilot-peer-name"),
    questPeerCode:$("#questpilot-peer-code"),questImport:$("#questpilot-import"),
    questTeamPlans:$("#questpilot-team-plans"),
    questHud:$("#questpilot-hud"),questHudColor:$("#questpilot-hud-color"),
    questHudTitle:$("#questpilot-hud-title"),questHudDetail:$("#questpilot-hud-detail"),
    profile:$("#profile"),floor:$("#floor"),floorCoverage:$("#floor-coverage"),zoomIn:$("#zoom-in"),zoomOut:$("#zoom-out"),
    fit:$("#fit"),center:$("#center-player"),zoomValue:$("#zoom-value"),mapName:$("#map-name"),
    markerCount:$("#marker-count"),health:$("#health"),details:$("#details"),tacticalWarning:$("#tactical-warning"),missingAsset:$("#missing-asset"),labels:$("#label-mode"),mapStyle:$("#map-style"),credit:$("#map-credit"),calibrate:$("#calibrate"),
    calibrationPanel:$("#calibration-panel"),calibrationMap:$("#calibration-map"),calibrationStatus:$("#calibration-status"),calibrationLandmark:$("#calibration-landmark"),calibrationReference:$("#calibration-reference"),calibrationPosition:$("#calibration-position"),calibrationOffset:$("#calibration-offset"),calibrationSave:$("#calibration-save"),calibrationReset:$("#calibration-reset"),calibrationClose:$("#calibration-close")
  });

  state.meta = await fetchJsonSafe("map-meta.json");
  state.catalog = await fetchJsonSafe("filter-catalog.json");
  state.assets = await fetchJsonSafe("assets/forge-manifest.json") || await fetchJsonSafe("assets/manifest.json") || {maps:{}};
  if (!state.meta || !state.catalog) {
    refs.health.textContent = "ERREUR: fichiers LocalMap manquants";
    refs.health.className = "health bad";
    return;
  }
  defaultEnabled(); loadPreferences();
  window.addEventListener("raidpilot-zoom-sync", () => {
    const fromHost = validMapZoomFactor(window.__raidPilotHostZoomFactor);
    if (fromHost === null || Math.abs(fromHost - state.zoomFactor) < 1e-9) return;
    state.zoomFactor = fromHost;
    savePreferences();
    if (state.mapKey && refs.baseMap?.naturalWidth > 0) {
      fitStage();
      if (followingPlayer()) centerPlayer();
    }
  });
  for (const [id,p] of Object.entries(state.catalog.profiles)) {
    const o=document.createElement("option");o.value=id;o.textContent=p.name;refs.profile.appendChild(o);
  }
  const custom=document.createElement("option");custom.value="custom";custom.textContent="Personnalisé";refs.profile.appendChild(custom);
  refs.profile.value=state.enabled.size<=4 && state.enabled.has("teammate")?"illustrated":"custom";
  wireUI();
  await loadCache();
  state.questPilot = new RPQuestPilot.Planner(window.localStorage,state.gameMode);
  refreshQuestPilotCatalogue();
  // Keep the planner's loaded progress. No automatic quest overlay away
  // from objectives; players can open their saved checklist manually.
  refs.questPanel?.classList.add("hidden");
  refs.questsBtn?.setAttribute("aria-expanded","false");
  refs.questsBtn.textContent="🎯 Afficher QuestPilot";
  renderQuestPilotPanel();
  if($("#game-mode"))$("#game-mode").value=state.gameMode;
  state.initialized = true;
  const requested = new URLSearchParams(location.search).get("map");
  const initial = requested || (state.meta.maps.customs ? "customs" : (Object.keys(state.meta.maps)[0] || "customs"));
  setMap(initial);
  updateHealth();
  // A pause in 10 Hz host updates must be visible even without a new message.
  // Update only status and SVG styling; do not redraw coordinates on this timer.
  if (typeof setInterval === "function") {
    const poseHealthTimer = setInterval(() => {
      if (state.initialized && state.player) updateHealth();
      // A stale GPS feed must hide the zone HUD even without a new map
      // frame from the host; do not leave old objectives floating on screen.
      if (state.initialized) renderQuestPilotHud();
      if (state.questPilot?.pruneLivePlans()) {
        renderQuestPilotPanel();
        render();
      }
    }, 750);
    if (typeof addEventListener === "function")
      addEventListener("pagehide", () => clearInterval(poseHealthTimer), {once:true});
  }

  if (window.chrome?.webview?.addEventListener) {
    window.chrome.webview.addEventListener("message", e => handleHostMessage(e.data));
  }
  postHost({type:"raidpilot-local-ready",payload:health()});
  window.dispatchEvent(new Event("raidpilot-local-initialized"));
}

const api = {
  version:"0.14.2-localfirst-hotfix11",
  setMap, canonicalMapKey, setPlayer, setPlayerPosition:setPlayer, updatePlayerPosition:setPlayer,
  setTeam, setTeammates:setTeam, setMarkers:setRuntimeMarkers, setPois:setRuntimeMarkers,
  setFloor, setLevel:setFloor, setCalibration, setBaseAsset, applySnapshot, health, render,
  handleMessage:handleHostMessage
  ,resetRaid,setRaidExtractions,applyRaidExtractions,clearRaidExtractions,setBoundaryHazards:p=>setHazards(p,"boundary"),setMineHazards:p=>setHazards(p,"mine"),clearBoundaryHazards:()=>clearHazards("boundary"),clearMineHazards:()=>clearHazards("mine"),setGuidanceTarget,clearGuidanceTarget
  ,setRaidQuests,clearRaidQuests,applyRaidQuests,applyQuestLogEvents,setGameMode,setLearnedRoute,clearLearnedRoute,setFloorPose,clearFloorPose
  ,setLiveTeamQuestPlan,clearLiveTeamQuestPlan,clearAllLiveTeamQuestPlans,
    questPilotLiveSnapshot:()=>state.questPilot?.liveSnapshot()||null
  ,questPilotContextProgress:()=>({
      // Native nearby hint must respect the same manually checked
      // objectives/quests and verified EFT completions as QuestPilot.
      done:[...(state.questPilot?.state.done||[])].slice(0,240),
      completedQuests:[...(state.questPilot?.state.completedQuests||[]),
        ...Object.entries(state.questPilot?.state.evidence||{})
          .filter(([,e])=>e?.status==="completed").map(([id])=>id)
      ].slice(0,240)
    })
  ,questPilotSnapshot:()=>({
      mode:state.questPilot?.mode,
      active:[...(state.questPilot?.state.active||[])],
      done:[...(state.questPilot?.state.done||[])],
      completedQuests:[...(state.questPilot?.state.completedQuests||[])],
      evidence:{...(state.questPilot?.state.evidence||{})},
      focused:state.questPilot?.state.focused||"",
      mapSteps:state.questPilot?.stepsForMap(state.questMapIds).map(s=>({
        id:s.id,number:s.number,questId:s.questId,locations:s.locations.length,done:s.done,color:s.color
      }))||[]
    }),
    questPilotToggle:(id,enabled)=>{
      if(!state.questPilot)return false;
      const value=state.questPilot.toggle(id,enabled);
      renderQuestPilotPanel();render();return value;
    },
    questPilotOpenPanel,
    questPilotClosePanel,
    questPilotCompleteFocused,
    questPilotStepDone:(id,done)=>{
      if(!state.questPilot)return false;
      const value=state.questPilot.toggleDone(id,done);
      renderQuestPilotPanel();render();return value;
    }
  ,availableLayers:()=>state.catalog.groups.flatMap(g=>[...g.layers,...[...dynamicLayerNames(state.markers)].filter(([id])=>inferGroup(id)===g.id).map(([id,name])=>({id,name}))].map(l=>({...l,count:layerCount(l),enabled:state.enabled.has(l.id)})))
};
window.RaidPilotLocalMap = api;
window.RaidPilot = Object.assign(window.RaidPilot || {}, api);
// Compatibility aliases for earlier injected scripts / host builds.
window.setPlayerPosition = setPlayer;
window.updatePlayerPosition = setPlayer;
window.raidPilotSetPlayerPosition = setPlayer;
window.setRaidPilotPosition = setPlayer;
window.setRaidPilotMap = setMap;
window.setRaidPilotTeam = setTeam;
window.setRaidPilotMarkers = setRuntimeMarkers;
window.setRaidPilotFloor = setFloor;
window.applyRaidPilotSnapshot = applySnapshot;

init().catch(err => {
  const h = document.querySelector("#health");
  if (h) { h.textContent = `ERREUR LOCAL MAP: ${err.message}`; h.className = "health bad"; }
  postHost({type:"raidpilot-local-error",payload:{message:String(err?.message || err),stack:String(err?.stack || "")}});
});
})();
