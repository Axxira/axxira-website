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
run(fs.readFileSync(path.join(__dirname, 'scenarios.lua'), 'utf8'), 'scenarios');
console.log('PASS: TOC loads; model and UI scenarios complete. These are simulated tests, not in-game validation.');
