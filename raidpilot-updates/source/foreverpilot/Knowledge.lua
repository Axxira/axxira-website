-- Evidence is local and partitioned by server/build. Absence is never promoted
-- to a universal prerequisite, exclusivity, or permanent blacklist.
local B=ForeverPilotLeveling
local K={};B.Knowledge=K
function K:Init()
    local _,build=B.Call(GetBuildInfo)
    local key=(GetRealmName() or "unknown")..":"..tostring(build or "unknown")..":"..(UnitFactionGroup("player") or "?")
    B.settings.knowledge=B.settings.knowledge or {}
    local roots=B.settings.knowledge
    roots[key]=roots[key] or {quests={},at=time()}
    self.db=roots[key];self.key=key;self.db.at=time()
    -- Keep at most four server/build snapshots, preserving the current one.
    local old={} for id,r in pairs(roots) do if id~=key then old[#old+1]={id=id,at=r.at or 0} end end
    table.sort(old,function(a,b) return a.at>b.at end)
    for i=4,#old do roots[old[i].id]=nil end
end
function K:Quest(q)
    if not self.db or type(q)~="number" then return nil end
    local r=self.db.quests[q]
    if not r then r={xp={},points={accept={},turnin={}},offers=0};self.db.quests[q]=r end
    return r
end
function K:Position(q,kind,npc,s)
    if not npc or not s.map or not s.x or not s.y or s.x<=0 or s.y<=0 then return end
    local r=self:Quest(q);if not r then return end
    local list=r.points[kind];if not list then return end
    local found
    for _,p in ipairs(list) do if p.npc==npc and p.map==s.map and math.abs(p.x-s.x)<1 and math.abs(p.y-s.y)<1 then found=p;break end end
    local p=found or {map=s.map,x=s.x,y=s.y,npc=npc,observed=true,samples=0}
    p.samples=p.samples+1;p.at=time()
    if not found then list[#list+1]=p;if #list>8 then table.remove(list,1) end end
end
function K:Offer(q,npc,s,name)
    local r=self:Quest(q);if not r then return end
    r.offers=r.offers+1;r.name=name or r.name;r.lastOffer=time();r.levelSeen=s.level
    self:Position(q,"accept",npc,s)
end
function K:TurnIn(q,xp,s,npc)
    local r=self:Quest(q);if not r then return end
    xp=tonumber(xp)
    if xp and xp>=0 and xp<1000000 then
        local k=tostring(s.level);local sample=r.xp[k] or {count=0,value=xp}
        sample.count=math.min(100,sample.count+1)
        sample.value=sample.value*0.7+xp*0.3;sample.at=time();r.xp[k]=sample
    end
    self:Position(q,"turnin",npc,s);r.lastTurnIn=time()
end
function K:XP(q,s)
    local r=self.db and self.db.quests[q];local sample=r and r.xp[tostring(s.level)]
    if sample then return sample.value,true end
end
function K:Points(q,kind)
    local r=self.db and self.db.quests[q];local out={}
    for _,p in ipairs(r and r.points[kind] or {}) do
        if time()-(p.at or 0)<30*86400 then out[#out+1]=p end
    end
    return out
end
