(() => {
  "use strict";
  // These are the exact entry points called by OverlayLocalPlayerMarker.cs
  // and OverlaySquad.cs. A picture alone must never be treated as a ready map.
  let generation = -1;
  let currentMap = null;
  let extractionsConfirmed = false;
  let guidanceGeneration = -1;
  let routeGeneration=-1,questConfirmed=false,timerSequence=-1,contextSequence=-1,floorSequence=-1;
  const team = new Map();
  const latestMemberTimestamp = new Map();
  const latestQuestPlanTimestamp = new Map();
  let renderedTeamMap = null;
  const pending = new Map();
  const api = () => window.RaidPilotLocalMap;
  // Support the previous minimal map bridge as well as the full LocalFirst renderer.
  const canonicalSquadMap = key => typeof api()?.canonicalMapKey === "function"
    ? api().canonicalMapKey(key)
    : String(key ?? "").trim().toLowerCase().replace(/[\s_]+/g, "-");
  const ready = () => api()?.health().initialized === true;
  const valid = p => p && p.x != null && p.z != null && Number.isFinite(Number(p.x)) && Number.isFinite(Number(p.z));
  function submit(key, fn, next = -1) {
    if (!ready()) {
      const previous=pending.get(key);if(previous&&previous.generation>next)return false;
      pending.set(key,{fn,generation:next});return true;
    }
    return fn();
  }
  function acceptGeneration(next) {
    if (!Number.isInteger(next) || next < generation) return false;
    if (next > generation) { generation = next; currentMap=null;renderedTeamMap=null;extractionsConfirmed=false;questConfirmed=false;timerSequence=-1;team.clear();latestMemberTimestamp.clear();api().resetRaid(); }
    return true;
  }
  window.__raidPilotSetLocalPose = payload => {
    if (!valid(payload)) return false;
    return submit("player", () => {
      if (!acceptGeneration(payload.generation)) return false;
      const nextMap = canonicalSquadMap(payload.map);
      if (currentMap && nextMap !== currentMap) { team.clear(); renderedTeamMap = null; api().setTeam([]); }
      currentMap = nextMap;
      return api().setPlayer({...payload,position:{x:payload.x,y:payload.y,z:payload.z},rotation:payload.yaw});
    },payload.generation);
  };
  window.__raidPilotClearLocalPose = next => submit("player", () => {
    if (!acceptGeneration(next)) return false;
    api().setPlayer(null);
    return true;
  },next);
  window.__raidPilotSetRaidExtractions = payload => {
    if(!payload||!Array.isArray(payload.extractions)||!Number.isInteger(payload.generation))return 0;
    return submit("extractions",()=>{
      if(!acceptGeneration(payload.generation))return 0;
      extractionsConfirmed=true;return api().setRaidExtractions(payload);
    },payload.generation);
  };
  window.__raidPilotApplyRaidExtractions=()=>ready()?api().applyRaidExtractions():0;
  window.__raidPilotClearRaidExtractions=next=>submit("extractions",()=>{
    if(next===generation&&extractionsConfirmed)return false;
    if(!acceptGeneration(next))return false;extractionsConfirmed=false;api().clearRaidExtractions();return true;
  },next);
  for(const kind of ["Boundary","Mine"]){
    window[`__raidPilotSet${kind}Hazards`]=payload=>{
      if(!payload||!Number.isInteger(payload.generation)||!payload.map||!Array.isArray(payload.zones))return false;
      return submit(kind,()=>acceptGeneration(payload.generation)&&api()[`set${kind}Hazards`](payload),payload.generation);
    };
    window[`__raidPilotClear${kind}Hazards`]=next=>submit(kind,()=>acceptGeneration(next)&&api()[`clear${kind}Hazards`](),next);
  }
  window.__raidPilotShowGuidance=payload=>{
    if(!payload||!Number.isInteger(payload.generation))return 0;
    return submit("guidance",()=>{
      if(payload.generation<guidanceGeneration)return 0;guidanceGeneration=payload.generation;return api().setGuidanceTarget(payload);
    },payload.generation);
  };
  window.__raidPilotApplyGuidanceTarget=()=>{api()?.render();return api()?.applyRaidExtractions()||0;};
  window.__raidPilotClearGuidance=next=>submit("guidance",()=>{
    if(!Number.isInteger(next)||next<guidanceGeneration)return false;guidanceGeneration=next;api().clearGuidanceTarget();return true;
  },next);
  window.__raidPilotSetGameMode=mode=>submit("gameMode",()=>api().setGameMode(mode));
  window.__raidPilotSetRaidQuests=p=>{
    if(!p||!Number.isInteger(p.generation)||!Array.isArray(p.quests))return 0;
    return submit("quests",()=>{if(!acceptGeneration(p.generation))return 0;questConfirmed=true;return api().setRaidQuests(p);},p.generation);
  };
  window.__raidPilotApplyRaidQuests=()=>ready()?api().applyRaidQuests():0;
  // Native EFT log payloads contain only public task IDs, mode, outcome and time.
  // They are applied to mode-isolated local profiles, never to unrelated PvE/PvP.
  // Live squad plans are ephemeral, never imported into personal progression.
  window.__raidPilotSetTeamQuestPlan=payload=>{
    if(!payload||typeof payload.id!=="string"||!Number.isSafeInteger(payload.at))return false;
    const previous=latestQuestPlanTimestamp.get(payload.id);
    if(previous!==undefined&&payload.at<=previous)return false;
    return submit("teamquest:"+payload.id,()=>{
      const latest=latestQuestPlanTimestamp.get(payload.id);
      if(latest!==undefined&&payload.at<=latest)return false;
      const ok=api().setLiveTeamQuestPlan(payload);
      if(ok)latestQuestPlanTimestamp.set(payload.id,payload.at);
      return ok;
    },payload.at);
  };
  window.__raidPilotClearTeamQuestPlan=id=>{
    if(typeof id!=="string"||id.length>64)return false;
    pending.delete("teamquest:"+id);
    latestQuestPlanTimestamp.delete(id);
    return submit("teamquestclear:"+id,()=>api().clearLiveTeamQuestPlan(id));
  };
  window.__raidPilotClearAllTeamQuestPlans=()=>{
    for(const key of pending.keys())if(key.startsWith("teamquest:"))pending.delete(key);
    latestQuestPlanTimestamp.clear();
    return submit("teamquestclear",()=>api().clearAllLiveTeamQuestPlans());
  };
  window.__raidPilotApplyQuestLogEvents=events=>{
    if(!Array.isArray(events)||events.length>64)return 0;
    return submit("quest-log",()=>api().applyQuestLogEvents(events));
  };

  window.__raidPilotClearRaidQuests=next=>submit("quests",()=>{
    if(next===generation&&questConfirmed)return false;
    if(!acceptGeneration(next))return false;questConfirmed=false;api().clearRaidQuests();return true;
  },next);
  function acceptTimer(gen,seq){
    if(!Number.isInteger(gen)||!Number.isSafeInteger(seq)||gen<generation||(gen===generation&&seq<timerSequence))return false;
    if(!acceptGeneration(gen))return false;timerSequence=seq;return true;
  }
  window.__raidPilotSetRaidTimer=p=>{
    if(!p||typeof p.remainingSeconds!=="number"||!Number.isFinite(p.remainingSeconds)||p.remainingSeconds<0||p.remainingSeconds>10800)return false;
    return submit("timer",()=>acceptTimer(p.generation,p.sequence)&&window.RaidPilotFeatures.setTimer(p),p.sequence);
  };
  window.__raidPilotClearRaidTimer=(gen,pending,seq,status)=>submit("timer",()=>acceptTimer(gen,seq)&&window.RaidPilotFeatures.clearTimer(pending,status),seq);
  window.__raidPilotApplyRaidTimer=()=>window.RaidPilotFeatures?.renderTimer();
  window.__raidPilotShowQuestContext=p=>{
    if(!p||!Number.isSafeInteger(p.sequence))return false;
    return submit("questContext",()=>{
      if(p.sequence<contextSequence||!acceptGeneration(p.generation)||p.map!==api().health().mapKey)return false;
      contextSequence=p.sequence;return window.RaidPilotFeatures.showQuest(p);
    },p.sequence);
  };
  window.__raidPilotClearQuestContext=seq=>submit("questContext",()=>{
    if(!Number.isSafeInteger(seq)||seq<contextSequence)return false;contextSequence=seq;window.RaidPilotFeatures.clearQuest();return true;
  },seq);
  window.__raidPilotShowLearnedRoute=p=>{
    if(!p||!Number.isInteger(p.generation))return false;
    return submit("route",()=>{if(p.generation<routeGeneration)return false;const ok=api().setLearnedRoute(p);if(ok)routeGeneration=p.generation;return ok;},p.generation);
  };
  window.__raidPilotClearLearnedRoute=next=>submit("route",()=>{
    if(!Number.isInteger(next)||next<routeGeneration)return false;routeGeneration=next;api().clearLearnedRoute();return true;
  },next);
  window.__raidPilotSetFloorPose=p=>{
    if(!p||!Number.isSafeInteger(p.sequence))return {status:"rejected"};
    return submit("floors",()=>{if(p.sequence<floorSequence||!acceptGeneration(p.generation))return {status:"rejected"};floorSequence=p.sequence;return api().setFloorPose(p);},p.sequence);
  };
  window.__raidPilotClearFloors=p=>submit("floors",()=>{
    if(!Number.isSafeInteger(p?.sequence)||p.sequence<floorSequence)return false;floorSequence=p.sequence;api().clearFloorPose();return true;
  },p?.sequence);
  // Keep accepted timestamps as tombstones. A late WebView2 callback
  // must never resurrect an old teammate after they left this map.
  const squadSequence = value => Number.isSafeInteger(value) && value > 0 ? value : -1;
  window.__raidPilotSetSquadPose = payload => {
    if (!valid(payload) || !payload.memberId) return false;
    const sequence = squadSequence(payload.sentAtUnixMs);
    return submit("team:" + payload.memberId, () => {
      const activeMap = api().health().mapKey;
      if (renderedTeamMap !== activeMap) {
        team.clear();
        renderedTeamMap = activeMap;
        api().setTeam([]);
      }
      if (payload.map && canonicalSquadMap(payload.map) !== activeMap) return false;
      const last = latestMemberTimestamp.get(payload.memberId);
      if (last !== undefined && (sequence < 0 || sequence <= last)) return false;
      if (sequence >= 0) latestMemberTimestamp.set(payload.memberId, sequence);
      team.set(payload.memberId, {id:payload.memberId,name:payload.name,position:{x:payload.x,y:payload.y,z:payload.z},rotation:payload.yaw});
      api().setTeam([...team.values()]);
      return true;
    }, sequence);
  };
  window.__raidPilotRemoveSquadMember = (id, sentAtUnixMs) => {
    const sequence = squadSequence(sentAtUnixMs);
    return submit("team:" + id, () => {
      const last = latestMemberTimestamp.get(id);
      if (sequence >= 0 && last !== undefined && sequence < last) return false;
      if (sequence >= 0) latestMemberTimestamp.set(id, sequence);
      team.delete(id);
      api().setTeam([...team.values()]);
      api().clearLiveTeamQuestPlan?.(id);
      return true;
    }, sequence);
  };
  window.__raidPilotClearSquad = () => {
    for (const key of pending.keys()) if (key.startsWith("team:")) pending.delete(key);
    return submit("team-clear", () => {
      team.clear(); latestMemberTimestamp.clear(); latestQuestPlanTimestamp.clear();
      for(const key of pending.keys())if(key.startsWith("teamquest:"))pending.delete(key);
      renderedTeamMap=null; api().setTeam([]);
      api().clearAllLiveTeamQuestPlans?.();return true;
    });
  };
  window.addEventListener("raidpilot-local-initialized", () => {
    const queued = [...pending.values()]; pending.clear();
    for (const item of queued) item.fn();
  });
})();
