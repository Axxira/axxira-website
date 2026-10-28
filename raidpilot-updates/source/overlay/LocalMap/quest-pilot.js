(function (root, factory) {
  "use strict";
  const moduleValue = factory();
  if (typeof module !== "undefined" && module.exports) module.exports = moduleValue;
  if (root) root.RPQuestPilot = moduleValue;
})(typeof globalThis !== "undefined" ? globalThis : this, function () {
  "use strict";

  // Intentionally distinct, accessible against the dark map and card surfaces.
  const COLORS = [
    "#50d8ff", "#ffc45c", "#c490ff", "#70e0a4", "#ff829d",
    "#ffe278", "#6faaff", "#e5a0e9", "#ffa96c", "#94dfd0",
    "#dbec8c", "#df9a7e"
  ];
  const MAX_SAVED = 240;
  const LIMIT_TASKS = 3000;
  const LIMIT_OBJECTIVES = 80;
  const LIMIT_LOCATIONS = 200;
  const HUD_ENTER_METERS = 30;
  const HUD_EXIT_METERS = 45;
  const HUD_MAX_UNCERTAINTY_METERS = 6;
  const HUD_FLOOR_TOLERANCE_METERS = 7;

  function identifier(value) {
    const s = String(value ?? "").trim();
    return s.length > 0 && s.length <= 128 && !/[\u0000-\u001f]/.test(s) ? s : "";
  }
  function modeName(mode) { return mode === "pve" ? "pve" : mode === "pvp-season" ? "pvp-season" : "regular"; }
  function key(mode) { return "raidpilot-questpilot-v1:" + modeName(mode); }
  function colorFor(id) {
    const s = String(id ?? "");
    let hash = 2166136261;
    for (let i = 0; i < s.length; i++) { hash ^= s.charCodeAt(i); hash = Math.imul(hash, 16777619); }
    return COLORS[(hash >>> 0) % COLORS.length];
  }
  function asArray(v) { return Array.isArray(v) ? v : v && typeof v === "object" ? Object.values(v) : []; }
  function fold(v) { return String(v ?? "").normalize("NFD").replace(/[\u0300-\u036f]/g,"").toLowerCase().trim(); }
  function text(dict, id, fallback) {
    const k = String(id || "");
    const mapped = dict && Object.prototype.hasOwnProperty.call(dict, k) ? dict[k] : null;
    const found = typeof mapped === "string" && mapped.trim() ? mapped.trim() : "";
    return found || String(fallback || k);
  }
  function point(p) {
    if (!p || typeof p !== "object" || p.x == null || p.z == null) return null;
    const x = Number(p.x), z = Number(p.z), y = p.y == null ? null : Number(p.y);
    if (!Number.isFinite(x) || !Number.isFinite(z) || Math.abs(x) > 100000 || Math.abs(z) > 100000
        || (y !== null && (!Number.isFinite(y) || Math.abs(y) > 100000))) return null;
    return {x, z, y};
  }
  function mapId(m) {
    if (typeof m === "string") return m;
    return typeof m?.id === "string" ? m.id
      : typeof m?.normalizedName === "string" ? m.normalizedName : "";
  }
  function objectiveLocations(obj) {
    const out = [], seen = new Set();
    const append = (map, p, choice, place, rawOutline) => {
      const pos = point(p), id = mapId(map);
      if (!id || !pos || out.length >= LIMIT_LOCATIONS) return;
      const signature = id + "|" + pos.x.toFixed(3) + "|" + pos.z.toFixed(3)
        + "|" + String(pos.y) + "|" + choice;
      if (seen.has(signature)) return;
      seen.add(signature);
      const outline = asArray(rawOutline).slice(0,128).map(point).filter(Boolean);
      out.push({map:id, position:pos, outline:outline.length>=3?outline:[],
        possible:choice, place:String(place || "").slice(0,160)});
    };
    for (const z of asArray(obj?.zones).slice(0,LIMIT_LOCATIONS)) {
      append(z.map, z.position, false, z.name, z.outline);
    }
    for (const option of asArray(obj?.possibleLocations).slice(0,LIMIT_LOCATIONS)) {
      for (const p of asArray(option.positions).slice(0,LIMIT_LOCATIONS)) {
        append(option.map, p, true, option.name);
      }
    }
    return out;
  }
  function equipmentNeeds(obj, itemLookup, translations) {
    const needs = [];
    const append = (name, prefix) => {
      const id = typeof name === "string" ? name : name?.id || name?.name || "";
      if (!id) return;
      const item = itemLookup?.[id];
      const label = text(translations, item?.name || id, item?.normalizedName || item?.name || id);
      needs.push((prefix ? prefix + " : " : "") + label);
    };
    for (const group of asArray(obj?.requiredKeys).slice(0,12)) {
      if (Array.isArray(group)) {
        if (group.length) needs.push("Clé requise" + (group.length > 1 ? " (plusieurs clés possibles)" : ""));
      } else append(group,"Clé");
    }
    if (obj?.markerItem) append(obj.markerItem,"Balise");
    if (obj?.questItem && (obj?.type === "plantQuestItem" || obj?.type === "plantItem"))
      append(obj.questItem,"Objet à déposer");
    if (asArray(obj?.wearing).length || asArray(obj?.usingWeapon).length
        || asArray(obj?.usingWeaponMods).length || asArray(obj?.notWearing).length)
      needs.push("Équipement ou arme spécifique requis");
    if (Number.isFinite(Number(obj?.timeFromHour)) && Number.isFinite(Number(obj?.timeUntilHour))
        && obj.timeFromHour != null && obj.timeUntilHour != null)
      needs.push("Horaire : " + obj.timeFromHour + "–" + obj.timeUntilHour + " h");
    return [...new Set(needs)].slice(0,12);
  }
  function makeTask(raw, translations, itemLookup, itemTranslations) {
    const id = identifier(raw?.id);
    if (!id) return null;
    const name = text(translations, raw.name, raw.normalizedName || raw.name || "Quête");
    const objectives = [], byObjective = new Map();
    let numberedLocations = 0;
    for (const obj of asArray(raw.objectives).slice(0,LIMIT_OBJECTIVES)) {
      const objectiveId = identifier(obj?.id);
      if (!objectiveId || byObjective.has(objectiveId)) continue;
      const locations = objectiveLocations(obj);
      // Every public objective remains visible in the manual checklist,
      // but only *geolocated* objectives get numbered map pins.
      // A kill/hand-over objective without a location must not shift ①②③.
      const number = locations.length ? ++numberedLocations : null;
      const step = {
        id:id + ":" + objectiveId, questId:id, objectiveId,
        number, objectiveCount:0, allNumber:objectives.length+1,
        allObjectiveCount:0,
        // Preserve plain-language objective text when the translation catalogue
        // lacks this entry; never replace a readable instruction with the raw type.
        // An untranslated 24-hex localization key must not be shown to players.
        name:text(translations,obj.description,
          typeof obj.description==="string" && obj.description.trim()
            && !/^[0-9a-f]{24}$/i.test(obj.description.trim())
              ?obj.description:obj.type||"Objectif"),
        type:String(obj.type || ""),
        optional:obj.optional === true,
        // Only explicit objective-level prerequisites imply a mandatory order.
        // Numbered pins are identifiers, NOT evidence of required sequencing.
        requires:asArray(obj.requiredPreviousObjectives).map(v=>identifier(v?.id||v))
          .filter(Boolean).map(v=>id+":"+v),
        locations,
        alternative:locations.some(l=>l.possible) || (locations.length > 1
          && !["plantItem","plantQuestItem"].includes(obj.type)),
        needs:equipmentNeeds(obj,itemLookup,itemTranslations),
      };
      objectives.push(step);
      byObjective.set(objectiveId,step);
    }
    for (const step of objectives) {
      step.objectiveCount = numberedLocations;
      step.allObjectiveCount = objectives.length;
    }
    return {id,name,color:colorFor(id),objectives,byObjective,
      mappedCount:numberedLocations,unmappedCount:objectives.length-numberedLocations};
  }
  function sanitizeArray(v) {
    return [...new Set((Array.isArray(v) ? v : []).map(identifier).filter(Boolean))].slice(0,MAX_SAVED);
  }
  function safeEvidence(input){
    const output=Object.create(null);
    if(!input || typeof input!=="object" || Array.isArray(input))return output;
    for(const [id,event] of Object.entries(input).slice(0,MAX_SAVED)){
      if(!/^[0-9a-f]{24}$/i.test(id)||!event||typeof event!=="object")continue;
      if(!["active","completed","failed"].includes(event.status)||event.source!=="eft-local-log"
        ||!Number.isSafeInteger(event.at)||event.at<1500000000||event.at>4102444800)continue;
      output[id.toLowerCase()]={status:event.status,source:"eft-local-log",at:event.at};
    }
    return output;
  }
  function safePeerPlans(raw){
    const result=Object.create(null);
    if(!raw||typeof raw!=="object"||Array.isArray(raw))return result;
    for(const [name,plan] of Object.entries(raw).slice(0,6)){
      const key=String(name).trim().slice(0,24);
      if(!key||!plan||typeof plan!=="object")continue;
      result[key]={
        active:sanitizeArray(plan.active).slice(0,80),
        done:sanitizeArray(plan.done).slice(0,120),
        imported:true
      };
    }
    return result;
  }
  function defaultState() { return {schemaVersion:1,active:[],excluded:[],done:[],completedQuests:[],packed:[],focused:"",
    evidence:Object.create(null),sharedPlans:Object.create(null)}; }
  function normalizeState(input) {
    if (!input || typeof input !== "object" || input.schemaVersion !== 1) return defaultState();
    return {
      schemaVersion:1,active:sanitizeArray(input.active), excluded:sanitizeArray(input.excluded),
      done:sanitizeArray(input.done), completedQuests:sanitizeArray(input.completedQuests),
      packed:sanitizeArray(input.packed),
      focused:identifier(input.focused), evidence:safeEvidence(input.evidence),
      sharedPlans:safePeerPlans(input.sharedPlans)
    };
  }
  function distanceTo(player, location) {
    if (!player || !location) return Infinity;
    const p = player.position || player;
    const x = Number(p.x), z = Number(p.z);
    return Number.isFinite(x)&&Number.isFinite(z)
      ? Math.hypot(x-location.position.x,z-location.position.z) : Infinity;
  }
  // Quest-zone distances: polygon edges are authoritative when the public
  // objective publishes an outline. A lone point is only an approximate zone,
  // not a claim about collision meshes or an exact EFT completion trigger.
  function segmentDistance(x,z,a,b) {
    const dx=b.x-a.x,dz=b.z-a.z,denom=dx*dx+dz*dz;
    const t=denom>0?Math.max(0,Math.min(1,((x-a.x)*dx+(z-a.z)*dz)/denom)):0;
    return Math.hypot(x-a.x-t*dx,z-a.z-t*dz);
  }
  function distanceToQuestZone(player,location) {
    if(!player||!location?.position)return Infinity;
    const p=point(player.position||player);
    if(!p)return Infinity;
    const target=location.position;
    // When altitude is known, do not show a second-floor quest on the ground
    // floor. A missing altitude must not masquerade as a verified floor.
    if(target.y!==null && (p.y===null||Math.abs(p.y-target.y)>HUD_FLOOR_TOLERANCE_METERS))
      return Infinity;
    const outline=location.outline;
    if(!Array.isArray(outline)||outline.length<3)
      return Math.hypot(p.x-target.x,p.z-target.z);
    let inside=false,minimum=Infinity;
    for(let i=0,j=outline.length-1;i<outline.length;j=i++){
      const a=outline[j],b=outline[i];
      minimum=Math.min(minimum,segmentDistance(p.x,p.z,a,b));
      if((a.z>p.z)!==(b.z>p.z)
          && p.x<(b.x-a.x)*(p.z-a.z)/(b.z-a.z)+a.x)inside=!inside;
    }
    return inside?0:minimum;
  }
  class Planner {
    constructor(storage, mode) {
      this.storage=storage || null;
      this.mode=modeName(mode);
      this.tasks=[];
      this.byId=new Map();
      this.state=defaultState();
      // Last reliable target is session-only; never persist guessed distances.
      this.lastReliableTarget=null;
      // Ephemeral live plans: NEVER saved as personal task completion.
      this.livePlans=Object.create(null);
      this.load();
    }
    load() {
      this.state=defaultState();
      try {
        const source=this.storage?.getItem(key(this.mode));
        if (source && source.length<=25000) this.state=normalizeState(JSON.parse(source));
      } catch (_) {}
    }
    save() {
      try { this.storage?.setItem(key(this.mode),JSON.stringify(this.state)); return true; }
      catch (_) { return false; }
    }
    setMode(mode) {
      if (this.mode===modeName(mode)) return;
      this.mode=modeName(mode);this.tasks=[];this.byId.clear();this.lastReliableTarget=null;this.load();
    }
    setCatalog(tasks, translations={}, itemLookup={}, itemTranslations={}) {
      const list=[];
      for (const raw of asArray(tasks).slice(0,LIMIT_TASKS)) {
        const task=makeTask(raw,translations,itemLookup,itemTranslations);
        if(task)list.push(task);
      }
      this.tasks=list.sort((a,b)=>a.name.localeCompare(b.name,"fr"));
      this.byId=new Map(this.tasks.map(q=>[q.id,q]));
    }
    isActive(id) { return this.state.active.includes(String(id)); }
    memberNamesFor(id){
      const taskId=String(id);
      const manual=Object.entries(this.state.sharedPlans||{})
        .filter(([,plan])=>plan.active.includes(taskId)).map(([name])=>name);
      const live=Object.values(this.livePlans||{})
        .filter(plan=>plan.mode===this.mode && plan.quests.includes(taskId)
          && Date.now()-plan.receivedAt<90000)
        .map(plan=>plan.name+" (direct)");
      return [...new Set([...manual,...live])];
    }
    setLivePlan(raw){
      if(!raw||typeof raw!=="object"||typeof raw.id!=="string"
        ||!/^[a-z0-9_-]{1,64}$/i.test(raw.id)
        ||typeof raw.name!=="string"||raw.name.length>24
        ||!["regular","pve","pvp-season"].includes(raw.mode)
        ||!Array.isArray(raw.quests)||raw.quests.length>80
        ||!Array.isArray(raw.done)||raw.done.length>120
        ||!Number.isSafeInteger(raw.at)||Math.abs(raw.at-Date.now())>120000)
        return false;
      const tasks=raw.quests.every(id=>typeof id==="string"&&/^[0-9a-f]{24}$/i.test(id));
      const steps=raw.done.every(id=>typeof id==="string"
        &&/^[0-9a-f]{24}:[0-9a-f]{24}$/i.test(id));
      if(!tasks||!steps)return false;
      const existing=this.livePlans[raw.id];
      if(existing && existing.at>=raw.at)return false;
      if(!existing && Object.keys(this.livePlans).length>=12)return false;
      this.livePlans[raw.id]={
        id:raw.id,name:raw.name,mode:raw.mode,
        quests:[...new Set(raw.quests.map(s=>s.toLowerCase()))],
        done:[...new Set(raw.done.map(s=>s.toLowerCase()))],
        at:raw.at,receivedAt:Date.now()};
      return true;
    }
    clearLivePlan(peerId){
      if(!Object.prototype.hasOwnProperty.call(this.livePlans,peerId))return false;
      delete this.livePlans[peerId];return true;
    }
    clearLivePlans(){this.livePlans=Object.create(null);}
    pruneLivePlans(now=Date.now()){
      let changed=false;
      for(const [id,plan] of Object.entries(this.livePlans)){
        if(now-plan.receivedAt>90000){delete this.livePlans[id];changed=true;}
      }
      return changed;
    }
    liveSnapshot(){
      // Narrow wire format: no nicknames, account IDs, logs, room code or coordinates.
      const quests=this.state.active.filter(id=>/^[0-9a-f]{24}$/i.test(id)).slice(0,80);
      const ids=new Set(quests);
      const done=this.state.done.filter(id=>/^[0-9a-f]{24}:[0-9a-f]{24}$/i.test(id)
        && ids.has(id.slice(0,24))).slice(0,120);
      return {mode:this.mode,quests,done};
    }
    isTeamActive(id) { return this.memberNamesFor(id).length>0; }
    exportShare(){
      // Deliberately contains only public mission IDs, manual checks and game mode.
      // Never includes room codes, profile/account identifiers or log evidence.
      const payload={version:1,mode:this.mode,
        active:this.state.active.slice(0,80),done:this.state.done.slice(0,120)};
      return "RPQ1:"+encodeURIComponent(JSON.stringify(payload));
    }
    importShare(code,name){
      const label=String(name||"Ami").trim().slice(0,24);
      if(!label||Array.from(label).some(c=>c.charCodeAt(0)<32)
        ||typeof code!=="string"||code.length>12000||!code.startsWith("RPQ1:"))return false;
      try{
        const data=JSON.parse(decodeURIComponent(code.slice(5)));
        if(!data||data.version!==1||data.mode!==this.mode
          ||!Array.isArray(data.active)||!Array.isArray(data.done)
          ||data.active.length>80||data.done.length>120)return false;
        const selected=sanitizeArray(data.active);
        const done=sanitizeArray(data.done);
        const plans=this.state.sharedPlans||Object.create(null);
        if(!Object.prototype.hasOwnProperty.call(plans,label)
          && Object.keys(plans).length>=6)return false;
        plans[label]={active:selected,done,imported:true};
        this.state.sharedPlans=plans;
        this.save();return true;
      }catch(_){return false;}
    }
    removeShare(name){
      if(!this.state.sharedPlans||!Object.prototype.hasOwnProperty.call(this.state.sharedPlans,name))
        return false;
      delete this.state.sharedPlans[name];this.save();return true;
    }
    isDone(id) { return this.state.done.includes(String(id)); }
    isQuestCompleted(id) { return this.state.evidence?.[String(id)]?.status==="completed"; }
    isQuestMarkedDone(id) { return this.state.completedQuests.includes(String(id)); }
    isQuestFinished(id) { return this.isQuestCompleted(id)||this.isQuestMarkedDone(id); }
    toggleQuestCompleted(id,force) {
      const valid=identifier(id);
      if(!valid||this.isQuestCompleted(valid))return false;
      const yes=typeof force==="boolean"?force:!this.isQuestMarkedDone(valid);
      this.state.completedQuests=this.state.completedQuests.filter(x=>x!==valid);
      if(yes)this.state.completedQuests.push(valid);
      this.state.completedQuests=sanitizeArray(this.state.completedQuests);
      this.save();
      return yes;
    }
    statusFor(id) { return this.state.evidence?.[String(id)]||null; }
    stageStatus(stepId,questId) {
      if(this.isQuestCompleted(questId))return {done:true,source:"eft-local-log"};
      if(this.isQuestMarkedDone(questId))return {done:true,source:"manual-quest"};
      if(this.isDone(stepId))return {done:true,source:"manual"};
      return {done:false,source:"unknown"};
    }
    recordEvidence(change){
      if(!change||change.mode!==this.mode
        ||typeof change.taskId!=="string"||!/^[0-9a-f]{24}$/i.test(change.taskId)
        ||!["active","completed","failed"].includes(change.status)
        ||change.source!=="eft-local-log"
        ||!Number.isSafeInteger(change.at)||change.at<1500000000||change.at>4102444800)
        return false;
      const id=change.taskId.toLowerCase();
      const previous=this.state.evidence[id];
      if(previous && previous.at>=change.at)return true; // Valid repeated/stale event: already handled.
      this.state.evidence[id]={status:change.status,at:change.at,source:"eft-local-log"};
      // An explicit manual exclusion wins: reading logs cannot force a task back.
      if(change.status==="active")this.recognize([id]);
      this.save();return true;
    }
    isPacked(id) { return this.state.packed.includes(String(id)); }
    toggle(id,force) {
      const valid=identifier(id);if(!valid)return false;
      const wanted=typeof force==="boolean"?force:!this.isActive(valid);
      this.state.active=this.state.active.filter(x=>x!==valid);
      if(wanted) {this.state.active.push(valid);this.state.excluded=this.state.excluded.filter(x=>x!==valid);}
      else if(!this.state.excluded.includes(valid))this.state.excluded.push(valid);
      this.state.active=sanitizeArray(this.state.active);
      this.state.excluded=sanitizeArray(this.state.excluded);
      this.save();return wanted;
    }
    recognize(ids) {
      let changed=false;
      for(const id of (Array.isArray(ids)?ids:[])) {
        const valid=identifier(id);
        if(valid && !this.state.excluded.includes(valid)&&!this.isActive(valid)
           && this.state.active.length<MAX_SAVED){this.state.active.push(valid);changed=true;}
      }
      if(changed)this.save();
      return changed;
    }
    toggleDone(stepId,force) {
      const id=identifier(stepId);if(!id)return false;
      const yes=typeof force==="boolean"?force:!this.isDone(id);
      this.state.done=this.state.done.filter(x=>x!==id);
      if(yes)this.state.done.push(id);
      this.state.done=sanitizeArray(this.state.done);this.save();return yes;
    }
    togglePacked(textId,force) {
      const id=identifier(textId);if(!id)return false;
      const yes=typeof force==="boolean"?force:!this.isPacked(id);
      this.state.packed=this.state.packed.filter(x=>x!==id);
      if(yes)this.state.packed.push(id);
      this.state.packed=sanitizeArray(this.state.packed);this.save();return yes;
    }
    focus(stepId) {
      this.state.focused=identifier(stepId);this.save();
    }
    taskFor(id) { return this.byId.get(String(id)) || null; }
    stepFor(questId,objectiveId){return this.taskFor(questId)?.byObjective.get(String(objectiveId))||null;}
    visibleTasks(mapIds) {
      const ids=mapIds instanceof Set?mapIds:new Set(mapIds||[]);
      return this.tasks.filter(q=>this.isActive(q.id)
        || q.objectives.some(s=>s.locations.some(l=>ids.has(l.map))));
    }
    searchTasks(mapIds,query="") {
      const needle=fold(query);
      // An explicit search is GLOBAL: a player can prepare a Woods task
      // while still looking at Customs, or find an objective without map data.
      const pool=needle ? this.tasks.filter(q=>fold(q.name).includes(needle)
        || q.objectives.some(step=>fold(step.name).includes(needle)))
        : this.visibleTasks(mapIds);
      return pool.sort((a,b)=>Number(this.isActive(b.id))-Number(this.isActive(a.id))
        || a.name.localeCompare(b.name,"fr"));
    }
    unmappedSteps() {
      const list=[];
      for(const id of this.state.active){
        const quest=this.byId.get(id);if(!quest)continue;
        for(const step of quest.objectives){
          if(step.locations.length)continue;
          const stage=this.stageStatus(step.id,quest.id);
          list.push({...step,questName:quest.name,color:quest.color,
            done:stage.done,doneSource:stage.source});
        }
      }
      return list;
    }
    preparationForMap(mapIds) {
      const ids=mapIds instanceof Set?mapIds:new Set(mapIds||[]),out=[],seen=new Set();
      for(const id of this.state.active){
        const quest=this.byId.get(id);if(!quest||this.isQuestFinished(id))continue;
        for(const step of quest.objectives){
          if(step.locations.length&&!step.locations.some(l=>ids.has(l.map)))continue;
          for(const need of step.needs){
            const k=id+":"+need;
            if(seen.has(k))continue;
            seen.add(k);
            out.push({id:k,questId:id,questName:quest.name,need,packed:this.isPacked(k)});
          }
        }
      }
      return out;
    }
    memberStatusesForStep(questId,stepId) {
      const id=String(questId),step=String(stepId),out=[];
      for(const [name,plan] of Object.entries(this.state.sharedPlans||{})){
        if(plan.active.includes(id))out.push({name,done:plan.done.includes(step),source:"importé"});
      }
      for(const plan of Object.values(this.livePlans||{})){
        if(plan.mode===this.mode && plan.quests.includes(id)
          && Date.now()-plan.receivedAt<90000)
          out.push({name:plan.name+" (direct)",done:plan.done.includes(step),source:"direct"});
      }
      return out;
    }
    sharedStepsForMap(mapIds) {
      const ids=mapIds instanceof Set?mapIds:new Set(mapIds||[]),out=[];
      for(const quest of this.tasks){
        if(!this.isTeamActive(quest.id))continue;
        for(const step of quest.objectives){
          if(!step.locations.some(loc=>ids.has(loc.map)))continue;
          out.push({...step,questName:quest.name,color:quest.color,
            mine:this.isActive(quest.id),
            members:this.memberStatusesForStep(quest.id,step.id)});
        }
      }
      return out;
    }
    stepsForMap(mapIds) {
      const ids=mapIds instanceof Set?mapIds:new Set(mapIds||[]);
      const list=[];
      for(const id of this.state.active) {
        const quest=this.byId.get(id);
        if(!quest)continue;
        for(const step of quest.objectives) {
          const locations=step.locations.filter(l=>ids.has(l.map));
          if(!locations.length)continue;
          const stage=this.stageStatus(step.id,quest.id);
          list.push({...step,locations,questName:quest.name,color:quest.color,
            done:stage.done,doneSource:stage.source,questStatus:this.statusFor(quest.id)});
        }
      }
      return list;
    }
    markerVisual(marker, mapIds) {
      const id=identifier(marker?.meta?.questId), obj=identifier(marker?.meta?.objectiveId);
      if(!id||(!this.isActive(id)&&!this.isTeamActive(id)))return null;
      const task=this.taskFor(id), step=task?.byObjective.get(obj);
      if(!task||!step)return null;
      const ids=mapIds instanceof Set?mapIds:new Set(mapIds||[]);
      if(!step.locations.some(l=>ids.has(l.map)))return null;
      return {
        color:task.color,number:step.number, label:String(step.number),
        id:step.id,name:task.name,description:step.name,
        possible:marker?.meta?.possible===true || step.alternative,
        done:this.isActive(id)&&this.stageStatus(step.id,id).done,
        doneSource:this.isActive(id)?this.stageStatus(step.id,id).source:"team",
        teamOnly:!this.isActive(id),teamNames:this.memberNamesFor(id),
        focused:this.state.focused===step.id,
      };
    }
    isStepUnlocked(step) {
      return (step.requires||[]).every(id=>this.isDone(id)
        || this.isQuestCompleted(step.questId));
    }
    positionReliable(player) {
      const pos=player?.position||player;
      if(pos?.x==null||pos?.z==null||!Number.isFinite(Number(pos.x))||!Number.isFinite(Number(pos.z)))return false;
      if(player?.positionReliable===false||player?.locationUncertain===true
        ||player?.quality==="low"||player?.quality==="lost")return false;
      const confidence=player?.positionConfidence;
      return confidence==null || (Number.isFinite(Number(confidence))&&Number(confidence)>=0.5);
    }
    nearbyForMap(mapIds,player,previousId="") {
      // Contextual, confidence-aware quest-zone HUD. No completion is inferred.
      if(!this.positionReliable(player))return null;
      const uncertainty=Number(player?.uncertainty);
      if(!Number.isFinite(uncertainty)||uncertainty<0
          ||uncertainty>HUD_MAX_UNCERTAINTY_METERS)return null;
      const steps=this.stepsForMap(mapIds).filter(s=>!s.done&&this.isStepUnlocked(s));
      let chosen=null,closest=Infinity,remembered=null;
      for(const step of steps) {
        for(const loc of step.locations) {
          const distance=distanceToQuestZone(player,loc);
          const threshold=(step.id===previousId?HUD_EXIT_METERS:HUD_ENTER_METERS);
          if(!Number.isFinite(distance)||distance+uncertainty>threshold)continue;
          const item={...step,nearest:loc,distance,locationUncertain:false};
          if(step.id===previousId && (!remembered||distance<remembered.distance))
            remembered=item;
          if(!chosen||distance<closest
              ||(distance===closest&&step.id.localeCompare(chosen.id)<0)){
            chosen=item;closest=distance;
          }
        }
      }
      // Hysteresis prevents flicker and switches only when another objective
      // is appreciably closer, never to an out-of-zone pinned/focused target.
      if(remembered && (!chosen||remembered.distance<=chosen.distance+10))
        return remembered;
      return chosen;
    }
    nextForMap(mapIds,player) {
      // UI HUD runs at up to 10Hz. Only ONE next step is needed,
      // so avoid O(n² log n) full-route sorting on every pose update.
      const steps=this.stepsForMap(mapIds).filter(s=>!s.done&&this.isStepUnlocked(s));
      const at=player?.position||player||null;
      if(!this.positionReliable(player)) {
        const focused=steps.find(s=>s.id===this.state.focused);
        const retained=steps.find(s=>s.id===this.lastReliableTarget);
        const selected=focused||retained||steps[0];
        return selected?{...selected,nearest:selected.locations[0],distance:null,
          locationUncertain:true}:null;
      }
      let chosen=null,closest=Infinity,chosenPlace=null;
      for(const step of steps){
        if(step.done)continue;
        let best=Infinity,nearest=null;
        for(const place of step.locations){
          const d=distanceTo(at,place);
          if(d<best){best=d;nearest=place;}
        }
        if(step.id===this.state.focused) {
          this.lastReliableTarget=step.id;
          return {...step,nearest:nearest||step.locations[0],distance:best,locationUncertain:false};
        }
        if(!chosen || best<closest
          ||(best===closest && step.id.localeCompare(chosen.id)<0)){
          chosen=step;closest=best;chosenPlace=nearest||step.locations[0];
        }
      }
      if(chosen)this.lastReliableTarget=chosen.id;
      return chosen?{...chosen,nearest:chosenPlace,distance:closest,locationUncertain:false}:null;
    }
    suggested(mapIds,player) {
      const steps=this.stepsForMap(mapIds).filter(s=>!s.done&&this.isStepUnlocked(s));
      if(!steps.length)return [];
      const reliable=this.positionReliable(player);
      const result=[];let at=reliable?player?.position||player:null;
      const pool=steps.slice();
      while(pool.length){
        pool.sort((a,b)=>{
          const da=Math.min(...a.locations.map(l=>distanceTo(at,l)));
          const db=Math.min(...b.locations.map(l=>distanceTo(at,l)));
          return da-db || a.id.localeCompare(b.id);
        });
        const step=pool.shift();
        const nearest=step.locations.slice().sort((a,b)=>distanceTo(at,a)-distanceTo(at,b))[0];
        const distance=distanceTo(at,nearest);
        result.push({...step,nearest,distance:reliable?distance:null,locationUncertain:!reliable});
        at=nearest.position;
      }
      const focusedIndex=result.findIndex(x=>x.id===this.state.focused);
      if(focusedIndex>0){const chosen=result.splice(focusedIndex,1)[0];result.unshift(chosen);}
      return result;
    }
    countActive(){return this.state.active.filter(id=>this.byId.has(id)).length;}
  }
  return {Planner,COLORS,colorFor,objectiveLocations,makeTask,normalizeState,
    distanceTo,distanceToQuestZone,HUD_ENTER_METERS,HUD_EXIT_METERS};
});
