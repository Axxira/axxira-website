(function (root, factory) {
  "use strict";
  const api = factory();
  if (typeof module !== "undefined" && module.exports) module.exports = api;
  if (root) root.RaidPilotKeyGuide = api;
})(typeof globalThis !== "undefined" ? globalThis : this, function () {
  "use strict";

  const MAX_LOCKS = 1200;
  const MAX_ACCESS_KEYS = 64;
  function arrayOf(value) {
    return Array.isArray(value) ? value : value && typeof value === "object"
      ? Object.values(value) : [];
  }
  function refId(ref) {
    if (typeof ref === "string") return ref.trim().slice(0, 128);
    if (!ref || typeof ref !== "object") return "";
    return typeof ref.id === "string" ? ref.id.slice(0, 128)
      : typeof ref.itemId === "string" ? ref.itemId.slice(0, 128) : "";
  }
  function readable(value) {
    if (typeof value !== "string") return "";
    const s = value.replace(/[\u0000-\u001f\u007f]/g, "").trim().slice(0, 120);
    // Tarkov game IDs and unresolved localization tokens are not door names.
    return s && !/^[0-9a-f]{24}$/i.test(s)
      && !/^(?:loc|key)[_-]/i.test(s)
      ? s : "";
  }
  function label(text, translation) {
    const t = typeof text === "string" && translation &&
      Object.prototype.hasOwnProperty.call(translation, text)
      ? translation[text] : null;
    return readable(t) || readable(text);
  }
  function itemFor(ref, itemRoot, translations) {
    const id = refId(ref), items = itemRoot?.items || {};
    const fromCatalogue = id && !Array.isArray(items) ? items[id] : null;
    const fromArray = !fromCatalogue && id && Array.isArray(items)
      ? items.find(x => x?.id === id) : null;
    const data = fromCatalogue || fromArray || (ref && typeof ref === "object" ? ref : {});
    const name = label(data.name, translations) ||
      label(ref && typeof ref === "object" ? ref.name : "", translations) ||
      label(data.normalizedName, translations);
    const shortName = label(data.shortName, translations);
    const wiki = typeof data.wikiLink === "string" &&
      /^https:\/\/escapefromtarkov\.fandom\.com\/wiki\/[a-z0-9_%-]+$/i.test(data.wikiLink)
      ? data.wikiLink : "";
    const uses = Number(data.properties?.units);
    return {
      id, name: name || "Clé non identifiée", shortName,
      identified: !!name, wiki,
      uses: Number.isInteger(uses) && uses > 0 && uses <= 100 ? uses : null
    };
  }
  function positionOf(raw) {
    if (!raw || typeof raw !== "object" ||
      !Number.isFinite(raw.x) || !Number.isFinite(raw.z) ||
      Math.abs(raw.x) > 100000 || Math.abs(raw.z) > 100000 ||
      raw.y != null && (!Number.isFinite(raw.y) || Math.abs(raw.y) > 100000))
      return null;
    return {x:raw.x, y:raw.y == null ? null : raw.y, z:raw.z};
  }
  function insideBounds(pos, bounds) {
    if (!Array.isArray(bounds) || bounds.length === 0) return true;
    return bounds.some(b => Array.isArray(b) && b.length === 2
      && Array.isArray(b[0]) && Array.isArray(b[1])
      && b[0].length >= 2 && b[1].length >= 2
      && [b[0][0],b[0][1],b[1][0],b[1][1]].every(Number.isFinite)
      && pos.x >= Math.min(b[0][0],b[1][0])
      && pos.x <= Math.max(b[0][0],b[1][0])
      && pos.z >= Math.min(b[0][1],b[1][1])
      && pos.z <= Math.max(b[0][1],b[1][1]));
  }
  function floorOf(pos, mapMeta) {
    if (pos?.y == null) return "";
    const choices = [];
    for (const floor of arrayOf(mapMeta?.floors)) {
      if (typeof floor?.name !== "string") continue;
      if (arrayOf(floor.extents).some(e =>
        Array.isArray(e?.height) && e.height.length >= 2
        && e.height.slice(0,2).every(Number.isFinite)
        && pos.y >= Math.min(...e.height.slice(0,2))
        && pos.y <= Math.max(...e.height.slice(0,2))
        && insideBounds(pos, e.bounds))) choices.push(floor.name.trim());
    }
    return choices.length === 1 ? choices[0] : "";
  }
  function doorKind(lockType) {
    const type = String(lockType || "").toLowerCase();
    if (/trunk|car|vehicle|coffre/.test(type)) return "Coffre de véhicule";
    if (/container|safe/.test(type)) return "Contenant verrouillé";
    return "Porte verrouillée";
  }
  function build(map, itemRoot = {}, translations = {}, mapMeta = {}) {
    const locks = [];
    const usedKeys = new Set();
    for (const [index, lock] of arrayOf(map?.locks).slice(0, MAX_LOCKS).entries()) {
      if (!lock || typeof lock !== "object") continue;
      const key = itemFor(lock.key, itemRoot, translations);
      if (key.id) usedKeys.add(key.id);
      const position = positionOf(lock.position);
      const floor = floorOf(position, mapMeta);
      const type = doorKind(lock.lockType);
      const ordinal = index + 1;
      locks.push({
        markerId:"lock:" + (typeof lock.id === "string" && lock.id ? lock.id : index),
        ordinal, keyId:key.id, keyName:key.name, shortName:key.shortName,
        identified:key.identified, wiki:key.wiki, uses:key.uses,
        kind:type, floor, position, needsPower:lock.needsPower === true,
        name: type + " · repère " + ordinal,
        note: !key.id ? "Clé non indiquée dans le catalogue"
          : !key.identified ? "Clé associée, mais nom non publié"
          : "Association clé–serrure publiée",
        searchText: [key.name,key.shortName,type,floor,"porte","clé","cle",
          "serrure",ordinal].join(" ").toLowerCase()
      });
    }
    const access = [];
    const seen = new Set();
    for (const ref of arrayOf(map?.accessKeys).slice(0, MAX_ACCESS_KEYS)) {
      const key = itemFor(ref,itemRoot,translations);
      if (!key.id || usedKeys.has(key.id) || seen.has(key.id)) continue;
      seen.add(key.id);
      access.push({...key,kind:"Accès à la carte",
        note:"Clé d'accès publiée sans porte localisée"});
    }
    locks.sort((a,b)=>a.keyName.localeCompare(b.keyName,"fr")
      || a.ordinal-b.ordinal);
    access.sort((a,b)=>a.name.localeCompare(b.name,"fr"));
    return {
      locks,access,
      identified:locks.filter(x=>x.identified).length,
      located:locks.filter(x=>x.position).length,
      keys:new Set(locks.map(x=>x.keyId).filter(Boolean)).size
    };
  }
  function search(catalog, query) {
    const needle = String(query || "").normalize("NFD")
      .replace(/[\u0300-\u036f]/g,"").toLowerCase().trim();
    if (!needle) return {
      locks:catalog.locks.slice(),access:catalog.access.slice()
    };
    const terms = needle.split(/\s+/).filter(Boolean);
    const matches = value => terms.every(term =>
      String(value || "").normalize("NFD")
        .replace(/[\u0300-\u036f]/g,"").toLowerCase().includes(term));
    return {
      locks:catalog.locks.filter(x=>matches(x.searchText)),
      access:catalog.access.filter(x=>matches(x.name+" "+x.shortName))
    };
  }
  return {build,search,itemFor,doorKind,positionOf,floorOf};
});
