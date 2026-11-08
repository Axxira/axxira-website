local B=ForeverPilotLeveling
local N={};B.Needs=N
function N:Choose(s,quest)
    if s.combat or s.dead or s.taxi then return quest end
    local health=s.health;local mana=s.mana
    if (health and health<0.20) or (mana and mana<0.08) then self.resting=true end
    if self.resting then
        if (not health or health>=0.65) and (not mana or mana>=0.50) then self.resting=nil
        elseif not (B.char.deferred.rest and B.char.deferred.rest>time()) then
            return {kind="rest",key="rest",label="Reprends tes forces",note="La boucle reprend dès que tu es prêt.",confidence="LIVE",batch={}}
        end
    end
    local repair=s.durability and s.durability<0.30
    local vendor=s.bags and s.bags<=2
    if not (repair or vendor) or not Ding.Services then return quest end
    local kind=repair and "repair" or "vendor"
    local urgent=(repair and s.durability<0.15) or (vendor and s.bags==0)
    local cache=self.cache
    if not cache or cache.kind~=kind or cache.map~=s.map or s.now-cache.at>10 then
        cache={kind=kind,map=s.map,at=s.now,list=B.Call(Ding.Services.Nearest,Ding.Services,kind,3) or {}};self.cache=cache
    end
    for _,npc in ipairs(cache.list) do
        local key="service:"..kind..":"..npc.npc
        if not (B.char.deferred[key] and B.char.deferred[key]>time()) and not npc.point.dungeon and B.Travel:Reachable(npc.point,s) then
            local travel=B.Travel:Seconds(npc.point,s)
            local onward=quest and quest.point and B.Travel:Between(npc.point,quest.point,s) or 0
            local direct=quest and quest.point and B.Travel:Seconds(quest.point,s) or 0
            local detour=travel+onward-direct
            if urgent or (travel<90 and detour<45) then
                -- A nearby turn-in can free the bag and should not be interrupted.
                if quest and quest.kind=="turnin" and direct<travel+30 then return quest end
                return {kind="service",key=key,service=kind,label=repair and "Répare ton équipement" or "Libère de la place",
                    note=(npc.name or "Marchand").." · "..(repair and "Réparation" or "Sacs presque pleins"),point=npc.point,
                    confidence="LIVE / POSITION DB",batch={},plan=quest and quest.plan}
            end
        end
    end
    return quest
end
function N:MerchantClosed()
    local a=B.Planner.current
    if a and a.kind=="service" and a.service~="flight" then
        -- Do not trap the player here if a repair was unaffordable or they chose
        -- to keep their items. No item is sold or discarded by this module.
        B.char.deferred[a.key]=time()+300;B.Recovery.revision=(B.Recovery.revision or 0)+1
    end
end
