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
I.slotNames={[1]=B.T("tête"),[2]=B.T("cou"),[3]=B.T("épaules"),[5]=B.T("torse"),[6]=B.T("taille"),[7]=B.T("jambes"),[8]=B.T("pieds"),[9]=B.T("poignets"),[10]=B.T("mains"),[11]=B.T("anneau 1"),[12]=B.T("anneau 2"),[13]=B.T("bijou 1"),[14]=B.T("bijou 2"),[15]=B.T("dos"),[16]=B.T("main droite"),[17]=B.T("main gauche"),[18]=B.T("à distance")}
I.stats={
    {"str",B.T("Force"),{"ITEM_MOD_STRENGTH_SHORT"}}, {"agi",B.T("Agilité"),{"ITEM_MOD_AGILITY_SHORT"}},
    {"sta",B.T("Endurance"),{"ITEM_MOD_STAMINA_SHORT"}}, {"int",B.T("Intelligence"),{"ITEM_MOD_INTELLECT_SHORT"}},
    {"spi",B.T("Esprit"),{"ITEM_MOD_SPIRIT_SHORT"}}, {"armor",B.T("Armure"),{"RESISTANCE0_NAME","ITEM_MOD_ARMOR_SHORT"}},
    {"ap",B.T("Puissance d'attaque"),{"ITEM_MOD_ATTACK_POWER_SHORT"}}, {"sp",B.T("Puissance des sorts"),{"ITEM_MOD_SPELL_POWER_SHORT"}},
    {"heal",B.T("Soins"),{"ITEM_MOD_SPELL_HEALING_DONE_SHORT"}}, {"dps",B.T("DPS de l'arme"),{"ITEM_MOD_DAMAGE_PER_SECOND_SHORT"}},
}
-- Deliberately modest leveling heuristics. No imported commercial scales.
I.profiles={
    melee={label=B.T("Mêlée"),str=2,agi=1,sta=1,int=0,spi=.15,armor=.015,ap=.5,sp=0,heal=0,dps=3},
    agile={label=B.T("Agilité"),str=1,agi=2,sta=1,int=0,spi=.15,armor=.01,ap=.5,sp=0,heal=0,dps=3},
    caster={label=B.T("Lanceur de sorts"),str=0,agi=0,sta=.8,int=2,spi=1,armor=.005,ap=0,sp=2,heal=0,dps=0},
    soin={label=B.T("Soins"),str=0,agi=0,sta=.7,int=2,spi=1.8,armor=.005,ap=0,sp=1.5,heal=1.5,dps=0},
    tank={label="Tank",str=1,agi=1,sta=2, int=0,spi=.1,armor=.04,ap=.3,sp=0,heal=0,dps=1},
    feral={label=B.T("Druide farouche"),str=1.5,agi=2,sta=1.2,int=.2,spi=.2,armor=.015,ap=.5,sp=0,heal=0,dps=0},
    ours={label=B.T("Druide ours (tank)"),str=1.5,agi=1.5,sta=2,int=.1,spi=.1,armor=.05,ap=.5,sp=0,heal=0,dps=0},
    hybride={label=B.T("Druide polyvalent"),str=1,agi=1,sta=1,int=1,spi=1,armor=.015,ap=.3,sp=1,heal=.5,dps=0},
    chasseur={label=B.T("Chasseur"),str=.2,agi=2,sta=1,int=.5,spi=.2,armor=.01,ap=.5,sp=0,heal=0,dps=0},
}
-- Classic tree order, independent of localized names. Shared roles deliberately
-- share modest leveling weights: these are not simulated spec-specific DPS scales.
I.classes={
    WARRIOR={label=B.T("Guerrier"),{B.T("Armes"),"melee"},{B.T("Fureur"),"melee"},{"Protection","tank"}},
    PALADIN={label="Paladin",{B.T("Sacré"),"soin"},{"Protection","tank"},{B.T("Vindicte"),"melee"}},
    HUNTER={label=B.T("Chasseur"),fallback="chasseur",{B.T("Maîtrise des bêtes"),"chasseur"},{B.T("Précision"),"chasseur"},{B.T("Survie"),"chasseur"}},
    ROGUE={label=B.T("Voleur"),fallback="agile",{B.T("Assassinat"),"agile"},{"Combat","agile"},{B.T("Finesse"),"agile"}},
    PRIEST={label=B.T("Prêtre"),{"Discipline","soin"},{B.T("Sacré"),"soin"},{B.T("Ombre"),"caster"}},
    SHAMAN={label=B.T("Chaman"),{B.T("Élémentaire"),"caster"},{B.T("Amélioration"),"melee"},{B.T("Restauration"),"soin"}},
    MAGE={label="Mage",fallback="caster",{B.T("Arcanes"),"caster"},{B.T("Feu"),"caster"},{B.T("Givre"),"caster"}},
    WARLOCK={label=B.T("Démoniste"),fallback="caster",{"Affliction","caster"},{B.T("Démonologie"),"caster"},{"Destruction","caster"}},
    DRUID={label=B.T("Druide"),{B.T("Équilibre"),"caster"},{B.T("Farouche"),"feral"},{B.T("Restauration"),"soin"}},
}
I.classOrder={"WARRIOR","PALADIN","HUNTER","ROGUE","PRIEST","SHAMAN","MAGE","WARLOCK","DRUID"}
B.LocalizeLabels(I.slotNames)
for _, stat in ipairs(I.stats) do B.LocalizeLabels(stat, {2}) end
for _, profile in pairs(I.profiles) do B.LocalizeLabels(profile, {"label"}) end
for _, class in pairs(I.classes) do
    B.LocalizeLabels(class, {"label"})
    for _, spec in ipairs(class) do B.LocalizeLabels(spec, {1}) end
end
-- The nine races recognized by Ding's Forever import API. Include the live
-- player race below if a later server patch introduces a different race.
I.races={{"Human",B.T("Humain")},{"Dwarf",B.T("Nain")},{"NightElf",B.T("Elfe de la nuit")},{"Gnome","Gnome"},
    {"Orc","Orc"},{"Scourge",B.T("Mort-vivant")},{"Tauren","Tauren"},{"Troll","Troll"},{"Skyborne","Skyborne"}}
for _, race in ipairs(I.races) do B.LocalizeLabels(race, {2}) end
I.classWeights={
    PALADIN={tank={int=.5,sp=.75},melee={int=.25,sp=.25},soin={spi=.5}},
    SHAMAN={melee={int=.5,sp=.3},soin={spi=.5}},
}
I.profileEvents={CHARACTER_POINTS_CHANGED=true,PLAYER_TALENT_UPDATE=true,ACTIVE_TALENT_GROUP_CHANGED=true,
    PLAYER_ENTERING_WORLD=true,PLAYER_LEVEL_UP=true}
function I:InvalidateProfile()
    self.talentCache=nil
    if B.Planner then B.Planner.signature=nil end
    B.dirty=true
end
function I:TalentTree()
    -- Read learned ranks, never recommended builds, preview points or a stance.
    if self:Number(B.Call(GetNumTalentTabs))~=3 then return nil,B.T("Talents non lisibles : choisis une spécialisation.") end
    local best,total,tied=nil,0,false
    for tab=1,3 do
        local count=self:Number(B.Call(GetNumTalents,tab))
        if not count or count<1 or count>100 or count%1~=0 then return nil,B.T("Talents non lisibles : choisis une spécialisation.") end
        local spent=0
        for index=1,count do
            local data=B.Call(function() return {GetTalentInfo(tab,index)} end)
            local rank,maxRank=self:Number(self:Field(data,5)),self:Number(self:Field(data,6))
            if not rank or rank<0 or rank%1~=0 or not maxRank or maxRank<1 or maxRank>100 or rank>maxRank then
                return nil,B.T("Talents non lisibles : choisis une spécialisation.")
            end
            spent=spent+rank
        end
        if spent>total then best,total,tied=tab,spent,false
        elseif spent==total then tied=true end
    end
    if best and not tied then return best end
    return nil,total==0 and B.T("Pas encore de talents : choisis une spécialisation.") or B.T("Build mixte : choisis une spécialisation.")
end
function I:AutoTree(class)
    local now=self:Number(B.Call(GetTime))
    local cache=self.talentCache
    if cache and cache.char==B.char and cache.class==class and now and now>=cache.at and now-cache.at<1 then
        return cache.tree,cache.reason
    end
    local tree,reason=self:TalentTree()
    self.talentCache=now and {char=B.char,class=class,at=now,tree=tree,reason=reason} or nil
    return tree,reason
end
function I:ResolveProfile(class,selection,live)
    local def=self.classes[class]
    if not def then return nil,nil,B.T("Classe inconnue"),B.T("Classe non prise en charge.") end
    if not self:IsSelectionForClass(class,selection) then
        return nil,nil,def.label,B.T("Ce profil ne correspond pas à cette classe. Choisis sa spécialisation.")
    end
    local key,tree,reason=selection
    if selection=="auto" then
        if live then tree,reason=self:AutoTree(class) end
        key=tree and def[tree][2] or def.fallback
    elseif type(selection)=="string" and selection:match("^spec:") then
        local owner,index=selection:match("^spec:([A-Z]+):([123])$")
        tree=owner==class and tonumber(index) or nil
        key=tree and def[tree][2] or nil
    end
    local label=def.label..(tree and " · "..def[tree][1] or B.T(" · spé à préciser"))
    if not key or not self.profiles[key] then return nil,nil,label,reason or B.T("Choisis une spécialisation dans Profil.") end
    local profile=B.Copy(self.profiles[key])
    for stat,weight in pairs(self.classWeights[class] and self.classWeights[class][key] or {}) do profile[stat]=weight end
    if selection~="auto" and not tree then label=def.label.." · "..profile.label end
    if selection~="auto" then label=label..B.T(" (manuel)") end
    return profile,key,label,reason
end
function I:PlayerClass()
    -- The live character is authoritative; browsing another class never changes it.
    local _,class=B.Call(UnitClass,"player")
    if self.classes[class] then return class end
    class=B.Call(Ding.Compat.PlayerClass)
    if self.classes[class] then return class end
end
function I:IsSelectionForClass(class,selection)
    local def=self.classes[class]
    if not def then return false end
    if selection=="auto" then return true end
    if type(selection)~="string" then return false end
    local owner,index=selection:match("^spec:([A-Z]+):([123])$")
    if owner then return owner==class and def[tonumber(index)]~=nil end
    -- Keep compatible legacy choices, but never let e.g. feral score a mage's gear.
    for tab=1,3 do if selection==def[tab][2] then return true end end
    return class=="DRUID" and (selection=="ours" or selection=="hybride")
end
function I:ProfileSelection()
    local selection=B.char and B.char.gearProfile or "auto"
    return self:IsSelectionForClass(self:PlayerClass(),selection) and selection or "auto"
end
function I:ClassChoices()
    local out={}
    for _,class in ipairs(self.classOrder) do out[#out+1]={key=class,label=self.classes[class].label} end
    return out
end
function I:Profile()
    return self:ResolveProfile(self:PlayerClass(),self:ProfileSelection(),true)
end
function I:SetProfile(key)
    if not self:IsSelectionForClass(self:PlayerClass(),key) then return false end
    B.char.gearProfile=key;self:InvalidateProfile();return true
end
function I:SpecChoices(class,includeAuto)
    local out=includeAuto and {{key="auto",label=B.T("Automatique · mes talents")}} or {}
    local def=self.classes[class];if not def then return out end
    for tab=1,3 do out[#out+1]={key="spec:"..class..":"..tab,label=def[tab][1]} end
    if class=="DRUID" then out[#out+1]={key="ours",label=B.T("Farouche · ours tank")} end
    return out
end
function I:RaceChoices()
    local out={};local name,token=B.Call(UnitRace,"player");local found=false
    name,token=self:Text(name),self:Text(token)
    for _,r in ipairs(self.races) do
        out[#out+1]={key=r[1],label=r[1]==token and name or r[2]};found=found or r[1]==token
    end
    if token and not found then out[#out+1]={key=token,label=name or token} end
    return out
end
function I:Priorities(profile)
    if not profile then return B.T("Choisis une spécialisation.") end
    local rows={}
    for _,stat in ipairs(self.stats) do
        local weight=profile[stat[1]] or 0
        if weight>0 then rows[#rows+1]={name=stat[2],weight=weight} end
    end
    table.sort(rows,function(a,b) if a.weight==b.weight then return a.name<b.name end;return a.weight>b.weight end)
    local out={};for i=1,math.min(5,#rows) do out[i]=rows[i].name end
    return table.concat(out," · ")
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
    local info=self:Info(link);if not info then return {status="unknown",text=B.T("Données de l'objet à charger")} end
    local slots=self.slots[info.equipLoc];if not slots then return end
    if not B.HasPremium("class") then return {info=info,status="premium",text=B.T("Conseils de classe : Options > Abonnement.")} end
    local result={info=info,status="unknown",text=B.T("Comparaison indisponible")}
    local profile,_,label,reason=self:Profile();result.profile=label
    if not profile then result.text=reason or B.T("Choisis une spécialisation dans Profil.");return result end
    if B.Call(InCombatLockdown)==true then result.text=B.T("Comparaison disponible hors combat");return result end
    local level=self:Number(B.Call(UnitLevel,"player"))
    if info.minLevel and level and info.minLevel>level then result.status="unusable";result.text=B.T("À garder pour le niveau ")..info.minLevel;return result end
    local usable=info.id and B.Call(C_PlayerInfo and C_PlayerInfo.CanUseItem,info.id)
    if usable==false then result.status="unusable";result.text=B.T("Non utilisable par ce personnage actuellement");return result end
    local stats=self:Stats(info.link);if not stats then result.text=B.T("Statistiques incomplètes : attends le chargement");return result end
    local score=self:Score(stats,profile);local best
    for _,slot in ipairs(slots) do
        local old=self:Equipped(slot)
        if old==nil then result.text=B.T("Équipement porté non lisible");return result end
        local oldInfo=old and self:Info(old)
        local oldStats=old and self:Stats(old) or {}
        if old and (not oldInfo or not oldStats) then result.text=B.T("Équipement porté à charger");return result end
        local before=self:Score(oldStats,profile)
        if not best or before<best.before then best={slot=slot,old=old,info=oldInfo,stats=oldStats,before=before} end
    end
    -- Two-handers remove both hands; never compare them to the main hand alone.
    if info.equipLoc=="INVTYPE_2HWEAPON" then
        local off=self:Equipped(17)
        if off==nil then result.text=B.T("Main gauche non lisible");return result end
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
    -- A rare item's raw stats can justify a modest route preference, but the
    -- tooltip still asks the player to compare it. Weapons/procs stay excluded.
    result.rewardStatUpgrade=not complex and result.gain>math.max(.5,math.abs(best.before)*.02)
    complex=complex or (info.quality or 0)>=3 or (best.info and (best.info.quality or 0)>=3)
    if complex then result.status="compare";result.text=B.T("À COMPARER · effets / restrictions non évalués")
    elseif result.gain>math.max(.5,math.abs(best.before)*.02) then result.status="upgrade";result.text=B.T("AMÉLIORATION ESTIMÉE")
    elseif result.gain<-.5 then result.status="lower";result.text=B.T("Score inférieur pour ce profil")
    else result.status="similar";result.text=B.T("Équivalent pour ce profil") end
    return result
end
function I:QuestReward(q)
    local best
    for _,r in ipairs(B.Knowledge:Rewards(q)) do
        local compare=self:Compare(r.link)
        if compare and compare.info and (compare.info.quality or 0)>=3 and compare.rewardStatUpgrade then
            local fraction=compare.gain/math.max(10,math.abs(compare.before))
            local bonus=math.min(.25,fraction*.5)
            if bonus>=.03 and (not best or bonus>best.bonus) then
                best={link=r.link,bonus=bonus,profile=compare.profile,deltas=compare.deltas}
            end
        end
    end
    return best
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
