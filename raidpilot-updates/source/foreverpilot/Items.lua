-- Original advisory model. Scores are preferences, never simulated DPS or a promise.
local B=ForeverPilotLeveling
local I={};B.Items=I
function I:Number(v)
    v=B.Public(v);if type(v)=="number" and v==v and math.abs(v)<1e15 then return v end
end
function I:Text(v) v=B.Public(v);if type(v)=="string" then return v end end
function I:Field(t,k) t=B.Public(t);if type(t)=="table" then return B.Public(t[k]) end end
function I:Info(link)
    link=self:Text(link);if not link then return end
    local fn=C_Item and C_Item.GetItemInfo or GetItemInfo
    local t=B.Call(function() return {fn(link)} end)
    if not t or not self:Text(t[1]) then
        local id=tonumber(link:match("item:(%d+)"))
        if id then B.Call(C_Item and C_Item.RequestLoadItemDataByID,id) end
        return
    end
    return {name=self:Text(t[1]),link=self:Text(t[2]) or link,quality=self:Number(t[3]),level=self:Number(t[4]),
        minLevel=self:Number(t[5]),equipLoc=self:Text(t[9]),vendor=self:Number(t[11]),classID=self:Number(t[12]),
        subclassID=self:Number(t[13]),bindType=self:Number(t[14]),setID=self:Number(t[16]),id=tonumber(link:match("item:(%d+)"))}
end
I.slots={INVTYPE_HEAD={1},INVTYPE_NECK={2},INVTYPE_SHOULDER={3},INVTYPE_CHEST={5},INVTYPE_ROBE={5},INVTYPE_WAIST={6},
    INVTYPE_LEGS={7},INVTYPE_FEET={8},INVTYPE_WRIST={9},INVTYPE_HAND={10},INVTYPE_FINGER={11,12},INVTYPE_TRINKET={13,14},
    INVTYPE_CLOAK={15},INVTYPE_WEAPON={16},INVTYPE_WEAPONMAINHAND={16},INVTYPE_2HWEAPON={16},INVTYPE_WEAPONOFFHAND={17},
    INVTYPE_SHIELD={17},INVTYPE_HOLDABLE={17},INVTYPE_RANGED={18},INVTYPE_RANGEDRIGHT={18},INVTYPE_THROWN={18},INVTYPE_RELIC={18}}
I.slotNames={[1]="tête",[2]="cou",[3]="épaules",[5]="torse",[6]="taille",[7]="jambes",[8]="pieds",[9]="poignets",[10]="mains",[11]="anneau 1",[12]="anneau 2",[13]="bijou 1",[14]="bijou 2",[15]="dos",[16]="main droite",[17]="main gauche",[18]="à distance"}
I.stats={
    {"str","Force",{"ITEM_MOD_STRENGTH_SHORT"}}, {"agi","Agilité",{"ITEM_MOD_AGILITY_SHORT"}},
    {"sta","Endurance",{"ITEM_MOD_STAMINA_SHORT"}}, {"int","Intelligence",{"ITEM_MOD_INTELLECT_SHORT"}},
    {"spi","Esprit",{"ITEM_MOD_SPIRIT_SHORT"}}, {"armor","Armure",{"RESISTANCE0_NAME","ITEM_MOD_ARMOR_SHORT"}},
    {"ap","Puissance d'attaque",{"ITEM_MOD_ATTACK_POWER_SHORT"}}, {"sp","Puissance des sorts",{"ITEM_MOD_SPELL_POWER_SHORT"}},
    {"heal","Soins",{"ITEM_MOD_SPELL_HEALING_DONE_SHORT"}}, {"dps","DPS de l'arme",{"ITEM_MOD_DAMAGE_PER_SECOND_SHORT"}},
}
-- Deliberately modest leveling heuristics. No imported commercial scales.
I.profiles={
    melee={label="Mêlée",str=2,agi=1,sta=1,int=0,spi=.15,armor=.015,ap=.5,sp=0,heal=0,dps=3},
    agile={label="Agilité",str=1,agi=2,sta=1,int=0,spi=.15,armor=.01,ap=.5,sp=0,heal=0,dps=3},
    caster={label="Lanceur de sorts",str=0,agi=0,sta=.8,int=2,spi=1,armor=.005,ap=0,sp=2,heal=0,dps=0},
    soin={label="Soins",str=0,agi=0,sta=.7,int=2,spi=1.8,armor=.005,ap=0,sp=1.5,heal=1.5,dps=0},
    tank={label="Tank",str=1,agi=1,sta=2, int=0,spi=.1,armor=.04,ap=.3,sp=0,heal=0,dps=1},
    feral={label="Druide farouche",str=1.5,agi=2,sta=1.2,int=.2,spi=.2,armor=.015,ap=.5,sp=0,heal=0,dps=0},
    hybride={label="Druide polyvalent",str=1,agi=1,sta=1,int=1,spi=1,armor=.015,ap=.3,sp=1,heal=.5,dps=0},
    chasseur={label="Chasseur",str=.2,agi=2,sta=1,int=.5,spi=.2,armor=.01,ap=.5,sp=0,heal=0,dps=0},
}
I.defaults={DRUID="hybride",PALADIN="melee",WARRIOR="melee",ROGUE="agile",HUNTER="chasseur",SHAMAN="melee",PRIEST="caster",MAGE="caster",WARLOCK="caster"}
function I:Profile()
    local class=B.Call(Ding.Compat.PlayerClass)
    local key=B.char.gearProfile or "auto"
    if key=="auto" then key=self.defaults[class] end
    return self.profiles[key],key
end
function I:SetProfile(key)
    if key~="auto" and not self.profiles[key] then return false end
    B.char.gearProfile=key;return true
end
function I:Stats(link)
    local t=B.Call(C_Item and C_Item.GetItemStats or GetItemStats,link)
    if type(t)~="table" then return end
    local out,recognized={},{}
    for _,s in ipairs(self.stats) do
        out[s[1]]=0
        for _,key in ipairs(s[3]) do
            recognized[key]=true
            local raw=t[key]
            if type(raw)~="nil" then
                local n=self:Number(raw);if not n then return end
                out[s[1]]=n;break
            end
        end
    end
    -- Any extra stat (crit, haste, sockets, resistances, etc.) prevents a strong verdict.
    for k,v in pairs(t) do
        if not recognized[k] then
            local n=self:Number(v);if not n or n~=0 then out.extra=true end
        end
    end
    return out
end
function I:Score(stats,profile)
    local n=0;for _,s in ipairs(self.stats) do n=n+(stats[s[1]] or 0)*(profile[s[1]] or 0) end;return n
end
function I:Equipped(slot)
    -- An unreadable link is not an empty slot. Require a readable zero texture for emptiness.
    local link=B.Call(GetInventoryItemLink,"player",slot)
    if type(link)=="string" then return link end
    local texture=B.Call(GetInventoryItemTexture,"player",slot)
    if texture==nil and type(GetInventoryItemTexture)=="function" and B.Call(InCombatLockdown)~=true then
        local ok,t=pcall(GetInventoryItemTexture,"player",slot)
        if ok and type(t)=="nil" then return false end
    end
end
function I:Compare(link)
    local info=self:Info(link);if not info then return {status="unknown",text="Données de l'objet à charger"} end
    local slots=self.slots[info.equipLoc];if not slots then return end
    local result={info=info,status="unknown",text="Comparaison indisponible"}
    local profile=self:Profile();if not profile then result.text="Choisis un profil de comparaison";return result end
    result.profile=profile.label
    if B.Call(InCombatLockdown)==true then result.text="Comparaison disponible hors combat";return result end
    local level=self:Number(B.Call(UnitLevel,"player"))
    if info.minLevel and level and info.minLevel>level then result.status="unusable";result.text="À garder pour le niveau "..info.minLevel;return result end
    local usable=info.id and B.Call(C_PlayerInfo and C_PlayerInfo.CanUseItem,info.id)
    if usable==false then result.status="unusable";result.text="Non utilisable par ce personnage actuellement";return result end
    local stats=self:Stats(info.link);if not stats then result.text="Statistiques incomplètes : attends le chargement";return result end
    local score=self:Score(stats,profile);local best
    for _,slot in ipairs(slots) do
        local old=self:Equipped(slot)
        if old==nil then result.text="Équipement porté non lisible";return result end
        local oldInfo=old and self:Info(old)
        local oldStats=old and self:Stats(old) or {}
        if old and (not oldInfo or not oldStats) then result.text="Équipement porté à charger";return result end
        local before=self:Score(oldStats,profile)
        if not best or before<best.before then best={slot=slot,old=old,info=oldInfo,stats=oldStats,before=before} end
    end
    -- Two-handers remove both hands; never compare them to the main hand alone.
    if info.equipLoc=="INVTYPE_2HWEAPON" then
        local off=self:Equipped(17)
        if off==nil then result.text="Main gauche non lisible";return result end
        if off then
            local st=self:Stats(off);if not st then return result end
            best.before=best.before+self:Score(st,profile)
            for _,s in ipairs(self.stats) do best.stats[s[1]]=(best.stats[s[1]] or 0)+(st[s[1]] or 0) end
            best.stats.extra=best.stats.extra or st.extra
        end
    end
    result.slot=best.slot;result.gain=score-best.before;result.before=best.before;result.after=score
    result.replaced=best.old;result.deltas={}
    for _,s in ipairs(self.stats) do
        local diff=(stats[s[1]] or 0)-(best.stats[s[1]] or 0)
        if diff~=0 then result.deltas[#result.deltas+1]={text=string.format("%+g %s",diff,s[2]),value=diff,weight=math.abs(diff*(profile[s[1]] or 0))} end
    end
    table.sort(result.deltas,function(a,b) if a.weight==b.weight then return a.text<b.text end;return a.weight>b.weight end)
    local spell=B.Call(C_Item and C_Item.GetItemSpell or GetItemSpell,info.link)
    local oldSpell=best.old and B.Call(C_Item and C_Item.GetItemSpell or GetItemSpell,best.old)
    local unique=B.Call(C_Item and C_Item.GetItemUniquenessByID,info.link)
    local complex=usable~=true or unique==true or info.classID==2 or best.slot>=16 or info.equipLoc=="INVTYPE_TRINKET"
        or stats.extra or best.stats.extra or spell or oldSpell or (info.setID or 0)>0 or (best.info and (best.info.setID or 0)>0)
        or (info.quality or 0)>=3 or (best.info and (best.info.quality or 0)>=3)
    if complex then result.status="compare";result.text="À COMPARER · effets / restrictions non évalués"
    elseif result.gain>math.max(.5,math.abs(best.before)*.02) then result.status="upgrade";result.text="AMÉLIORATION ESTIMÉE"
    elseif result.gain<-.5 then result.status="lower";result.text="Score inférieur pour ce profil"
    else result.status="similar";result.text="Équivalent pour ce profil" end
    return result
end
function I:Bags()
    local out={};local C=Ding.Compat
    for _,bag in ipairs(C.BAGS or {0,1,2,3,4}) do
        local n=self:Number(B.Call(C.BagSize,bag)) or 0
        for slot=1,math.min(n,100) do
            local item=B.Call(C.BagItem,bag,slot)
            local link=self:Text(self:Field(item,"link"))
            if link then out[#out+1]={bag=bag,slot=slot,link=link} end
        end
    end
    return out
end
