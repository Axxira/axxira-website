-- Review local evidence without abandoning a quest or erasing unrelated history.
local B=ForeverPilotLeveling
local U={mode="skipped",page=1};B.RecoveryUI=U
local function finite(v) return type(v)=="number" and v==v and v>-math.huge and v<math.huge end
local function questID(v) return finite(v) and v>=1 and v<=2147483647 and v%1==0 end
local reasons={reportedUnavailable="REC_REPORTED",pickupUnconfirmed="REC_UNCONFIRMED",notOffered="REC_NOT_OFFERED"}
function U:List(mode,now)
    local out={};now=finite(now) and now or 0
    local source=B.char and B.char[mode]
    if (mode~="skipped" and mode~="unavailable") or type(source)~="table" then return out end
    for id,record in pairs(source) do
        if questID(id) and type(record)=="table" and (mode=="skipped" or not finite(record.expires) or record.expires>now) then
            local node=B.Graph and B.Call(B.Graph.Node,B.Graph,id)
            local title=type(record.name)=="string" and record.name or type(node)=="table" and node.name
            title=type(title)=="string" and title:gsub("|",""):gsub("[%z\1-\31\127]"," ") or ("#"..id)
            out[#out+1]={id=id,name=title,at=finite(record.at) and record.at or 0,
                reason=mode=="skipped" and "REC_SKIPPED_REASON" or reasons[record.reason] or "REC_OTHER_REASON",
                remaining=finite(record.expires) and math.max(0,math.ceil((record.expires-now)/60)) or nil}
        end
    end
    table.sort(out,function(a,b) if a.at~=b.at then return a.at>b.at end;return a.id<b.id end)
    return out
end
function U:Restore(mode,id)
    if not questID(id) or (mode~="skipped" and mode~="unavailable") or not B.char then return false end
    if B.Call(InCombatLockdown)==true then return false end
    local source=B.char[mode]
    if type(source)~="table" or type(source[id])~="table" then return false end
    if mode=="skipped" then
        if not B.Recovery:Resume(id) then return false end
    else
        source[id]=nil
        B.Recovery:Invalidate(id);B.Recovery:Replan()
        B.Trace("recheck",id)
    end
    if B.Guard and B.Update then B.Guard(B.Update) end
    self:Render()
    return true
end
local function text(parent,w,h,size)
    local f=parent:CreateFontString(nil,"OVERLAY","GameFontHighlight")
    f:SetSize(w,h);f:SetJustifyH("LEFT");f:SetJustifyV("TOP");f:SetWordWrap(true)
    B.SetUIFont(f,size or 12);return f
end
local function button(parent,width,callback)
    local f=CreateFrame("Button",nil,parent,"UIPanelButtonTemplate")
    f:SetSize(width,28);f:SetScript("OnClick",callback);return f
end
function U:Create()
    if self.frame then return end
    local f=CreateFrame("Frame","ForeverPilotRecoveryFrame",UIParent,"BackdropTemplate")
    self.frame=f;self.rows={}
    f:SetSize(850,620);f:SetPoint("CENTER");f:SetFrameStrata("DIALOG");f:SetClampedToScreen(true)
    f:SetBackdrop({bgFile="Interface\\Buttons\\WHITE8X8",edgeFile="Interface\\Buttons\\WHITE8X8",edgeSize=1})
    f:SetBackdropColor(.04,.06,.09,.99);f:SetBackdropBorderColor(.23,.29,.36,1)
    f:EnableMouse(true);f:SetMovable(true);f:RegisterForDrag("LeftButton")
    f:SetScript("OnDragStart",function(x) x:StartMoving() end);f:SetScript("OnDragStop",function(x) x:StopMovingOrSizing() end)
    self.title=text(f,760,30,20);self.title:SetPoint("TOPLEFT",20,-18)
    local close=button(f,30,function() f:Hide() end);close:SetPoint("TOPRIGHT",-12,-12);close:SetText("×")
    self.skipped=button(f,250,function() self.mode="skipped";self.page=1;self:Render() end);self.skipped:SetPoint("TOPLEFT",20,-65)
    self.unavailable=button(f,250,function() self.mode="unavailable";self.page=1;self:Render() end);self.unavailable:SetPoint("TOPLEFT",282,-65)
    self.hint=text(f,810,62,12);self.hint:SetPoint("TOPLEFT",20,-108)
    for i=1,7 do
        local row=CreateFrame("Frame",nil,f);row:SetSize(810,47);row:SetPoint("TOPLEFT",20,-182-(i-1)*49)
        row.name=text(row,610,20,14);row.name:SetPoint("TOPLEFT",0,0);row.name:SetWordWrap(false)
        row.detail=text(row,610,22,11);row.detail:SetPoint("TOPLEFT",0,-24);row.detail:SetWordWrap(false)
        row.restore=button(row,180,function()
            if row.data then self:Restore(row.mode,row.data.id) end
        end);row.restore:SetPoint("RIGHT",0,0)
        self.rows[i]=row
    end
    self.empty=text(f,750,90,14);self.empty:SetPoint("TOPLEFT",40,-260)
    self.previous=button(f,90,function() self.page=math.max(1,self.page-1);self:Render() end);self.previous:SetPoint("BOTTOMLEFT",20,48);self.previous:SetText("‹")
    self.next=button(f,90,function() self.page=self.page+1;self:Render() end);self.next:SetPoint("BOTTOMLEFT",185,48);self.next:SetText("›")
    self.pageLabel=text(f,75,22,12);self.pageLabel:SetPoint("BOTTOMLEFT",110,48);self.pageLabel:SetJustifyH("CENTER")
    self.note=text(f,810,28,11);self.note:SetPoint("BOTTOMLEFT",20,10)
    if UISpecialFrames then UISpecialFrames[#UISpecialFrames+1]="ForeverPilotRecoveryFrame" end
    for _,event in ipairs({"QUEST_LOG_UPDATE","QUEST_ACCEPTED","QUEST_TURNED_IN","PLAYER_LEVEL_UP","PLAYER_REGEN_DISABLED","PLAYER_REGEN_ENABLED"}) do pcall(f.RegisterEvent,f,event) end
    f:SetScript("OnEvent",function() if f:IsShown() then self:Render() end end)
    f:SetScript("OnUpdate",function(_,dt)
        self.elapsed=(self.elapsed or 0)+dt
        if self.elapsed>=5 then self.elapsed=0;self:Render() end
    end)
    f:Hide()
end
function U:Render()
    if not self.frame or not self.frame:IsShown() then return end
    local now=B.Call(time) or 0
    local skipped,unavailable=self:List("skipped",now),self:List("unavailable",now)
    local list=self.mode=="skipped" and skipped or unavailable
    self.title:SetText(B.T("REC_TITLE"))
    self.skipped:SetText(B.T("REC_SKIPPED").." ("..#skipped..")")
    self.unavailable:SetText(B.T("REC_UNAVAILABLE").." ("..#unavailable..")")
    self.hint:SetText(B.T("REC_HINT"))
    local pages=math.max(1,math.ceil(#list/7));self.page=math.max(1,math.min(pages,self.page))
    local combat=B.Call(InCombatLockdown)==true
    for i,row in ipairs(self.rows) do
        local data=list[(self.page-1)*7+i];row.data=data;row.mode=self.mode;row:SetShown(data~=nil)
        if data then
            row.name:SetText(data.name.."  #"..data.id)
            row.detail:SetText(B.T(data.reason)..(data.remaining and (" · "..string.format(B.T("REC_MINUTES"),data.remaining)) or ""))
            row.restore:SetText(B.T(self.mode=="skipped" and "REC_RESTORE" or "REC_RECHECK"));row.restore:SetEnabled(not combat)
        end
    end
    self.empty:SetText(#list==0 and B.T("REC_EMPTY") or "");self.empty:SetShown(#list==0)
    self.pageLabel:SetText(self.page.." / "..pages);self.previous:SetEnabled(self.page>1);self.next:SetEnabled(self.page<pages)
    self.note:SetText(B.T(combat and "REC_COMBAT" or "REC_NOTE"))
end
function U:Open()
    self:Create();self.frame:SetScale(math.max(.5,math.min(1,(UIParent:GetWidth()-30)/850,(UIParent:GetHeight()-30)/620)))
    self.frame:Show();self:Render()
end
