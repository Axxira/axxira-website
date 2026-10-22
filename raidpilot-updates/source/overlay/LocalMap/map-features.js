(() => {
  "use strict";
  const $=id=>document.getElementById(id);
  let deadline=null,timerPending=false,timerReadState="waiting",interval=null;
  function renderTimer(){
    const el=$("raid-timer");if(!el)return;
    const seconds=deadline===null?null:Math.max(0,Math.ceil((deadline-performance.now())/1000));
    el.hidden=seconds===null&&!timerPending;
    el.textContent=seconds===null?"--:--":`${Math.floor(seconds/60).toString().padStart(2,"0")}:${(seconds%60).toString().padStart(2,"0")}`;
    el.dataset.state=timerReadState;el.classList.toggle("critical",seconds!==null&&seconds<=300);
    el.title=seconds===null?"Temps non synchronisé : rendre le panneau O lisible.":"Temps restant synchronisé avec le raid";
  }
  function setTimer(p){
    if(typeof p.remainingSeconds!=="number"||!Number.isFinite(p.remainingSeconds)||p.remainingSeconds<0||p.remainingSeconds>10800)return false;
    deadline=performance.now()+p.remainingSeconds*1000;timerPending=false;timerReadState="synchronized";
    if(interval===null)interval=window.setInterval(renderTimer,250);renderTimer();return true;
  }
  function clearTimer(pending=false,status="waiting"){
    deadline=null;timerPending=pending===true;timerReadState=status;
    if(interval!==null)window.clearInterval(interval);interval=null;renderTimer();return true;
  }
  function clearQuest(){const el=$("quest-context");if(el)el.hidden=true;}
  function showQuest(p){
    if(typeof p.name!=="string"||!p.name.trim()||typeof p.instruction!=="string"||!p.instruction.trim()){clearQuest();return false;}
    const el=$("quest-context");if(!el)return false;
    for(const key of ["name","where","instruction","note"]){$("quest-"+key).textContent=String(p[key]||"").slice(0,1800);}
    el.title=[p.name,p.where,p.instruction,p.note].filter(Boolean).join("\n");el.hidden=false;return true;
  }
  function renderGuidance(p){
    const el=$("guidance-hud");if(!el)return;
    el.hidden=!p;if(!p)return;
    $("guidance-name").textContent=String(p.name||"Extraction").slice(0,200);
    const details=[];
    if(p.arrived)details.push("À proximité");else if(Number.isFinite(p.distance))details.push(`${Math.round(p.distance)} m`);
    if(Number.isFinite(p.vertical)&&Math.abs(p.vertical)>2)details.push(p.vertical>0?"Plus haut":"Plus bas");
    if(p.conditional)details.push("Conditions à vérifier");
    if(p.needsReanchor)details.push("Recaler la position");else if(p.estimated||p.provisional)details.push("Position estimée");
    $("guidance-details").textContent=details.join(" · ");
    const arrow=$("guidance-arrow");arrow.hidden=!Number.isFinite(p.angle);arrow.style.transform=`rotate(${Number.isFinite(p.angle)?p.angle:0}deg)`;
  }
  window.RaidPilotFeatures={setTimer,clearTimer,renderTimer,showQuest,clearQuest,renderGuidance,reset(){clearTimer();clearQuest();renderGuidance(null);}};
  window.addEventListener("pagehide",()=>{if(interval!==null)window.clearInterval(interval);});
})();
