const fs = require('fs'), path = require('path');
const { lua, lauxlib, lualib, to_luastring, to_jsstring } = require('../foreverpilot-tests/node_modules/fengari');
const L = lauxlib.luaL_newstate(); lualib.luaL_openlibs(L);
const root = path.join(__dirname, '../dungeonmappilot');
function run(text, name) {
  if (lauxlib.luaL_loadbuffer(L, to_luastring(text), null, to_luastring(name)) !== lua.LUA_OK || lua.lua_pcall(L, 0, 0, 0) !== lua.LUA_OK)
    throw new Error(name + ': ' + to_jsstring(lua.lua_tostring(L, -1)));
}
run(fs.readFileSync(path.join(__dirname, 'fixture.lua'), 'utf8'), 'fixture');
const toc = fs.readFileSync(path.join(root, 'DungeonMapPilot.toc'), 'utf8');
for (const file of toc.split(/\r?\n/).filter(x => x.endsWith('.lua'))) run(fs.readFileSync(path.join(root, file), 'utf8'), file);
for (const name of ['scenarios', 'all_guides', 'forever_guides', 'roster_level',
                    'boss_search', 'notebook', 'notebook_limits']) {
  run(fs.readFileSync(path.join(__dirname, name + '.lua'), 'utf8'), name);
}
console.log('PASS: 7 DungeonMapPilot regression suites, TOC, maps, guides, rosters, boss search and notes. Simulated WoW APIs only.');
