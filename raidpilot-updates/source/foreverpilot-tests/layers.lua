-- Exercise requests through the same events and button handlers as the client.
local B=ForeverPilotLeveling
local L,U=B.Layers,B.LayerUI
local count=0
local function check(v,message) count=count+1;assert(v,message) end
local sent,invites,confirmed,drafts,leaves={},{},0,{},0
local group,dead,instance,locked,guild,joined=false,false,false,false,false,true
local map,faction=1413,"Horde"
local result,throwInvite=0,false
function UnitName() return "Peko Puffpuffpass" end
function GetNormalizedRealmName() return "Forever" end
function UnitFactionGroup() return faction end
function UnitAffectingCombat() return TEST.combat end
function UnitIsDeadOrGhost() return dead end
function IsInGroup() return group end
function IsInInstance() return instance end
function IsInGuild() return guild end
function GetChannelName() return joined and 7 or 0 end
function JoinChannelByName() end
function LeaveChannelByName() leaves=leaves+1;joined=false end
function GetChannelList() return 1,"Trade",false,3,"General",false end
function GetRealZoneText() return "The Barrens" end
function StaticPopup_Visible() return false end
C_Map={GetBestMapForUnit=function() return map end}
C_ChatInfo={
 RegisterAddonMessagePrefix=function() return 0 end,
 InChatMessagingLockdown=function() return locked end,
 SendAddonMessage=function(prefix,text,channel,target) sent[#sent+1]={text=text,channel=channel,target=target};return result end,
 SendChatMessage=function(text,channel,language,target) sent[#sent+1]={text=text,channel=channel,target=target} end,
 GetChannelInfoFromIdentifier=function(id) return {zoneChannelID=id==3 and 1 or 2} end,
}
C_PartyInfo={CanInvite=function() return true end,InviteUnit=function(name) if throwInvite then error("restricted") end;invites[#invites+1]=name end}
C_PlayerInteractionManager={ShardTransferConfirm=function() confirmed=confirmed+1 end}
ChatFrameUtil={OpenChat=function(text) drafts[#drafts+1]=text end}
B.char={};B.settings={enabled=true};B.UI={frame=CreateFrame("Frame")}
L:Init()
check(L.registered and not B.char.layerHelper and #sent==0,"init must be silent and helper opt-in")
check(not L:Pending(),"false popup is not pending")
U.button.scripts.OnClick();check(U.frame:IsShown() and #sent==0,"opening controls must not send")
U.close.scripts.OnClick();check(not U.frame:IsShown(),"close controls")
check(U.shelf.w==344 and U.shelf.h==30 and U.button.w==135,"compact shelf")
local function reset()
 TEST.combat=false;dead=false;group=false;instance=false;locked=false;joined=true;guild=false
 map=1413;faction="Horde";result=0;L.nextSearch=nil;L.pending=nil;L.pendingInvite=nil;L.helperAt=nil;L.joinAttempted=nil;L.ownsChannel=nil
 B.settings.enabled=true;B.char.layerHelper=false;L:Stop();joined=true;sent={};invites={}
end
reset();U.primary.scripts.OnClick()
check(L.state=="search" and #sent==1 and sent[1].channel=="CHANNEL" and sent[1].target==7,"single visible request on button click")
local token=L.request.token
L:Receive("1|O|wrong|1413|Horde","WHISPER","Alice")
L:Receive("1|O|"..token.."|1420|Horde","WHISPER","Alice")
L:Receive("1|O|"..token.."|1413|Alliance","WHISPER","Alice")
L:Receive("1|O|"..token.."|1413|Horde","GUILD","Alice")
L:Receive("1|O|"..token.."|1413|Horde","WHISPER","Bad|name")
check(L.state=="search" and #sent==1,"invalid offers ignored")
L:Receive("1|O|"..token.."|1413|Horde","WHISPER","Alice Helper-Forever")
check(L.state=="invite" and #sent==2 and sent[2].target=="Alice Helper-Forever","one helper selected including Forever surnames")
L:Receive("1|O|"..token.."|1413|Horde","WHISPER","Bob")
check(#sent==2,"no invitation fanout")
group=true;L:Event("GROUP_ROSTER_UPDATE")
check(L.state=="transfer" and L.state~="success","joining a group does not prove transfer")
TEST.clock=TEST.clock+31;L:Tick();check(L.state=="unconfirmed" and group,"unconfirmed timeout preserves group")
L:Event("SHARD_TRANSFER");check(L.state=="success","server confirms transfer")
reset();L:Start();L:Stop();L:Receive("1|O|"..token.."|1413|Horde","WHISPER","Late")
check(L.state=="idle" and #sent==1,"late offer ignored after cancel")
L:Start();check(L.state=="cooldown" and #sent==1,"request throttle")
reset();L:Start();TEST.clock=TEST.clock+21;L:Tick();check(L.state=="timeout","no helper timeout")
reset();L:Start();TEST.combat=true;L:Event("PLAYER_REGEN_DISABLED");check(not L.request and L.state=="idle","combat cancels search")
reset();TEST.combat=true;L:Start();check(L.state=="blocked" and #sent==0,"no request in combat")
reset();dead=true;L:Start();check(L.state=="blocked" and #sent==0,"no request dead")
reset();instance=true;L:Start();check(L.state=="blocked" and #sent==0,"no request in instance")
reset();group=true;L:Start();check(L.state=="blocked" and #sent==0,"no automatic group leave")
reset();locked=true;L:Start();check(L.state=="blocked" and #sent==0,"chat lockdown")
reset();L:Start();map=1420;L:Tick();check(not L.request,"zone change cancels search")
reset();joined=false;L:Start();check(L.state=="joining" and #sent==0,"async join never sends without click")
joined=true;L:Tick();check(L.state=="idle" and #sent==0,"join completion still needs click")
L:Start();check(L.state=="search" and #sent==1,"click after join works")
L:Event("CHAT_MSG_CHANNEL_NOTICE","NOT_ALLOWED_IN_CHANNEL",nil,nil,nil,nil,nil,nil,nil,L.channel)
check(L.state=="unavailable","server channel rejection reported")
reset();L:Confirm();check(confirmed==0,"no arbitrary solo transfer")
L:Event("SHARD_TRANSFER_IMMINENT");check(U.primary.label.text=="Changer maintenant","pending server transfer changes action")
TEST.combat=true;L:Confirm();check(confirmed==0,"combat prevents confirmation")
TEST.combat=false;L:Confirm();check(confirmed==1 and L.state=="transfer","explicit native transfer request")
L:Event("SHARD_TRANSFER_CANCELLED");check(not L:Pending() and L.state=="idle","server cancellation clears pending")
function StaticPopup_Visible(which) if which=="SHARD_TRANSFER_IMMEDIATE" then return 1 end end
check(L:Pending(),"pending native popup recovered after reload")
function StaticPopup_Visible() end
reset();L:DraftGeneral();check(#drafts==1 and drafts[1]=="/3 Could someone invite me to change layers in The Barrens? Thanks!" and #sent==0,"General uses actual channel and prepares only an English draft")
reset();L:Receive("1|Q|111-1|1413|Horde","GUILD","Other Player")
check(#sent==0,"helper off by default")
L:SetHelper(true);L:Receive("1|Q|111-1|1413|Horde","GUILD","Other Player")
check(#sent==1 and sent[1].text:find("|O|",1,true),"opt-in helper offers")
L:Receive("1|P|111-1|1413|Horde","WHISPER","Spoofer")
check(#invites==0,"selected sender required")
L:Receive("1|P|111-1|1413|Horde","WHISPER","Other Player")
L:Receive("1|P|111-1|1413|Horde","WHISPER","Other Player")
check(#invites==1 and invites[1]=="Other Player","single invite and replay rejection")
reset();L:SetHelper(true);L:Event("CHAT_MSG_CHANNEL","ForeverPilot layer: 111-2|1413|Horde","Other",nil,nil,nil,nil,nil,nil,"WrongChannel")
check(#sent==0,"other channels ignored")
L:Event("CHAT_MSG_CHANNEL","ForeverPilot layer: 111-2|1413|Horde","Other",nil,nil,nil,nil,nil,nil,L.channel)
check(#sent==1,"own channel discovery")
L:SetHelper(false);L:Receive("1|P|111-2|1413|Horde","WHISPER","Other");check(#invites==0,"helper disable cancels offer")
reset();L:SetHelper(true);L:Receive("1|Q|111-3|1413|Horde","GUILD","Other")
TEST.clock=TEST.clock+13;L:Receive("1|P|111-3|1413|Horde","WHISPER","Other");check(#invites==0,"expired offer rejected")
reset();L:Start();B.settings.enabled=false;L:SyncEnabled();check(not L.request and not L:Busy() and not U.frame:IsShown(),"disable cancels assistance")
reset();L:Safe(function() error("simulated failure") end)
check(L.state=="error" and B.settings.enabled and L.lastError:find("simulated"),"layer fault does not disable guidance")
print("PASS: "..count.." layer lifecycle, protocol and UI checks")
