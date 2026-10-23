const fs = require('fs');
const path = require('path');
const vm = require('vm');
const assert = require('node:assert/strict');

const source = fs.readFileSync(path.join(__dirname, '..', 'InjectedBehavior.js'), 'utf8');
const start = source.indexOf('    const mapZoomStoragePrefix = "raidpilot.mapZoom.";');
const end = source.indexOf('    const mapViewportHeight = ', start);
assert.ok(start >= 0 && end > start, 'Production zoom controller not found');
const controllerSource = source.slice(start, end);
let checks = 0;
function check(condition, message) { assert.ok(condition, message); checks++; }

function makeEnvironment({ savedFactor = null, legacyZoom = null } = {}) {
    const storage = new Map();
    if (savedFactor !== null) storage.set('raidpilot.mapZoomFactor.global.v1', String(savedFactor));
    if (legacyZoom !== null) storage.set('raidpilot.mapZoom./map/customs', String(legacyZoom));
    const messages = [];
    let activeMap = null;
    const domEvents = {};
    const pageEvents = {};
    const elementById = {};
    const container = {
        id: 'leaflet-map',
        listeners: {},
        addEventListener(name, callback) {
            (this.listeners[name] ||= []).push(callback);
        },
        fire(name) { for (const callback of this.listeners[name] || []) callback({ type: name }); },
        appendChild(child) { child.parentElement = this; elementById[child.id] = child; },
    };
    elementById['leaflet-map'] = container;
    const makeControl = () => ({
        id: '',
        parentElement: null,
        listeners: {},
        innerHTML: '',
        querySelector: () => ({ textContent: '', disabled: false }),
        addEventListener(name, callback) {
            (this.listeners[name] ||= []).push(callback);
        },
        remove() {},
    });
    const browser = {
        WeakSet, Date, Math, Number, String, setTimeout, clearTimeout, console,
        window: null,
        location: { pathname: '/map/customs' },
        document: {
            getElementById: id => elementById[id] || null,
            createElement: () => makeControl()
        },
        localStorage: {
            getItem: k => storage.has(k) ? storage.get(k) : null,
            setItem: (k, v) => storage.set(k, String(v))
        },
        addEventListener: (name, callback) => { (pageEvents[name] ||= []).push(callback); },
        chrome: { webview: { postMessage: message => messages.push(message) } },
        findLeafletMapInstance: () => activeMap,
        localPlayerPoseState: null,
    };
    browser.window = browser;
    const controller = vm.runInNewContext(
        '(() => {' + controllerSource + '\nreturn { ensureMapZoomControls };})()',
        browser);
    const makeMap = (defaultZoom, minZoom = 0, maxZoom = 10) => {
        let currentZoom = defaultZoom;
        const mapEvents = {};
        return {
            options: {},
            getZoom: () => currentZoom,
            getMinZoom: () => minZoom,
            getMaxZoom: () => maxZoom,
            getContainer: () => container,
            scrollWheelZoom: { enable() {} },
            dragging: { enable() {} },
            on(event, callback) { (mapEvents[event] ||= []).push(callback); },
            off(event, callback) {
                if (mapEvents[event]) mapEvents[event] = mapEvents[event].filter(fn => fn !== callback);
            },
            setZoom(value) {
                const bounded = Math.max(minZoom, Math.min(maxZoom, Number(value)));
                if (Math.abs(bounded - currentZoom) < 1e-9) return;
                currentZoom = bounded;
                for (const cb of mapEvents.zoom || []) cb();
                for (const cb of mapEvents.zoomend || []) cb();
            },
            zoomIn(n = 1) { this.setZoom(currentZoom + n); },
            zoomOut(n = 1) { this.setZoom(currentZoom - n); },
        };
    };
    return {
        browser, storage, messages, container, makeMap, controller, 
        use(map) { activeMap = map; check(controller.ensureMapZoomControls(), 'No map controller'); },
        sync(factor) {
            browser.__raidPilotHostZoomFactor = factor;
            for (const cb of pageEvents['raidpilot-zoom-sync'] || []) cb();
        }
    };
}

(async () => {
    const test = makeEnvironment();
    const customs = test.makeMap(4);
    test.use(customs);
    check(customs.getZoom() === 4, 'New player should retain original zoom');
    test.container.fire('wheel');
    customs.zoomIn(1);
    check(customs.getZoom() === 5, 'Manual zoom did not affect current map');
    check(Math.abs(Number(test.storage.get('raidpilot.mapZoomFactor.global.v1')) - 2) < 1e-8,
        'Manual zoom not saved globally');

    const woods = test.makeMap(2);
    test.browser.location.pathname = '/map/woods';
    test.use(woods);
    check(Math.abs(woods.getZoom() - 3) < 1e-7,
        'Woods did not inherit previous map zoom');
    woods.setZoom(2.5); // website/programmatic change, NOT user gesture
    check(Math.abs(Number(test.storage.get('raidpilot.mapZoomFactor.global.v1')) - 2) < 1e-8,
        'A programmatic map zoom changed user preference');
    await new Promise(resolve => setTimeout(resolve, 190));
    check(test.messages.some(m => m.type === 'raidpilot-map-zoom-preference'
        && Math.abs(m.payload.factor - 2) < 1e-8),
        'Zoom selection not sent to the durable native store');

    const restored = makeEnvironment();
    restored.sync(2.5); // AppData preference delivered before Leaflet initialization
    const next = restored.makeMap(4);
    restored.use(next);
    check(Math.abs(next.getZoom() - (4 + Math.log2(2.5))) < 1e-7,
        'Durable native preference not reapplied after program restart');

    const legacy = makeEnvironment({ legacyZoom: 6 });
    const previousMap = legacy.makeMap(4);
    legacy.use(previousMap);
    check(previousMap.getZoom() === 6, 'Old per-map saved zoom was not migrated');
    console.log(JSON.stringify({status: 'PASS', checks}));
})().catch(error => { console.error(error); process.exitCode = 1; });
