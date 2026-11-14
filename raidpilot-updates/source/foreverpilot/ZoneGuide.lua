-- FastXp Zone Explorer: free, non-destructive zone-by-zone quest guidance.
-- Uses the installed Ding zone catalog and live quest log; never fabricates NPC offers.
local B=ForeverPilotLeveling
local Z={};B.ZoneGuide=Z
local function validMap(value)
 return type(value)=="number" and value>0 and value<1000000 and value%1==0
end
function Z:ZoneRoute(node)
 if not node or type(node.routes)~="table" then return false end
 for _,route in ipairs(node.routes) do
  if route.guide and route.guide.pack=="ding-zones" then return true end
 end
 return false
end
function Z:HasPointInZone(node,map)
 if not validMap(map) or not node then return false end
 local graph=B.Graph
 for _,kind in ipairs({"accept","turnin"}) do
  for _,p in ipairs(graph:Locations(node,kind) or {}) do
   if p.map==map then return true end
  end
 end
 for _,target in ipairs(graph:Locations(node,"complete") or {}) do
  for _,p in ipairs(target.points or {}) do
   if p.map==map then return true end
  end
 end
 return false
end
function Z:Eligible(node,state)
 if not node or not state or not validMap(state.map) then return false end
 if node.profession or node.manual or not B.Planner:Solo(node) then return false end
 if node.min>state.level or node.level>state.level+3 then return false end
 if B.Validator.offers[node.id] then return true end
 local guideZone=B.char and B.char.freeZone
 local lock=type(guideZone)=="table" and guideZone.map or nil
 if not self:ZoneRoute(node) then
  -- A quest discovered at a real NPC can be included even if Ding omitted it.
  return false
 end
 return self:HasPointInZone(node,state.map) or
    (validMap(lock) and self:HasPointInZone(node,lock))
end
function Z:Touches(action,map)
 if not action or not validMap(map) then return false end
 if action.point and action.point.map==map then return true end
 if action.kind=="accept" and action.node then
  for _,t in ipairs(B.Graph:Locations(action.node,"complete") or {}) do
   for _,p in ipairs(t.points or {}) do if p.map==map then return true end end
  end
 end
 return false
end
function Z:SetZone(map)
 if not validMap(map) or not B.char then return false end
 local current=B.char.freeZone
 if type(current)~="table" or current.map~=map then
  B.char.freeZone={map=map,at=time()}
  if B.Journey then B.Journey:Reset() end
  if B.Planner then B.Planner.signature=nil;B.Planner.current=nil end
  B.dirty=true
 end
 return true
end
function Z:Current(state)
 local setting=B.char and B.char.freeZone
 if type(setting)=="table" and validMap(setting.map) then return setting.map end
 return state and validMap(state.map) and state.map or nil
end
-- Keep a zone while viable steps remain; do not ping-pong between nearby hubs.
function Z:Filter(actions,state)
 if B.Mode()~="zone" or not state then return actions end
 local active=self:Current(state)
 local localActions={}
 for _,a in ipairs(actions) do if self:Touches(a,active) then localActions[#localActions+1]=a end end
 if #localActions==0 and active~=state.map and validMap(state.map) then
  for _,a in ipairs(actions) do if self:Touches(a,state.map) then localActions[#localActions+1]=a end end
  if #localActions>0 then active=state.map end
 end
 if #localActions>0 then
  self:SetZone(active)
  -- Ready rewards can be collected outside the zone after all local work.
  -- A player in the middle of a field outing stays in the selected map.
  actions=localActions
 elseif #actions>0 then
  -- No tasks left in the saved zone. Prefer a real held quest, then nearest
  -- eligible zone quest, rather than an arbitrary distant XP loop.
  local best,travel
  for _,a in ipairs(actions) do
   local prefer=state.log[a.q] and 0 or 1
   local cost=prefer*100000+(a.travel or 99999)
   if not travel or cost<travel then best,travel=a,cost end
  end
  local map=best and best.point and best.point.map
  if validMap(map) then self:SetZone(map) end
  local selected={}
  for _,a in ipairs(actions) do if self:Touches(a,map) then selected[#selected+1]=a end end
  if #selected>0 then actions=selected end
 end
 local map=self:Current(state)
 for _,a in ipairs(actions) do a.zoneMode=true;a.zoneMap=map end
 return actions
end
function Z:Progress(state,map)
 if not state or not validMap(map) then return 0,0 end
 local total,done=0,0
 for _,node in pairs(B.Graph.nodes or {}) do
  if self:ZoneRoute(node) and not node.profession and
     node.min<=state.level and self:HasPointInZone(node,map) then
   total=total+1
   if state.done[node.id] then done=done+1 end
  end
 end
 return done,total
end
-- A known guide section can supply a next-zone destination, not a promised quest.
-- Never invent a teleport or point if the map dataset is incomplete.
function Z:Transition(state)
 if not state or not B.Graph or not Ding or not Ding.IterateGuides then return nil end
 local best,score
 for _,guide in Ding:IterateGuides("ding-zones") do
  if guide and guide.minLevel<=state.level+2 and guide.maxLevel>=state.level-5
     and Ding:GuideMatchesPlayer(guide) then
   local first
   for _,step in ipairs(guide.steps or {}) do
    if Ding:StepMatchesPlayer(step) then
     for _,q in ipairs(B.List(step.accept)) do
      local node=B.Graph:Node(q)
      if node and not state:IsDone(q) and not state.log[q] and
         node.min<=state.level and not B.Recovery:IsBlocked(q,state) then
       for _,p in ipairs(B.Graph:Locations(node,"accept")) do
        if B.Travel:Reachable(p,state) then first=p;break end
       end
      end
      if first then break end
     end
    end
    if first then break end
   end
   if first then
    local travel=B.Travel:Seconds(first,state)
    local cost=travel+math.abs(state.level-(guide.minLevel or state.level))*60
    if not score or cost<score then
     score=cost
     best={kind="zone",key="zone:"..guide.id,point=first,
      label=guide.title or guide.id,confidence="DATABASE",batch={},
      note=B.T("Nouvelle zone suggérée : consulte les quêtes réellement offertes au PNJ.")}
    end
   end
  end
 end
 return best
end
