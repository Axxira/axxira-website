(function(root, factory) {
  const api = factory();
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.RPMapDisplay = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function() {
  'use strict';
  const intersects = (a,b) => a.x < b.x+b.width && a.x+a.width > b.x && a.y < b.y+b.height && a.y+a.height > b.y;
  function screenPoint(point, view) {
    return {x:view.panX + point.x*view.zoom, y:view.panY + point.y*view.zoom};
  }
  function inViewport(point, view, margin=24) {
    return point.x >= -margin && point.y >= -margin && point.x <= view.width+margin && point.y <= view.height+margin;
  }
  function shortName(name, limit=25) {
    const text = String(name || '').replace(/\s+/g,' ').trim();
    return text.length <= limit ? text : text.slice(0,limit-1).trimEnd() + '…';
  }
  // Labels are laid out in screen pixels, independently of map/world zoom.
  // Reserve icon space first so a name cannot cover another POI or the player.
  function layoutLabels(points, view, mode='auto') {
    if (mode === 'hover') return [];
    const occupied = points.map(p => ({x:p.x-10,y:p.y-10,width:20,height:20}));
    const labels = [];
    const candidates = points.filter(p => p.label && p.name).sort((a,b) => (b.priority||0)-(a.priority||0) || String(a.id).localeCompare(String(b.id)));
    for (const p of candidates) {
      const text = shortName(p.name);
      const width = Math.min(172, Math.max(42, [...text].length*6.4+12)), height=22;
      const options = [
        {x:p.x+13,y:p.y-height/2,width,height},
        {x:p.x-width-13,y:p.y-height/2,width,height},
        {x:p.x-width/2,y:p.y-height-13,width,height},
        {x:p.x-width/2,y:p.y+13,width,height}
      ];
      const box = options.find(b => b.x>=5 && b.y>=5 && b.x+b.width<=view.width-5 && b.y+b.height<=view.height-5 && !occupied.some(o => intersects(b,o)));
      if (!box) continue;
      occupied.push({x:box.x-4,y:box.y-3,width:box.width+8,height:box.height+6});
      labels.push({...box,id:p.id,text});
    }
    return labels;
  }
  return {screenPoint,inViewport,shortName,layoutLabels,intersects};
});
