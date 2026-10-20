/* RaidPilot automatic map levels. Public Leaflet metadata + existing pose observations only.
   No screenshot, input simulation, network call or game-memory access in this module. */
(function (root, factory) {
    const api = factory();
    if (typeof module === "object" && module.exports) module.exports = api;
    else root.RaidPilotFloors = api;
})(typeof globalThis !== "undefined" ? globalThis : this, function () {
    "use strict";
    const BASE = "__base__";
    const finite = n => typeof n === "number" && Number.isFinite(n);
    const pointOk = p => p && [p.x, p.y, p.z].every(v => finite(v) && Math.abs(v) <= 20000);
    const distance = (a,b) => Math.hypot(a.x-b.x,a.z-b.z);
    function regions(raw) {
        if (!Array.isArray(raw) || raw.length > 128) return [];
        const result=[];
        for (const e of raw) {
            if (!Array.isArray(e?.height) || e.height.length!==2 || !e.height.every(finite) || e.height[0]>=e.height[1]) continue;
            const r={low:e.height[0],high:e.height[1],boxes:null};
            if (e.bounds!==undefined) {
                if (!Array.isArray(e.bounds) || e.bounds.length>256) continue;
                const boxes=[];
                for (const b of e.bounds) {
                    if (!Array.isArray(b) || !Array.isArray(b[0]) || !Array.isArray(b[1])
                        || b[0].length!==2 || b[1].length!==2 || ![...b[0],...b[1]].every(finite)) continue;
                    const box={x0:Math.min(b[0][0],b[1][0]),x1:Math.max(b[0][0],b[1][0]),
                        z0:Math.min(b[0][1],b[1][1]),z1:Math.max(b[0][1],b[1][1])};
                    if (box.x0<box.x1 && box.z0<box.z1) boxes.push(box);
                }
                if (!boxes.length) continue; // malformed bounds never mean an unbounded floor
                r.boxes=boxes;
            }
            result.push(r);
        }
        return result;
    }
    function horizontal(r,p,margin=0) {
        return r.boxes===null || r.boxes.some(b=>p.x>=b.x0+margin && p.x<=b.x1-margin && p.z>=b.z0+margin && p.z<=b.z1-margin);
    }
    function inside(r,p,strict=false) {
        const yMargin=strict?Math.min(0.15,(r.high-r.low)*0.1):0;
        return p.y>=r.low+yMargin && p.y<r.high-yMargin && horizontal(r,p,strict?0.25:0);
    }
    function classify(catalog,p,strict=true) {
        if (!pointOk(p)) return {kind:"invalid",id:null};
        let hits=catalog.levels.filter(l=>l.regions.some(r=>inside(r,p,strict)));
        const bounded=hits.filter(l=>l.regions.some(r=>r.boxes!==null && inside(r,p,strict)));
        if (bounded.length) hits=bounded; // local basements override a broad global height band
        if (hits.length===1) return {kind:"level",id:hits[0].id};
        if (hits.length>1) return {kind:"ambiguous",id:null};
        if (strict && catalog.levels.some(l=>l.regions.some(r=>inside(r,p,false)))) return {kind:"boundary",id:null};
        if (catalog.base.some(r=>inside(r,p,false))) return {kind:"base",id:BASE};
        return {kind:"unmapped",id:null};
    }
    function label(name) {
        const n=String(name??"").replace(/<[^>]*>/g,"").trim().slice(0,80);
        const names={"Underground":"Souterrain","Bunkers":"Bunkers","Tunnels":"Tunnels","Garage":"Garage",
            "Technical":"Niveau technique","Second Level":"Niveau 2","Infirmary":"Infirmerie","Helipad":"Héliport",
            "Gym/Canteen":"Gym / cantine","Accommodation (lower)":"Logements bas","Accommodation (mid)":"Logements interm.",
            "Accommodation (upper)":"Logements hauts","Officers' Deck":"Pont des officiers","Stairs (blocked)":"Escalier bloqué",
            "Bridge":"Passerelle","Bridge Roof":"Toit passerelle","Control Room":"Salle de contrôle",
            "Engine Room":"Salle des machines","Engine Room (upper)":"Machines haut","Fuel Pumps (lower)":"Pompes bas",
            "Fuel Pumps":"Pompes","Storage/Security":"Stockage / sécurité"};
        const floor=/^(\d+)(?:st|nd|rd|th) Floor$/.exec(n);
        return floor?"Niveau "+floor[1]:(names[n]??n);
    }
    class Tracker {
        constructor(){this.reset();}
        reset(){this.catalog=null;this.selected=null;this.anchorId=-1;this.acceptedAnchor=null;this.state=null;this.generation=-1;}
        setCatalog(catalog){this.reset();this.catalog=catalog;}
        update(payload) {
            const c=this.catalog;
            if(!c)return this.result("loading",null,"Chargement des niveaux",false);
            if(payload.generation!==this.generation){this.selected=null;this.anchorId=-1;this.acceptedAnchor=null;this.generation=payload.generation;}
            if(payload.map!==c.map)return this.result("loading",null,"Carte en cours de changement",false);
            if(!payload.enabled)return this.result("disabled",null,"Niveaux auto désactivés",false);
            if(!c.levels.length)return this.result("single_map",null,"Vue unique publiée",false);
            if(!payload.active)return this.result("paused",this.selected,"Altitude en attente",false);
            const p=payload.position, a=payload.anchor;
            if(!pointOk(p)||!finite(payload.uncertainty)||payload.uncertainty<0||payload.uncertainty>8
                ||!finite(payload.poseAge)||payload.poseAge<0||payload.poseAge>2) {
                return this.result("uncertain",this.selected,"Position à confirmer",false);
            }
            if(!a||!pointOk(a)||!Number.isSafeInteger(a.id)||!finite(a.age)||a.age<0||a.age>36000
                ||!["native","visual"].includes(a.source))return this.result("waiting_anchor",this.selected,"Attente d'altitude",false);
            const moved=distance(p,a);
            // A repeated timer tick does not turn an old height into a new vertical measurement.
            if(a.id>this.anchorId && a.age<=15 && moved<=3) {
                this.anchorId=a.id;
                const choice=classify(c,a,true);
                if(choice.id!==null){this.selected=choice.id;this.acceptedAnchor={...a};}
                else {
                    this.acceptedAnchor=null;
                    return this.result(choice.kind==="boundary"||choice.kind==="ambiguous"?"boundary":"uncertain",
                        this.selected,"Entre niveaux / non confirmé",false);
                }
            }
            if(this.selected===null)return this.result("waiting_anchor",null,"Attente d'altitude récente",false);
            const selected=c.levels.find(l=>l.id===this.selected);
            // Exit a locally bounded building/bunker using XY only; never infer a new upper floor from stale Y.
            if(selected && selected.regions.every(r=>r.boxes!==null)
                && !selected.regions.some(r=>horizontal(r,p,-(3+payload.uncertainty)))) {
                this.selected=BASE;this.acceptedAnchor=null;
                return this.result("overview",BASE,"Hors du secteur mesuré",false);
            }
            if(!this.acceptedAnchor || this.acceptedAnchor.id!==a.id)
                return this.result("uncertain",this.selected,"Altitude à confirmer",false);
            if(a.age>60 || moved>30) {
                this.selected=BASE;this.acceptedAnchor=null;
                return this.result("overview",BASE,"Altitude périmée · vue générale",false);
            }
            const fresh=a.age<=4 && moved<=1.5;
            const status=fresh && a.source==="native"?"measured":fresh?"recognized":"estimated";
            return this.result(status,this.selected,status==="measured"?"Altitude reçue":status==="recognized"?"Repère reconnu":"Dernier niveau · estimé",fresh);
        }
        result(status,selected,detail,filter){
            const level=this.catalog?.levels.find(l=>l.id===selected);
            return this.state={status,selection:selected,name:level?label(level.name):selected===BASE?"Vue de base":"Niveau non confirmé",
                detail,filter:Boolean(filter && selected!==null),levelCount:this.catalog?.levels.length??0};
        }
    }

    function createController(getMap,document,hostWindow) {
        const tracker=new Tracker();let map=null,catalog=null,entries=[],baseRefs=[],layerSignature="",lastPayload=null;
        let changeDepth=0,lockLevels=false,sequence=-1,filterKey="",lastSeenAt=0;
        const now=()=>hostWindow.performance.now();
        function badge(state){
            let el=document.getElementById("raidpilot-auto-floor");
            if(!el){el=document.createElement("div");el.id="raidpilot-auto-floor";
                const title=document.createElement("strong"),detail=document.createElement("span");el.append(title,detail);document.body?.appendChild(el);}
            const title=state.status==="single_map"?"AUTO · Vue unique":"AUTO · "+state.name;
            const detail=state.status==="disabled"?"Désactivé":state.detail;
            if(el.children[0].textContent!==title)el.children[0].textContent=title;
            if(el.children[1].textContent!==detail)el.children[1].textContent=detail;
            el.dataset.state=state.status;el.title=title+"\n"+detail+"\nLes changements de hauteur dépendent des observations reçues; aucun recalage supplémentaire n'est déclenché.";
        }
        const isLevel=l=>l?.options?.type==="map-layer"&&l.options.layerType==="level";
        function clearFilter(){
            document.documentElement.classList.remove("raidpilot-auto-floor-filter");
            for(const el of document.querySelectorAll(".raidpilot-other-floor"))el.classList.remove("raidpilot-other-floor");
            filterKey="";
        }
        function safeToOwn(l){return lockLevels && !changeDepth && map?.options?.id===catalog?.map
            && isLevel(l) && entries.some(e=>e.layer===l) && baseRefs.some(b=>map.hasLayer(b));}
        function wrapMap(m){
            if(m.__raidPilotAutoFloorGuard)return;
            const originalAdd=m.addLayer,originalRemove=m.removeLayer;
            m.addLayer=function(l){if(m===map && safeToOwn(l))return this;return originalAdd.call(this,l);};
            m.removeLayer=function(l){if(m===map && safeToOwn(l))return this;return originalRemove.call(this,l);};
            m.__raidPilotAutoFloorGuard=true;
        }
        function refresh(){
            const candidate=getMap();
            if(!candidate?.layerControl || !Array.isArray(candidate.layerControl._layers))return false;
            if(candidate!==map){map=candidate;layerSignature="";lockLevels=false;tracker.reset();clearFilter();wrapMap(map);}
            const all=map.layerControl._layers;
            const levels=all.filter(e=>isLevel(e?.layer));
            const bases=Object.values(map._layers??{}).filter(l=>l?.options?.type==="map-layer"&&l.options.layerType==="main");
            if(!bases.length)return false;
            const sig=String(map.options.id)+"|"+levels.map(e=>e.layer._leaflet_id??e.layer.options.name).join("|")
                +"|"+bases.map(b=>b._leaflet_id??b.options.layerName).join("|");
            if(sig!==layerSignature){
                entries=levels;baseRefs=bases;layerSignature=sig;lockLevels=false;
                catalog={map:String(map.options.id),base:regions(bases[0].options.extents),levels:levels.slice(0,64).map((e,i)=>({
                    id:String(i),name:e.name||e.layer.options.name||"Niveau",regions:regions(e.layer.options.extents),layer:e.layer
                })).filter(l=>l.regions.length)};
                tracker.setCatalog(catalog);clearFilter();
            }
            return true;
        }
        function restoreBase(){
            for(const b of baseRefs){
                const groups=b._image?.children?.[0]?.children;
                if(groups)for(const g of groups){
                    if(g.classList?.contains("base-layer"))g.classList.remove("hidden-layer");
                    else if(g.classList?.contains("overlay-layer"))g.classList.add("hidden-layer");
                }
                b._image?.classList?.remove("off-level");b._container?.classList?.remove("off-level");
            }
        }
        function apply(state){
            if(!map || !catalog)return;
            if(state.selection!==null && state.status!=="disabled"){
                const target=catalog.levels.find(l=>l.id===state.selection)?.layer;
                changeDepth++;
                try{
                    for(const e of entries)if(e.layer!==target && map.hasLayer(e.layer))map.removeLayer(e.layer);
                    if(target && !map.hasLayer(target))map.addLayer(target);
                    // Removing an SVG overlay in upstream can leave every <g> hidden. Restore ONLY the base groups.
                    if(state.selection===BASE)restoreBase();
                    lockLevels=true;
                }finally{changeDepth--;}
            }else if(state.status==="disabled")lockLevels=false;
            const key=state.selection+"|"+state.filter+"|"+layerSignature;
            if(key!==filterKey){
                clearFilter();filterKey=key;
                if(state.filter){
                    document.documentElement.classList.add("raidpilot-auto-floor-filter");
                    for(const marker of Object.values(map._layers??{})){
                        const icon=marker._icon,p=marker.options?.position;
                        if(!icon?.classList || !pointOk(p))continue;
                        if(!icon.matches(".raidpilot-quest-marker, .raidpilot-rare-loot-marker, .extract-icon"))continue;
                        const part=classify(catalog,p,false);
                        // Unknown/overlapping locations stay visible; attributed exits are dimmed, never removed.
                        icon.classList.toggle("raidpilot-other-floor",part.id!==null&&part.id!==state.selection);
                    }
                }
            }
        }
        function set(payload){
            if(!payload || !Number.isSafeInteger(payload.sequence)||payload.sequence<sequence)return {status:"rejected"};
            sequence=payload.sequence;lastPayload=payload;lastSeenAt=now();
            if(!refresh()){const s=tracker.result("loading",null,"Chargement des niveaux",false);badge(s);return s;}
            const state=tracker.update(payload);apply(state);badge(state);return state;
        }
        function clear(payload){
            if(!payload||!Number.isSafeInteger(payload.sequence)||payload.sequence<sequence)return;
            sequence=payload.sequence;lastPayload=null;lockLevels=false;tracker.reset();clearFilter();
            badge({status:"waiting_anchor",name:"Niveau non confirmé",detail:"Attente du raid",levelCount:0});
            // No old measurement is restored after a raid change.
            if(map){changeDepth++;try{for(const e of entries)if(map.hasLayer(e.layer))map.removeLayer(e.layer);restoreBase();}finally{changeDepth--;}}
            layerSignature="";
        }
        function refreshMarkers(){filterKey="";if(tracker.state){apply(tracker.state);badge(tracker.state);}}
        const watchdog=hostWindow.setInterval(()=>{
            if(lastPayload && now()-lastSeenAt>2200){clearFilter();lockLevels=false;
                badge({status:"uncertain",name:tracker.state?.name??"Niveau non confirmé",detail:"Suivi interrompu",levelCount:catalog?.levels.length??0});}
        },1000);
        hostWindow.addEventListener?.("pagehide",()=>{hostWindow.clearInterval(watchdog);lockLevels=false;clearFilter();},{once:true});
        return {set,clear,refreshMarkers,getState:()=>tracker.state,
            getCatalog:()=>catalog?{map:catalog.map,base:catalog.base,levelCount:catalog.levels.length,levels:catalog.levels.map(l=>({id:l.id,name:label(l.name),regions:l.regions,active:map.hasLayer(l.layer)}))}:null};
    }
    return {BASE,regions,inside,horizontal,classify,label,Tracker,createController};
});