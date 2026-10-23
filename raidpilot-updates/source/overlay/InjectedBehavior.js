(() => {
    "use strict";

    if (window.top !== window || location.origin !== "https://tarkov.dev") {
        return;
    }

    // AddScriptToExecuteOnDocumentCreated and the post-navigation safety call can
    // both execute in the same document. Keep one observer and one settings merge.
    if (window.__raidPilotMapFilterInstalled === true) {
        return;
    }
    window.__raidPilotMapFilterInstalled = true;

    // A remote ID is a bearer-style secret. sessionStorage belongs to this
    // WebView instance, so a crash/restart cannot silently reuse a persisted ID.
    // tarkov.dev only enables its receiver when `connection` is present while
    // React mounts. Restore it before the site scripts run after a reload or a
    // same-origin map change, then the host strips it again once connected.
    try {
        const gpsEnabled = sessionStorage.getItem("__raidpilotGpsEnabled") === "1";
        if (!gpsEnabled) {
            localStorage.removeItem("sessionId");
        } else {
            let remoteId = null;
            try {
                remoteId = JSON.parse(localStorage.getItem("sessionId") ?? "null");
            } catch {
                remoteId = null;
            }

            if (typeof remoteId === "string"
                && /^[A-HJ-NP-Z2-9]{16}$/.test(remoteId)) {
                const receiverUrl = new URL(location.href);
                if (!receiverUrl.searchParams.has("connection")) {
                    receiverUrl.searchParams.set("connection", remoteId);
                    history.replaceState(
                        null,
                        "",
                        receiverUrl.pathname + receiverUrl.search + receiverUrl.hash);
                }
            }
        }
    } catch {
        // The GPS activation path also clears the value on failure/exit.
    }

    const rareLootGroupKey = "Loose Loot";
    const rareLootMarkerClass = "raidpilot-rare-loot-marker";
    const rareLootItemKeys = new Set([
        "ledx-skin-transilluminator",
        "graphics-card",
        "physical-bitcoin",
        "intelligence-folder",
        "military-cofdm-wireless-signal-transmitter",
        "vpx-flash-storage-module",
        "virtex-programmable-processor",
        "phased-array-element",
        "military-power-filter",
        "advanced-current-converter",
        "far-forward-gps-signal-amplifier-unit",
        "microcontroller-board",
        "uhf-rfid-reader",
        "secure-magnetic-tape-cassette",
        "portable-defibrillator",
        "ophthalmoscope",
        "military-flash-drive",
        "terragroup-labs-keycard-red",
        "terragroup-labs-keycard-green",
        "terragroup-labs-keycard-blue",
        "terragroup-labs-keycard-black",
        "terragroup-labs-keycard-violet",
        "terragroup-labs-keycard-yellow",
        "keycard-with-a-blue-marking",
        "dorm-room-314-marked-key",
        "rb-bk-marked-key",
        "rb-vo-marked-key",
        "rb-pkpm-marked-key",
        "abandoned-factory-marked-key",
        "mysterious-room-marked-key",
        "substation-marked-key",
    ]);
    const rareLootItemIds = new Set([
        "5c0530ee86f774697952d952",
        "57347ca924597744596b4e71",
        "59faff1d86f7746c51718c9c",
        "5c12613b86f7743bbe2c3f76",
        "5c052f6886f7746b1e3db148",
        "5c05300686f7746dce784e5d",
        "5c05308086f7746b2101e90b",
        "5d03775b86f774203e7e0c4b",
        "5d0378d486f77420421a5ff4",
        "6389c85357baa773a825b356",
        "6389c7f115805221fb410466",
        "6389c7750ef44505c87f5996",
        "5c052fb986f7746b2101e909",
        "61bf7c024770ee6f9c6b8b53",
        "5c052e6986f7746b207bc3c9",
        "5af0534a86f7743b6f354284",
        "62a0a16d0b9d3c46de5b6e97",
        "5c1d0efb86f7744baf2e7b7b",
        "5c1d0dc586f7744baf2e7b79",
        "5c1d0c5f86f7744bb2683cf0",
        "5c1d0f4986f7744bb01837fa",
        "5c1e495a86f7743109743dfb",
        "5c1d0d6d86f7744bb2683e1f",
        "5efde6b4f5448336730dbd61",
        "5780cf7f2459777de4559322",
        "5d80c60f86f77440373c4ece",
        "5d80c62a86f7744036212b3f",
        "5ede7a8229445733cb4c18e2",
        "63a3a93f8a56922e82001f5d",
        "64ccc25f95763a1ae376e447",
        "62987dfc402c7f69bf010923",
    ]);
    const requiredLayers = new Set([
        "place-names",
        "extract_pmc",
        "extract_shared",
        "extract_scav",
        "extract_transit",
        "spawn_boss",
        "player-position",
    ]);
    const hiddenSpawnLayers = new Set([
        "spawn_pmc",
        "spawn_scav",
        "spawn_sniper_scav",
        "spawn_cultist-priest",
        "spawn_rogue",
        "spawn_black-div",
        "spawn_af",
        "spawn_bloodhound",
    ]);
    const visibleGroups = new Set([
        "Extracts",
        "Landmarks",
        "Levels",
        "Spawns",
    ]);

    // tarkov.dev reads this value only once when its React map mounts. Merge
    // instead of replacing it so unrelated site defaults remain forward-compatible.
    try {
        let settings = {};
        const rawSettings = localStorage.getItem("savedMapSettings");
        if (rawSettings) {
            const parsed = JSON.parse(rawSettings);
            if (parsed && typeof parsed === "object" && !Array.isArray(parsed)) {
                settings = parsed;
            }
        }

        settings.style = "svg";
        const preservedHiddenLayers = Array.isArray(settings.hiddenLayers)
            ? settings.hiddenLayers.filter((key) => !requiredLayers.has(key))
            : [];
        settings.hiddenLayers = [...new Set([
            ...preservedHiddenLayers,
            ...hiddenSpawnLayers,
        ])];
        settings.hiddenGroups = Array.isArray(settings.hiddenGroups)
            ? settings.hiddenGroups.filter((key) => !visibleGroups.has(key))
            : [];
        settings.alwaysShowExtracts = true;
        settings.alwaysShowHazardBounds = false;
        settings.alwaysShowSnipers = false;
        settings.expandMapLegend = false;
        settings.expandSearch = false;
        localStorage.setItem("savedMapSettings", JSON.stringify(settings));
    } catch {
        // Storage can be unavailable in a transient renderer. The CSS whitelist
        // below remains the authoritative visual boundary.
    }

    let updateScheduled = false;
    let guidanceTargetLabels = [];
    let guidanceTargetId = "";
    let guidanceGeneration = 0;
    let raidExtractionEntries = [];
    let raidExtractionGeneration = 0;
    let raidExtractionsConfirmed = false;
    let raidQuestEntries = [];
    let raidQuestGeneration = 0;
    let raidQuestsConfirmed = false;
    let raidQuestRetryTimer = 0;
    let raidQuestRetryAttempts = 0;
    let raidTimerGeneration = 0;
    let raidTimerDeadline = null;
    let raidTimerPending = false;
    let raidTimerInterval = 0;
    let raidTimerLastSecond = null;
    const activeRaidQuestLayers = new Map();
    const activeRareLootMarkers = new Map();
    const localPlayerCoordinateLimit = 1_000_000;
    const localPlayerEvents = ["move", "zoom", "resize", "viewreset"];
    let localPlayerGeneration = 0;
    let localPlayerPoseState = null;
    let localPlayerBinding = null;
    let localPlayerRetryTimer = 0;
    let localPlayerRetryDelay = 75;
    let localPlayerSnapResetFrame = 0;
    const learnedRoutePointLimit = 512;
    const learnedRouteCoordinateLimit = 1_000_000;
    const learnedRouteEvents = ["move", "zoom", "resize", "viewreset"];
    let learnedRouteGeneration = 0;
    let learnedRouteState = null;
    let learnedRouteBinding = null;
    let learnedRouteRetryTimer = 0;
    let learnedRouteRetryDelay = 100;

    const isLeafletMapForContainer = (candidate, container) => {
        if (!candidate
            || typeof candidate !== "object"
            || typeof candidate.panTo !== "function"
            || typeof candidate.getContainer !== "function") {
            return false;
        }
        try {
            return candidate.getContainer() === container;
        } catch {
            return false;
        }
    };

    // Leaflet is bundled as a module by tarkov.dev, so it is not reliably
    // exposed as window.L. Resolve it from React only when the map/container
    // actually changes; the hot 10 Hz path reuses the validated live instance.
    let cachedLeafletMapInstance = null;
    let cachedLeafletContainer = null;
    const findLeafletMapInstance = () => {
        const container = document.getElementById("leaflet-map");
        if (!container) {
            cachedLeafletMapInstance = null;
            cachedLeafletContainer = null;
            return null;
        }

        const offlineMap = window.__raidPilotOfflineMap;
        if (isLeafletMapForContainer(offlineMap, container)) {
            cachedLeafletMapInstance = offlineMap;
            cachedLeafletContainer = container;
            return offlineMap;
        }

        if (cachedLeafletContainer === container
            && isLeafletMapForContainer(cachedLeafletMapInstance, container)) {
            return cachedLeafletMapInstance;
        }

        cachedLeafletMapInstance = null;
        cachedLeafletContainer = null;

        const fiberKey = Object.keys(container).find((key) => key.startsWith("__reactFiber$"));
        if (!fiberKey) {
            return null;
        }

        const pending = [container[fiberKey]];
        const visitedFibers = new Set();
        for (let inspectedFibers = 0; pending.length > 0 && inspectedFibers < 160; inspectedFibers += 1) {
            const fiber = pending.shift();
            if (!fiber || visitedFibers.has(fiber)) {
                continue;
            }
            visitedFibers.add(fiber);

            let hook = fiber.memoizedState;
            const visitedHooks = new Set();
            for (let inspectedHooks = 0; hook && inspectedHooks < 128; inspectedHooks += 1) {
                if (visitedHooks.has(hook)) {
                    break;
                }
                visitedHooks.add(hook);
                const state = hook.memoizedState;
                const candidate = state && typeof state === "object" && "current" in state
                    ? state.current
                    : state;
                if (isLeafletMapForContainer(candidate, container)) {
                    cachedLeafletMapInstance = candidate;
                    cachedLeafletContainer = container;
                    return candidate;
                }
                hook = hook.next;
            }

            if (fiber.return) {
                pending.push(fiber.return);
            }
            if (fiber.alternate) {
                pending.push(fiber.alternate);
            }
        }
        return null;
    };

    // tarkov.dev rotates several interactive maps relative to EFT world
    // coordinates. Match its native player marker rule exactly so a Unity yaw
    // always points toward the direction the player is actually facing on the
    // currently displayed map.
    const raidPilotDisplayYaw = (yaw, mapInstance) => {
        let addRotation = Number(mapInstance?.options?.baseData?.coordinateRotation ?? 0);
        if (!Number.isFinite(addRotation)) {
            addRotation = 0;
        }

        const normalizedRotation = ((addRotation % 360) + 360) % 360;
        if (normalizedRotation === 90 || normalizedRotation === 270) {
            addRotation += 180;
        }

        return ((yaw + addRotation) % 360 + 360) % 360;
    };

    // Source of truth for marker heading: project the player's world position
    // and a point four meters straight ahead through Leaflet's live CRS. This
    // automatically absorbs coordinateRotation, axis inversion, anisotropic
    // scale and any future map transform. The old tarkov.dev angle rule is
    // retained only as a fallback if projection is temporarily unavailable.
    const raidPilotProjectedDisplayYaw = (yaw, x, z, mapInstance) => {
        if (![yaw, x, z].every(Number.isFinite)
            || typeof mapInstance?.latLngToContainerPoint !== "function") {
            return raidPilotDisplayYaw(yaw, mapInstance);
        }

        const radians = yaw * Math.PI / 180;
        const distance = 4;
        const forwardX = x + Math.sin(radians) * distance;
        const forwardZ = z + Math.cos(radians) * distance;
        try {
            const origin = mapInstance.latLngToContainerPoint([z, x]);
            const ahead = mapInstance.latLngToContainerPoint([forwardZ, forwardX]);
            const dx = Number(ahead?.x) - Number(origin?.x);
            const dy = Number(ahead?.y) - Number(origin?.y);
            if (Number.isFinite(dx)
                && Number.isFinite(dy)
                && Math.hypot(dx, dy) >= 0.01) {
                // CSS marker arrow points upward at 0°. Positive rotation is
                // clockwise, hence atan2(screenX, -screenY).
                return ((Math.atan2(dx, -dy) * 180 / Math.PI) + 360) % 360;
            }
        } catch {
        }

        return raidPilotDisplayYaw(yaw, mapInstance);
    };

    const raidPilotMetricRadiusPixels = (mapInstance, z, x, meters) => {
        if (!Number.isFinite(meters) || meters <= 0.05
            || typeof mapInstance?.latLngToContainerPoint !== "function") {
            return 0;
        }
        try {
            const center = mapInstance.latLngToContainerPoint([z, x]);
            const east = mapInstance.latLngToContainerPoint([z, x + meters]);
            const north = mapInstance.latLngToContainerPoint([z + meters, x]);
            const rx = Math.hypot(Number(east?.x) - Number(center?.x), Number(east?.y) - Number(center?.y));
            const rz = Math.hypot(Number(north?.x) - Number(center?.x), Number(north?.y) - Number(center?.y));
            if (![rx, rz].every(Number.isFinite)) {
                return 0;
            }
            return Math.min(120, Math.max(4, (rx + rz) * 0.5));
        } catch {
            return 0;
        }
    };

    const parseLocalPlayerGeneration = (value) => typeof value === "number"
        && Number.isSafeInteger(value)
        && value >= 0
        ? value
        : null;

    const normalizeLocalPlayerPose = (payload) => {
        const generation = parseLocalPlayerGeneration(payload?.generation);
        const x = payload?.x;
        const y = payload?.y;
        const z = payload?.z;
        const yaw = payload?.yaw;
        const uncertainty = payload?.uncertainty;
        const mineAlert = payload?.mineAlert === true;
        const mineDistance = Number.isFinite(payload?.mineDistance)
            ? Math.max(0, payload.mineDistance)
            : null;
        const boundaryWarning = payload?.boundaryWarning === true;
        const boundaryCritical = payload?.boundaryCritical === true;
        const boundaryDistance = Number.isFinite(payload?.boundaryDistance)
            ? Math.max(0, payload.boundaryDistance)
            : null;
        if (generation === null
            || ![x, y, z, yaw, uncertainty].every((value) => Number.isFinite(value))
            || Math.abs(x) > localPlayerCoordinateLimit
            || Math.abs(y) > localPlayerCoordinateLimit
            || Math.abs(z) > localPlayerCoordinateLimit
            || Math.abs(yaw) > 1_000_000
            || uncertainty < 0
            || uncertainty > 1000
            || typeof payload?.map !== "string"
            || payload.map.length < 1
            || payload.map.length > 64) {
            return null;
        }

        return {
            generation,
            map: payload.map,
            x,
            y,
            z,
            yaw: ((yaw % 360) + 360) % 360,
            uncertainty,
            snap: payload?.snap === true,
            mineAlert,
            mineDistance,
            boundaryWarning,
            boundaryCritical,
            boundaryDistance,
        };
    };

    const removeOrphanedLocalPlayerMarkers = (preserved = null) => {
        for (const marker of document.querySelectorAll("#raidpilot-local-player-marker")) {
            if (marker !== preserved) {
                marker.remove();
            }
        }
    };

    const makeLocalPlayerMarker = (container) => {
        const marker = document.createElement("div");
        marker.id = "raidpilot-local-player-marker";
        marker.setAttribute("aria-hidden", "true");
        marker.setAttribute("role", "presentation");

        const heading = document.createElement("div");
        heading.className = "raidpilot-local-player-heading";
        const arrow = document.createElement("div");
        arrow.className = "raidpilot-local-player-arrow";
        const core = document.createElement("div");
        core.className = "raidpilot-local-player-core";
        const pulse = document.createElement("div");
        pulse.className = "raidpilot-local-player-pulse";
        heading.appendChild(arrow);
        marker.append(pulse, core, heading);
        container.appendChild(marker);
        return marker;
    };

    const compassCardinals = new Map([
        [0, "N"],
        [45, "NE"],
        [90, "E"],
        [135, "SE"],
        [180, "S"],
        [225, "SO"],
        [270, "O"],
        [315, "NO"],
    ]);
    const compassTickCount = 25;
    const compassTickStepDegrees = 5;
    const compassPixelsPerDegree = 4.1;

    const ensureCompass = (container) => {
        let compass = document.getElementById("raidpilot-compass");
        if (!compass || compass.parentElement !== container) {
            compass?.remove();
            compass = document.createElement("div");
            compass.id = "raidpilot-compass";
            compass.className = "raidpilot-compass";
            compass.setAttribute("role", "presentation");
            compass.setAttribute("aria-hidden", "true");

            const viewport = document.createElement("div");
            viewport.className = "raidpilot-compass-viewport";
            const track = document.createElement("div");
            track.className = "raidpilot-compass-track";
            for (let index = 0; index < compassTickCount; index++) {
                const tick = document.createElement("div");
                tick.className = "raidpilot-compass-tick";
                const label = document.createElement("span");
                label.className = "raidpilot-compass-label";
                tick.appendChild(label);
                track.appendChild(tick);
            }
            viewport.appendChild(track);

            const pointer = document.createElement("div");
            pointer.className = "raidpilot-compass-pointer";
            const heading = document.createElement("output");
            heading.className = "raidpilot-compass-heading";
            heading.textContent = "000°";

            compass.append(viewport, pointer, heading);
            container.appendChild(compass);
        }
        return compass;
    };

    const clearCompass = () => {
        document.getElementById("raidpilot-compass")?.remove();
    };

    const normalizeCompassDegrees = (value) => {
        const normalized = ((value % 360) + 360) % 360;
        return normalized >= 359.95 ? 0 : normalized;
    };

    const updateCompass = (container, pose) => {
        const yaw = normalizeCompassDegrees(pose?.yaw ?? 0);
        const compass = ensureCompass(container);
        const track = compass.querySelector(".raidpilot-compass-track");
        const heading = compass.querySelector(".raidpilot-compass-heading");
        if (!track || !heading) return;

        const centerIndex = Math.floor(compassTickCount / 2);
        const nearestTick = Math.round(yaw / compassTickStepDegrees)
            * compassTickStepDegrees;
        const fractionalOffset = nearestTick - yaw;
        const ticks = track.children;

        for (let index = 0; index < ticks.length; index++) {
            const relativeIndex = index - centerIndex;
            const absolute = nearestTick
                + (relativeIndex * compassTickStepDegrees);
            const normalized = normalizeCompassDegrees(absolute);
            const tick = ticks[index];
            const label = tick.querySelector(".raidpilot-compass-label");
            const offsetDegrees = fractionalOffset
                + (relativeIndex * compassTickStepDegrees);
            tick.style.left = "calc(50% + "
                + (Math.round(offsetDegrees * compassPixelsPerDegree * 10) / 10)
                + "px)";

            const rounded = Math.round(normalized);
            const cardinal = compassCardinals.get(rounded);
            const major = rounded % 15 === 0;
            tick.classList.toggle("raidpilot-compass-tick-major", major);
            tick.classList.toggle(
                "raidpilot-compass-tick-cardinal",
                Boolean(cardinal));
            if (label) {
                label.textContent = cardinal || (major ? String(rounded) : "");
            }
        }

        heading.textContent =
            String(Math.round(yaw) % 360).padStart(3, "0") + "°";
        compass.dataset.heading = yaw.toFixed(1);
    };

    const normalizeHazardOverlayZones = (zones) => (
        Array.isArray(zones)
            ? zones.map((zone) => ({
                id: String(zone?.id ?? ""),
                x: Number(zone?.x),
                z: Number(zone?.z),
                outline: Array.isArray(zone?.outline)
                    ? zone.outline
                        .map((point) => ({
                            x: Number(point?.x),
                            z: Number(point?.z),
                        }))
                        .filter((point) =>
                            Number.isFinite(point.x)
                            && Number.isFinite(point.z))
                    : [],
            }))
            : []
    );

    let tacticalHazardRenderFrame = 0;
    const scheduleTacticalHazardRender = () => {
        if (tacticalHazardRenderFrame) return;
        tacticalHazardRenderFrame = window.requestAnimationFrame(() => {
            tacticalHazardRenderFrame = 0;
            renderMineHazards();
            renderBoundaryHazards();
        });
    };

    let mineHazardOverlayState = {
        generation: -1,
        map: "",
        zones: [],
    };
    let mineHazardElements = [];
    let mineHazardRenderRetry = 0;

    const ensureMineHazardOverlay = (container) => {
        let overlay = document.getElementById("raidpilot-minefield-overlay");
        if (!overlay || overlay.parentElement !== container) {
            overlay?.remove();
            overlay = document.createElementNS(
                "http://www.w3.org/2000/svg",
                "svg");
            overlay.id = "raidpilot-minefield-overlay";
            overlay.classList.add("raidpilot-minefield-overlay");
            overlay.setAttribute("aria-hidden", "true");
            overlay.setAttribute("focusable", "false");
            container.appendChild(overlay);
        }
        return overlay;
    };

    const scheduleMineHazardRender = () => {
        if (mineHazardRenderRetry) return;
        mineHazardRenderRetry = window.setTimeout(() => {
            mineHazardRenderRetry = 0;
            renderMineHazards();
        }, 120);
    };

    const rebuildMineHazardElements = (overlay) => {
        overlay.replaceChildren();
        mineHazardElements = mineHazardOverlayState.zones.map((zone) => {
            const element = document.createElementNS(
                "http://www.w3.org/2000/svg",
                zone.outline.length >= 3 ? "polygon" : "circle");
            element.classList.add("raidpilot-minefield-zone");
            element.dataset.mineId = zone.id;
            overlay.appendChild(element);
            return element;
        });
    };

    const renderMineHazards = () => {
        const mapInstance = findLeafletMapInstance();
        const container = document.getElementById("leaflet-map");
        if (!mapInstance
            || !container
            || typeof mapInstance.latLngToContainerPoint !== "function") {
            scheduleMineHazardRender();
            return false;
        }

        const overlay = ensureMineHazardOverlay(container);
        overlay.classList.toggle(
            "raidpilot-minefield-overlay-critical",
            localPlayerPoseState?.mineAlert === true);
        overlay.setAttribute(
            "viewBox",
            "0 0 "
                + Math.max(1, container.clientWidth)
                + " "
                + Math.max(1, container.clientHeight));

        if (mineHazardElements.length !== mineHazardOverlayState.zones.length
            || mineHazardElements.some((element) =>
                element.parentElement !== overlay)) {
            rebuildMineHazardElements(overlay);
        }

        for (let index = 0;
             index < mineHazardOverlayState.zones.length;
             index += 1) {
            const zone = mineHazardOverlayState.zones[index];
            const element = mineHazardElements[index];

            if (zone.outline.length >= 3) {
                const points = zone.outline.map((point) => {
                    const projected = mapInstance.latLngToContainerPoint(
                        [point.z, point.x]);
                    return Number(projected?.x).toFixed(1)
                        + ","
                        + Number(projected?.y).toFixed(1);
                });
                if (points.some((point) => point.includes("NaN"))) {
                    element.setAttribute("visibility", "hidden");
                    continue;
                }
                element.removeAttribute("visibility");
                element.setAttribute("points", points.join(" "));
                continue;
            }

            if (!Number.isFinite(zone.x) || !Number.isFinite(zone.z)) {
                element.setAttribute("visibility", "hidden");
                continue;
            }
            const center = mapInstance.latLngToContainerPoint(
                [zone.z, zone.x]);
            const edge = mapInstance.latLngToContainerPoint(
                [zone.z, zone.x + 10]);
            const radius = Math.max(
                6,
                Math.hypot(
                    Number(edge?.x) - Number(center?.x),
                    Number(edge?.y) - Number(center?.y)));
            if (![center?.x, center?.y, radius].every(Number.isFinite)) {
                element.setAttribute("visibility", "hidden");
                continue;
            }
            element.removeAttribute("visibility");
            element.setAttribute("cx", Number(center.x).toFixed(1));
            element.setAttribute("cy", Number(center.y).toFixed(1));
            element.setAttribute("r", radius.toFixed(1));
        }
        return true;
    };

    window.__raidPilotSetMineHazards = (payload) => {
        const generation = Number(payload?.generation);
        const map = String(payload?.map ?? "").trim();
        const zones = normalizeHazardOverlayZones(payload?.zones);
        if (!Number.isInteger(generation) || !map) return false;

        mineHazardOverlayState = {
            generation,
            map,
            zones,
        };
        mineHazardElements = [];
        scheduleTacticalHazardRender();
        return true;
    };

    window.__raidPilotClearMineHazards = (generation) => {
        if (Number.isInteger(generation)
            && generation < mineHazardOverlayState.generation) {
            return false;
        }
        mineHazardOverlayState = {
            generation: Number.isInteger(generation) ? generation : -1,
            map: "",
            zones: [],
        };
        mineHazardElements = [];
        document.getElementById("raidpilot-minefield-overlay")?.remove();
        return true;
    };

    let boundaryHazardOverlayState = {
        generation: -1,
        map: "",
        zones: [],
    };
    let boundaryHazardElements = [];

    const ensureBoundaryOverlay = (container) => {
        let overlay = document.getElementById("raidpilot-boundary-overlay");
        if (!overlay || overlay.parentElement !== container) {
            overlay?.remove();
            overlay = document.createElementNS(
                "http://www.w3.org/2000/svg",
                "svg");
            overlay.id = "raidpilot-boundary-overlay";
            overlay.classList.add("raidpilot-boundary-overlay");
            overlay.setAttribute("aria-hidden", "true");
            overlay.setAttribute("focusable", "false");
            container.appendChild(overlay);
        }
        return overlay;
    };

    const rebuildBoundaryHazardElements = (overlay) => {
        overlay.replaceChildren();
        boundaryHazardElements = boundaryHazardOverlayState.zones.map((zone) => {
            const polygon = document.createElementNS(
                "http://www.w3.org/2000/svg",
                "polygon");
            polygon.classList.add("raidpilot-boundary-zone");
            polygon.dataset.boundaryId = zone.id;
            overlay.appendChild(polygon);
            return polygon;
        });
    };

    const renderBoundaryHazards = () => {
        const mapInstance = findLeafletMapInstance();
        const container = document.getElementById("leaflet-map");
        if (!mapInstance
            || !container
            || typeof mapInstance.latLngToContainerPoint !== "function") {
            return false;
        }

        const overlay = ensureBoundaryOverlay(container);
        overlay.classList.toggle(
            "raidpilot-boundary-overlay-warning",
            localPlayerPoseState?.boundaryWarning === true);
        overlay.classList.toggle(
            "raidpilot-boundary-overlay-critical",
            localPlayerPoseState?.boundaryCritical === true);
        overlay.setAttribute(
            "viewBox",
            "0 0 "
                + Math.max(1, container.clientWidth)
                + " "
                + Math.max(1, container.clientHeight));

        if (boundaryHazardElements.length !== boundaryHazardOverlayState.zones.length
            || boundaryHazardElements.some((element) =>
                element.parentElement !== overlay)) {
            rebuildBoundaryHazardElements(overlay);
        }

        for (let index = 0;
             index < boundaryHazardOverlayState.zones.length;
             index += 1) {
            const zone = boundaryHazardOverlayState.zones[index];
            const polygon = boundaryHazardElements[index];
            if (zone.outline.length < 3) {
                polygon.setAttribute("visibility", "hidden");
                continue;
            }

            const points = zone.outline.map((point) => {
                const projected = mapInstance.latLngToContainerPoint(
                    [point.z, point.x]);
                return Number(projected?.x).toFixed(1)
                    + ","
                    + Number(projected?.y).toFixed(1);
            });
            if (points.some((point) => point.includes("NaN"))) {
                polygon.setAttribute("visibility", "hidden");
                continue;
            }

            polygon.removeAttribute("visibility");
            polygon.setAttribute("points", points.join(" "));
        }
        return true;
    };

    window.__raidPilotSetBoundaryHazards = (payload) => {
        const generation = Number(payload?.generation);
        const map = String(payload?.map ?? "").trim();
        const zones = normalizeHazardOverlayZones(payload?.zones);
        if (!Number.isInteger(generation) || !map) return false;

        boundaryHazardOverlayState = {
            generation,
            map,
            zones,
        };
        boundaryHazardElements = [];
        scheduleTacticalHazardRender();
        return true;
    };

    window.__raidPilotClearBoundaryHazards = (generation) => {
        if (Number.isInteger(generation)
            && generation < boundaryHazardOverlayState.generation) {
            return false;
        }
        boundaryHazardOverlayState = {
            generation: Number.isInteger(generation) ? generation : -1,
            map: "",
            zones: [],
        };
        boundaryHazardElements = [];
        document.getElementById("raidpilot-boundary-overlay")?.remove();
        document.getElementById("raidpilot-boundary-warning")?.remove();
        return true;
    };

    const ensureBoundaryWarning = (container) => {
        let warning = document.getElementById("raidpilot-boundary-warning");
        if (!warning || warning.parentElement !== container) {
            warning?.remove();
            warning = document.createElement("div");
            warning.id = "raidpilot-boundary-warning";
            warning.className = "raidpilot-boundary-warning";
            warning.hidden = true;
            warning.setAttribute("role", "presentation");
            warning.setAttribute("aria-hidden", "true");

            const label = document.createElement("div");
            label.className = "raidpilot-boundary-warning-label";
            warning.appendChild(label);
            container.appendChild(warning);
        }
        return warning;
    };

    const updateBoundaryWarning = (container, pose) => {
        const warning = ensureBoundaryWarning(container);
        const active = pose?.boundaryWarning === true;
        const critical = pose?.boundaryCritical === true;
        warning.hidden = !active;
        warning.classList.toggle(
            "raidpilot-boundary-warning-critical",
            critical);
        const label = warning.querySelector(
            ".raidpilot-boundary-warning-label");
        if (label) {
            const distance = Number.isFinite(pose?.boundaryDistance)
                ? Math.max(0, pose.boundaryDistance)
                : null;
            label.textContent = critical
                ? "LIMITE DE ZONE — DEMI-TOUR"
                : "ATTENTION — LIMITE DE ZONE"
                    + (distance === null ? "" : " · " + distance.toFixed(0) + " m");
        }
        scheduleTacticalHazardRender();
    };

    const clearBoundaryWarning = () => {
        const warning = document.getElementById("raidpilot-boundary-warning");
        if (warning) {
            warning.hidden = true;
            warning.classList.remove("raidpilot-boundary-warning-critical");
        }
        document
            .getElementById("raidpilot-boundary-overlay")
            ?.classList.remove(
                "raidpilot-boundary-overlay-warning",
                "raidpilot-boundary-overlay-critical");
    };

    const ensureMineWarning = (container) => {
        let warning = document.getElementById("raidpilot-mine-warning");
        if (!warning || warning.parentElement !== container) {
            warning?.remove();
            warning = document.createElement("div");
            warning.id = "raidpilot-mine-warning";
            warning.className = "raidpilot-mine-warning";
            warning.hidden = true;
            warning.setAttribute("role", "presentation");
            warning.setAttribute("aria-hidden", "true");
            container.appendChild(warning);
        }
        return warning;
    };

    const clearMineWarning = () => {
        const warning = document.getElementById("raidpilot-mine-warning");
        if (warning) {
            warning.hidden = true;
            warning.classList.remove("raidpilot-mine-warning-critical");
            warning.removeAttribute("data-distance");
        }
        document
            .getElementById("raidpilot-minefield-overlay")
            ?.classList.remove("raidpilot-minefield-overlay-critical");
    };

    const updateMineWarning = (container, pose) => {
        const warning = ensureMineWarning(container);
        const critical = pose?.mineAlert === true;
        warning.hidden = !critical;
        warning.classList.toggle(
            "raidpilot-mine-warning-critical",
            critical);
        document
            .getElementById("raidpilot-minefield-overlay")
            ?.classList.toggle(
                "raidpilot-minefield-overlay-critical",
                critical);
        if (Number.isFinite(pose?.mineDistance)) {
            warning.dataset.distance =
                Math.max(0, pose.mineDistance).toFixed(1);
        } else {
            warning.removeAttribute("data-distance");
        }
    };

    const cancelLocalPlayerRetry = () => {
        if (localPlayerRetryTimer) {
            window.clearTimeout(localPlayerRetryTimer);
            localPlayerRetryTimer = 0;
        }
        localPlayerRetryDelay = 75;
    };

    const detachLocalPlayerBinding = () => {
        const binding = localPlayerBinding;
        localPlayerBinding = null;
        if (!binding) {
            return;
        }
        if (binding.frame) {
            window.cancelAnimationFrame(binding.frame);
        }
        if (typeof binding.map?.off === "function") {
            for (const eventName of localPlayerEvents) {
                binding.map.off(eventName, binding.schedule);
            }
        }
        binding.resizeObserver?.disconnect?.();
        if (binding.windowResize) {
            window.removeEventListener("resize", binding.schedule);
        }
        binding.marker.remove();
    };

    const scheduleLocalPlayerRetry = () => {
        if (!localPlayerPoseState || localPlayerRetryTimer) {
            return;
        }
        const delay = localPlayerRetryDelay;
        localPlayerRetryDelay = Math.min(1000, Math.ceil(localPlayerRetryDelay * 1.6));
        localPlayerRetryTimer = window.setTimeout(() => {
            localPlayerRetryTimer = 0;
            window.__raidPilotApplyLocalPose?.();
        }, delay);
    };

    const scheduleLocalPlayerRender = () => {
        const binding = localPlayerBinding;
        if (!binding || binding.frame) {
            return;
        }
        binding.frame = window.requestAnimationFrame(() => {
            if (localPlayerBinding !== binding) {
                return;
            }
            binding.frame = 0;
            window.__raidPilotApplyLocalPose?.();
        });
    };

    const bindLocalPlayerMarker = (mapInstance, container) => {
        detachLocalPlayerBinding();
        removeOrphanedLocalPlayerMarkers();
        const marker = makeLocalPlayerMarker(container);
        const binding = {
            map: mapInstance,
            container,
            marker,
            frame: 0,
            schedule: () => {
                scheduleLocalPlayerRender();
                scheduleTacticalHazardRender();
            },
            resizeObserver: null,
            windowResize: false,
            lastPoint: null,
        };
        localPlayerBinding = binding;

        if (typeof mapInstance.on === "function") {
            for (const eventName of localPlayerEvents) {
                mapInstance.on(eventName, binding.schedule);
            }
        }
        if (typeof ResizeObserver === "function") {
            binding.resizeObserver = new ResizeObserver(binding.schedule);
            binding.resizeObserver.observe(container);
        } else {
            window.addEventListener("resize", binding.schedule, { passive: true });
            binding.windowResize = true;
        }
        syncStablePlayerMarker();
        return binding;
    };

    window.__raidPilotApplyLocalPose = () => {
        const pose = localPlayerPoseState;
        if (!pose) {
            clearMineWarning();
            clearBoundaryWarning();
            clearCompass();
            cancelLocalPlayerRetry();
            detachLocalPlayerBinding();
            removeOrphanedLocalPlayerMarkers();
            return false;
        }

        const mapInstance = findLeafletMapInstance();
        const container = document.getElementById("leaflet-map");
        if (container) {
            updateMineWarning(container, pose);
            updateBoundaryWarning(container, pose);
        }
        if (!mapInstance
            || !container
            || typeof mapInstance.latLngToContainerPoint !== "function") {
            if (localPlayerBinding) {
                localPlayerBinding.marker.setAttribute("hidden", "");
            }
            scheduleLocalPlayerRetry();
            return false;
        }

        scheduleTacticalHazardRender();

        let binding = localPlayerBinding;
        if (!binding
            || binding.map !== mapInstance
            || binding.container !== container
            || !binding.marker.isConnected
            || binding.marker.parentElement !== container) {
            binding = bindLocalPlayerMarker(mapInstance, container);
        }

        let screenPoint;
        try {
            const playerLatLng = [pose.z, pose.x];
            const currentZoom = Number(mapInstance.getZoom?.());
            const currentCenter = mapInstance.getCenter?.();
            const centerLat = Number(currentCenter?.lat);
            const centerLng = Number(currentCenter?.lng);
            const centerDrift = Number.isFinite(centerLat) && Number.isFinite(centerLng)
                ? Math.hypot(centerLng - pose.x, centerLat - pose.z)
                : Number.POSITIVE_INFINITY;
            if (centerDrift > 0.01) {
                if (typeof mapInstance.setView === "function" && Number.isFinite(currentZoom)) {
                    // GPS-style center lock: the player stays fixed in the middle
                    // while Leaflet moves the world, teammates and tactical layers
                    // underneath the marker at the current user-selected zoom.
                    mapInstance.setView(playerLatLng, currentZoom, { animate: false });
                } else {
                    mapInstance.panTo?.(playerLatLng, { animate: false });
                }
            }
            screenPoint = mapInstance.latLngToContainerPoint([pose.z, pose.x]);
        } catch {
            binding.marker.setAttribute("hidden", "");
            scheduleLocalPlayerRetry();
            return false;
        }
        const projectedX = Number(screenPoint?.x);
        const projectedY = Number(screenPoint?.y);
        const centerX = Number(container.clientWidth) * 0.5;
        const centerY = Number(container.clientHeight) * 0.5;
        const x = Number.isFinite(centerX) && centerX > 0 ? centerX : projectedX;
        const y = Number.isFinite(centerY) && centerY > 0 ? centerY : projectedY;
        if (!Number.isFinite(projectedX)
            || !Number.isFinite(projectedY)
            || !Number.isFinite(x)
            || !Number.isFinite(y)
            || Math.abs(projectedX) > 10_000_000
            || Math.abs(projectedY) > 10_000_000) {
            binding.marker.setAttribute("hidden", "");
            scheduleLocalPlayerRetry();
            return false;
        }

        const previous = binding.lastPoint;
        const shouldSnap = pose.snap
            || !previous
            || Math.hypot(x - previous.x, y - previous.y) >= 72;
        if (shouldSnap) {
            if (localPlayerSnapResetFrame) {
                window.cancelAnimationFrame(localPlayerSnapResetFrame);
            }
            binding.marker.classList.add("raidpilot-local-player-snap");
        }

        binding.marker.style.transform =
            `translate3d(${Math.round(x * 10) / 10}px, ${Math.round(y * 10) / 10}px, 0) translate(-50%, -50%)`;
        const displayYaw = raidPilotProjectedDisplayYaw(
            pose.yaw,
            pose.x,
            pose.z,
            mapInstance);
        const uncertaintyRadius = raidPilotMetricRadiusPixels(
            mapInstance,
            pose.z,
            pose.x,
            pose.uncertainty);
        binding.marker.style.setProperty("--raidpilot-local-yaw", `${displayYaw}deg`);
        binding.marker.style.setProperty(
            "--raidpilot-local-uncertainty-radius",
            `${Math.round(uncertaintyRadius * 10) / 10}px`);
        binding.marker.dataset.quality = pose.snap
            ? "exact"
            : pose.uncertainty <= 3
                ? "good"
                : pose.uncertainty <= 8
                    ? "estimated"
                    : "uncertain";
        binding.marker.dataset.uncertainty = pose.uncertainty.toFixed(1);
        binding.marker.dataset.generation = String(pose.generation);
        binding.marker.removeAttribute("hidden");
        binding.lastPoint = { x, y };

        if (shouldSnap) {
            localPlayerSnapResetFrame = window.requestAnimationFrame(() => {
                localPlayerSnapResetFrame = 0;
                binding.marker.classList.remove("raidpilot-local-player-snap");
            });
        }

        // Native tarkov.dev marker churn is handled by the existing
        // MutationObserver. Do not rescan the marker pane on every 10 Hz pose.
        cancelLocalPlayerRetry();
        return true;
    };

    window.__raidPilotSetLocalPose = (payload) => {
        const pose = normalizeLocalPlayerPose(payload);
        if (!pose || pose.generation < localPlayerGeneration) {
            return false;
        }
        if (pose.generation > localPlayerGeneration || pose.snap) {
            localPlayerTrail.length = 0;
        }
        localPlayerGeneration = pose.generation;
        localPlayerPoseState = pose;
        rememberTacticalTrailSample(localPlayerTrail, pose);
        cancelLocalPlayerRetry();
        window.__raidPilotApplyLocalPose();
        renderTacticalTrails();
        return true;
    };

    window.__raidPilotClearLocalPose = (generation = localPlayerGeneration) => {
        const requestedGeneration = parseLocalPlayerGeneration(generation);
        if (requestedGeneration === null || requestedGeneration < localPlayerGeneration) {
            return false;
        }
        localPlayerGeneration = requestedGeneration;
        localPlayerPoseState = null;
        clearMineWarning();
        clearCompass();
        localPlayerTrail.length = 0;
        renderTacticalTrails();
        cancelLocalPlayerRetry();
        detachLocalPlayerBinding();
        removeOrphanedLocalPlayerMarkers();
        window.queueMicrotask(() => {
            try {
                syncStablePlayerMarker?.();
            } catch {
                // Remote sharing is optional; local marker cleanup is complete.
            }
        });
        return true;
    };

    const squadMemberLimit = 12;
    const squadMemberExpiryMs = 8000;
    const squadMemberStaleMs = 2500;
    const squadEvents = ["move", "zoom", "resize", "viewreset"];
    const squadMembers = new Map();
    let squadBinding = null;
    let squadCleanupTimer = 0;

    const normalizeSquadPose = (payload) => {
        const memberId = typeof payload?.memberId === "string"
            ? payload.memberId.trim().slice(0, 64)
            : "";
        const name = typeof payload?.name === "string"
            ? payload.name.replace(/[\u0000-\u001f\u007f]/g, "").trim().slice(0, 24)
            : "";
        const map = typeof payload?.map === "string" ? payload.map.trim().slice(0, 64) : "";
        const values = [payload?.x, payload?.y, payload?.z, payload?.yaw, payload?.uncertainty];
        const sentAtUnixMs = payload?.sentAtUnixMs;
        if (!memberId
            || !name
            || !map
            || !values.every(Number.isFinite)
            || !Number.isSafeInteger(sentAtUnixMs)
            || sentAtUnixMs <= 0
            || Math.abs(payload.x) > 1_000_000
            || Math.abs(payload.y) > 1_000_000
            || Math.abs(payload.z) > 1_000_000
            || payload.uncertainty < 0
            || payload.uncertainty > 1000) {
            return null;
        }
        return {
            memberId,
            name,
            map,
            x: payload.x,
            y: payload.y,
            z: payload.z,
            yaw: ((payload.yaw % 360) + 360) % 360,
            uncertainty: payload.uncertainty,
            sentAtUnixMs,
            receivedAt: performance.now(),
        };
    };

    const squadColorForId = (memberId) => {
        const palette = ["#50c8ff", "#d58cff", "#ffca5a", "#62e89d", "#ff7b8a", "#7ea5ff", "#f58fff", "#8de7df"];
        let hash = 2166136261;
        for (const char of memberId) {
            hash ^= char.codePointAt(0);
            hash = Math.imul(hash, 16777619);
        }
        return palette[Math.abs(hash >>> 0) % palette.length];
    };

    const tacticalTrailLifetimeMs = 7000;
    const tacticalTrailPointLimit = 48;
    const tacticalTrailEvents = ["move", "zoom", "resize", "viewreset"];
    let localPlayerTrail = [];
    let tacticalTrailBinding = null;

    const rememberTacticalTrailSample = (trail, pose, at = performance.now()) => {
        if (!Array.isArray(trail)
            || !pose
            || ![pose.x, pose.z].every(Number.isFinite)
            || typeof pose.map !== "string") {
            return;
        }

        const last = trail.at(-1);
        if (last && last.map !== pose.map) {
            trail.length = 0;
        } else if (last) {
            const meters = Math.hypot(pose.x - last.x, pose.z - last.z);
            const elapsed = at - last.at;
            // A large instantaneous correction is a recalage, not a travelled
            // segment. Start a fresh trail instead of drawing a false teleport.
            if (meters >= 12 && elapsed < 1200) {
                trail.length = 0;
            } else if (meters < 0.35 && elapsed < 350) {
                return;
            }
        }

        trail.push({ x: pose.x, z: pose.z, map: pose.map, at });
        while (trail.length > tacticalTrailPointLimit) {
            trail.shift();
        }
    };

    const pruneTacticalTrail = (trail, now) => {
        while (trail.length > 0 && now - trail[0].at > tacticalTrailLifetimeMs) {
            trail.shift();
        }
    };

    const tacticalPathData = (mapInstance, trail) => {
        const points = [];
        for (const sample of trail) {
            try {
                const projected = mapInstance.latLngToContainerPoint([sample.z, sample.x]);
                const x = Number(projected?.x);
                const y = Number(projected?.y);
                if (Number.isFinite(x) && Number.isFinite(y)) {
                    points.push({ x, y });
                }
            } catch {
                return "";
            }
        }
        if (points.length < 2) {
            return "";
        }
        return points.map((point, index) =>
            `${index === 0 ? "M" : "L"}${Math.round(point.x * 10) / 10} ${Math.round(point.y * 10) / 10}`).join(" ");
    };

    const convexHull = (points) => {
        if (points.length <= 2) {
            return points.slice();
        }
        const sorted = points
            .slice()
            .sort((a, b) => a.x === b.x ? a.y - b.y : a.x - b.x);
        const cross = (origin, a, b) =>
            (a.x - origin.x) * (b.y - origin.y)
            - (a.y - origin.y) * (b.x - origin.x);
        const lower = [];
        for (const point of sorted) {
            while (lower.length >= 2
                && cross(lower.at(-2), lower.at(-1), point) <= 0) {
                lower.pop();
            }
            lower.push(point);
        }
        const upper = [];
        for (let index = sorted.length - 1; index >= 0; index -= 1) {
            const point = sorted[index];
            while (upper.length >= 2
                && cross(upper.at(-2), upper.at(-1), point) <= 0) {
                upper.pop();
            }
            upper.push(point);
        }
        lower.pop();
        upper.pop();
        return [...lower, ...upper];
    };

    const tacticalFormationPath = (points) => {
        if (points.length < 2) {
            return "";
        }
        const hull = convexHull(points);
        if (hull.length === 2) {
            return `M${hull[0].x} ${hull[0].y} L${hull[1].x} ${hull[1].y}`;
        }
        return hull.map((point, index) =>
            `${index === 0 ? "M" : "L"}${point.x} ${point.y}`).join(" ") + " Z";
    };

    const updateAdaptiveMapLabels = (mapInstance, container) => {
        const labels = [...container.querySelectorAll(".map-area-label")];
        for (const label of labels) {
            label.classList.remove("raidpilot-map-label-decluttered");
        }
        if (!localPlayerPoseState || labels.length < 4) {
            return;
        }

        const pixelsPerMeter = raidPilotMetricRadiusPixels(
            mapInstance,
            localPlayerPoseState.z,
            localPlayerPoseState.x,
            10) / 10;
        if (!Number.isFinite(pixelsPerMeter) || pixelsPerMeter >= 2.2) {
            return;
        }

        let localPoint;
        try {
            localPoint = mapInstance.latLngToContainerPoint([
                localPlayerPoseState.z,
                localPlayerPoseState.x,
            ]);
        } catch {
            return;
        }
        if (!Number.isFinite(localPoint?.x) || !Number.isFinite(localPoint?.y)) {
            return;
        }

        const mapRect = container.getBoundingClientRect();
        const candidates = labels
            .map((label) => {
                const rect = label.getBoundingClientRect();
                return {
                    label,
                    rect,
                    distance: Math.hypot(
                        rect.left + rect.width * 0.5 - mapRect.left - localPoint.x,
                        rect.top + rect.height * 0.5 - mapRect.top - localPoint.y),
                };
            })
            .filter(({ rect }) =>
                rect.width > 1
                && rect.height > 1
                && rect.right > mapRect.left
                && rect.left < mapRect.right
                && rect.bottom > mapRect.top
                && rect.top < mapRect.bottom)
            .sort((a, b) => a.distance - b.distance);

        const kept = [];
        const aggressive = pixelsPerMeter < 0.9;
        const padding = aggressive ? 10 : 6;
        const maximumHidden = Math.floor(
            candidates.length * (aggressive ? 0.55 : 0.35));
        let hidden = 0;

        for (const candidate of candidates) {
            const expanded = {
                left: candidate.rect.left - padding,
                top: candidate.rect.top - padding,
                right: candidate.rect.right + padding,
                bottom: candidate.rect.bottom + padding,
            };
            const overlaps = kept.some((rect) =>
                expanded.left < rect.right
                && expanded.right > rect.left
                && expanded.top < rect.bottom
                && expanded.bottom > rect.top);
            if (overlaps && hidden < maximumHidden) {
                candidate.label.classList.add("raidpilot-map-label-decluttered");
                hidden += 1;
            } else {
                kept.push(expanded);
            }
        }
    };

    const makeTacticalTrailLayer = (container) => {
        const namespace = "http://www.w3.org/2000/svg";
        const svg = document.createElementNS(namespace, "svg");
        svg.id = "raidpilot-tactical-trails";
        svg.setAttribute("aria-hidden", "true");
        svg.setAttribute("focusable", "false");

        const formation = document.createElementNS(namespace, "path");
        formation.setAttribute("class", "raidpilot-squad-formation");
        const localBase = document.createElementNS(namespace, "path");
        localBase.setAttribute("class", "raidpilot-tactical-trail-local-base");
        const localRecent = document.createElementNS(namespace, "path");
        localRecent.setAttribute("class", "raidpilot-tactical-trail-local-recent");
        const squadGroup = document.createElementNS(namespace, "g");
        squadGroup.setAttribute("class", "raidpilot-tactical-trails-squad");
        svg.append(formation, localBase, localRecent, squadGroup);
        container.appendChild(svg);
        return { svg, formation, localBase, localRecent, squadGroup, squadPaths: new Map() };
    };

    const detachTacticalTrailLayer = () => {
        const binding = tacticalTrailBinding;
        tacticalTrailBinding = null;
        if (!binding) {
            return;
        }
        if (binding.frame) {
            window.cancelAnimationFrame(binding.frame);
        }
        if (typeof binding.map?.off === "function") {
            for (const eventName of tacticalTrailEvents) {
                binding.map.off(eventName, binding.schedule);
            }
        }
        binding.resizeObserver?.disconnect?.();
        if (binding.windowResize) {
            window.removeEventListener("resize", binding.schedule);
        }
        binding.svg.remove();
    };

    const renderTacticalTrails = () => {
        const mapInstance = findLeafletMapInstance();
        const container = document.getElementById("leaflet-map");
        if (!mapInstance
            || !container
            || typeof mapInstance.latLngToContainerPoint !== "function") {
            return false;
        }

        let binding = tacticalTrailBinding;
        if (!binding
            || binding.map !== mapInstance
            || binding.container !== container
            || !binding.svg.isConnected) {
            detachTacticalTrailLayer();
            const elements = makeTacticalTrailLayer(container);
            const schedule = () => {
                if (tacticalTrailBinding?.frame) {
                    return;
                }
                tacticalTrailBinding.frame = window.requestAnimationFrame(() => {
                    if (tacticalTrailBinding) {
                        tacticalTrailBinding.frame = 0;
                    }
                    renderTacticalTrails();
                });
            };
            binding = {
                ...elements,
                map: mapInstance,
                container,
                schedule,
                frame: 0,
                resizeObserver: null,
                windowResize: false,
            };
            tacticalTrailBinding = binding;
            if (typeof mapInstance.on === "function") {
                for (const eventName of tacticalTrailEvents) {
                    mapInstance.on(eventName, schedule);
                }
            }
            if (typeof ResizeObserver === "function") {
                binding.resizeObserver = new ResizeObserver(schedule);
                binding.resizeObserver.observe(container);
            } else {
                window.addEventListener("resize", schedule, { passive: true });
                binding.windowResize = true;
            }
        }

        const width = Math.max(1, Math.round(container.clientWidth));
        const height = Math.max(1, Math.round(container.clientHeight));
        binding.svg.setAttribute("viewBox", `0 0 ${width} ${height}`);

        const now = performance.now();
        pruneTacticalTrail(localPlayerTrail, now);
        const localMapMatches = localPlayerPoseState?.map
            && localPlayerTrail.every((sample) => sample.map === localPlayerPoseState.map);
        const localData = localMapMatches
            ? tacticalPathData(mapInstance, localPlayerTrail)
            : "";
        binding.localBase.setAttribute("d", localData);
        binding.localRecent.setAttribute(
            "d",
            localData && localPlayerTrail.length >= 2
                ? tacticalPathData(mapInstance, localPlayerTrail.slice(-6))
                : "");

        const formationPoints = [];
        const pushFormationPoint = (pose) => {
            try {
                const projected = mapInstance.latLngToContainerPoint([pose.z, pose.x]);
                const x = Number(projected?.x);
                const y = Number(projected?.y);
                if (Number.isFinite(x) && Number.isFinite(y)) {
                    formationPoints.push({
                        x: Math.round(x * 10) / 10,
                        y: Math.round(y * 10) / 10,
                    });
                }
            } catch {
                // A transient Leaflet navigation simply omits this point.
            }
        };
        if (localPlayerPoseState) {
            pushFormationPoint(localPlayerPoseState);
        }
        for (const entry of squadMembers.values()) {
            if (now - entry.pose.receivedAt < squadMemberStaleMs
                && (!localPlayerPoseState?.map
                    || entry.pose.map === localPlayerPoseState.map)) {
                pushFormationPoint(entry.pose);
            }
        }
        binding.formation.setAttribute(
            "d",
            tacticalFormationPath(formationPoints));

        const activeIds = new Set();
        for (const [memberId, entry] of squadMembers) {
            pruneTacticalTrail(entry.trail ??= [], now);
            if (now - entry.pose.receivedAt >= squadMemberExpiryMs
                || (localPlayerPoseState?.map && entry.pose.map !== localPlayerPoseState.map)
                || entry.trail.length < 2) {
                continue;
            }

            activeIds.add(memberId);
            let paths = binding.squadPaths.get(memberId);
            if (!paths) {
                const namespace = "http://www.w3.org/2000/svg";
                const base = document.createElementNS(namespace, "path");
                const recent = document.createElementNS(namespace, "path");
                base.setAttribute("class", "raidpilot-tactical-trail-squad-base");
                recent.setAttribute("class", "raidpilot-tactical-trail-squad-recent");
                const color = squadColorForId(memberId);
                base.style.setProperty("--raidpilot-trail-color", color);
                recent.style.setProperty("--raidpilot-trail-color", color);
                binding.squadGroup.append(base, recent);
                paths = { base, recent };
                binding.squadPaths.set(memberId, paths);
            }

            paths.base.setAttribute("d", tacticalPathData(mapInstance, entry.trail));
            paths.recent.setAttribute("d", tacticalPathData(mapInstance, entry.trail.slice(-6)));
        }

        for (const [memberId, paths] of [...binding.squadPaths]) {
            if (!activeIds.has(memberId)) {
                paths.base.remove();
                paths.recent.remove();
                binding.squadPaths.delete(memberId);
            }
        }
        updateAdaptiveMapLabels(mapInstance, container);
        return true;
    };

    window.setInterval(() => {
        if (localPlayerTrail.length > 0 || squadMembers.size > 0) {
            renderTacticalTrails();
        }
    }, 1000);

    const makeSquadMarker = (container, pose) => {
        const marker = document.createElement("div");
        marker.className = "raidpilot-squad-marker";
        marker.dataset.memberId = pose.memberId;
        marker.setAttribute("aria-hidden", "true");
        marker.style.setProperty("--raidpilot-squad-color", squadColorForId(pose.memberId));

        const ring = document.createElement("div");
        ring.className = "raidpilot-squad-ring";
        const heading = document.createElement("div");
        heading.className = "raidpilot-squad-heading";
        const arrow = document.createElement("div");
        arrow.className = "raidpilot-squad-arrow";
        const label = document.createElement("div");
        label.className = "raidpilot-squad-label";
        heading.appendChild(arrow);
        marker.append(ring, heading, label);
        container.appendChild(marker);
        return marker;
    };

    const makeSquadEdgeIndicator = (container, pose) => {
        const indicator = document.createElement("div");
        indicator.className = "raidpilot-squad-edge-indicator";
        indicator.dataset.memberId = pose.memberId;
        indicator.setAttribute("aria-hidden", "true");
        indicator.style.setProperty("--raidpilot-squad-color", squadColorForId(pose.memberId));

        const arrow = document.createElement("div");
        arrow.className = "raidpilot-squad-edge-arrow";
        const label = document.createElement("div");
        label.className = "raidpilot-squad-edge-label";
        indicator.append(arrow, label);
        container.appendChild(indicator);
        return indicator;
    };

    const updateSquadEdgeIndicator = (entry, point, container) => {
        const width = container.clientWidth;
        const height = container.clientHeight;
        const margin = 28;
        const inside = point.x >= margin
            && point.x <= width - margin
            && point.y >= margin
            && point.y <= height - margin;

        if (inside) {
            entry.edge?.setAttribute("hidden", "");
            return false;
        }

        if (!entry.edge?.isConnected || entry.edge.parentElement !== container) {
            entry.edge?.remove();
            entry.edge = makeSquadEdgeIndicator(container, entry.pose);
        }

        const centerX = width * 0.5;
        const centerY = height * 0.5;
        const dx = point.x - centerX;
        const dy = point.y - centerY;
        const maxX = Math.max(1, centerX - margin);
        const maxY = Math.max(1, centerY - margin);
        const scaleX = Math.abs(dx) > 0.001 ? maxX / Math.abs(dx) : Number.POSITIVE_INFINITY;
        const scaleY = Math.abs(dy) > 0.001 ? maxY / Math.abs(dy) : Number.POSITIVE_INFINITY;
        const scale = Math.min(scaleX, scaleY, 1);
        const edgeX = centerX + dx * scale;
        const edgeY = centerY + dy * scale;
        const angle = Math.atan2(dy, dx) * 180 / Math.PI + 90;

        const distance = localPlayerPoseState
            && localPlayerPoseState.map === entry.pose.map
            ? Math.hypot(
                entry.pose.x - localPlayerPoseState.x,
                entry.pose.z - localPlayerPoseState.z)
            : null;
        const vertical = localPlayerPoseState
            && localPlayerPoseState.map === entry.pose.map
            ? entry.pose.y - localPlayerPoseState.y
            : 0;
        const distanceText = Number.isFinite(distance) ? ` · ${Math.round(distance)}m` : "";
        const levelText = Math.abs(vertical) >= 2.5
            ? ` · ${vertical > 0 ? "▲" : "▼"}${Math.round(Math.abs(vertical))}m`
            : "";

        entry.edge.style.transform =
            `translate3d(${Math.round(edgeX * 10) / 10}px, ${Math.round(edgeY * 10) / 10}px, 0) translate(-50%, -50%)`;
        entry.edge.querySelector(".raidpilot-squad-edge-arrow")?.style.setProperty(
            "transform",
            `rotate(${Math.round(angle * 10) / 10}deg)`);
        const label = entry.edge.querySelector(".raidpilot-squad-edge-label");
        if (label) {
            label.textContent = entry.pose.name + distanceText + levelText;
        }
        entry.edge.removeAttribute("hidden");
        return true;
    };

    const renderSquadMembers = () => {
        const mapInstance = findLeafletMapInstance();
        const container = document.getElementById("leaflet-map");
        if (!mapInstance
            || !container
            || typeof mapInstance.latLngToContainerPoint !== "function") {
            return false;
        }

        if (!squadBinding
            || squadBinding.map !== mapInstance
            || squadBinding.container !== container) {
            if (squadBinding?.map && typeof squadBinding.map.off === "function") {
                for (const eventName of squadEvents) {
                    squadBinding.map.off(eventName, squadBinding.render);
                }
            }
            const render = () => window.requestAnimationFrame(renderSquadMembers);
            squadBinding = { map: mapInstance, container, render };
            if (typeof mapInstance.on === "function") {
                for (const eventName of squadEvents) {
                    mapInstance.on(eventName, render);
                }
            }
        }

        const now = performance.now();
        for (const [memberId, entry] of [...squadMembers]) {
            const age = now - entry.pose.receivedAt;
            if (age >= squadMemberExpiryMs) {
                entry.marker?.remove();
                entry.edge?.remove();
                squadMembers.delete(memberId);
                continue;
            }

            if (localPlayerPoseState?.map && entry.pose.map !== localPlayerPoseState.map) {
                entry.marker?.setAttribute("hidden", "");
                entry.edge?.setAttribute("hidden", "");
                continue;
            }

            let point;
            try {
                point = mapInstance.latLngToContainerPoint([entry.pose.z, entry.pose.x]);
            } catch {
                entry.marker?.setAttribute("hidden", "");
                entry.edge?.setAttribute("hidden", "");
                continue;
            }
            if (!Number.isFinite(point?.x) || !Number.isFinite(point?.y)) {
                entry.marker?.setAttribute("hidden", "");
                entry.edge?.setAttribute("hidden", "");
                continue;
            }

            if (!entry.marker?.isConnected || entry.marker.parentElement !== container) {
                entry.marker?.remove();
                entry.marker = makeSquadMarker(container, entry.pose);
            }

            const markerState = age >= squadMemberStaleMs
                ? "stale"
                : entry.pose.uncertainty > 8
                    ? "uncertain"
                    : "live";
            if (updateSquadEdgeIndicator(entry, point, container)) {
                entry.edge.dataset.state = markerState;
                entry.marker.setAttribute("hidden", "");
                continue;
            }

            entry.marker.style.transform =
                `translate3d(${Math.round(point.x * 10) / 10}px, ${Math.round(point.y * 10) / 10}px, 0) translate(-50%, -50%)`;
            const displayYaw = raidPilotProjectedDisplayYaw(
                entry.pose.yaw,
                entry.pose.x,
                entry.pose.z,
                mapInstance);
            const uncertaintyRadius = raidPilotMetricRadiusPixels(
                mapInstance,
                entry.pose.z,
                entry.pose.x,
                entry.pose.uncertainty);
            entry.marker.style.setProperty("--raidpilot-squad-yaw", `${displayYaw}deg`);
            entry.marker.style.setProperty(
                "--raidpilot-squad-uncertainty-radius",
                `${Math.round(uncertaintyRadius * 10) / 10}px`);
            entry.marker.dataset.state = markerState;

            const distance = localPlayerPoseState
                && localPlayerPoseState.map === entry.pose.map
                ? Math.hypot(
                    entry.pose.x - localPlayerPoseState.x,
                    entry.pose.z - localPlayerPoseState.z)
                : null;
            const vertical = localPlayerPoseState
                && localPlayerPoseState.map === entry.pose.map
                ? entry.pose.y - localPlayerPoseState.y
                : 0;
            const distanceText = Number.isFinite(distance) ? ` · ${Math.round(distance)} m` : "";
            const levelText = Math.abs(vertical) >= 2.5
                ? ` · ${vertical > 0 ? "▲" : "▼"}${Math.round(Math.abs(vertical))}m`
                : "";
            const label = entry.marker.querySelector(".raidpilot-squad-label");
            if (label) {
                label.textContent = entry.pose.name + distanceText + levelText;
            }
            entry.marker.removeAttribute("hidden");
        }
        return true;
    };

    window.__raidPilotSetSquadPose = (payload) => {
        const pose = normalizeSquadPose(payload);
        if (!pose) {
            return false;
        }

        let entry = squadMembers.get(pose.memberId);
        if (!entry && squadMembers.size >= squadMemberLimit) {
            const oldest = [...squadMembers.entries()]
                .sort((a, b) => a[1].pose.receivedAt - b[1].pose.receivedAt)[0];
            oldest?.[1]?.marker?.remove();
            oldest?.[1]?.edge?.remove();
            if (oldest) {
                squadMembers.delete(oldest[0]);
            }
        }
        if (!entry) {
            entry = { pose, marker: null, edge: null, trail: [] };
            squadMembers.set(pose.memberId, entry);
        } else {
            if (pose.sentAtUnixMs <= entry.pose.sentAtUnixMs) {
                return false;
            }
            entry.pose = pose;
        }
        rememberTacticalTrailSample(entry.trail, pose, pose.receivedAt);

        if (!squadCleanupTimer) {
            squadCleanupTimer = window.setInterval(renderSquadMembers, 500);
        }
        renderSquadMembers();
        renderTacticalTrails();
        return true;
    };

    window.__raidPilotRemoveSquadMember = (memberId) => {
        const entry = squadMembers.get(String(memberId ?? ""));
        entry?.marker?.remove();
        entry?.edge?.remove();
        const removed = squadMembers.delete(String(memberId ?? ""));
        renderTacticalTrails();
        return removed;
    };

    window.__raidPilotClearSquad = () => {
        for (const entry of squadMembers.values()) {
            entry.marker?.remove();
            entry.edge?.remove();
        }
        squadMembers.clear();
        renderTacticalTrails();
        if (squadBinding?.map && typeof squadBinding.map.off === "function") {
            for (const eventName of squadEvents) {
                squadBinding.map.off(eventName, squadBinding.render);
            }
        }
        squadBinding = null;
        if (squadCleanupTimer) {
            window.clearInterval(squadCleanupTimer);
            squadCleanupTimer = 0;
        }
        return true;
    };

    // The same live Leaflet layers already loaded by tarkov.dev contain the public height/bounds data.
    // Keep floor selection local to this WebView, independent of the optional remote player-point socket.
    const automaticFloorController = window.RaidPilotFloors?.createController(findLeafletMapInstance, document, window);
    window.__raidPilotSetFloorPose = payload => automaticFloorController?.set(payload);
    window.__raidPilotClearFloors = payload => automaticFloorController?.clear(payload);
    window.__raidPilotRefreshFloorMarkers = () => automaticFloorController?.refreshMarkers();
    window.__raidPilotFloorDiagnostics = () => ({state:automaticFloorController?.getState(), catalog:automaticFloorController?.getCatalog()});

    const cancelLearnedRouteRetry = () => {
        if (learnedRouteRetryTimer) {
            window.clearTimeout(learnedRouteRetryTimer);
            learnedRouteRetryTimer = 0;
        }
        learnedRouteRetryDelay = 100;
    };

    const detachLearnedRouteBinding = () => {
        const binding = learnedRouteBinding;
        learnedRouteBinding = null;
        if (!binding) {
            return;
        }

        if (binding.frame) {
            window.cancelAnimationFrame(binding.frame);
        }
        if (typeof binding.map?.off === "function") {
            for (const eventName of learnedRouteEvents) {
                binding.map.off(eventName, binding.schedule);
            }
        }
        binding.resizeObserver?.disconnect?.();
        if (binding.windowResize) {
            window.removeEventListener("resize", binding.schedule);
        }
        binding.svg.remove();
    };

    const removeOrphanedLearnedRoutes = (preservedSvg = null) => {
        for (const svg of document.querySelectorAll("#raidpilot-learned-route")) {
            if (svg !== preservedSvg) {
                svg.remove();
            }
        }
    };

    const makeLearnedRouteSvg = (container) => {
        const namespace = "http://www.w3.org/2000/svg";
        const svg = document.createElementNS(namespace, "svg");
        svg.id = "raidpilot-learned-route";
        svg.setAttribute("aria-hidden", "true");
        svg.setAttribute("focusable", "false");

        const outline = document.createElementNS(namespace, "path");
        outline.setAttribute("class", "raidpilot-learned-route-outline");
        const travelled = document.createElementNS(namespace, "path");
        travelled.setAttribute("class", "raidpilot-learned-route-travelled");
        const active = document.createElementNS(namespace, "path");
        active.setAttribute("class", "raidpilot-learned-route-active");
        const next = document.createElementNS(namespace, "circle");
        next.setAttribute("class", "raidpilot-learned-route-next");
        next.setAttribute("r", "5");
        svg.append(outline, travelled, active, next);
        container.appendChild(svg);
        return { svg, outline, travelled, active, next };
    };

    const scheduleLearnedRouteRetry = () => {
        if (!learnedRouteState || learnedRouteRetryTimer) {
            return;
        }
        const delay = learnedRouteRetryDelay;
        learnedRouteRetryDelay = Math.min(1000, Math.ceil(learnedRouteRetryDelay * 1.6));
        learnedRouteRetryTimer = window.setTimeout(() => {
            learnedRouteRetryTimer = 0;
            window.__raidPilotApplyLearnedRoute?.();
        }, delay);
    };

    const scheduleLearnedRouteRender = () => {
        const binding = learnedRouteBinding;
        if (!binding || binding.frame) {
            return;
        }
        binding.frame = window.requestAnimationFrame(() => {
            if (learnedRouteBinding !== binding) {
                return;
            }
            binding.frame = 0;
            window.__raidPilotApplyLearnedRoute?.();
        });
    };

    const bindLearnedRoute = (mapInstance, container) => {
        detachLearnedRouteBinding();
        removeOrphanedLearnedRoutes();
        const elements = makeLearnedRouteSvg(container);
        const binding = {
            ...elements,
            map: mapInstance,
            container,
            frame: 0,
            schedule: scheduleLearnedRouteRender,
            resizeObserver: null,
            windowResize: false,
        };
        learnedRouteBinding = binding;

        if (typeof mapInstance.on === "function") {
            for (const eventName of learnedRouteEvents) {
                mapInstance.on(eventName, binding.schedule);
            }
        }
        if (typeof ResizeObserver === "function") {
            binding.resizeObserver = new ResizeObserver(binding.schedule);
            binding.resizeObserver.observe(container);
        } else {
            window.addEventListener("resize", binding.schedule, { passive: true });
            binding.windowResize = true;
        }
        return binding;
    };

    const parseLearnedRouteGeneration = (value) => typeof value === "number"
        && Number.isSafeInteger(value)
        && value >= 0
        ? value
        : null;

    const normalizeLearnedRoute = (payload, generation) => {
        if (!payload
            || payload.kind !== "learned"
            || (payload.confidence !== "high" && payload.confidence !== "medium")
            || !Array.isArray(payload.points)
            || payload.points.length < 2
            || payload.points.length > learnedRoutePointLimit) {
            return null;
        }

        const points = [];
        for (const candidate of payload.points) {
            const x = candidate?.x;
            const y = candidate?.y;
            const z = candidate?.z;
            if (![x, y, z].every((value) => Number.isFinite(value)
                && Math.abs(value) <= learnedRouteCoordinateLimit)) {
                return null;
            }

            const previous = points.at(-1);
            if (!previous || previous.x !== x || previous.y !== y || previous.z !== z) {
                points.push({ x, y, z });
            }
        }
        if (points.length < 2) {
            return null;
        }

        let nextIndex = 0;
        if (payload.nextIndex !== undefined && payload.nextIndex !== null) {
            nextIndex = payload.nextIndex;
            if (!Number.isSafeInteger(nextIndex)
                || nextIndex < 0
                || nextIndex >= points.length) {
                return null;
            }
        }
        return {
            generation,
            kind: "learned",
            confidence: payload.confidence,
            points,
            nextIndex,
        };
    };

    const makeSvgPath = (points) => points.length < 2
        ? ""
        : points.map((point, index) => {
            const x = Math.round(point.x * 10) / 10;
            const y = Math.round(point.y * 10) / 10;
            return `${index === 0 ? "M" : "L"}${x} ${y}`;
        }).join(" ");

    window.__raidPilotApplyLearnedRoute = () => {
        const route = learnedRouteState;
        if (!route) {
            cancelLearnedRouteRetry();
            detachLearnedRouteBinding();
            removeOrphanedLearnedRoutes();
            return false;
        }

        const mapInstance = findLeafletMapInstance();
        const container = document.getElementById("leaflet-map");
        if (!mapInstance
            || !container
            || typeof mapInstance.latLngToContainerPoint !== "function") {
            if (learnedRouteBinding) {
                learnedRouteBinding.svg.setAttribute("hidden", "");
            }
            if (!learnedRouteBinding?.container?.isConnected
                || (container && learnedRouteBinding?.container !== container)) {
                detachLearnedRouteBinding();
            }
            scheduleLearnedRouteRetry();
            return false;
        }

        let binding = learnedRouteBinding;
        if (!binding
            || binding.map !== mapInstance
            || binding.container !== container
            || !binding.svg.isConnected
            || binding.svg.parentElement !== container) {
            binding = bindLearnedRoute(mapInstance, container);
        }

        const width = Math.round(container.clientWidth);
        const height = Math.round(container.clientHeight);
        if (width < 2 || height < 2) {
            binding.svg.setAttribute("hidden", "");
            scheduleLearnedRouteRetry();
            return false;
        }
        const projected = [];
        try {
            for (const point of route.points) {
                const screenPoint = mapInstance.latLngToContainerPoint([point.z, point.x]);
                const x = Number(screenPoint?.x);
                const y = Number(screenPoint?.y);
                if (!Number.isFinite(x)
                    || !Number.isFinite(y)
                    || Math.abs(x) > 10_000_000
                    || Math.abs(y) > 10_000_000) {
                    throw new Error("Invalid Leaflet projection");
                }
                projected.push({ x, y });
            }
        } catch {
            binding.svg.setAttribute("hidden", "");
            scheduleLearnedRouteRetry();
            return false;
        }

        const fullPath = makeSvgPath(projected);
        const travelledPath = makeSvgPath(projected.slice(0, route.nextIndex + 1));
        const activeStart = Math.max(0, route.nextIndex - 1);
        const activePath = makeSvgPath(projected.slice(activeStart));
        const nextPoint = projected[route.nextIndex];
        binding.svg.setAttribute("viewBox", `0 0 ${width} ${height}`);
        binding.svg.setAttribute("width", String(width));
        binding.svg.setAttribute("height", String(height));
        binding.svg.setAttribute("class", `raidpilot-learned-route-confidence-${route.confidence}`);
        binding.svg.dataset.generation = String(route.generation);
        binding.outline.setAttribute("d", fullPath);
        binding.travelled.setAttribute("d", travelledPath);
        binding.active.setAttribute("d", activePath);
        binding.next.setAttribute("cx", String(Math.round(nextPoint.x * 10) / 10));
        binding.next.setAttribute("cy", String(Math.round(nextPoint.y * 10) / 10));
        binding.next.removeAttribute("hidden");
        binding.svg.removeAttribute("hidden");
        removeOrphanedLearnedRoutes(binding.svg);
        cancelLearnedRouteRetry();
        return true;
    };

    window.__raidPilotClearLearnedRoute = (generation = learnedRouteGeneration) => {
        const requestedGeneration = parseLearnedRouteGeneration(generation);
        if (requestedGeneration === null
            || requestedGeneration < learnedRouteGeneration) {
            return false;
        }
        learnedRouteGeneration = requestedGeneration;
        learnedRouteState = null;
        cancelLearnedRouteRetry();
        detachLearnedRouteBinding();
        removeOrphanedLearnedRoutes();
        return true;
    };

    window.__raidPilotShowLearnedRoute = (payload) => {
        const requestedGeneration = parseLearnedRouteGeneration(payload?.generation);
        if (requestedGeneration === null
            || requestedGeneration < learnedRouteGeneration) {
            return false;
        }

        learnedRouteGeneration = requestedGeneration;
        const route = normalizeLearnedRoute(payload, requestedGeneration);
        if (!route) {
            learnedRouteState = null;
            cancelLearnedRouteRetry();
            detachLearnedRouteBinding();
            removeOrphanedLearnedRoutes();
            return false;
        }

        learnedRouteState = route;
        cancelLearnedRouteRetry();
        window.__raidPilotApplyLearnedRoute();
        return true;
    };

    const collectLooseLootMarkers = (layer, markers, visited = new Set()) => {
        if (!layer || typeof layer !== "object" || visited.has(layer)) {
            return;
        }
        visited.add(layer);

        if (typeof layer.getPopup === "function" && layer.options) {
            markers.add(layer);
        }
        if (!layer._layers || typeof layer._layers !== "object") {
            return;
        }
        for (const child of Object.values(layer._layers)) {
            collectLooseLootMarkers(child, markers, visited);
        }
    };

    const getRareLootIdentity = (marker) => {
        const itemKeys = new Set();
        const itemIds = new Set();
        const popupContent = marker?.getPopup?.()?.getContent?.();
        if (popupContent instanceof Element) {
            for (const link of popupContent.querySelectorAll('a[href*="/item/"]')) {
                try {
                    const path = new URL(link.getAttribute("href"), location.origin).pathname;
                    const match = path.match(/^\/item\/([^/]+)\/?$/i);
                    if (match) {
                        itemKeys.add(decodeURIComponent(match[1]).toLowerCase());
                    }
                } catch {
                    // Ignore a malformed third-party link and try the asset ID.
                }
            }
            for (const image of popupContent.querySelectorAll("img[src]")) {
                const match = image.getAttribute("src")?.match(
                    /(?:^|\/)([0-9a-f]{24})-(?:base-image|icon|grid-image)(?:\.|-)/i);
                if (match) {
                    itemIds.add(match[1].toLowerCase());
                }
            }
        }
        return { itemKeys, itemIds };
    };

    const isDedicatedRareLootMarker = (marker) => {
        const { itemKeys, itemIds } = getRareLootIdentity(marker);
        if (itemKeys.size === 1) {
            return rareLootItemKeys.has(itemKeys.values().next().value);
        }
        if (itemKeys.size === 0 && itemIds.size === 1) {
            return rareLootItemIds.has(itemIds.values().next().value);
        }
        return false;
    };

    const releaseRareLootMarker = (marker, state) => {
        if (marker?._icon instanceof Element) {
            marker._icon.classList.remove(rareLootMarkerClass);
        }
        if (state?.addedByRaidPilot && state.map?.hasLayer?.(marker)) {
            state.map.removeLayer(marker);
        }
    };

    const clearActiveRareLootMarkers = () => {
        for (const [marker, state] of [...activeRareLootMarkers.entries()]) {
            releaseRareLootMarker(marker, state);
            activeRareLootMarkers.delete(marker);
        }
    };

    // tarkov.dev has no native "rare loot" layer. Keep every Loose Loot group
    // inactive, inspect its marker metadata without displaying it, then add only
    // dedicated one-item spawns from the strict list above. Generic pools that
    // merely *can* contain a rare item stay hidden to avoid hundreds of dots.
    const enforceRareLootMarkers = () => {
        const mapInstance = findLeafletMapInstance();
        const entries = mapInstance?.layerControl?._layers;
        if (!mapInstance || !Array.isArray(entries)) {
            clearActiveRareLootMarkers();
            return false;
        }

        const looseLootEntries = entries.filter(
            (entry) => entry?.group?.key === rareLootGroupKey);
        if (looseLootEntries.length === 0) {
            clearActiveRareLootMarkers();
            return false;
        }

        const candidates = new Set();
        for (const entry of looseLootEntries) {
            collectLooseLootMarkers(entry?.layer, candidates);
            if (entry?.layer && mapInstance.hasLayer(entry.layer)) {
                mapInstance.removeLayer(entry.layer);
            }
        }

        // Removing one category can temporarily re-add a shared marker through
        // another active category. Sweep after every group has been removed.
        for (const marker of candidates) {
            if (mapInstance.hasLayer(marker)) {
                mapInstance.removeLayer(marker);
            }
        }

        clearActiveRareLootMarkers();

        const selected = [...candidates].filter(isDedicatedRareLootMarker);
        for (const marker of selected) {
            marker.addTo(mapInstance);
            const icon = marker._icon;
            if (icon instanceof Element) {
                icon.classList.add(rareLootMarkerClass);
                icon.setAttribute(
                    "aria-label",
                    icon.getAttribute("title") || "Objet rare possible");
            }
            activeRareLootMarkers.set(marker, {
                map: mapInstance,
                addedByRaidPilot: true,
            });
        }
        return selected.length > 0;
    };

    // tarkov.dev animates a full-map pan for every player packet. Patch the
    // actual map instance: keep the map still while the marker moves, then
    // recenter instantly only when the marker approaches an edge.
    const installStablePlayerFollow = () => {
        const mapInstance = findLeafletMapInstance();
        if (!mapInstance) {
            return false;
        }
        if (mapInstance.__raidPilotStablePlayerFollow === true) {
            return true;
        }

        const originalPanTo = mapInstance.panTo;
        Object.defineProperty(mapInstance, "__raidPilotStablePlayerFollow", {
            value: true,
        });
        mapInstance.panTo = function (latlng, options) {
            const container = this.getContainer?.();
            const playerLayerIsActive = this.layerControl?._layers?.some?.(
                (entry) => entry?.key === "player-position"
                    || entry?.layer?.key === "player-position");
            const playerMarkerIsVisible = container?.querySelector?.(
                ".leaflet-marker-pane > .marker");
            const isLivePlayerPan = container?.id === "leaflet-map"
                && options?.animate === true
                && (playerLayerIsActive || playerMarkerIsVisible);
            if (!isLivePlayerPan) {
                return originalPanTo.call(this, latlng, options);
            }

            const centerIdentity = location.pathname;
            if (this.__raidPilotCenterIdentity !== centerIdentity) {
                this.__raidPilotCenterIdentity = centerIdentity;
                this.__raidPilotDidInitialCenter = false;
            }

            let shouldRecenter = this.__raidPilotDidInitialCenter !== true;
            if (!shouldRecenter) {
                try {
                    const point = this.latLngToContainerPoint(latlng);
                    const size = this.getSize();
                    const marginX = Math.min(96, size.x * 0.22);
                    const marginY = Math.min(72, size.y * 0.22);
                    shouldRecenter = point.x < marginX
                        || point.y < marginY
                        || point.x > size.x - marginX
                        || point.y > size.y - marginY;
                } catch {
                    shouldRecenter = false;
                }
            }

            if (!shouldRecenter) {
                return this;
            }

            this.__raidPilotDidInitialCenter = true;
            const stableOptions = options && typeof options === "object"
                ? { ...options, animate: false }
                : { animate: false };
            return originalPanTo.call(this, latlng, stableOptions);
        };
        return true;
    };

    if (!installStablePlayerFollow()) {
        // React/Leaflet can take a moment to mount at raid start. Walking the
        // Fiber graph every 50 ms competed with the map renderer exactly when
        // it was busiest. A short exponential backoff remains responsive while
        // cutting the worst-case startup scans from 300 to roughly twenty.
        const installDeadline = performance.now() + 15000;
        let installRetryDelay = 100;
        const retryStablePlayerFollow = () => {
            if (installStablePlayerFollow() || performance.now() >= installDeadline) {
                return;
            }
            installRetryDelay = Math.min(1000, Math.ceil(installRetryDelay * 1.6));
            window.setTimeout(retryStablePlayerFollow, installRetryDelay);
        };
        window.setTimeout(retryStablePlayerFollow, installRetryDelay);
    }

    let playerMarkerRemovalTimer = 0;
    let playerMarkerTransitionResetFrame = 0;

    const normalizeSignedAngle = (angle) => {
        const wrapped = ((angle + 180) % 360 + 360) % 360 - 180;
        return wrapped === -180 ? 180 : wrapped;
    };

    const getMarkerPixelPosition = (marker) => {
        const transform = marker?.style?.getPropertyValue("transform") || "";
        if (!transform || transform === "none") {
            return null;
        }

        try {
            const matrix = new DOMMatrixReadOnly(transform);
            if (Number.isFinite(matrix.m41) && Number.isFinite(matrix.m42)) {
                return { x: matrix.m41, y: matrix.m42 };
            }
        } catch {
            // Older WebView2 builds can reject a valid translate3d value.
        }

        const match = transform.match(
            /translate(?:3d)?\(\s*(-?[\d.]+)px\s*,\s*(-?[\d.]+)px/i);
        if (!match) {
            return null;
        }
        const x = Number(match[1]);
        const y = Number(match[2]);
        return Number.isFinite(x) && Number.isFinite(y) ? { x, y } : null;
    };

    const updateStablePlayerMarkerStyle = (stable, source, pane) => {
        if (stable.style.cssText === source.style.cssText) {
            return;
        }

        const previous = getMarkerPixelPosition(stable);
        const next = getMarkerPixelPosition(source);
        const mapSize = Math.min(pane.clientWidth || innerWidth, pane.clientHeight || innerHeight);
        const snapDistance = Math.max(48, Math.min(120, mapSize * 0.22));
        const shouldSnap = !previous
            || !next
            || Math.hypot(next.x - previous.x, next.y - previous.y) >= snapDistance;

        // Ordinary live packets are interpolated by CSS. A recenter, zoom or
        // exact F8 correction can move dozens of pixels at once; snap those
        // changes so the marker never flies across the map after the map jumps.
        if (shouldSnap) {
            if (playerMarkerTransitionResetFrame !== 0) {
                window.cancelAnimationFrame(playerMarkerTransitionResetFrame);
            }
            stable.classList.add("raidpilot-player-marker-snap");
        }
        stable.style.cssText = source.style.cssText;
        if (shouldSnap) {
            playerMarkerTransitionResetFrame = window.requestAnimationFrame(() => {
                playerMarkerTransitionResetFrame = 0;
                stable.classList.remove("raidpilot-player-marker-snap");
            });
        }
    };

    const getImageRotation = (image) => {
        const rawRotation = image?.style?.getPropertyValue("rotate") || "";
        const rotation = Number.parseFloat(rawRotation);
        return Number.isFinite(rotation) ? rotation : null;
    };

    const updateStablePlayerImageStyle = (stableImage, sourceImage) => {
        const previousRotation = getImageRotation(stableImage);
        const nextRotation = getImageRotation(sourceImage);
        if (stableImage.style.cssText !== sourceImage.style.cssText) {
            stableImage.style.cssText = sourceImage.style.cssText;
        }
        if (previousRotation === null || nextRotation === null) {
            return;
        }

        const continuousRotation = previousRotation + normalizeSignedAngle(
            nextRotation - normalizeSignedAngle(previousRotation));
        stableImage.style.setProperty("rotate", `${continuousRotation}deg`);
    };

    const syncStablePlayerMarker = () => {
        const pane = document.querySelector("#leaflet-map .leaflet-marker-pane");
        if (!pane) {
            return;
        }

        const sourceMarkers = [...pane.querySelectorAll(
            ":scope > .leaflet-marker-icon.marker:not(.raidpilot-player-marker)")];
        const source = sourceMarkers.at(-1);
        let stable = pane.querySelector(":scope > .raidpilot-player-marker");

        if (localPlayerPoseState) {
            for (const marker of sourceMarkers) {
                marker.classList.add("raidpilot-player-marker-source");
            }
            stable?.remove();
            return;
        }

        if (!source) {
            if (stable && playerMarkerRemovalTimer === 0) {
                playerMarkerRemovalTimer = window.setTimeout(() => {
                    playerMarkerRemovalTimer = 0;
                    if (!pane.querySelector(
                        ":scope > .leaflet-marker-icon.marker:not(.raidpilot-player-marker)")) {
                        stable.remove();
                    }
                }, 650);
            }
            return;
        }

        if (playerMarkerRemovalTimer !== 0) {
            window.clearTimeout(playerMarkerRemovalTimer);
            playerMarkerRemovalTimer = 0;
        }

        for (const marker of sourceMarkers) {
            marker.classList.add("raidpilot-player-marker-source");
        }
        if (!stable) {
            stable = source.cloneNode(true);
            stable.classList.remove("raidpilot-player-marker-source", "leaflet-interactive");
            stable.classList.add("raidpilot-player-marker");
            stable.removeAttribute("tabindex");
            stable.removeAttribute("role");
            stable.setAttribute("aria-hidden", "true");
            pane.appendChild(stable);
        }

        updateStablePlayerMarkerStyle(stable, source, pane);
        const sourceImage = source.querySelector("img");
        let stableImage = stable.querySelector("img");
        if (sourceImage && !stableImage) {
            stable.replaceChildren(sourceImage.cloneNode(true));
            stableImage = stable.querySelector("img");
        }
        if (sourceImage && stableImage) {
            const sourceUrl = sourceImage.getAttribute("src");
            if (stableImage.getAttribute("src") !== sourceUrl) {
                stableImage.setAttribute("src", sourceUrl || "");
            }
            updateStablePlayerImageStyle(stableImage, sourceImage);
        }
    };

    const setImportantStyle = (element, property, value) => {
        if (element.style.getPropertyValue(property) === value
            && element.style.getPropertyPriority(property) === "important") {
            return;
        }
        element.style.setProperty(property, value, "important");
    };

    const hideElement = (element) => {
        if (element instanceof HTMLElement || element instanceof SVGElement) {
            setImportantStyle(element, "display", "none");
        }
    };

    // One preference for every map: zoom relative to the map's default level.
    const mapZoomStoragePrefix = "raidpilot.mapZoom.";
    const mapZoomGlobalStorageKey = "raidpilot.mapZoomFactor.global.v1";
    const zoomFactorMin = 0.20;
    const zoomFactorMax = 32;
    let mapZoomBoundInstance = null;
    let mapZoomBoundHandlers = null;
    let mapZoomGestureDeadline = 0;
    let mapZoomHostTimer = null;
    let mapZoomLastStoredFactor = null;
    const mapZoomGestureContainers = new WeakSet();

    const mapZoomStorageKey = () =>
        mapZoomStoragePrefix + location.pathname.toLowerCase();

    const validZoomFactor = raw => {
        if (raw === null || raw === undefined || raw === "") return null;
        const value = Number(raw);
        return Number.isFinite(value) && value >= zoomFactorMin && value <= zoomFactorMax
            ? value : null;
    };

    const clampMapZoom = (mapInstance, zoom) => {
        if (zoom === null || zoom === undefined || zoom === "") return null;
        let value = Number(zoom);
        if (!Number.isFinite(value)) return null;
        const minimum = Number(mapInstance?.getMinZoom?.());
        const maximum = Number(mapInstance?.getMaxZoom?.());
        if (Number.isFinite(minimum)) value = Math.max(minimum, value);
        if (Number.isFinite(maximum)) value = Math.min(maximum, value);
        return value;
    };

    const globalZoomFactor = () => {
        const fromHost = validZoomFactor(window.__raidPilotHostZoomFactor);
        if (fromHost !== null) return fromHost;
        try {
            return validZoomFactor(localStorage.getItem(mapZoomGlobalStorageKey));
        } catch {
            return null;
        }
    };

    const zoomForSavedFactor = (mapInstance, factor) => {
        const base = Number(mapInstance?.__raidPilotBaseZoom);
        if (!Number.isFinite(base) || factor === null) return null;
        return clampMapZoom(mapInstance, base + Math.log2(factor));
    };

    const markMapZoomGesture = () => {
        mapZoomGestureDeadline = Date.now() + 1600;
    };

    const saveMapZoomFromGesture = (mapInstance) => {
        if (Date.now() > mapZoomGestureDeadline) return;
        const zoom = Number(mapInstance?.getZoom?.());
        const base = Number(mapInstance?.__raidPilotBaseZoom);
        if (!Number.isFinite(zoom) || !Number.isFinite(base)) return;

        const rawFactor = 2 ** (zoom - base);
        if (!Number.isFinite(rawFactor)) return;
        const factor = Math.max(zoomFactorMin, Math.min(zoomFactorMax, rawFactor));
        if (mapZoomLastStoredFactor !== null
            && Math.abs(mapZoomLastStoredFactor - factor) < 1e-6) return;
        mapZoomLastStoredFactor = factor;

        try {
            localStorage.setItem(mapZoomGlobalStorageKey, String(factor));
            localStorage.setItem(mapZoomStorageKey(), String(zoom));
        } catch {
        }

        if (mapZoomHostTimer !== null) clearTimeout(mapZoomHostTimer);
        mapZoomHostTimer = setTimeout(() => {
            mapZoomHostTimer = null;
            try {
                window.chrome?.webview?.postMessage?.({
                    type: "raidpilot-map-zoom-preference",
                    payload: { factor }
                });
            } catch {
            }
        }, 160);
    };

    const updateMapZoomControls = (mapInstance = mapZoomBoundInstance) => {
        const controls = document.getElementById("raidpilot-zoom-controls");
        if (!controls || !mapInstance) return;
        const zoom = Number(mapInstance.getZoom?.());
        const baseZoom = Number(mapInstance.__raidPilotBaseZoom);
        const output = controls.querySelector(".raidpilot-zoom-value");
        if (output && Number.isFinite(zoom)) {
            const percent = Number.isFinite(baseZoom)
                ? Math.round(100 * (2 ** (zoom - baseZoom)))
                : 100;
            output.textContent = String(Math.max(1, percent)) + "%";
        }
        const min = Number(mapInstance.getMinZoom?.());
        const max = Number(mapInstance.getMaxZoom?.());
        const minus = controls.querySelector('[data-raidpilot-zoom="out"]');
        const plus = controls.querySelector('[data-raidpilot-zoom="in"]');
        if (minus) minus.disabled = Number.isFinite(min) && Number.isFinite(zoom) && zoom <= min;
        if (plus) plus.disabled = Number.isFinite(max) && Number.isFinite(zoom) && zoom >= max;
    };

    const applySavedMapZoom = (mapInstance) => {
        let factor = globalZoomFactor();
        if (factor === null) {
            // Upgrade the old per-map zoom without losing existing preferences.
            try {
                const legacyZoom = clampMapZoom(
                    mapInstance, localStorage.getItem(mapZoomStorageKey()));
                const baseZoom = Number(mapInstance.__raidPilotBaseZoom);
                if (legacyZoom !== null && Number.isFinite(baseZoom)) {
                    factor = validZoomFactor(2 ** (legacyZoom - baseZoom));
                }
            } catch {
            }
        }
        const target = zoomForSavedFactor(mapInstance, factor);
        if (target !== null && Math.abs(Number(mapInstance.getZoom?.()) - target) > 1e-7) {
            mapInstance.setZoom?.(target, { animate: false });
        }
        mapZoomLastStoredFactor = factor;
    };

    window.addEventListener("raidpilot-zoom-sync", () => {
        const factor = validZoomFactor(window.__raidPilotHostZoomFactor);
        if (factor === null) return;
        try {
            localStorage.setItem(mapZoomGlobalStorageKey, String(factor));
        } catch {
        }
        mapZoomLastStoredFactor = factor;
        mapZoomGestureDeadline = 0;
        if (mapZoomBoundInstance) {
            const target = zoomForSavedFactor(mapZoomBoundInstance, factor);
            if (target !== null) {
                mapZoomBoundInstance.setZoom?.(target, { animate: false });
                updateMapZoomControls(mapZoomBoundInstance);
            }
        }
    });

    const ensureMapZoomControls = () => {
        const container = document.getElementById("leaflet-map");
        const mapInstance = findLeafletMapInstance();
        if (!container || !mapInstance) return false;

        mapInstance.scrollWheelZoom?.enable?.();
        mapInstance.dragging?.enable?.();

        if (!mapZoomGestureContainers.has(container)) {
            mapZoomGestureContainers.add(container);
            for (const eventName of ["wheel", "dblclick", "touchstart"]) {
                container.addEventListener(eventName, markMapZoomGesture, { capture: true, passive: true });
            }
        }

        if (mapZoomBoundInstance !== mapInstance) {
            if (mapZoomBoundInstance && mapZoomBoundHandlers) {
                mapZoomBoundInstance.off?.("zoomend", mapZoomBoundHandlers.zoomend);
                mapZoomBoundInstance.off?.("zoom", mapZoomBoundHandlers.zoom);
                mapZoomBoundInstance.off?.("viewreset", mapZoomBoundHandlers.viewreset);
            }
            mapZoomBoundInstance = mapInstance;
            mapZoomGestureDeadline = 0;
            mapInstance.__raidPilotBaseZoom = Number(mapInstance.getZoom?.());
            if (mapInstance.__raidPilotZoomRestored !== true) {
                mapInstance.__raidPilotZoomRestored = true;
                applySavedMapZoom(mapInstance);
            }
            mapZoomBoundHandlers = {
                zoomend: () => {
                    saveMapZoomFromGesture(mapInstance);
                    updateMapZoomControls(mapInstance);
                },
                zoom: () => {
                    saveMapZoomFromGesture(mapInstance);
                    updateMapZoomControls(mapInstance);
                },
                viewreset: () => updateMapZoomControls(mapInstance),
            };
            mapInstance.on?.("zoomend", mapZoomBoundHandlers.zoomend);
            mapInstance.on?.("zoom", mapZoomBoundHandlers.zoom);
            mapInstance.on?.("viewreset", mapZoomBoundHandlers.viewreset);
        }

        let controls = document.getElementById("raidpilot-zoom-controls");
        if (!controls || controls.parentElement !== container) {
            controls?.remove();
            controls = document.createElement("div");
            controls.id = "raidpilot-zoom-controls";
            controls.className = "raidpilot-zoom-controls";
            controls.innerHTML = [
                '<button type="button" data-raidpilot-zoom="out" aria-label="Zoom arrière" title="Zoom arrière">−</button>',
                '<output class="raidpilot-zoom-value" aria-live="polite">100%</output>',
                '<button type="button" data-raidpilot-zoom="in" aria-label="Zoom avant" title="Zoom avant">+</button>',
                '<button type="button" data-raidpilot-zoom="center" aria-label="Recentrer sur mon joueur" title="Recentrer sur mon joueur">⌖</button>',
            ].join("");
            for (const eventName of ["pointerdown", "mousedown", "dblclick", "wheel"]) {
                controls.addEventListener(eventName, (event) => event.stopPropagation(), { passive: eventName === "wheel" });
            }
            controls.addEventListener("click", (event) => {
                const button = event.target?.closest?.("button[data-raidpilot-zoom]");
                if (!button) return;
                event.preventDefault();
                event.stopPropagation();
                const liveMap = mapZoomBoundInstance || findLeafletMapInstance();
                if (!liveMap) return;
                const action = button.dataset.raidpilotZoom;
                if (action === "in") {
                    markMapZoomGesture();
                    liveMap.zoomIn?.(1, { animate: false });
                } else if (action === "out") {
                    markMapZoomGesture();
                    liveMap.zoomOut?.(1, { animate: false });
                } else if (action === "center"
                    && localPlayerPoseState
                    && Number.isFinite(localPlayerPoseState.x)
                    && Number.isFinite(localPlayerPoseState.z)) {
                    liveMap.setView?.(
                        [localPlayerPoseState.z, localPlayerPoseState.x],
                        liveMap.getZoom?.(),
                        { animate: false });
                }
            });
            container.appendChild(controls);
        }

        updateMapZoomControls(mapInstance);
        return true;
    };

    const mapViewportHeight = "calc(100vh - var(--raidpilot-guidance-height, 72px) - var(--raidpilot-quest-height, 0px))";
    let kioskMapElement = null;
    let kioskMapResizeObserver = null;
    let kioskResizeFrame = 0;
    const observeMapViewportSize = () => {
        const container = document.getElementById("leaflet-map");
        if (container === kioskMapElement) return;
        kioskMapResizeObserver?.disconnect();
        if (kioskResizeFrame) window.cancelAnimationFrame(kioskResizeFrame);
        kioskMapElement = container;
        kioskResizeFrame = 0;
        if (!container || typeof ResizeObserver !== "function") return;
        kioskMapResizeObserver = new ResizeObserver(() => {
            if (kioskResizeFrame) return;
            kioskResizeFrame = window.requestAnimationFrame(() => {
                kioskResizeFrame = 0;
                if (container !== kioskMapElement || !container.isConnected) return;
                // Layout-only: keep the current map center, zoom and GPS state.
                const map = findLeafletMapInstance();
                map?.invalidateSize?.({ pan: false, animate: false });
            });
        });
        kioskMapResizeObserver.observe(container);
    };

    const applyKioskLayout = () => {
        for (const selector of [
            ".navigation",
            ".footer-wrapper",
            ".CookieConsent",
            ".id-wrapper",
            ".leaflet-control-container",
            ".leaflet-popup-pane",
            ".leaflet-tooltip-pane",
            ".leaflet-shadow-pane",
            "body > #root > .App > .MuiBox-root",
        ]) {
            for (const element of document.querySelectorAll(selector)) {
                hideElement(element);
            }
        }

        for (const [selector, properties] of [
            ["html", { width: "100vw", height: "100vh", overflow: "hidden" }],
            ["body", { width: "100vw", height: "100vh", margin: "0px", overflow: "hidden" }],
            ["#root", { width: "100vw", height: "100vh" }],
            [".App", { width: "100vw", height: "100vh" }],
            [".display-wrapper.map-page", {
                position: "fixed",
                inset: "var(--raidpilot-guidance-height, 72px) 0px var(--raidpilot-quest-height, 0px) 0px",
                width: "100vw",
                height: mapViewportHeight,
                margin: "0px",
                padding: "0px",
            }],
            ["#leaflet-map", { width: "100vw", height: mapViewportHeight, margin: "0px" }],
        ]) {
            const element = document.querySelector(selector);
            if (!element) {
                continue;
            }
            for (const [property, value] of Object.entries(properties)) {
                setImportantStyle(element, property, value);
            }
        }
    };

    const turnOffGroup = (group) => {
        const key = group.dataset.key;

        // Level selectors currently have no stable data-key. Preserve any group
        // without a key, as well as the three explicitly allowed stable groups.
        if (!key || visibleGroups.has(key)) {
            return;
        }

        const groupSelector = group.querySelector(":scope > .leaflet-control-layers-group-label .leaflet-control-layers-group-selector");
        if (groupSelector) {
            if (groupSelector.checked || groupSelector.indeterminate) {
                // Starting from checked guarantees click() ends in the off state,
                // including the browser's otherwise ambiguous indeterminate state.
                groupSelector.indeterminate = false;
                groupSelector.checked = true;
                groupSelector.click();
            }
            return;
        }

        // Defensive fallback for an exclusive group without a group checkbox.
        for (const selector of group.querySelectorAll("input.leaflet-control-layers-selector:checked")) {
            selector.click();
        }
    };

    const groupNeedsLayerPolicy = (group) => {
        const key = group.dataset.key;
        if (key === rareLootGroupKey) {
            return true;
        }
        if (!key || visibleGroups.has(key)) {
            return false;
        }

        const groupSelector = group.querySelector(
            ":scope > .leaflet-control-layers-group-label .leaflet-control-layers-group-selector");
        if (groupSelector) {
            return groupSelector.checked || groupSelector.indeterminate;
        }
        return Boolean(group.querySelector(
            "input.leaflet-control-layers-selector:checked"));
    };

    const nodeContainsActiveDisallowedGroup = (node) => {
        if (!(node instanceof Element)) {
            return false;
        }
        const groups = node.matches(".leaflet-control-layers-group")
            ? [node]
            : [...node.querySelectorAll(".leaflet-control-layers-group")];
        return groups.some(groupNeedsLayerPolicy);
    };

    const applyLayerPolicy = () => {
        updateScheduled = false;
        installStablePlayerFollow();
        applyKioskLayout();
        observeMapViewportSize();
        ensureMapZoomControls();
        for (const group of document.querySelectorAll(".leaflet-control-layers-group")) {
            turnOffGroup(group);
        }
        enforceRareLootMarkers();

        const map = document.getElementById("leaflet-map");
        if (map) {
            let credit = document.getElementById("raidpilot-map-credit");
            if (!credit) {
                credit = document.createElement("div");
                credit.id = "raidpilot-map-credit";
                credit.className = "raidpilot-map-credit";
                map.appendChild(credit);
            }

            const author = document.querySelector(".leaflet-control-raid-info a")?.textContent?.trim();
            const creditText = author ? `Carte · ${author} · tarkov.dev` : "Carte · tarkov.dev";
            if (credit.textContent !== creditText) {
                credit.textContent = creditText;
            }
        }

        window.__raidPilotApplyLocalPose?.();
        syncStablePlayerMarker();
        window.__raidPilotApplyGuidanceTarget?.();
        window.__raidPilotApplyRaidExtractions?.();
        window.__raidPilotApplyRaidQuests?.();
        window.__raidPilotApplyLearnedRoute?.();
        window.__raidPilotApplyRaidTimer?.();
        window.__raidPilotRefreshFloorMarkers?.();
    };

    const scheduleLayerPolicy = () => {
        if (updateScheduled) {
            return;
        }
        updateScheduled = true;
        window.requestAnimationFrame(applyLayerPolicy);
    };

    const normalizeLabel = (value) => String(value ?? "")
        .normalize("NFD")
        .replace(/[\u0300-\u036f]/g, "")
        .replace(/[^a-z0-9]+/gi, " ")
        .trim()
        .toLowerCase();

    const clearGuidanceTarget = () => {
        for (const marker of document.querySelectorAll(".raidpilot-guidance-target")) {
            marker.classList.remove("raidpilot-guidance-target");
        }
    };

    const clearRaidExtractionMarkers = () => {
        for (const marker of document.querySelectorAll(".raidpilot-raid-extraction")) {
            marker.classList.remove("raidpilot-raid-extraction");
        }
    };

    window.__raidPilotClearRaidExtractions = (generation = raidExtractionGeneration) => {
        const requestedGeneration = Number.isFinite(Number(generation))
            ? Number(generation)
            : raidExtractionGeneration;
        if (requestedGeneration < raidExtractionGeneration) {
            return;
        }
        if (requestedGeneration === raidExtractionGeneration
            && raidExtractionsConfirmed) {
            return;
        }
        raidExtractionGeneration = requestedGeneration;
        raidExtractionEntries = [];
        raidExtractionsConfirmed = false;
        document.documentElement.classList.remove("raidpilot-raid-extractions-confirmed");
        clearRaidExtractionMarkers();
    };

    window.__raidPilotSetRaidExtractions = (payload) => {
        if (!payload || !Array.isArray(payload.extractions)) {
            return;
        }
        const requestedGeneration = Number.isFinite(Number(payload.generation))
            ? Number(payload.generation)
            : raidExtractionGeneration;
        if (requestedGeneration < raidExtractionGeneration) {
            return;
        }

        raidExtractionGeneration = requestedGeneration;
        raidExtractionEntries = payload.extractions
            .map((entry) => ({
                id: String(entry?.id ?? "").trim().toLowerCase(),
                labels: [...new Set((Array.isArray(entry?.labels) ? entry.labels : [])
                    .map(normalizeLabel)
                    .filter(Boolean))],
            }))
            .filter((entry) => entry.id || entry.labels.length > 0);
        raidExtractionsConfirmed = true;
        document.documentElement.classList.remove("raidpilot-raid-extractions-confirmed");
        return window.__raidPilotApplyRaidExtractions();
    };

    window.__raidPilotApplyRaidExtractions = () => {
        clearRaidExtractionMarkers();
        if (!raidExtractionsConfirmed) {
            return 0;
        }

        const indexedIcons = new Map();
        const mapInstance = findLeafletMapInstance();
        const layers = mapInstance?._layers && typeof mapInstance._layers === "object"
            ? Object.values(mapInstance._layers)
            : [];
        for (const layer of layers) {
            const icon = layer?._icon;
            if (!(icon instanceof Element) || !icon.classList.contains("extract-icon")) {
                continue;
            }
            const layerId = String(layer?.options?.id ?? "").trim().toLowerCase();
            indexedIcons.set(icon, layerId);
        }

        let matchedCount = 0;
        for (const marker of document.querySelectorAll("#leaflet-map .extract-icon")) {
            const layerId = indexedIcons.get(marker) || "";
            const matchesId = layerId && raidExtractionEntries.some(
                (entry) => entry.id === layerId);
            const label = marker.getAttribute("title")
                || marker.querySelector(".extract-name")?.textContent
                || "";
            const normalized = normalizeLabel(label);
            const matchesLabel = normalized && raidExtractionEntries.some(
                (entry) => entry.labels.includes(normalized));
            const matches = Boolean(matchesId || matchesLabel);
            marker.classList.toggle("raidpilot-raid-extraction", matches);
            if (matches) {
                matchedCount += 1;
            }
        }
        document.documentElement.classList.toggle(
            "raidpilot-raid-extractions-confirmed",
            matchedCount > 0);
        return matchedCount;
    };

    const questLayerKeys = new Set(["quest_item", "quest_objective"]);

    const collectQuestMarkerLayers = (mapInstance) => {
        const controlEntries = Array.isArray(mapInstance?.layerControl?._layers)
            ? mapInstance.layerControl._layers
            : [];
        const pending = controlEntries
            .filter((entry) => questLayerKeys.has(String(entry?.key ?? "")))
            .map((entry) => entry?.layer)
            .filter(Boolean);
        const visited = new Set();
        const markers = [];

        while (pending.length > 0 && visited.size < 12000) {
            const layer = pending.pop();
            if (!layer || visited.has(layer)) {
                continue;
            }
            visited.add(layer);

            const questId = String(layer?.options?.questId ?? "").trim().toLowerCase();
            if (questId && typeof layer.addTo === "function") {
                markers.push(layer);
            }

            const children = layer?._layers && typeof layer._layers === "object"
                ? Object.values(layer._layers)
                : [];
            pending.push(...children);
        }
        return markers;
    };

    const releaseRaidQuestLayer = (layer, state) => {
        layer?._icon?.classList?.remove("raidpilot-quest-marker");
        layer?._icon?.removeAttribute?.("data-raidpilot-quest-name");
        if (state?.addedByRaidPilot === true
            && state.map
            && typeof state.map.hasLayer === "function"
            && state.map.hasLayer(layer)) {
            state.map.removeLayer(layer);
        }
        activeRaidQuestLayers.delete(layer);
    };

    const clearRaidQuestMarkers = () => {
        for (const [layer, state] of [...activeRaidQuestLayers.entries()]) {
            releaseRaidQuestLayer(layer, state);
        }
        for (const marker of document.querySelectorAll(".raidpilot-quest-marker")) {
            marker.classList.remove("raidpilot-quest-marker");
            marker.removeAttribute("data-raidpilot-quest-name");
        }
    };

    const scheduleRaidQuestRetry = () => {
        if (raidQuestRetryTimer !== 0
            || !raidQuestsConfirmed
            || raidQuestRetryAttempts >= 40) {
            return;
        }
        raidQuestRetryTimer = window.setTimeout(() => {
            raidQuestRetryTimer = 0;
            raidQuestRetryAttempts += 1;
            const matched = window.__raidPilotApplyRaidQuests?.() ?? 0;
            const requested = new Set(raidQuestEntries.map((entry) => entry.id)).size;
            if (matched < requested) {
                scheduleRaidQuestRetry();
            }
        }, 250);
    };

    window.__raidPilotClearRaidQuests = (generation = raidQuestGeneration) => {
        const requestedGeneration = Number.isFinite(Number(generation))
            ? Number(generation)
            : raidQuestGeneration;
        if (requestedGeneration < raidQuestGeneration) {
            return;
        }
        if (requestedGeneration === raidQuestGeneration && raidQuestsConfirmed) {
            return;
        }

        raidQuestGeneration = requestedGeneration;
        raidQuestEntries = [];
        raidQuestsConfirmed = false;
        raidQuestRetryAttempts = 0;
        if (raidQuestRetryTimer !== 0) {
            window.clearTimeout(raidQuestRetryTimer);
            raidQuestRetryTimer = 0;
        }
        clearRaidQuestMarkers();
    };

    window.__raidPilotSetRaidQuests = (payload) => {
        if (!payload || !Array.isArray(payload.quests)) {
            return 0;
        }
        const requestedGeneration = Number.isFinite(Number(payload.generation))
            ? Number(payload.generation)
            : raidQuestGeneration;
        if (requestedGeneration < raidQuestGeneration) {
            return 0;
        }

        raidQuestGeneration = requestedGeneration;
        raidQuestEntries = payload.quests
            .map((entry) => ({
                id: String(entry?.id ?? "").trim().toLowerCase(),
                name: String(entry?.name ?? "").trim(),
            }))
            .filter((entry) => entry.id);
        raidQuestsConfirmed = true;
        raidQuestRetryAttempts = 0;
        const matched = window.__raidPilotApplyRaidQuests();
        const requested = new Set(raidQuestEntries.map((entry) => entry.id)).size;
        if (matched < requested) {
            scheduleRaidQuestRetry();
        }
        return matched;
    };

    window.__raidPilotApplyRaidQuests = () => {
        const mapInstance = findLeafletMapInstance();
        if (!raidQuestsConfirmed || !mapInstance) {
            return 0;
        }

        const wanted = new Map(raidQuestEntries.map((entry) => [entry.id, entry]));
        const available = collectQuestMarkerLayers(mapInstance);
        const selected = new Set(available.filter((layer) => wanted.has(
            String(layer?.options?.questId ?? "").trim().toLowerCase())));

        for (const [layer, state] of [...activeRaidQuestLayers.entries()]) {
            if (!selected.has(layer) || state.map !== mapInstance) {
                releaseRaidQuestLayer(layer, state);
            }
        }

        const matchedQuestIds = new Set();
        for (const layer of selected) {
            const questId = String(layer.options.questId).trim().toLowerCase();
            const quest = wanted.get(questId);
            let state = activeRaidQuestLayers.get(layer);
            if (!mapInstance.hasLayer(layer)) {
                layer.addTo(mapInstance);
                state = { map: mapInstance, addedByRaidPilot: true };
            } else if (!state) {
                state = { map: mapInstance, addedByRaidPilot: false };
            }
            activeRaidQuestLayers.set(layer, state);

            const icon = layer._icon;
            if (icon instanceof Element) {
                icon.classList.add("raidpilot-quest-marker");
                icon.setAttribute("data-raidpilot-quest-name", quest?.name || "Quête");
                icon.setAttribute("aria-label", quest?.name || "Objectif de quête");
                matchedQuestIds.add(questId);
            }
        }
        return matchedQuestIds.size;
    };

    // Render identity is independent of EXFIL recognition and protected against stale async updates.
    let raidTimerUiSequence = -1;
    let raidTimerReadState = "waiting";
    const timerStatusLabel = state => ({reading:"LECTURE", unreadable:"NON LU",
        confirmationrequired:"À CONFIRMER", failed:"NON LU", synchronized:"SYNCHRO"}[state] ?? "NON LU");

    const ensureRaidTimerHud = () => {
        let hud = document.getElementById("raidpilot-raid-timer");
        if (hud) return hud;
        hud = document.createElement("div");
        hud.id = "raidpilot-raid-timer";
        hud.hidden = true;
        hud.setAttribute("aria-label", "Temps restant du raid");
        document.body.appendChild(hud);
        return hud;
    };

    const formatRaidTimer = (totalSeconds) => {
        const value = Math.max(0, Math.trunc(totalSeconds));
        const hours = Math.floor(value / 3600);
        const minutes = Math.floor((value % 3600) / 60);
        const seconds = value % 60;
        return hours > 0
            ? `${hours}:${String(minutes).padStart(2, "0")}:${String(seconds).padStart(2, "0")}`
            : `${String(minutes).padStart(2, "0")}:${String(seconds).padStart(2, "0")}`;
    };

    const renderRaidTimer = () => {
        const hud = ensureRaidTimerHud();
        if (!Number.isFinite(raidTimerDeadline)) {
            raidTimerLastSecond = null;
            hud.classList.remove("raidpilot-raid-timer-warning", "raidpilot-raid-timer-critical");
            hud.dataset.status = timerStatusLabel(raidTimerReadState);
            hud.dataset.state = raidTimerReadState;
            hud.title = "Temps non synchronisé. Le panneau O doit être lisible. Une lecture ratée reste récupérable même après reconnaissance des sorties.";
            if (raidTimerPending) {
                if (hud.textContent !== "--:--") hud.textContent = "--:--";
                hud.classList.add("raidpilot-raid-timer-pending");
                hud.hidden = false;
            } else {
                hud.classList.remove("raidpilot-raid-timer-pending");
                hud.hidden = true;
            }
            return;
        }
        const remainingSeconds = Math.max(0, Math.ceil((raidTimerDeadline - performance.now()) / 1000));
        raidTimerLastSecond = remainingSeconds;
        const text = formatRaidTimer(remainingSeconds);
        // Also repaint a DOM node recreated within the same second.
        if (hud.textContent !== text) hud.textContent = text;
        hud.dataset.status = "SYNCHRO";
        hud.dataset.state = "synchronized";
        hud.title = "Synchronisé sur une lecture du panneau du jeu, puis décompté localement. Le temps affiché par Tarkov fait foi.";
        hud.classList.remove("raidpilot-raid-timer-pending");
        hud.classList.toggle("raidpilot-raid-timer-warning", remainingSeconds <= 10 * 60);
        hud.classList.toggle("raidpilot-raid-timer-critical", remainingSeconds <= 5 * 60);
        hud.hidden = false;
    };

    const ensureRaidTimerLoop = () => {
        if (!raidTimerInterval) raidTimerInterval = window.setInterval(renderRaidTimer, 250);
    };

    const acceptTimerUpdate = (generation, sequence) => {
        if (!Number.isSafeInteger(generation) || generation < 0 || generation < raidTimerGeneration) return null;
        const next = sequence === undefined ? raidTimerUiSequence + 1 : sequence;
        if (!Number.isSafeInteger(next) || next < 0
            || (generation === raidTimerGeneration && next <= raidTimerUiSequence)) return null;
        return next;
    };

    window.__raidPilotClearRaidTimer = (generation = raidTimerGeneration, showPending = false, sequence, state = "waiting") => {
        const next = acceptTimerUpdate(generation, sequence);
        if (next === null) return false;
        raidTimerGeneration = generation;
        raidTimerUiSequence = next;
        raidTimerDeadline = null;
        raidTimerPending = showPending === true;
        raidTimerReadState = ["waiting","reading","unreadable","confirmationrequired","failed"].includes(state) ? state : "waiting";
        raidTimerLastSecond = null;
        if (raidTimerPending) ensureRaidTimerLoop();
        else if (raidTimerInterval) { window.clearInterval(raidTimerInterval); raidTimerInterval = 0; }
        renderRaidTimer();
        return true;
    };

    window.__raidPilotSetRaidTimer = payload => {
        const generation = payload?.generation, remainingSeconds = payload?.remainingSeconds;
        const next = acceptTimerUpdate(generation, payload?.sequence);
        if (next === null || typeof remainingSeconds !== "number" || !Number.isFinite(remainingSeconds)
            || remainingSeconds < 0 || remainingSeconds > 3 * 60 * 60) return false;
        raidTimerGeneration = generation;
        raidTimerUiSequence = next;
        raidTimerDeadline = performance.now() + remainingSeconds * 1000;
        raidTimerPending = false;
        raidTimerReadState = "synchronized";
        raidTimerLastSecond = null;
        ensureRaidTimerLoop();
        renderRaidTimer();
        return true;
    };

    window.__raidPilotApplyRaidTimer = renderRaidTimer;
    window.addEventListener("pageshow", renderRaidTimer);
    document.addEventListener("visibilitychange", renderRaidTimer);

    // Context is informational: only host-confirmed quests and a fresh, bounded-error pose.
    // No capture, network request or completion inference is performed here.
    let questContextSequence = -1;
    const setQuestContextSpace = (visible) => {
        const value = visible ? "96px" : "0px";
        const root = document.documentElement;
        if (root.style.getPropertyValue("--raidpilot-quest-height") !== value) {
            root.style.setProperty("--raidpilot-quest-height", value);
            window.requestAnimationFrame(() => window.dispatchEvent(new Event("resize")));
        }
    };
    const ensureQuestContext = () => {
        let panel = document.getElementById("raidpilot-quest-context");
        if (panel) return panel;
        panel = document.createElement("section");
        panel.id = "raidpilot-quest-context";
        panel.hidden = true;
        panel.setAttribute("aria-label", "Quête à proximité, progression non vérifiée");
        for (const [tag, cls] of [["strong", "name"], ["span", "where"], ["span", "instruction"], ["small", "note"]]) {
            const node = document.createElement(tag);
            node.className = "raidpilot-quest-context-" + cls;
            panel.appendChild(node);
        }
        document.body.appendChild(panel);
        return panel;
    };
    window.__raidPilotClearQuestContext = (sequence) => {
        sequence = Number(sequence);
        if (!Number.isSafeInteger(sequence) || sequence < questContextSequence) return false;
        questContextSequence = sequence;
        const panel = document.getElementById("raidpilot-quest-context");
        if (panel) panel.hidden = true;
        setQuestContextSpace(false);
        return true;
    };
    window.__raidPilotShowQuestContext = (context) => {
        if (!context || !Number.isSafeInteger(context.sequence) || context.sequence < questContextSequence) return false;
        if (!document.getElementById("leaflet-map")) return false;
        if (typeof context.name !== "string" || !context.name.trim() || typeof context.instruction !== "string" || !context.instruction.trim())
            return window.__raidPilotClearQuestContext(context.sequence);
        questContextSequence = context.sequence;
        const panel = ensureQuestContext();
        for (const key of ["name", "where", "instruction", "note"]) {
            const node = panel.querySelector(".raidpilot-quest-context-" + key);
            const text = typeof context[key] === "string" ? context[key].slice(0, 1800) : "";
            if (node.textContent !== text) node.textContent = text;
        }
        panel.title = [context.name, context.where, context.instruction, context.note, "Source : tarkov.dev/tasks"].join("\n");
        panel.dataset.source = "tarkov.dev/tasks";
        panel.hidden = false;
        setQuestContextSpace(true);
        return true;
    };

    const ensureGuidanceHud = () => {
        let hud = document.getElementById("raidpilot-guidance");
        if (hud) {
            return hud;
        }

        hud = document.createElement("div");
        hud.id = "raidpilot-guidance";
        hud.hidden = true;

        const arrow = document.createElement("div");
        arrow.className = "raidpilot-guidance-arrow";
        arrow.setAttribute("aria-hidden", "true");

        const label = document.createElement("div");
        label.className = "raidpilot-guidance-label";
        const name = document.createElement("strong");
        name.className = "raidpilot-guidance-name";
        const details = document.createElement("span");
        details.className = "raidpilot-guidance-details";
        label.append(name, details);
        hud.append(arrow, label);
        document.body.appendChild(hud);
        return hud;
    };

    const getContinuousGuidanceRotation = (arrow, requestedAngle) => {
        const canonical = normalizeSignedAngle(requestedAngle);
        const previousCanonical = Number(arrow.dataset.raidpilotCanonicalAngle);
        const previousRotation = Number(arrow.dataset.raidpilotRotation);
        const rotation = Number.isFinite(previousCanonical) && Number.isFinite(previousRotation)
            ? previousRotation + normalizeSignedAngle(canonical - previousCanonical)
            : canonical;
        arrow.dataset.raidpilotCanonicalAngle = String(canonical);
        arrow.dataset.raidpilotRotation = String(rotation);
        return rotation;
    };

    window.__raidPilotClearGuidance = (generation = guidanceGeneration) => {
        const requestedGeneration = Number.isFinite(Number(generation)) ? Number(generation) : guidanceGeneration;
        if (requestedGeneration < guidanceGeneration) {
            return;
        }
        guidanceGeneration = requestedGeneration;
        guidanceTargetLabels = [];
        guidanceTargetId = "";
        clearGuidanceTarget();
        const hud = document.getElementById("raidpilot-guidance");
        if (hud) {
            hud.hidden = true;
        }
    };

    window.__raidPilotShowGuidance = (guidance) => {
        if (!guidance || typeof guidance.name !== "string") {
            window.__raidPilotClearGuidance();
            return;
        }

        const requestedGeneration = Number.isFinite(Number(guidance.generation))
            ? Number(guidance.generation)
            : guidanceGeneration;
        if (requestedGeneration < guidanceGeneration) {
            return;
        }
        guidanceGeneration = requestedGeneration;

        const hud = ensureGuidanceHud();
        const arrow = hud.querySelector(".raidpilot-guidance-arrow");
        const name = hud.querySelector(".raidpilot-guidance-name");
        const details = hud.querySelector(".raidpilot-guidance-details");
        const angle = Number.isFinite(Number(guidance.angle)) ? Number(guidance.angle) : 0;
        const distance = Number.isFinite(Number(guidance.distance)) ? Math.max(0, Number(guidance.distance)) : 0;
        const vertical = Number.isFinite(Number(guidance.vertical)) ? Number(guidance.vertical) : 0;
        const uncertainty = Number.isFinite(Number(guidance.uncertainty))
            ? Math.max(0, Number(guidance.uncertainty))
            : 0;
        const altitude = Math.abs(vertical) >= 3
            ? ` · ${vertical > 0 ? "↑" : "↓"} ${Math.round(Math.abs(vertical))} m`
            : "";
        const liveStatus = guidance.estimated === true
            ? (guidance.needsReanchor === true || uncertainty > 4)
                ? ` · F8 ≈${Math.round(uncertainty)} m`
                : ` · LIVE ≈${Math.round(uncertainty)} m`
            : "";
        const conditionStatus = guidance.conditional === true ? " · ⚠ CONDITION" : "";
        const learnedRouteStatus = guidance.route === "learned"
            ? guidance.routeConfidence === "medium"
                ? "CHEMIN APPRIS · CONFIANCE MOYENNE"
                : "CHEMIN APPRIS"
            : "CAP DIRECT";

        // Keep rotation continuous around north: 179 -> -179 is a two-degree
        // correction, not a distracting full revolution of the arrow.
        const arrowRotation = getContinuousGuidanceRotation(
            arrow,
            guidance.arrived === true ? 0 : angle);
        const arrowTransform = `rotate(${arrowRotation}deg)`;
        if (arrow.style.transform !== arrowTransform) {
            arrow.style.transform = arrowTransform;
        }
        arrow.classList.toggle("arrived", guidance.arrived === true);
        if (name.textContent !== guidance.name) {
            name.textContent = guidance.name;
        }
        const detailText = guidance.arrived === true
            ? "ARRIVÉ"
            : guidance.provisional === true
                ? `${Math.round(distance)} m · PROVISOIRE · REFAIS F8${altitude}${conditionStatus}`
                : `${Math.round(distance)} m · ${learnedRouteStatus}${altitude}${liveStatus}${conditionStatus}`;
        if (details.textContent !== detailText) {
            details.textContent = detailText;
        }
        hud.classList.toggle("raidpilot-guidance-degraded", guidance.estimated === true && uncertainty >= 8);
        hud.classList.toggle("raidpilot-guidance-provisional", guidance.provisional === true);
        hud.classList.toggle("raidpilot-guidance-learned", guidance.route === "learned");
        if (hud.hidden) {
            hud.hidden = false;
        }

        const aliases = Array.isArray(guidance.aliases) ? guidance.aliases : [];
        const nextTargetId = String(guidance.id ?? "").trim().toLowerCase();
        const nextTargetLabels = [...new Set([guidance.name, ...aliases]
            .map(normalizeLabel)
            .filter(Boolean))];
        const targetChanged = nextTargetId !== guidanceTargetId
            || nextTargetLabels.length !== guidanceTargetLabels.length
            || nextTargetLabels.some((label, index) => label !== guidanceTargetLabels[index]);
        if (!targetChanged) {
            // Live guidance arrives several times per second. The destination
            // marker does not change with each angle/distance update, so avoid
            // rescanning every Leaflet layer and extraction icon on this hot
            // path. DOM additions are handled by the observer below.
            return document.querySelectorAll(
                "#leaflet-map .raidpilot-guidance-target").length;
        }
        guidanceTargetId = nextTargetId;
        guidanceTargetLabels = nextTargetLabels;
        return window.__raidPilotApplyGuidanceTarget();
    };

    window.__raidPilotApplyGuidanceTarget = () => {
        clearGuidanceTarget();
        const indexedIcons = new Map();
        if (guidanceTargetId) {
            const mapInstance = findLeafletMapInstance();
            const layers = mapInstance?._layers && typeof mapInstance._layers === "object"
                ? Object.values(mapInstance._layers)
                : [];
            for (const layer of layers) {
                const icon = layer?._icon;
                if (!(icon instanceof Element) || !icon.classList.contains("extract-icon")) {
                    continue;
                }
                const layerId = String(layer?.options?.id ?? "").trim().toLowerCase();
                indexedIcons.set(icon, layerId);
            }
        }

        let matchedCount = 0;
        for (const marker of document.querySelectorAll("#leaflet-map .extract-icon")) {
            const matchesId = guidanceTargetId
                && indexedIcons.get(marker) === guidanceTargetId;
            const label = marker.getAttribute("title")
                || marker.querySelector(".extract-name")?.textContent
                || "";
            const isTarget = Boolean(matchesId
                || guidanceTargetLabels.includes(normalizeLabel(label)));
            if (isTarget) {
                marker.classList.add("raidpilot-guidance-target");
                matchedCount += 1;
            }
        }
        return matchedCount;
    };

    const raidPilotOwnsNode = (node) => {
        const element = node instanceof Element ? node : node.parentElement;
        const ownedSelector = "#raidpilot-auto-floor, #raidpilot-quest-context, #raidpilot-guidance, #raidpilot-raid-timer, #raidpilot-map-credit, #raidpilot-learned-route, .raidpilot-player-marker";
        return element?.matches?.(ownedSelector)
            || Boolean(element?.closest?.(ownedSelector));
    };

    const nodeMatchesOrContains = (node, selector) => node instanceof Element
        && (node.matches(selector) || Boolean(node.querySelector(selector)));

    let playerMarkerStyleObserver = null;
    let observedPlayerMarkerPane = null;

    const observePlayerMarkerStyles = (mutations) => {
        for (const mutation of mutations) {
            const target = mutation.target;
            const sourceMarker = target instanceof Element
                ? target.closest(
                    ".leaflet-marker-pane > .leaflet-marker-icon.marker:not(.raidpilot-player-marker)")
                : null;
            if (sourceMarker) {
                syncStablePlayerMarker();
                return;
            }
        }
    };

    const ensurePlayerMarkerStyleObserver = () => {
        const pane = document.querySelector("#leaflet-map .leaflet-marker-pane");
        if (pane === observedPlayerMarkerPane) {
            return;
        }
        playerMarkerStyleObserver?.disconnect();
        playerMarkerStyleObserver = null;
        observedPlayerMarkerPane = pane;
        if (!pane) {
            return;
        }
        playerMarkerStyleObserver = new MutationObserver(observePlayerMarkerStyles);
        playerMarkerStyleObserver.observe(pane, {
            subtree: true,
            attributes: true,
            attributeFilter: ["style"],
        });
        window.__raidPilotPlayerMarkerStyleObserver = playerMarkerStyleObserver;
    };

    const observeMapChanges = (mutations) => {
        let needsLayerPolicy = false;
        let needsGuidanceTarget = false;
        let needsPlayerMarkerSync = false;
        let needsQuestMarkers = false;
        let needsLearnedRoute = false;
        const policySelector = [
            "#leaflet-map",
            ".display-wrapper.map-page",
        ].join(", ");

        for (const mutation of mutations) {
            const addedNodes = [...mutation.addedNodes];
            if (raidPilotOwnsNode(mutation.target)
                || (addedNodes.length > 0 && addedNodes.every(raidPilotOwnsNode))) {
                continue;
            }

            const target = mutation.target instanceof Element
                ? mutation.target
                : mutation.target.parentElement;
            const targetGroup = target?.closest?.(".leaflet-control-layers-group");
            if (targetGroup && groupNeedsLayerPolicy(targetGroup)) {
                needsLayerPolicy = true;
            }
            for (const node of addedNodes) {
                needsLayerPolicy ||= nodeMatchesOrContains(node, policySelector);
                needsLayerPolicy ||= nodeContainsActiveDisallowedGroup(node);
                needsGuidanceTarget ||= nodeMatchesOrContains(node, ".extract-icon");
                needsPlayerMarkerSync ||= nodeMatchesOrContains(node, ".leaflet-marker-icon.marker");
                needsQuestMarkers ||= nodeMatchesOrContains(
                    node,
                    '.leaflet-control-layers-group[data-key="Tasks"]');
            }
            for (const node of mutation.removedNodes) {
                needsPlayerMarkerSync ||= nodeMatchesOrContains(node, ".leaflet-marker-icon.marker");
                needsLearnedRoute ||= nodeMatchesOrContains(node, "#raidpilot-learned-route");
            }
        }

        ensurePlayerMarkerStyleObserver();
        if (needsPlayerMarkerSync) {
            syncStablePlayerMarker();
        }
        if (needsLayerPolicy) {
            scheduleLayerPolicy();
        } else if (needsGuidanceTarget || needsQuestMarkers) {
            window.requestAnimationFrame(() => {
                window.__raidPilotApplyGuidanceTarget?.();
                window.__raidPilotApplyRaidExtractions?.();
                window.__raidPilotApplyRaidQuests?.();
                window.__raidPilotApplyLearnedRoute?.();
            });
        }
        if (needsLearnedRoute) {
            window.requestAnimationFrame(() => window.__raidPilotApplyLearnedRoute?.());
        }
    };

    const startObserver = () => {
        scheduleLayerPolicy();
        const observer = new MutationObserver(observeMapChanges);
        observer.observe(document.documentElement, {
            childList: true,
            subtree: true,
        });
        window.__raidPilotMapFilterObserver = observer;
        ensurePlayerMarkerStyleObserver();
    };

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", startObserver, { once: true });
    } else {
        startObserver();
    }
})();
