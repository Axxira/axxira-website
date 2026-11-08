unpack=table.unpack;loadstring=load
table.maxn=function(t) local n=0 for k in pairs(t) do if type(k)=="number" and k>n then n=k end end return n end
TEST={level=16,clock=1000,race="Scourge",class="PRIEST",log={},done={},map=1421,x=54,y=69}
function GetTime() return TEST.clock end
function time() return TEST.clock+1000000 end
function UnitLevel() return TEST.level end
function UnitName() return "Test" end
function UnitFactionGroup() return "Horde" end
function GetRealmName() return "Harness" end
function GetLocale() return "enUS" end
function UnitAffectingCombat() return TEST.combat or false end
function GetBuildInfo() return '1.60.1',TEST.build or 'test-build-1' end
DEFAULT_CHAT_FRAME={AddMessage=function(_,s) print(s) end}
C_Timer={After=function(delay,fn) TEST.timers=TEST.timers or {};TEST.timers[#TEST.timers+1]=fn end}
local C={}
C.PlayerRace=function() return TEST.race end
C.PlayerClass=function() return TEST.class end
C.IsQuestCompleted=function(q) return TEST.done[q] or false end
C.IsInLog=function(q) return TEST.log[q]~=nil end
C.IsQuestReady=function(q) return TEST.log[q] and TEST.log[q].ready end
C.GetObjectives=function(q) return TEST.log[q] and TEST.log[q].objectives or {} end
C.LogQuests=function() local t={} for q in pairs(TEST.log) do t[#t+1]=q end return t end
C.NumQuestsInLog=function() return #C.LogQuests() end
C.PlayerXP=function() return 500,10000,0 end
C.GetPlayerMapPosition=function() return TEST.map,TEST.x,TEST.y end
C.GetPlayerWorld=function() return TEST.map,TEST.x*40,TEST.y*40,TEST.map end
C.MapToWorld=function(map,x,y) return map,x*40,y*40 end
C.ItemCount=function() return 0 end
C.IsDeadOrGhost=function() return TEST.dead or false end
C.IsGhost=function() return TEST.ghost or false end
C.OnTaxi=function() return false end
C.FreeBagSlots=function() return 10 end
C.BindLocation=function() return "The Sepulcher" end
C.IsAddOnLoaded=function() return false end
C.InGroup=function() return false end
C.MapName=function(id) return 'Map '..tostring(id) end
C.AreaName=C.MapName
C.CorpsePoint=function() return {map=1421,x=50,y=50} end
C.NpcUnitID=function() return TEST.npc end
C.GossipQuests=function() return TEST.active,TEST.available end
C.GreetingQuests=C.GossipQuests
Ding={Compat=C,L=setmetatable({},{__index=function(_,k) return k end}),db={ui={}},char={},Engine={turnedIn={}},Travel={},Grind={Spot=function() end}}
Ding.Debug=function() end;Ding.Print=function() end
Ding.StepApplies=function(self,s) return self:StepMatchesPlayer(s) and not s.elite and not s.dungeon end
-- The broad logical simulation assumes transport is available. Real transport
-- refusal (including Zephras Isle) is tested separately in navigation.lua.
Ding.Travel.Reach=function()
 local map,x,y=TEST.map,TEST.x,TEST.y
 return {Seconds=function(_,p)
  if p.map==map then return math.sqrt((p.x-x)^2+(p.y-y)^2)*40/7 end
  return 1800
 end}
end
