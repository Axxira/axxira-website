const fs = require('node:fs'), path = require('node:path');
const {lua, lauxlib, lualib, to_luastring, to_jsstring} = require('fengari');
const state = lauxlib.luaL_newstate(); lualib.luaL_openlibs(state);
function load(source, name, execute = true) {
  if (lauxlib.luaL_loadbuffer(state, to_luastring(source), null, to_luastring(name)) !== lua.LUA_OK ||
      (execute && lua.lua_pcall(state, 0, 0, 0) !== lua.LUA_OK)) {
    throw new Error(name + ': ' + to_jsstring(lua.lua_tostring(state, -1)));
  }
  if (!execute) lua.lua_pop(state, 1);
}
const addon = path.resolve(__dirname, '../foreverpilot');
const toc = fs.readFileSync(path.join(addon, 'ForeverPilot_Leveling.toc'), 'utf8');
for (const file of toc.split(/\r?\n/).filter(line => line.endsWith('.lua'))) {
  load(fs.readFileSync(path.join(addon, file), 'utf8'), file, false);
}
load(`
TEST={clock=100,timers={}};Ding={db={},Compat={}}
function GetTime() return TEST.clock end
function time() return TEST.clock+1000000 end
function InCombatLockdown() return TEST.combat or false end
C_Timer={After=function(_,fn) TEST.timers[#TEST.timers+1]=fn end}
`, 'host-fixture');
load(fs.readFileSync(path.join(__dirname, 'ui-runtime.lua'), 'utf8'), 'UI fixture');
for (const file of ['Bootstrap.lua', 'Items.lua', 'Market.lua', 'AuctionUI.lua', 'Advisor.lua'])
  load(fs.readFileSync(path.join(addon, file), 'utf8'), file);
load('local B=ForeverPilotLeveling; B.char={}; B.settings={enabled=true}; B.Market:Init(); B.AuctionUI:Init()', 'initialize');
load(fs.readFileSync(path.join(__dirname, 'auction-ui.lua'), 'utf8'), 'auction UI regression');
load(fs.readFileSync(path.join(__dirname, 'market-tooltip.lua'), 'utf8'), 'inventory tooltip regression');
console.log('PASS: all addon modules parse; auction integration and scan lifecycle verified.');
