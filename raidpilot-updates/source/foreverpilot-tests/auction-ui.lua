local B=ForeverPilotLeveling;local M,U=B.Market,B.AuctionUI
M.db={prices={}};B.char.market=M.db;M.open=false;M.requestAt=nil;M.pending=nil;M.job=nil;M.status=nil
B.settings.enabled=true
local requests=0
C_AuctionHouse={IsThrottledMessageSystemReady=function() return true end,ReplicateItems=function() requests=requests+1 end,
 GetNumReplicateItems=function() return 2 end,
 GetReplicateItemInfo=function(i) return 'Herb',123,20,1,true,1,'',0,0,i==0 and 20000 or 10000 end,
 GetReplicateItemLink=function() return 'item:765' end}
assert(not U.button,'auction UI created without Blizzard auction frame')
AuctionHouseFrame=CreateFrame('Frame','AuctionHouseFrame',UIParent)
local oldShow=0;AuctionHouseFrame:SetScript('OnShow',function() oldShow=oldShow+1 end)
U.events.scripts.OnEvent(U.events,'ADDON_LOADED','Blizzard_AuctionHouseUI')
assert(U.button and U.button.parent==AuctionHouseFrame and U.footer.parent==AuctionHouseFrame,'controls not attached to AH')
assert(M.open and U.button.enabled and U.title.text=='Scanner HDV','already-open AH not detected')
assert(U.button.anchor=='TOPRIGHT' and U.button.h==22 and U.footer.h==20,'toolbar covers main AH content')
assert(U.button.strata=='HIGH' and U.button:GetFrameLevel()>AuctionHouseFrame:GetFrameLevel()+5,'scan button hidden behind Blizzard title decoration')
local first=U.button;local frames=#TEST.frames
U:Attach();U.events.scripts.OnEvent(U.events,'AUCTION_HOUSE_SHOW')
assert(U.button==first and #TEST.frames==frames,'duplicate AH controls')
AuctionHouseFrame.scripts.OnShow();assert(oldShow==1,'Blizzard OnShow overwritten')
U.button.scripts.OnClick()
assert(requests==1 and M.pending and not U.button.enabled and U.status.text:find('attente du serveur'),'click did not start scan')
U.button.scripts.OnClick();assert(requests==1,'double click sent duplicate scan')
M:Event('REPLICATE_ITEM_LIST_UPDATE')
assert(M.db.count==1 and M:Get('item:765').price==500 and not M.pending and not M.job,'prices not committed')
assert(U.status.text:find('1 variantes mémorisées') and U.title.text:find('15:00'),'result or cooldown not visible')
TEST.clock=TEST.clock+60;U.button.scripts.OnUpdate(U.button,1)
assert(U.title.text:find('14:00'),'countdown did not advance')
M.requestAt=nil;M:Init();U:Refresh()
assert(M:CooldownRemaining()==840 and not U.button.enabled,'reload bypassed cooldown')
TEST.clock=TEST.clock+841;U:Refresh();assert(U.button.enabled,'expired cooldown kept button disabled')
TEST.combat=true;U:Refresh();assert(not U.button.enabled and U.status.text:find('combat'),'combat reason missing');TEST.combat=false
C_AuctionHouse.IsThrottledMessageSystemReady=function() return false end
U:Refresh();assert(not U.button.enabled and U.status.text:find('occupé'),'throttle not visible')
C_AuctionHouse.IsThrottledMessageSystemReady=function() return true end
U:Refresh();U.button.scripts.OnClick();local timeout=TEST.timers[#TEST.timers]
timeout();assert(not M.pending and U.status.text:find('répondu'),'timeout hidden behind cooldown')
TEST.clock=TEST.clock+901;U:Refresh();U.button.scripts.OnClick()
AuctionHouseFrame:Hide();AuctionHouseFrame.scripts.OnHide()
assert(not M.open and not M.pending and not M.job,'closing AH did not cancel')
M:Event('REPLICATE_ITEM_LIST_UPDATE');assert(M.db.count==1,'late event changed prices')
B.settings.enabled=false;U:SyncEnabled();assert(not U.button:IsShown() and not U.footer:IsShown(),'off left controls visible')
AuctionHouseFrame:Show();AuctionHouseFrame.scripts.OnShow();assert(not M.open,'disabled addon collected AH state')
B.settings.enabled=true;U:SyncEnabled();assert(M.open and U.button:IsShown(),'enabling with AH open did not recover')
TEST.clock=TEST.clock+901;M.db.lastRequestAt=nil;M.requestAt=nil
C_AuctionHouse.GetNumReplicateItems=function() return 501 end
U:Refresh();U.button.scripts.OnClick();M:Event('REPLICATE_ITEM_LIST_UPDATE')
assert(M.job and M.job.index==250 and U.title.text:find('49 %%'),'chunk progress not visible')
local continue=TEST.timers[#TEST.timers];AuctionHouseFrame:Hide();AuctionHouseFrame.scripts.OnHide();continue()
assert(not M.job and M.db.count==1,'closing during chunks published a partial scan')
AuctionHouseFrame:Show();AuctionHouseFrame.scripts.OnShow();TEST.clock=TEST.clock+901
C_AuctionHouse.ReplicateItems=function() error('test refusal') end
U:Refresh();U.button.scripts.OnClick()
assert(not M.pending and U.status.text:find('refusé') and U.button.enabled,'refusal retained a fake cooldown')
C_AuctionHouse=nil;U:Refresh();assert(not U.button.enabled and U.status.text:find('indisponible'),'missing API not explained')
AuctionHouseFrame:Hide();AuctionHouseFrame.scripts.OnHide()
print('PASS auction UI: lazy load, existing frame, hooks, single click, result, persistent countdown, combat, throttle, timeout, close, re-enable, chunks, refusal and missing API')
