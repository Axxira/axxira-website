-- FastXP: verified flight-path knowledge transfer.
-- Reads only this character's already-discovered Ding flight masters and
-- matches them against the bundled routing engine's native taxi database.
-- Never creates access to an unvisited flight path.
local B=ForeverPilotLeveling
local F={new=0,matched=0,unmatched=0,known=0};B.FlightSync=F
local function normalize(s)
    if type(s)~="string" then return nil end
    -- Ding and the routing engine use the same English taxi names on
    -- Forever. Case/spacing differ in some localized clients.
    return (s:lower():gsub("^%s+",""):gsub("%s+$",""):gsub("%s+"," "))
end
local function factionOf(core)
    if not core.Compat or type(core.Compat.GetPlayerInfo)~="function" then return nil end
    local player=B.Call(core.Compat.GetPlayerInfo)
    local side=type(player)=="table" and player.faction or nil
    if side=="Horde" then return 2 end
    if side=="Alliance" then return 1 end
end
local function matching(node,side)
    return type(node)=="table" and type(node.id)=="number"
        and (node.faction==side or node.faction==3)
end
function F:Sync(core,s)
    if not core or not core.Nav or not core.CharState or
        type(core.Nav.GetTravelData)~="function" then return 0 end
    if s and (s.combat or s.taxi) then return 0 end
    if self.at and s and s.now and s.now-self.at<12 then return 0 end
    local side=factionOf(core)
    if not side then return 0 end
    local ding=Ding and Ding.char
    local flights=ding and ding.flights
    if type(flights)~="table" then return 0 end
    local native=B.Call(core.CharState)
    if type(native)~="table" or type(native.flags)~="table" then return 0 end
    local data=B.Call(core.Nav.GetTravelData)
    if type(data)~="table" or type(data.taxi)~="table" then return 0 end
    local flags=native.flags
    if type(flags.taxi)~="table" then flags.taxi={} end
    local byName,byShort={},{}
    for _,node in ipairs(data.taxi) do
        if matching(node,side) and type(node.name)=="string" then
            local name=normalize(node.name)
            if name then
                local list=byName[name] or {}
                list[#list+1]=node
                byName[name]=list
                local short=name:match("^([^,]+)")
                if short then
                    local sList=byShort[short] or {}
                    sList[#sList+1]=node
                    byShort[short]=sList
                end
            end
        end
    end
    local new,matched,unmatched=0,0,0
    for npc,name in pairs(flights) do
        if type(npc)=="number" and type(name)=="string" then
            local key=normalize(name)
            local list=key and byName[key]
            if not list or #list~=1 then
                local short=key and key:match("^([^,]+)")
                local shortMatches=short and byShort[short]
                list=shortMatches and #shortMatches==1 and shortMatches or nil
            end
            if list and #list==1 then
                local node=list[1]
                matched=matched+1
                if not flags.taxi[node.id] then flags.taxi[node.id]=true;new=new+1 end
            else
                unmatched=unmatched+1
            end
        end
    end
    self.new,self.matched,self.unmatched=new,matched,unmatched
    self.known=0
    for _,node in ipairs(data.taxi) do
        if matching(node,side) and flags.taxi[node.id]==true then self.known=self.known+1 end
    end
    self.at=s and s.now or 0
    -- Changing the known route graph changes optimal transport choices.
    if new>0 and core.Nav.travel and type(core.Nav.travel.Replan)=="function" then
        B.Call(core.Nav.travel.Replan)
    end
    return new
end
function F:Report()
    B.Print(string.format(B.T("Vols connus : %d · transférés maintenant : %d · correspondances : %d · inconnus : %d"),
        self.known or 0,self.new or 0,self.matched or 0,self.unmatched or 0))
    local ns=B.EmbeddedEngine and B.EmbeddedEngine:Runtime()
    local Nav=ns and ns.Nav
    local legs,index
    if Nav and type(Nav.GetRoute)=="function" then
        legs,index=B.Call(Nav.GetRoute)
    end
    local n=type(legs)=="table" and #legs or 0
    if n==0 then
        B.Print(B.T("Pas de trajet composé actif. Vérifie le chemin et les maîtres de vol connus."))
        return
    end
    B.Print(string.format(B.T("Trajet en %d étapes ; étape actuelle : %d"),n,index or 1))
    for i=math.max(1,index or 1),math.min(n,(index or 1)+3) do
        local leg=legs[i]
        local label=type(leg)=="table" and (leg.label or leg.kind) or "?"
        B.Print(tostring(i)..". "..tostring(label))
    end
end
