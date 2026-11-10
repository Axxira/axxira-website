local B=ForeverPilotLeveling;local I,A=B.Items,B.Advisor
local old={char=B.char,level=UnitLevel,race=UnitRace,unitClass=UnitClass,combat=InCombatLockdown,item=C_Item,player=C_PlayerInfo,link=GetInventoryItemLink,texture=GetInventoryItemTexture,class=Ding.Compat.PlayerClass,tabs=GetNumTalentTabs,num=GetNumTalents,talent=GetTalentInfo,time=GetTime,ui=B.UI,settings=B.settings}
local class,spent,clock,reads='DRUID',{0,7,0},10,0
B.char={gearProfile='auto'};Ding.Compat.PlayerClass=function() return class end
UnitClass=function() return I.classes[class] and I.classes[class].label,class end
UnitRace=function() return 'Tauren','Tauren',6 end
UnitLevel=function() return 16 end;InCombatLockdown=function() return false end
GetTime=function() return clock end
GetNumTalentTabs=function() return 3 end;GetNumTalents=function() return 11 end
local function talent(tab,index)
 reads=reads+1;return 'Talent',123,math.ceil(index/2),1,math.max(0,math.min(5,spent[tab]-(index-1)*5)),5
end
GetTalentInfo=talent
local function build(c,points) class=c;spent=points;I:InvalidateProfile() end
GetInventoryItemLink=function(_,slot) if slot==6 then return 'item:70100' end end
GetInventoryItemTexture=function(_,slot) if slot==6 then return 123 end end
C_Item={GetItemInfo=function(link)
 local equipped=link=='item:70100'
 return equipped and "Scout Ranger's Belt" or 'Jackseed Belt',link,2,16,1,'Armor',equipped and 'Leather' or 'Cloth',1,'INVTYPE_WAIST',123,56,4,equipped and 2 or 1,1,0,0
end,GetItemStats=function(link)
 return link=='item:70100' and {ITEM_MOD_STRENGTH_SHORT=1,RESISTANCE0_NAME=31} or {ITEM_MOD_INTELLECT_SHORT=3,RESISTANCE0_NAME=16}
end,GetItemUniquenessByID=function() return false end}
C_PlayerInfo={CanUseItem=function() return true end}
local checks=0
local function check(ok,message) checks=checks+1;assert(ok,message) end
local expected={
 WARRIOR={{'melee','Armes'},{'melee','Fureur'},{'tank','Protection'}},
 PALADIN={{'soin','Sacré'},{'tank','Protection'},{'melee','Vindicte'}},
 HUNTER={{'chasseur','Maîtrise des bêtes'},{'chasseur','Précision'},{'chasseur','Survie'}},
 ROGUE={{'agile','Assassinat'},{'agile','Combat'},{'agile','Finesse'}},
 PRIEST={{'soin','Discipline'},{'soin','Sacré'},{'caster','Ombre'}},
 SHAMAN={{'caster','Élémentaire'},{'melee','Amélioration'},{'soin','Restauration'}},
 MAGE={{'caster','Arcanes'},{'caster','Feu'},{'caster','Givre'}},
 WARLOCK={{'caster','Affliction'},{'caster','Démonologie'},{'caster','Destruction'}},
 DRUID={{'caster','Équilibre'},{'feral','Farouche'},{'soin','Restauration'}},
}
for c,trees in pairs(expected) do
 for tab,entry in ipairs(trees) do
  local points={5,5,5};points[tab]=31;build(c,points)
  local profile,key,label=I:Profile()
  check(profile and key==entry[1] and label:find(entry[2],1,true),c..' tree '..tab..' wrong auto role/spec')
  check(label:find(I.classes[c].label,1,true),'missing class in tooltip')
  check(I:SetProfile('spec:'..c..':'..tab),'manual specialization refused')
  spent={0,0,0};I:InvalidateProfile()
  check(select(2,I:Profile())==entry[1],'manual spec must override zero talents')
  check(select(3,I:Profile()):find('manuel',1,true),'manual override not identified')
  check(I:SetProfile('auto'),'return to auto failed')
 end
end
-- A legacy druid role must never be applied to another player's equipment.
for c in pairs(expected) do
 build(c,{0,31,0})
 if c~='DRUID' then
  B.char.gearProfile='feral'
  local profile,key,label=I:Profile()
  check(profile and key~='feral' and label:find(I.classes[c].label,1,true),'saved druid role contaminated '..c)
  check(not I:SetProfile('feral') and not I:SetProfile('ours') and not I:SetProfile('hybride'),'foreign legacy role accepted for '..c)
  check(not I:ResolveProfile(c,'feral',false),'preview accepted druid weights for '..c)
 end
 B.char.gearProfile='spec:DRUID:2'
 check(I:ProfileSelection()==(c=='DRUID' and 'spec:DRUID:2' or 'auto'),'foreign spec not normalized for '..c)
 I:SetProfile('auto')
end
for _,case in ipairs({{'DRUID',2,'lower'},{'WARRIOR',1,'lower'},{'ROGUE',2,'lower'},
 {'MAGE',3,'upgrade'},{'WARLOCK',1,'upgrade'},{'PRIEST',2,'upgrade'},
 {'PALADIN',1,'upgrade'},{'SHAMAN',1,'upgrade'},{'HUNTER',1,'upgrade'}}) do
 build(case[1],{0,0,0});I:SetProfile('spec:'..case[1]..':'..case[2])
 check(I:Compare('item:70101').status==case[3],'same item not evaluated for '..case[1]..' specialization')
end
-- Read the actual client class even if a compatibility provider is stale.
build('MAGE',{0,0,31});B.char.gearProfile='feral'
Ding.Compat.PlayerClass=function() return 'DRUID' end
check(I:PlayerClass()=='MAGE' and select(2,I:Profile())=='caster','live class lost to stale compatibility data')
UnitClass=nil
check(I:PlayerClass()=='DRUID','compatible class fallback failed when native API missing')
Ding.Compat.PlayerClass=function() return class end
UnitClass=function() return I.classes[class] and I.classes[class].label,class end
B.char.gearProfile='auto'
build('DRUID',{0,7,0})
local r=I:Compare('item:70101')
check(r.status=='lower' and r.profile:find('Farouche') and A:UpgradeSummary(r)=='Upgrade : NON','reported feral belt regression')
check(math.abs(r.gain+1.125)<.001,'reported stat tradeoff incorrectly weighted')
I:SetProfile('hybride');check(I:Compare('item:70101').status=='upgrade','fixture no longer reproduces old error')
I:SetProfile('ours');check(I:Compare('item:70101').status=='lower' and I:Profile().dps==0,'bear must ignore weapon DPS and value armor')
I:SetProfile('feral');build('DRUID',{0,0,20});check(select(2,I:Profile())=='feral','explicit role overridden by respec')
I:SetProfile('auto');check(select(2,I:Profile())=='soin','restoration respec not detected')
for _,c in ipairs({'DRUID','PALADIN','WARRIOR','SHAMAN','PRIEST'}) do
 build(c,{5,5,0});check(not I:Profile() and I:Compare('item:70101').status=='unknown','mixed role must not guess: '..c)
 build(c,{0,0,0});check(not I:Profile(),'unspent hybrid talents must request choice: '..c)
end
for _,c in ipairs({'MAGE','WARLOCK','ROGUE','HUNTER'}) do
 build(c,{0,0,0});local p,_,label=I:Profile();check(p and label:find('spé à préciser',1,true),'single-role class needs honest early-level fallback')
end
build('DRUID',{0,7,0});reads=0;I:Profile();local count=reads
I:Profile();check(reads==count,'tooltip cache missing')
spent={9,0,0};clock=clock+1.1;check(select(2,I:Profile())=='caster' and reads>count,'expired cache did not recover from missed event')
GetTalentInfo=function() return 'Talent',123,1,1,nil,5 end;I:InvalidateProfile();check(not I:Profile(),'unreadable rank must not guess')
GetTalentInfo=function() return 'Talent',123,1,1,6,5 end;I:InvalidateProfile();check(not I:Profile(),'invalid rank must not guess')
GetTalentInfo=function() error('secret API') end;I:InvalidateProfile();check(not I:Profile(),'API failure must not escape')
GetTalentInfo=talent;GetNumTalentTabs=nil;I:InvalidateProfile();check(not I:Profile(),'missing API must not guess')
check(I:SetProfile('spec:DRUID:2') and select(2,I:Profile())=='feral','manual spec unavailable without API')
check(not I:SetProfile('spec:PALADIN:1') and not I:SetProfile('spec:DRUID:4') and not I:SetProfile('bogus'),'invalid/foreign spec accepted')
GetNumTalentTabs=function() return 3 end;I:SetProfile('auto');build('DRUID',{0,7,0})
local savedChar=B.char;I:Profile();B.char={gearProfile='auto'};class='PALADIN';spent={31,0,0}
check(select(2,I:Profile())=='soin','profile leaked between characters')
I:SetProfile('spec:PALADIN:2');B.char=savedChar;class='DRUID';spent={0,7,0}
check(B.char.gearProfile=='auto' and select(2,I:Profile())=='feral','manual override leaked between characters')
-- Exercise real registered callbacks: talent event invalidates before drawer refresh.
B.UI={frame=CreateFrame('Frame')};B.settings={enabled=true};A:Init()
for _,event in ipairs({'CHARACTER_POINTS_CHANGED','PLAYER_TALENT_UPDATE','ACTIVE_TALENT_GROUP_CHANGED','PLAYER_ENTERING_WORLD','PLAYER_LEVEL_UP'}) do
 check(A.events.events[event],'missing talent refresh event '..event)
 I:Profile();A.events.scripts.OnEvent(A.events,event)
 check(I.talentCache==nil,'event failed to invalidate '..event)
end
-- Real selector callbacks; preview must never change live upgrades or identity.
A:Toggle('gear');A.profileButton.scripts.OnClick()
check(A.profileFrame:IsShown() and A.raceChoice.enabled==false and A.classChoice.enabled==true,'all-class browser must be available in live mode')
A.classChoice.scripts.OnClick()
local classCount=0;for key in pairs(A.choiceButtons) do classCount=classCount+1;check(expected[key],'unexpected class in selector') end
check(classCount==9,'not all nine classes visible from live profile')
A.choiceButtons.DRUID.scripts.OnClick()
check(A.profileMode=='live','choosing live class should preserve live mode')
A.specChoice.scripts.OnClick();A.choiceButtons['spec:DRUID:2'].scripts.OnClick()
check(B.char.gearProfile=='auto','draft choice applied before confirmation')
A.profileApply.scripts.OnClick();check(B.char.gearProfile=='spec:DRUID:2','manual specialization not saved')
A.profileButton.scripts.OnClick();A.previewModeButton.scripts.OnClick()
A.classChoice.scripts.OnClick();A.choiceButtons.PALADIN.scripts.OnClick()
A.raceChoice.scripts.OnClick();A.choiceButtons.Human.scripts.OnClick()
A.specChoice.scripts.OnClick();A.choiceButtons['spec:PALADIN:1'].scripts.OnClick()
check(A.profileInfo.text:find('Sacré',1,true) and A.profileInfo.text:find('Aperçu',1,true),'preview did not render requested class/spec')
check(I:Compare('item:70101').status=='lower','preview contaminated live tooltip')
A.profileApply.scripts.OnClick()
check(B.char.gearPreview.class=='PALADIN' and B.char.gearPreview.race=='Human' and B.char.gearProfile=='spec:DRUID:2','preview save contaminated current profile')
A.profileButton.scripts.OnClick();A.classChoice.scripts.OnClick();A.choiceButtons.MAGE.scripts.OnClick()
check(A.profileMode=='preview' and A.preview.class=='MAGE' and I:PlayerClass()=='DRUID','direct class browser changed real identity')
A.specChoice.scripts.OnClick();check(A.choiceButtons['spec:MAGE:3'] and not A.choiceButtons['spec:DRUID:2'],'foreign specs still visible after class change')
A.choiceFrame:Hide();A.profileFrame:Hide()
A.profileButton.scripts.OnClick();A.previewModeButton.scripts.OnClick()
check(A.preview.class=='PALADIN' and A.preview.selection=='spec:PALADIN:1','preview not remembered')
A.liveModeButton.scripts.OnClick();A.specChoice.scripts.OnClick();A.choiceButtons.auto.scripts.OnClick();A.profileApply.scripts.OnClick()
check(B.char.gearProfile=='auto','return to automatic via button failed')
spent={15,0,0};A.events.scripts.OnEvent(A.events,'PLAYER_TALENT_UPDATE')
check(select(2,I:Profile())=='caster','real event failed to follow new tree')
UnitRace=function() return 'Future race','FutureRace' end
local races=I:RaceChoices();check(races[#races].key=='FutureRace','unknown live race was dropped')
check(#I:SpecChoices('DRUID',true)==5 and #I:SpecChoices('MAGE',false)==3,'spec choices missing or class mixup')
B.char={gearProfile='auto'};A.profileFrame:Hide();A:ToggleProfile()
check(A.preview.class=='DRUID' and A.preview.race=='FutureRace','saved preview leaked into a different character')
A.profileFrame:Hide();A.frame:Hide()
B.char=old.char;B.UI=old.ui;B.settings=old.settings;UnitLevel=old.level;UnitRace=old.race;UnitClass=old.unitClass;InCombatLockdown=old.combat;C_Item=old.item;C_PlayerInfo=old.player;GetInventoryItemLink=old.link;GetInventoryItemTexture=old.texture;Ding.Compat.PlayerClass=old.class;GetNumTalentTabs=old.tabs;GetNumTalents=old.num;GetTalentInfo=old.talent;GetTime=old.time;I:InvalidateProfile()
print('PASS equipment profiles: '..checks..' checks; 27 specs, belt regression, manual/auto, talents, character isolation and selector/preview')
