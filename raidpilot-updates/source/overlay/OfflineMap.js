(() => {
    "use strict";

    const container = document.getElementById("leaflet-map");
    const asset = document.getElementById("raidpilot-offline-map-asset");
    const query = new URLSearchParams(location.search);
    const requestedMap = String(query.get("map") || "customs").trim().toLowerCase();

    const listeners = new Map();
    const emit = (name) => {
        for (const handler of listeners.get(name) || []) {
            try { handler(); } catch {}
        }
    };
    const on = (name, handler) => {
        if (typeof handler !== "function") return api;
        if (!listeners.has(name)) listeners.set(name, new Set());
        listeners.get(name).add(handler);
        return api;
    };
    const off = (name, handler) => {
        listeners.get(name)?.delete(handler);
        return api;
    };

    let config = null;
    let zoom = 2;
    let center = { lat: 0, lng: 0 };
    let boundsProjected = null;

    const normalizeLatLng = (value) => {
        if (Array.isArray(value)) {
            return { lat: Number(value[0]) || 0, lng: Number(value[1]) || 0 };
        }
        if (value && typeof value === "object") {
            return { lat: Number(value.lat) || 0, lng: Number(value.lng) || 0 };
        }
        return { lat: 0, lng: 0 };
    };

    const rotate = (latLng, degrees) => {
        const angle = (Number(degrees) || 0) * Math.PI / 180;
        if (Math.abs(angle) < 1e-12) return latLng;
        const c = Math.cos(angle);
        const s = Math.sin(angle);
        const x = latLng.lng;
        const y = latLng.lat;
        return {
            lng: x * c - y * s,
            lat: x * s + y * c,
        };
    };

    const projectBase = (latLng) => {
        const rotated = rotate(latLng, config?.coordinateRotation || 0);
        const t = Array.isArray(config?.transform) ? config.transform : [1, 0, 1, 0];
        return {
            x: rotated.lng * Number(t[0] ?? 1) + Number(t[1] ?? 0),
            y: rotated.lat * (-Number(t[2] ?? 1)) + Number(t[3] ?? 0),
        };
    };

    const unprojectBase = (point) => {
        const t = Array.isArray(config?.transform) ? config.transform : [1, 0, 1, 0];
        const sx = Number(t[0] ?? 1) || 1;
        const sy = -Number(t[2] ?? 1) || -1;
        const rotated = {
            lng: (point.x - Number(t[1] ?? 0)) / sx,
            lat: (point.y - Number(t[3] ?? 0)) / sy,
        };
        return rotate(rotated, -(config?.coordinateRotation || 0));
    };

    const zoomScale = () => Math.pow(2, zoom);

    const latLngToContainerPoint = (value) => {
        const ll = normalizeLatLng(value);
        const p = projectBase(ll);
        const c = projectBase(center);
        const scale = zoomScale();
        return {
            x: ((p.x - c.x) * scale) + (container.clientWidth / 2),
            y: ((p.y - c.y) * scale) + (container.clientHeight / 2),
        };
    };

    const containerPointToLatLng = (value) => {
        const x = Number(value?.x ?? value?.[0]) || 0;
        const y = Number(value?.y ?? value?.[1]) || 0;
        const c = projectBase(center);
        const scale = zoomScale();
        return unprojectBase({
            x: c.x + ((x - container.clientWidth / 2) / scale),
            y: c.y + ((y - container.clientHeight / 2) / scale),
        });
    };

    const calculateProjectedBounds = () => {
        const raw = config?.bounds;
        if (!Array.isArray(raw) || raw.length < 2) return null;
        const x1 = Number(raw[0]?.[0]);
        const z1 = Number(raw[0]?.[1]);
        const x2 = Number(raw[1]?.[0]);
        const z2 = Number(raw[1]?.[1]);
        if (![x1, z1, x2, z2].every(Number.isFinite)) return null;

        const corners = [
            { lat: z1, lng: x1 },
            { lat: z1, lng: x2 },
            { lat: z2, lng: x1 },
            { lat: z2, lng: x2 },
        ].map(projectBase);
        return {
            minX: Math.min(...corners.map((p) => p.x)),
            maxX: Math.max(...corners.map((p) => p.x)),
            minY: Math.min(...corners.map((p) => p.y)),
            maxY: Math.max(...corners.map((p) => p.y)),
            centerWorld: {
                lat: (z1 + z2) / 2,
                lng: (x1 + x2) / 2,
            },
        };
    };

    const render = () => {
        if (!config || !boundsProjected) return;
        const scale = zoomScale();
        const centerP = projectBase(center);
        const left = ((boundsProjected.minX - centerP.x) * scale) + container.clientWidth / 2;
        const top = ((boundsProjected.minY - centerP.y) * scale) + container.clientHeight / 2;
        const width = Math.max(1, (boundsProjected.maxX - boundsProjected.minX) * scale);
        const height = Math.max(1, (boundsProjected.maxY - boundsProjected.minY) * scale);
        asset.style.left = left + "px";
        asset.style.top = top + "px";
        asset.style.width = width + "px";
        asset.style.height = height + "px";
    };

    const api = {
        options: {
            id: requestedMap,
            baseData: { coordinateRotation: 0 },
        },
        _layers: {},
        layerControl: { _layers: [] },
        getContainer: () => container,
        getCenter: () => ({ ...center }),
        getZoom: () => zoom,
        getMinZoom: () => Number(config?.minZoom ?? 1),
        getMaxZoom: () => Number(config?.maxZoom ?? 7),
        setView(value, requestedZoom) {
            center = normalizeLatLng(value);
            if (Number.isFinite(Number(requestedZoom))) {
                zoom = Math.max(api.getMinZoom(), Math.min(api.getMaxZoom(), Number(requestedZoom)));
            }
            render();
            emit("move");
            emit("zoom");
            return api;
        },
        panTo(value) {
            center = normalizeLatLng(value);
            render();
            emit("move");
            return api;
        },
        setZoom(value) {
            const next = Math.max(api.getMinZoom(), Math.min(api.getMaxZoom(), Number(value) || zoom));
            if (Math.abs(next - zoom) < 1e-9) return api;
            zoom = next;
            render();
            emit("zoom");
            return api;
        },
        zoomIn(delta = 1) { return api.setZoom(zoom + Number(delta || 1)); },
        zoomOut(delta = 1) { return api.setZoom(zoom - Number(delta || 1)); },
        latLngToContainerPoint,
        containerPointToLatLng,
        hasLayer: () => false,
        removeLayer: () => api,
        on,
        off,
        invalidateSize() {
            render();
            emit("resize");
            return api;
        },
        scrollWheelZoom: { enable() {} },
        dragging: { enable() {} },
    };

    window.__raidPilotOfflineMap = api;

    container.addEventListener("wheel", (event) => {
        event.preventDefault();
        const direction = event.deltaY < 0 ? 0.25 : -0.25;
        api.setZoom(zoom + direction);
    }, { passive: false });

    new ResizeObserver(() => api.invalidateSize()).observe(container);

    fetch("/OfflineMaps/config.json", { cache: "no-store" })
        .then((response) => {
            if (!response.ok) throw new Error("offline config unavailable");
            return response.json();
        })
        .then((items) => {
            const aliases = {
                streets: "streets-of-tarkov",
                labs: "the-lab",
                lab: "the-lab",
                labyrinth: "the-labyrinth",
                groundzero: "ground-zero",
            };
            const wanted = aliases[requestedMap] || requestedMap;
            config = Array.isArray(items)
                ? items.find((item) =>
                    String(item?.normalizedName || "").toLowerCase() === wanted)
                : null;
            if (!config) throw new Error("offline map not found: " + wanted);

            api.options.id = config.normalizedName;
            api.options.baseData = {
                coordinateRotation: Number(config.coordinateRotation || 0),
            };
            zoom = Math.max(
                Number(config.minZoom || 1),
                Math.min(
                    Number(config.maxZoom || 7),
                    Number(config.minZoom || 1) + 1));
            boundsProjected = calculateProjectedBounds();
            if (!boundsProjected) throw new Error("offline bounds invalid");
            center = boundsProjected.centerWorld;

            asset.src = config.assetUrl
                ? String(config.assetUrl)
                : "/OfflineMaps/" + encodeURIComponent(config.asset);
            asset.onload = () => {
                render();
                emit("viewreset");
                document.documentElement.dataset.raidpilotOfflineReady = "1";
            };
            asset.onerror = () => {
                document.documentElement.dataset.raidpilotOfflineError = "asset";
            };
            render();
        })
        .catch((error) => {
            document.documentElement.dataset.raidpilotOfflineError =
                String(error?.message || error);
        });
})();
