-- Level suitability is independent of guide cursor and proximity. A committed
-- outing cannot keep obsolete objectives alive after the character outlevels it.
local B=ForeverPilotLeveling
local P={};B.Progression=P
function P:Friendly(npc,s,info)
    info=info or (npc and Ding.Data:NpcInfo(npc))
    if not info then return false end
    if info.friendly==(s.faction=="Horde" and "H" or "A") then return true end
    if info.friendly=="AH" then
        -- Ding also exports neutral attackable creatures as AH (e.g. 1501).
        -- A known zero NPC-flags value distinguishes them from service NPCs.
        self.npcFlags=self.npcFlags or {}
        if self.npcFlags[npc]==nil then
            local raw=LibDingDB and LibDingDB.Get("npc",npc)
            self.npcFlags[npc]=raw and raw.npcFlags or false
        end
        return self.npcFlags[npc]~=0
    end
    return false
end
function P:Hostile(npc,s)
    local info=npc and Ding.Data:NpcInfo(npc)
    if not info or self:Friendly(npc,s,info) then return end
    local low,high=info.minLevel,info.maxLevel or info.minLevel
    if type(low)=="number" and low>0 and type(high)=="number" and high>0 then return low,high end
end
function P:Preferred(points,s)
    local nearLevel={};local found=false
    for _,p in ipairs(points) do
        local _,high=self:Hostile(p.npc,s)
        if not high or high>=s.level-2 then nearLevel[#nearLevel+1]=p;if high then found=true end end
    end
    return found and nearLevel or points
end
function P:Progress(row)
    local have,total=0,0
    for _,o in ipairs(row and row.objectives or {}) do
        local need=o.numRequired or 1
        if need>0 then
            total=total+need
            have=have+math.min(need,o.finished and need or o.numFulfilled or 0)
        end
    end
    return total>0 and have/total or 0,total-have
end
function P:Unlock(n,s,future,classOnly)
    -- Only unfinished, compatible descendants in the current bracket justify
    -- doing a low-level parent now. An already-satisfied OR branch does not.
    local key=classOnly and "classUnlocks" or future and "futureUnlocks" or "unlocks"
    s[key]=s[key] or {};local cache=s[key]
    if cache[n.id]~=nil then return cache[n.id] or nil end
    local visited={};local limit=0
    local function needs(child,parent)
        for _,q in ipairs(child.all or {}) do if math.abs(q)==parent then return true end end
        local uses=false
        for _,q in ipairs(child.any or {}) do
            if (q>0 and s:IsDone(q)) or (q<0 and s.log[-q]) then return false end
            if math.abs(q)==parent then uses=true end
        end
        return uses
    end
    local function follow(current,depth)
        if visited[current.id] or depth>20 or limit>=120 then return end
        visited[current.id]=true;limit=limit+1
        local ids={};for q in pairs(current.next or {}) do ids[#ids+1]=q end;table.sort(ids)
        for _,q in ipairs(ids) do
            local child=B.Graph:Node(q)
            local isClass=child and B.Journal:IsClass(child)
            local useful=child and (classOnly and (isClass or child.classPrerequisite) or child.fast)
            local valid=useful and not child.profession and not B.Recovery:IsSkipped(q) and not s:IsDone(q) and not s.log[q] and Ding.Data:QuestOpenToPlayer(q) and needs(child,current.id)
            if valid then
                for _,exclusive in ipairs(child.exclusive or {}) do if s:IsDone(exclusive) or s.log[exclusive] then valid=false end end
            end
            if valid then
                if (classOnly and isClass or not classOnly and (child.level or 0)>=s.level-1) and (future or ((child.min or 0)<=s.level and child.level<=s.level+2)) then return q end
                local deeper=follow(child,depth+1);if deeper then return deeper end
            end
        end
    end
    local q=follow(n,0);cache[n.id]=q or false;return q
end
function P:Evaluate(n,kind,s,a)
    local row=s.log[n.id]
    local level=row and row.level or n.level
    if type(level)~="number" or level<=0 then level=nil end
    local fit={questLevel=level,playerLevel=s.level,gap=level and math.max(0,s.level-level) or 0,factor=1}
    if a and a.target then
        fit.mobMin,fit.mobMax=self:Hostile(a.point and a.point.npc,s)
    end
    if fit.mobMax then fit.gap=math.max(fit.gap,s.level-fit.mobMax) end
    fit.old=fit.gap>=3
    if fit.old then
        local fraction,remaining=self:Progress(row)
        if kind=="turnin" then fit.keep="ready"
        elseif B.Journal:IsClass(n) then fit.keep="class"
        elseif n.classPrerequisite and self:Unlock(n,s,false,true) then fit.keep="classPrerequisite"
        elseif kind=="complete" and fraction>0 and (fraction>=0.8 or remaining<=2) then fit.keep="almostDone"
        else fit.unlock=self:Unlock(n,s);if fit.unlock then fit.keep="prerequisite" end end
        fit.retire=not fit.keep
        fit.factor=fit.keep=="prerequisite" and 0.6 or (fit.keep and 0.85 or 0.2)
    elseif fit.gap==2 then fit.factor=0.85 end
    return fit
end
function P:Describe(fit)
    if not fit then return end
    local text=B.T("Ton niveau : ")..fit.playerLevel
    if fit.questLevel then text=text..B.T(" · Quête : ")..fit.questLevel end
    if fit.mobMax then text=text..B.T(" · Monstres : ")..fit.mobMin.."–"..fit.mobMax..B.T(" (base)") end
    if fit.keep=="prerequisite" then
        local n=B.Graph:Node(fit.unlock)
        text=text..B.T(". Prérequis pour ")..(n and n.name or tostring(fit.unlock)).."."
    elseif fit.keep=="almostDone" then text=text..B.T(". Objectifs presque terminés.")
    elseif fit.keep=="ready" then text=text..B.T(". XP prête à récupérer.")
    elseif fit.keep=="class" then text=text..B.T(". Progression de classe conservée.") end
    return text
end
