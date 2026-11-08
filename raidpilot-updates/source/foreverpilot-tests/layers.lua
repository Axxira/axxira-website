local B=ForeverPilotLeveling
local L,U=B.Layers,B.LayerUI
local count=0
local function check(v,m) count=count+1;assert(v,m) end
local sent,go,confirmed,drafts,leaves,options=0,0,0,{},0,0
local group,dead,instance,locked,loaded,throwGo=false,false,false,false,true,false
function UnitAffectingCombat() return TEST.combat end
function UnitIsDeadOrGhost() return dead end
function IsInGroup() return group end
function IsInInstance() return instance end
function LeaveChannelByName(name) assert(name=="ForeverPilotLayers");leaves=leaves+1 end
function GetChannelList() return 1,"Trade",false,3,"General",false end
function GetRealZoneText() return "The Barrens" end
function StaticPopup_Visible() return false end
C_AddOns={IsAddOnLoaded=function(name) assert(name=="ChangeLayer");return loaded end}
C_ChatInfo={InChatMessagingLockdown=function() return locked end,
 SendAddonMessage=function() sent=sent+1 end,SendChatMessage=function() sent=sent+1 end,
 GetChannelInfoFromIdentifier=function(id) return {zoneChannelID=id==3 and 1 or 2} end}
C_PlayerInteractionManager={ShardTransferConfirm=function() confirmed=confirmed+1 end}
ChatFrameUtil={OpenChat=function(text) drafts[#drafts+1]=text end}
function ChangeLayer_Go() if throwGo then error("provider failure") end;go=go+1 end
SlashCmdList={CHANGELAYER=function(cmd) assert(cmd=="options");options=options+1 end}
B.char={layerHelper=true};B.settings={enabled=true};B.UI={frame=CreateFrame("Frame")}
L:Init()
check(leaves==1 and B.char.layerHelper==false,"retire private channel")
check(go==0 and sent==0,"silent init")
L:Init();check(leaves==1,"idempotent init")
check(not L:Pending(),"false popup not pending")
U.button.scripts.OnClick();check(U.frame:IsShown() and go==0,"opening panel is silent")
U.close.scripts.OnClick();check(not U.frame:IsShown(),"close panel")
check(U.shelf.w==344 and U.shelf.h==30,"compact shelf")
local function reset()
 TEST.combat=false;dead=false;group=false;instance=false;locked=false;loaded=true;throwGo=false
 L.clickedAt=nil;L.pending=nil;L.transferDeadline=nil;L.state="idle";B.settings.enabled=true
end
reset();U.primary.scripts.OnClick();check(go==1 and L.state=="provider","button delegates to original provider")
U.primary.scripts.OnClick();check(go==1,"double click ignored")
TEST.clock=TEST.clock+1;U.primary.scripts.OnClick();check(go==2,"next click delegates provider cancellation")
check(L.state~="success","provider invocation does not claim transfer")
U.helper.scripts.OnClick();check(options==1,"opens provider settings")
loaded=false;L:Start();check(L.state=="missing" and go==2,"disabled provider rejected despite stale global")
loaded=true;local saved=ChangeLayer_Go;ChangeLayer_Go=nil;L:Start();check(L.state=="missing","missing provider")
ChangeLayer_Go=saved
local before=go
reset();TEST.combat=true;L:Start();check(L.state=="blocked" and go==before,"combat blocks request")
L:Options();check(options==1,"combat blocks settings")
reset();dead=true;L:Start();check(L.state=="blocked" and go==before,"dead blocked")
reset();instance=true;L:Start();check(L.state=="blocked" and go==before,"instance blocked")
reset();group=true;L:Start();check(L.state=="blocked" and go==before and group,"no implicit leave and retry")
reset();locked=true;L:Start();check(L.state=="blocked" and go==before,"chat lockdown")
reset();B.settings.enabled=false;L:Start();check(L.state=="blocked" and go==before,"disabled guidance")
reset();throwGo=true;L:Start();check(L.state=="error" and B.settings.enabled and L.lastError:find("provider failure"),"provider failure isolated")
reset();L:Confirm();check(confirmed==0,"no arbitrary solo transfer")
L:Event("SHARD_TRANSFER_IMMINENT");check(U.primary.label.text=="Changer maintenant","native pending action")
TEST.combat=true;L:Confirm();check(confirmed==0,"combat prevents confirmation")
TEST.combat=false;group=true;L:Confirm();check(confirmed==1 and L.state=="transfer" and group,"explicit native transfer while grouped")
TEST.clock=TEST.clock+31;L:Tick();check(L.state=="unconfirmed" and group,"timeout preserves group")
L:Event("SHARD_TRANSFER");check(L.state=="success" and not L:Pending(),"server confirms transfer")
L:Event("SHARD_TRANSFER_IMMINENT");L:Event("SHARD_TRANSFER_CANCELLED")
check(not L:Pending() and L.state=="idle","server cancellation")
function StaticPopup_Visible(which) if which=="SHARD_TRANSFER_IMMEDIATE" then return 1 end end
check(L:Pending(),"native popup recovered after reload")
function StaticPopup_Visible() return false end
reset();loaded=false;L:DraftGeneral()
check(#drafts==1 and drafts[1]=="/3 Could someone invite me to change layers in The Barrens? Thanks!" and sent==0,"English draft uses actual General index without provider")
function GetChannelList() return 1,"Trade",false end
L:DraftGeneral();check(#drafts==1 and L.state=="unavailable","missing General never falls back to wrong channel")
reset();U.frame:Show();B.settings.enabled=false;L:SyncEnabled();check(not U.frame:IsShown(),"disabled controls hidden")
reset();L:Safe(function() error("simulated failure") end)
check(L.state=="error" and B.settings.enabled,"adapter error preserves guidance")
check(sent==0,"adapter never sends chat or private protocol")
print("PASS: "..count.." ChangeLayer adapter and UI checks")
