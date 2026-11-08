local B=ForeverPilotLeveling;local A,M,I=B.Advisor,B.Market,B.Items
local old={info=I.Info,compare=I.Compare,item=C_Item,util=TooltipUtil,tip=GameTooltip,ref=ItemRefTooltip,db=M.db,currency=C_CurrencyInfo,coin=GetCoinTextureString}
local link='item:765';local lines={};local quantity=5;local bound=false;local binding=0
local function tip() return {AddLine=function(_,s) lines[#lines+1]=s end,GetPrimaryTooltipData=function() return {guid='bag-item'} end} end
GameTooltip=tip();ItemRefTooltip=tip();TooltipUtil={GetDisplayedItem=function() return 'Herb',link end}
local location={}
C_Item={GetItemLocation=function(guid) assert(guid=='bag-item');return location end,
 GetStackCount=function(loc) assert(loc==location);return quantity end,IsBound=function(loc) assert(loc==location);return bound end}
I.Info=function() return {vendor=10,bindType=binding} end;I.Compare=function() return nil end
C_CurrencyInfo=nil;GetCoinTextureString=nil
local gold='|TInterface\\MoneyFrame\\UI-GoldIcon:14:14:2:0|t'
local silver='|TInterface\\MoneyFrame\\UI-SilverIcon:14:14:2:0|t'
local copper='|TInterface\\MoneyFrame\\UI-CopperIcon:14:14:2:0|t'
assert(M:Money(12345)=='1'..gold..' 23'..silver..' 45'..copper,'three coin amounts/icons')
assert(M:Money(7)=='7'..copper and M:Money(0)=='0'..copper and M:Money(10000)=='1'..gold,'small/zero/exact gold')
assert(M:Money(-1)=='?' and M:Money(0/0)=='?' and M:Money(math.huge)=='?','invalid prices')
C_CurrencyInfo={GetCoinTextureString=function(amount,height) assert(amount==20405 and height==14);return 'native coins' end}
assert(M:Money(20405)=='native coins','prefer Forever currency API')
C_CurrencyInfo=nil;GetCoinTextureString=function() return 'legacy coins' end
assert(M:Money(20405)=='legacy coins','legacy client currency API')
GetCoinTextureString=function() error('unavailable') end
assert(M:Money(20405)=='2'..gold..' 4'..silver..' 5'..copper,'fallback still has icons');GetCoinTextureString=nil
M.db={prices={}};local batch={};M:Add(batch,link,2500,5);M:Commit(batch,'Scan HDV',false)
local function render(target)
 lines={};A.lastError=nil;A:Tooltip(target or GameTooltip);assert(not A.lastError,A.lastError);return table.concat(lines,'\n')
end
local content=render()
assert(content:find('HDV : 5'..silver..' / unité',1,true) and content:find('Pile de 5 : 25'..silver,1,true),'unit/hovered-stack price missing')
assert(content:find('Prix min.') and content:find("à l'instant") and not content:find('Marchand'),'price freshness and no duplicate vendor row')
quantity=2;assert(render():find('Pile de 2 : 10'..silver,1,true),'stack value retained from another slot')
quantity=1;assert(not render():find('Pile de'),'single item duplicated as stack')
quantity=0/0;assert(not render():find('Pile de'),'invalid count used')
quantity=5;bound=true;content=render();assert(content:find('non vendable') and not content:find(silver,1,true),'bound item shown as saleable')
bound=false;binding=1;assert(render():find('non vendable'));binding=0
local price=M:Get(link);price.at=time()-90000;price.partial=true
content=render();assert(content:find('ANCIEN') and content:find('partiel'),'old/incomplete observation presented as live')
link='item:765:0:0:0:0:0:4';content=render();assert(content:find('prix inconnu') and not content:find(silver,1,true),'different suffix borrowed another price')
link='item:765';I.Compare=function() error('equipment API unavailable') end
assert(render():find(silver,1,true),'comparison failure swallowed prices')
I.Info=function() return nil end;assert(render():find(silver,1,true),'uncached item info swallowed prices')
ItemRefTooltip.GetPrimaryTooltipData=function() return {hyperlink=link} end
content=render(ItemRefTooltip);assert(content:find(silver,1,true) and not content:find('Pile de'))
TooltipUtil.GetDisplayedItem=function() return nil end;GameTooltip.GetItem=function() return 'Herb',link end
assert(render():find(silver,1,true),'legacy GetItem fallback lost price')
-- One verdict, explicit gains/tradeoffs, no internal score. Unknown is not a false NO.
local result={status='upgrade',profile='Druide farouche',gain=12.1,slot=6,deltas={{text='+3 Agilité',value=3},{text='-15 Armure',value=-15}}}
I.Compare=function() return result end
content=render();assert(content:find('Upgrade : OUI',1,true) and content:find('Gains : +3 Agilité',1,true) and content:find('Pertes : -15 Armure',1,true),'clear verdict and stat tradeoff')
assert(content:find('Druide farouche',1,true) and not content:find('points') and not content:find('Score') and not content:find('Comparé à'),'internal comparison clutter')
assert(content:find('Upgrade',1,true)<content:find('HDV',1,true),'upgrade must lead the tooltip')
for _,status in ipairs({'lower','similar','unusable'}) do result.status=status;result.text='Niveau 20 requis';assert(render():find('Upgrade : NON',1,true),'negative verdict') end
for _,status in ipairs({'unknown','compare'}) do result.status=status;result.text='Données à charger';content=render();assert(content:find('Upgrade : À VÉRIFIER',1,true) and not content:find('Upgrade : NON',1,true),'uncertain data misrepresented') end
B.settings.enabled=false;assert(render()=='','disabled addon still adds prices');B.settings.enabled=true
I.Info=old.info;I.Compare=old.compare;C_Item=old.item;TooltipUtil=old.util;GameTooltip=old.tip;ItemRefTooltip=old.ref;M.db=old.db;C_CurrencyInfo=old.currency;GetCoinTextureString=old.coin
print('PASS tooltip: simple verdicts, gains/losses, native/fallback coin icons, stack prices, age, binding, unknown data, variants and off switch')
