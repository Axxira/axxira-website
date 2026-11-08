local B=ForeverPilotLeveling;local A,M,I=B.Advisor,B.Market,B.Items
local old={info=I.Info,compare=I.Compare,item=C_Item,util=TooltipUtil,tip=GameTooltip,ref=ItemRefTooltip,db=M.db}
local link='item:765';local lines={};local quantity=5;local bound=false;local binding=0
local function tip()
 return {AddLine=function(_,s) lines[#lines+1]=s end,
 GetPrimaryTooltipData=function() return {guid='bag-item'} end}
end
GameTooltip=tip();ItemRefTooltip=tip()
TooltipUtil={GetDisplayedItem=function() return 'Herb',link end}
local location={}
C_Item={GetItemLocation=function(guid) assert(guid=='bag-item');return location end,
 GetStackCount=function(loc) assert(loc==location);return quantity end,
 IsBound=function(loc) assert(loc==location);return bound end}
I.Info=function() return {vendor=10,bindType=binding} end
I.Compare=function() return nil end
M.db={prices={}};local batch={};M:Add(batch,link,2500,5);M:Commit(batch,'Scan HDV',false)
local function render(target)
 lines={};A.lastError=nil;A:Tooltip(target or GameTooltip);assert(not A.lastError,A.lastError);return table.concat(lines,'\n')
end
local content=render()
assert(content:find('HDV / unité : 5 Silver 0 Copper',1,true) and content:find('HDV / pile de 5 : 25 Silver 0 Copper',1,true),'unit/hovered-stack price missing')
assert(M:Money(12345)=='1 Gold 23 Silver 45 Copper' and M:Money(7)=='7 Copper','currency labels differ from preference')
assert(content:find('Minimum observé') and content:find("à l'instant") and content:find('Marchand / unité'))
quantity=2;assert(render():find('pile de 2 : 10 Silver 0 Copper',1,true),'stack value retained from another slot')
quantity=1;assert(not render():find('pile de'),'single item duplicated as stack')
quantity=0/0;assert(not render():find('pile de'),'invalid count used')
quantity=5;bound=true;content=render();assert(content:find('objet lié') and not content:find('HDV / unité'),'soulbound BoE shown as saleable')
bound=false;binding=1;assert(render():find('objet lié'));binding=0
local price=M:Get(link);price.at=time()-90000;price.partial=true
content=render();assert(content:find('ANCIEN') and content:find('partiel'),'old/incomplete observation presented as live')
link='item:765:0:0:0:0:0:4';content=render()
assert(content:find('prix inconnu') and not content:find('HDV / unité'),'different suffix borrowed another price')
link='item:765';I.Compare=function() error('equipment API unavailable') end
assert(render():find('HDV / unité'),'comparison failure swallowed prices')
I.Info=function() return nil end;assert(render():find('HDV / unité'),'uncached item info swallowed prices')
-- Chat links have no concrete bag location: do not invent a stack or binding.
ItemRefTooltip.GetPrimaryTooltipData=function() return {hyperlink=link} end
content=render(ItemRefTooltip);assert(content:find('HDV / unité') and not content:find('pile de'))
TooltipUtil.GetDisplayedItem=function() return nil end
GameTooltip.GetItem=function() return 'Herb',link end
assert(render():find('HDV / unité'),'legacy GetItem fallback lost price')
B.settings.enabled=false;assert(render()=='','disabled addon still adds prices');B.settings.enabled=true
I.Info=old.info;I.Compare=old.compare;C_Item=old.item;TooltipUtil=old.util;GameTooltip=old.tip;ItemRefTooltip=old.ref;M.db=old.db
print('PASS tooltip: unit/stack prices, age, partial/unknown variants, bound items, uncached gear, fallback and off switch')
