(function (root, factory) {
  "use strict";
  const moduleValue = factory();
  if (typeof module !== "undefined" && module.exports) module.exports = moduleValue;
  if (root) root.RaidPilotLootIntel = moduleValue;
})(typeof globalThis !== "undefined" ? globalThis : this, function () {
  "use strict";
  const MAX_STORED_ITEMS = 500;
  const MAX_STATIONS = 80;
  const MAX_TASKS = 3500;
  const MAX_REQUIREMENTS = 3000;
  const STORAGE_PREFIX = "raidpilot-intelligence-v1:";
  function asArray(value) {
    return Array.isArray(value) ? value
      : value && typeof value === "object" ? Object.values(value) : [];
  }
  function object(value) {
    return value && typeof value === "object" && !Array.isArray(value)
      ? value : {};
  }
  function dataRoot(value) {
    const d = object(value);
    return object(d.data || d);
  }
  function idOf(ref) {
    const id = typeof ref === "string" ? ref
      : typeof ref?.id === "string" ? ref.id
      : typeof ref?.itemId === "string" ? ref.itemId : "";
    return id.length <= 128 ? id : "";
  }
  function token(text) {
    return String(text || "").normalize("NFD")
      .replace(/[\u0300-\u036f]/g, "").toLocaleLowerCase().trim();
  }
  function display(text, dict, fallback) {
    const value = typeof text === "string" ? text : "";
    const localized = typeof dict?.[value] === "string" ? dict[value] : "";
    const name = localized.trim() || value.trim();
    if (!name || /^[a-f0-9]{24}$/i.test(name)) return fallback || "Nom indisponible";
    return name.slice(0, 150);
  }
  function countOf(ref) {
    const value = ref?.count ?? ref?.quantity ?? ref?.amount;
    const n = Number(value);
    return Number.isInteger(n) && n >= 1 && n <= 10000 ? n : 0;
  }
  function cleanState(data) {
    const raw = object(data), owned = {}, stationLevels = {};
    for (const [id,quantity] of Object.entries(object(raw.owned)).slice(0,MAX_STORED_ITEMS)) {
      const amount = Number(quantity);
      if (id.length > 0 && id.length <= 128 && Number.isInteger(amount)
        && amount >= 0 && amount <= 100000) owned[id] = amount;
    }
    for (const [id,level] of Object.entries(object(raw.stationLevels)).slice(0,MAX_STATIONS)) {
      const value = Number(level);
      if (id.length > 0 && id.length <= 128 && Number.isInteger(value)
        && value >= 0 && value <= 50) stationLevels[id] = value;
    }
    return {schemaVersion:1,owned,stationLevels};
  }
  function getCollection(value, key) {
    return asArray(dataRoot(value)[key]);
  }
  function itemsFrom(source, translations) {
    const items = object(dataRoot(source).items);
    const result = new Map();
    for (const entry of asArray(items)) {
      const id = idOf(entry);
      if (!id || result.size >= 10000 || result.has(id)) continue;
      result.set(id, {
        id,name:display(entry.name, translations, entry.shortName || "Objet inconnu"),
        shortName:display(entry.shortName, translations, ""),
        // Prices are external snapshots, not quotes or profits.
        basePrice:Number.isFinite(entry.basePrice) && entry.basePrice>=0
          ? entry.basePrice : null
      });
    }
    return result;
  }
  function extractHideout(source, items, translations) {
    const stations = getCollection(source, "hideoutStations").length
      ? getCollection(source, "hideoutStations")
      : getCollection(source, "stations").length
        ? getCollection(source, "stations")
        : getCollection(source, "hideout");
    const out=[];
    for(const raw of stations.slice(0,MAX_STATIONS)){
      const id=idOf(raw);
      if(!id)continue;
      const levels=[];
      for(const level of asArray(raw.levels).slice(0,50)){
        const number=Number(level?.level);
        if(!Number.isInteger(number)||number<1||number>50)continue;
        const ingredients=[];
        for(const ingredient of asArray(level.itemRequirements || level.requirements).slice(0,120)){
          const itemId=idOf(ingredient?.item || ingredient);
          const quantity=countOf(ingredient);
          if(!itemId||!quantity)continue;
          const name=items.get(itemId)?.name || display(
            ingredient.item?.name,translations,"Objet non identifié");
          ingredients.push({itemId,quantity,name,fir:ingredient.foundInRaid===true});
        }
        levels.push({level:number,ingredients,description:display(level.description,translations,"")});
      }
      levels.sort((a,b)=>a.level-b.level);
      out.push({id,name:display(raw.name,translations,"Module de planque"),
        levels});
    }
    return out;
  }
  function extractTaskRequirements(source, items, translations) {
    const output=[];
    for(const task of getCollection(source,"tasks").slice(0,MAX_TASKS)){
      const taskId=idOf(task);
      if(!taskId)continue;
      const title=display(task.name,translations,task.normalizedName || "Quête");
      for(const objective of asArray(task.objectives).slice(0,120)){
        const objectiveId=idOf(objective);
        const kind=String(objective?.type || "").toLowerCase();
        // Alternative item lists are deliberately NOT counted as simultaneous
        // mandatory inventory. Task steps without explicit item identity remain
        // part of QuestPilot but cannot generate an invented shopping list.
        if (!["giveitem","finditem","plantitem","plantquestitem","collectitem","pickupitem",
          "bringitem","handoveritem"].includes(kind)) continue;
        const refs=[];
        if(objective.item) refs.push(objective.item);
        if(objective.questItem) refs.push(objective.questItem);
        if(objective.markerItem) refs.push(objective.markerItem);
        if(objective.requiredItem) refs.push(objective.requiredItem);
        const alternatives=asArray(objective.items);
        if(alternatives.length===1) refs.push(alternatives[0]);
        const distinct=[...new Set(refs.map(idOf).filter(Boolean))];
        if(distinct.length!==1)continue; // No invented choice/total.
        const itemId=distinct[0], qty=countOf(objective)||1;
        const item=items.get(itemId);
        if(!item||output.length>=MAX_REQUIREMENTS)continue;
        output.push({itemId,quantity:qty,fir:objective.foundInRaid===true,
          taskId,objectiveId,taskName:title,
          type:kind,optional:objective.optional===true,
          source:"quête"});
      }
    }
    return output;
  }
  class Planner {
    constructor(storage=null,mode="regular") {
      this.storage=storage;
      this.mode="regular";
      this.state=cleanState(null);
      this.items=new Map();
      this.stations=[];
      this.taskNeeds=[];
      this.hideoutLoaded=false;
      this.knownTaskIds=new Set();
      this.tracked=new Set();
      this.completedQuests=new Set();
      this.completedObjectives=new Set();
      this.setMode(mode);
    }
    setMode(mode){
      const next=["regular","pve"].includes(mode)?mode:"regular";
      if(this.mode===next&&this.loaded)return;
      this.mode=next;
      this.loaded=true;
      let raw=null;
      try{
        const text=this.storage?.getItem(STORAGE_PREFIX+next);
        if(text && text.length<100000)raw=JSON.parse(text);
      }catch(_){}
      this.state=cleanState(raw);
    }
    save(){
      try{this.storage?.setItem(STORAGE_PREFIX+this.mode,
        JSON.stringify(this.state));return true;}catch(_){return false;}
    }
    setData({items=null,tasks=null,hideout=null,itemTranslations={},
      taskTranslations={},hideoutTranslations={},
      activeQuests=[],completedQuests=[],completedObjectives=[]}={}){
      this.items=itemsFrom(items,object(itemTranslations));
      this.taskNeeds=extractTaskRequirements(tasks,this.items,object(taskTranslations));
      this.stations=extractHideout(hideout,this.items,object(hideoutTranslations));
      this.hideoutLoaded=this.stations.length>0;
      this.tracked=new Set(asArray(activeQuests).filter(x=>typeof x==="string"));
      this.completedQuests=new Set(asArray(completedQuests).filter(x=>typeof x==="string"));
      this.completedObjectives=new Set(asArray(completedObjectives).filter(x=>typeof x==="string"));
      this.knownTaskIds=new Set(this.taskNeeds.map(x=>x.taskId));
      return this.summary();
    }
    setProgress({activeQuests=[],completedQuests=[],completedObjectives=[]}={}){
      this.tracked=new Set(asArray(activeQuests));
      this.completedQuests=new Set(asArray(completedQuests));
      this.completedObjectives=new Set(asArray(completedObjectives));
    }
    setOwned(id,quantity){
      if(!this.items.has(id))return false;
      const n=Number(quantity);
      if(!Number.isInteger(n)||n<0||n>100000)return false;
      const keys=Object.keys(this.state.owned);
      if(!Object.hasOwn(this.state.owned,id)&&keys.length>=MAX_STORED_ITEMS)
        return false;
      if(n===0)delete this.state.owned[id];
      else this.state.owned[id]=n;
      this.save();return true;
    }
    setStationLevel(id,level){
      const station=this.stations.find(x=>x.id===id);
      const n=Number(level),maximum=Math.max(0,...(station?.levels||[]).map(x=>x.level));
      if(!station||!Number.isInteger(n)||n<0||n>maximum)return false;
      this.state.stationLevels[id]=n;
      this.save();return true;
    }
    nextLevel(station){
      const current=this.state.stationLevels[station.id]||0;
      return station.levels.find(l=>l.level>current)||null;
    }
    requirements({allQuests=false,allHideout=false}={}){
      const merged=new Map();
      const add=(id,quantity,fir,reason,kind)=>{
        if(!this.items.has(id)||!quantity)return;
        if(!merged.has(id))
          merged.set(id,{...this.items.get(id),quantity:0,firQuantity:0,
            questQuantity:0,hideoutQuantity:0,reasons:[]});
        const item=merged.get(id);
        item.quantity+=quantity;
        if(fir)item.firQuantity+=quantity;
        if(kind==="quest")item.questQuantity+=quantity;
        if(kind==="hideout")item.hideoutQuantity+=quantity;
        if(item.reasons.length<10)item.reasons.push(reason);
      };
      for(const need of this.taskNeeds){
        if(need.optional||this.completedQuests.has(need.taskId)
          ||this.completedObjectives.has(need.taskId+":"+need.objectiveId)
          ||!allQuests&&!this.tracked.has(need.taskId))continue;
        add(need.itemId,need.quantity,need.fir,
          "Quête : "+need.taskName,"quest");
      }
      for(const station of this.stations){
        const levels=allHideout
          ?station.levels.filter(l=>l.level>(this.state.stationLevels[station.id]||0))
          :[this.nextLevel(station)].filter(Boolean);
        for(const level of levels)
          for(const item of level.ingredients)
            add(item.itemId,item.quantity,item.fir,
              "Planque : "+station.name+" niv. "+level.level,"hideout");
      }
      return [...merged.values()].map(item=>({
        ...item,owned:this.state.owned[item.id]||0,
        missing:Math.max(0,item.quantity-(this.state.owned[item.id]||0)),
        recommendation:item.firQuantity>0?"À garder · quête FIR"
          :item.questQuantity>0?"À garder · quête"
            :"À garder · planque"
      })).sort((a,b)=>b.missing-a.missing||a.name.localeCompare(b.name,"fr"));
    }
    search(query="",options={}){
      const text=token(query);
      let rows=this.requirements(options);
      if(text)rows=rows.filter(x=>token(x.name+" "+x.shortName+" "+x.reasons.join(" ")).includes(text));
      return rows.slice(0,300);
    }
    summary(){
      return {
        gameMode:this.mode,items:this.items.size,
        stationCount:this.stations.length,hideoutLoaded:this.hideoutLoaded,
        trackedQuests:this.tracked.size,
        ownedItems:Object.keys(this.state.owned).length,
        requirements:this.requirements().length
      };
    }
    itemInfo(id){
      const item=this.items.get(id);
      if(!item)return null;
      const need=this.requirements({allQuests:true,allHideout:true}).find(x=>x.id===id);
      return {...item,owned:this.state.owned[id]||0,
        knownNeed:need||null,
        assessment:need?.quantity>0?"Usages recensés dans les données de progression"
          :"Aucun besoin recensé ici : vendre reste à vérifier (crafts, trocs et autres usages non couverts)"};
    }
  }
  return {Planner,itemsFrom,extractHideout,extractTaskRequirements,cleanState};
});
