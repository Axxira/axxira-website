// CI guard: every released version must have a real, authored note.
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');

const repositoryRoot = path.resolve(__dirname, '../../../../');
const folder = path.join(repositoryRoot, 'raidpilot-updates');
const feedPath = path.join(folder, 'release-history.json');
const feed = JSON.parse(fs.readFileSync(feedPath, 'utf8'));
assert.equal(feed.schemaVersion, 1);
assert.ok(Array.isArray(feed.entries) && feed.entries.length > 0
    && feed.entries.length <= 40, 'Missing or oversized release history');

const version = (component, project) => {
    const xml = fs.readFileSync(
        path.join(folder, 'source', component, project), 'utf8');
    const match = xml.match(/<Version>([^<]+)<\/Version>/);
    assert.ok(match, 'Project version missing: ' + project);
    return match[1];
};
const versions = {
    raidPilotVersion: version('overlay', 'RaidPilot.Overlay.csproj'),
    beaconVersion: version('beacon', 'RaidPilot.Beacon.csproj'),
    launcherVersion: version('launcher', 'RaidPilot.Launcher.csproj'),
};
let checks = 0;
function check(ok, message) { assert.ok(ok, message); checks++; }

const unique = new Set();
for (const entry of feed.entries) {
    const key = entry.component + ':' + entry.version;
    check(!unique.has(key), 'Duplicate release note ' + key);
    unique.add(key);
    check(['raidpilot', 'beacon', 'launcher'].includes(entry.component),
        'Unknown component in ' + key);
    check(/^\d+\.\d+(\.\d+){0,2}$/.test(entry.version),
        'Invalid version in ' + key);
    check(/^\d{4}-\d{2}-\d{2}$/.test(entry.date),
        'Invalid date in ' + key);
    check(typeof entry.summary === 'string'
        && entry.summary.length > 5 && entry.summary.length <= 115,
        'Empty or oversized summary in ' + key);
    check(typeof entry.details === 'string'
        && entry.details.length > 15 && entry.details.length <= 2000,
        'Empty or oversized detailed note in ' + key);
    for (const property of Object.keys(versions))
        check(/^\d+\.\d+(\.\d+){0,2}$/.test(entry[property]),
            'Missing version tuple field ' + property + ' in ' + key);
}
const matches = feed.entries.filter(entry =>
    Object.keys(versions).every(key => entry[key] === versions[key]));
check(matches.length === 1, 'Version tuple has no unique authored changelog note');
check(matches[0] === feed.entries[0], 'Current release must be first');
check(matches[0].version === versions[
    matches[0].component === 'raidpilot' ? 'raidPilotVersion'
    : matches[0].component === 'beacon' ? 'beaconVersion' : 'launcherVersion'],
    'Current entry version does not match its component');

const launcher = fs.readFileSync(
    path.join(folder, 'source/launcher/LauncherForm.cs'), 'utf8');
check(launcher.includes('RecentUpdates[i]'),
    'Launcher home page is not bound to dynamic history');
check(!launcher.includes('"v0.11.0"'),
    'Launcher still uses obsolete hardcoded release rows');
check(launcher.includes('ReleaseHistoryCatalog.FormatDetailed'),
    'Voir les notes does not open full history');
const pipeline = fs.readFileSync(
    path.join(repositoryRoot, '.github/workflows/raidpilot-release.yml'), 'utf8');
check(pipeline.includes('Verify versioned patch notes'),
    'Stable publishing does not require authored patch notes');
check(pipeline.includes('releaseHistory = $recent'),
    'Stable manifest does not publish release history');
console.log(JSON.stringify({
    status: 'PASS', checks,
    latest: matches[0].component + ' v' + matches[0].version,
    historyEntries: feed.entries.length
}));
