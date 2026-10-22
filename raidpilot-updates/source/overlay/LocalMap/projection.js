(function (root, factory) {
  const api = factory();
  if (typeof module !== "undefined" && module.exports) module.exports = api;
  else root.RPProjection = api;
})(typeof globalThis !== "undefined" ? globalThis : this, function () {
  "use strict";

  function finite(value, fallback) {
    const n = Number(value);
    return Number.isFinite(n) ? n : fallback;
  }

  function rotateXZ(x, z, degrees) {
    const r = finite(degrees, 0) * Math.PI / 180;
    if (!r) return { x: finite(x, 0), z: finite(z, 0) };
    const cos = Math.cos(r);
    const sin = Math.sin(r);
    const px = finite(x, 0);
    const pz = finite(z, 0);
    return {
      x: px * cos - pz * sin,
      z: px * sin + pz * cos
    };
  }

  function rotatedBounds(bounds, rotation) {
    if (!Array.isArray(bounds) || bounds.length < 2) return null;
    const xs = [finite(bounds[0][0], 0), finite(bounds[1][0], 0)];
    const zs = [finite(bounds[0][1], 0), finite(bounds[1][1], 0)];
    const corners = [
      rotateXZ(xs[0], zs[0], rotation),
      rotateXZ(xs[0], zs[1], rotation),
      rotateXZ(xs[1], zs[0], rotation),
      rotateXZ(xs[1], zs[1], rotation)
    ];
    return {
      minX: Math.min(...corners.map(p => p.x)),
      maxX: Math.max(...corners.map(p => p.x)),
      minZ: Math.min(...corners.map(p => p.z)),
      maxZ: Math.max(...corners.map(p => p.z))
    };
  }

  function projectNormalized(position, mapMeta, calibration) {
    const pos = position || {};
    if (calibration && Array.isArray(calibration.affine) && calibration.affine.length === 6) {
      const a = calibration.affine.map(v => finite(v, 0));
      const px = a[0] * finite(pos.x, 0) + a[1] * finite(pos.z, 0) + a[2];
      const py = a[3] * finite(pos.x, 0) + a[4] * finite(pos.z, 0) + a[5];
      const w = Math.max(1, finite(calibration.width, 1));
      const h = Math.max(1, finite(calibration.height, 1));
      return { u: px / w, v: py / h };
    }

    const bounds = rotatedBounds(mapMeta && mapMeta.bounds, mapMeta && mapMeta.rotation);
    if (!bounds) return { u: 0.5, v: 0.5 };
    const p = rotateXZ(pos.x, pos.z, mapMeta.rotation);
    const width = Math.max(1e-9, bounds.maxX - bounds.minX);
    const height = Math.max(1e-9, bounds.maxZ - bounds.minZ);
    return {
      u: (p.x - bounds.minX) / width,
      // Tarkov.dev's CRS uses a negative Y scale. Screen Y therefore runs from max Z down to min Z.
      v: (bounds.maxZ - p.z) / height
    };
  }

  function toPixels(position, mapMeta, width, height, calibration) {
    const n = projectNormalized(position, mapMeta, calibration);
    return {
      x: n.u * Math.max(1, finite(width, 1)),
      y: n.v * Math.max(1, finite(height, 1)),
      inBounds: n.u >= 0 && n.u <= 1 && n.v >= 0 && n.v <= 1
    };
  }

  function pointInExtentBounds(position, boundsSpec) {
    if (!Array.isArray(boundsSpec) || boundsSpec.length < 2) return true;
    const a = boundsSpec[0], b = boundsSpec[1];
    if (!Array.isArray(a) || !Array.isArray(b)) return true;
    const x = finite(position && position.x, 0);
    const z = finite(position && position.z, 0);
    const minX = Math.min(finite(a[0], x), finite(b[0], x));
    const maxX = Math.max(finite(a[0], x), finite(b[0], x));
    const minZ = Math.min(finite(a[1], z), finite(b[1], z));
    const maxZ = Math.max(finite(a[1], z), finite(b[1], z));
    return x >= minX && x <= maxX && z >= minZ && z <= maxZ;
  }

  function markerOverlapsExtent(marker, extent) {
    if (!marker || !extent || !Array.isArray(extent.height)) return false;
    const y = finite(marker.position && marker.position.y, NaN);
    if (!Number.isFinite(y) && marker.top == null && marker.bottom == null) return false;
    const top = finite(marker.top, y);
    const bottom = finite(marker.bottom, y);
    const lo = Math.min(finite(extent.height[0], -Infinity), finite(extent.height[1], Infinity));
    const hi = Math.max(finite(extent.height[0], -Infinity), finite(extent.height[1], Infinity));
    if (top < lo || bottom > hi) return false;

    if (Array.isArray(extent.bounds) && extent.bounds.length) {
      return extent.bounds.some(b => pointInExtentBounds(marker.position || {}, b));
    }
    return true;
  }

  function markerOnFloor(marker, mapMeta, floorName) {
    if (!floorName || floorName === "AUTO" || floorName === "BASE") return true;
    const floors = (mapMeta && mapMeta.floors) || [];
    const floor = floors.find(f => String(f.name).toLowerCase() === String(floorName).toLowerCase());
    if (!floor) return true;
    return (floor.extents || []).some(ext => markerOverlapsExtent(marker, ext));
  }

  return { rotateXZ, rotatedBounds, projectNormalized, toPixels, pointInExtentBounds, markerOverlapsExtent, markerOnFloor };
});
