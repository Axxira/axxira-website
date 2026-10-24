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
    const append = (map, p, choice, place) => {
      const pos = point(p), id = mapId(map);
      if (!id || !pos || out.length >= LIMIT_LOCATIONS) return;
      const signature = id + "|" + pos.x.toFixed(3) + "|" + pos.z.toFixed(3)
        + "|" + String(pos.y) + "|" + choice;
      if (seen.has(signature)) return;
      seen.add(signature);
      out.push({map:id, position:pos, possible:choice, place:String(place || "").slice(0,160)});
    };
    for (const z of asArray(obj?.zones).slice(0,LIMIT_LOCATIONS)) {
      append(z.map, z.position, false, z.name);
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
    for (const obj of asArray(raw.objectives).slice(0,LIMIT_OBJECTIVES)) {
      const objectiveId = identifier(obj?.id);
      if (!objectiveId || byObjective.has(objectiveId)) continue;
      const locations = objectiveLocations(obj);
      if (!locations.length) continue;
      const step = {
        id:id + ":" + objectiveId, questId:id, objectiveId,
        number:objectives.length + 1, objectiveCount:0,
        name:text(translations, obj.description, obj.type || "Objectif"),
        type:String(obj.type || ""),
        optional:obj.optional === true,
        locations,
        alternative:locations.some(l=>l.possible) || (locations.length > 1
          && !["plantItem","plantQuestItem"].includes(obj.type)),
        needs:equipmentNeeds(obj,itemLookup,itemTranslations),
      };
      objectives.push(step);
      byObjective.set(objectiveId,step);
    }
    for (const step of objectives) step.objectiveCount = objectives.length;
    return {id,name,color:colorFor(id),objectives,byObjective};
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
  function defaultState() { return {schemaVersion:1,active:[],excluded:[],done:[],packed:[],focused:"",
    evidence:Object.create(null),sharedPlans:Object.create(null)}; }
  function normalizeState(input) {
    if (!input || typeof input !== "object" || input.schemaVersion !== 1) return defaultState();
    return {
      schemaVersion:1,active:sanitizeArray(input.active), excluded:sanitizeArray(input.excluded),
      done:sanitizeArray(input.done), packed:sanitizeArray(input.packed),
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
  class Planner {
    constructor(storage, mode) {
      this.storage=storage || null;
      this.mode=modeName(mode);
      this.tasks=[];
      this.byId=new Map();
      this.state=defaultState();
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
      this.mode=modeName(mode);this.tasks=[];this.byId.clear();this.load();
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
      return Object.entries(this.state.sharedPlans||{})
        .filter(([,plan])=>plan.active.includes(String(id))).map(([name])=>name);
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
      if(!label||/[\\u0000-\\u001f]/.test(label)
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
    statusFor(id) { return this.state.evidence?.[String(id)]||null; }
    stageStatus(stepId,questId) {
      if(this.isQuestCompleted(questId))return {done:true,source:"eft-local-log"};
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
    stepsForMap(mapIds) {
      const ids=mapIds instanceof Set?mapIds:new Set(mapIds||[]);
      const list=[];
      for(const quest of this.tasks) {
        if(!this.isActive(quest.id))continue;
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
    suggested(mapIds,player) {
      const steps=this.stepsForMap(mapIds).filter(s=>!s.done);
      if(!steps.length)return [];
      const result=[];let at=player?.position||player||null;
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
        result.push({...step,nearest,distance});
        at=nearest.position;
      }
      const focusedIndex=result.findIndex(x=>x.id===this.state.focused);
      if(focusedIndex>0){const chosen=result.splice(focusedIndex,1)[0];result.unshift(chosen);}
      return result;
    }
    countActive(){return this.state.active.filter(id=>this.byId.has(id)).length;}
  }
  return {Planner,COLORS,colorFor,objectiveLocations,makeTask,normalizeState,distanceTo};
});
