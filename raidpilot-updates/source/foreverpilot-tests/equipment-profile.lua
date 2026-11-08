local B=ForeverPilotLeveling;local I,A=B.Items,B.Advisor
local old={char=B.char,level=UnitLevel,combat=InCombatLockdown,item=C_Item,player=C_PlayerInfo,link=GetInventoryItemLink,texture=GetInventoryItemTexture,class=Ding.Compat.PlayerClass,tabs=GetNumTalentTabs,num=GetNumTalents,talent=GetTalentInfo}
B.char={gearProfile='auto'};Ding.Compat.PlayerClass=function() return 'DRUID' end
UnitLevel=function() return 16 end;InCombatLockdown=function() return false end
GetNumTalentTabs=function() return 3 end;GetNumTalents=function() return 1 end
local spent={0,7,0}
GetTalentInfo=function(tab) return 'Talent',123,1,1,spent[tab],5 end
GetInventoryItemLink=function(_,slot) if slot==6 then return 'item:70100' end end
GetInventoryItemTexture=function(_,slot) if slot==6 then return 123 end end
C_Item={GetItemInfo=function(link)
 local equipped=link=='item:70100'
 return equipped and "Scout Ranger's Belt" or 'Jackseed Belt',link,2,16,1,'Armor',equipped and 'Leather' or 'Cloth',1,'INVTYPE_WAIST',123,56,4,equipped and 2 or 1,1,0,0
end,GetItemStats=function(link)
 return link=='item:70100' and {ITEM_MOD_STRENGTH_SHORT=1,RESISTANCE0_NAME=31} or {ITEM_MOD_INTELLECT_SHORT=3,RESISTANCE0_NAME=16}
end,GetItemUniquenessByID=function() return false end}
C_PlayerInfo={CanUseItem=function() return true end}
local r=I:Compare('item:70101')
assert(r.status=='lower' and r.profile=='Druide farouche' and A:UpgradeSummary(r)=='Upgrade : NON','reported belt must not be a feral upgrade')
assert(math.abs(r.gain+1.125)<.001,'reported stat tradeoff incorrectly weighted')
I:SetProfile('hybride');assert(I:Compare('item:70101').status=='upgrade','regression must reproduce wrong old default')
I:SetProfile('feral');spent={0,0,20};assert(select(2,I:Profile())=='feral','explicit player role must override talent inference')
I:SetProfile('auto');assert(select(2,I:Profile())=='soin','restoration tree')
spent={8,0,0};assert(select(2,I:Profile())=='caster','balance tree')
spent={5,5,0};assert(not I:Profile() and I:Compare('item:70101').status=='unknown','tied build must request a role')
spent={0,0,0};assert(not I:Profile(),'unspent talents must not invent polyvalent role')
GetTalentInfo=function() return 'Talent',123,1,1,nil,5 end;assert(not I:Profile(),'unreadable talents must not infer a role')
GetNumTalentTabs=nil;assert(not I:Profile(),'missing API must not fall back to polyvalent')
I:SetProfile('feral');assert(select(2,I:Profile())=='feral','manual selection works without talent API')
I:SetProfile('auto');Ding.Compat.PlayerClass=function() return 'WARRIOR' end;assert(select(2,I:Profile())=='melee','other class defaults unchanged')
B.char=old.char;UnitLevel=old.level;InCombatLockdown=old.combat;C_Item=old.item;C_PlayerInfo=old.player;GetInventoryItemLink=old.link;GetInventoryItemTexture=old.texture;Ding.Compat.PlayerClass=old.class;GetNumTalentTabs=old.tabs;GetNumTalents=old.num;GetTalentInfo=old.talent
print('PASS druid profile: reported belt, feral/balance/restoration, explicit override, mixed/unspent/unreadable talents')
