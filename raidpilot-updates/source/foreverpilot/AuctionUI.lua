-- Compact controls attached to the real auction window. No trade actions.
local B=ForeverPilotLeveling
local U={};B.AuctionUI=U
local function label(parent,size)
    local t=parent:CreateFontString(nil,"OVERLAY")
    t:SetFont(STANDARD_TEXT_FONT,size,"");t:SetAllPoints(parent)
    t:SetJustifyH("LEFT");t:SetJustifyV("MIDDLE");t:SetWordWrap(false)
    return t
end
function U:SyncEnabled()
    if B.settings and B.settings.enabled and AuctionHouseFrame and AuctionHouseFrame:IsShown() then
        B.Market:Event("AUCTION_HOUSE_SHOW")
    end
    self:Refresh()
end
function U:Refresh()
    if not self.button then return end
    local enabled=B.settings and B.settings.enabled
    self.button:SetShown(enabled==true);self.footer:SetShown(enabled==true)
    if not enabled then return end
    local M=B.Market;local ready,title,reason=M:Availability()
    self.button:SetEnabled(ready);self.title:SetText(title)
    self.title:SetTextColor(ready and .95 or .64,ready and .94 or .71,ready and .81 or .74)
    self.background:SetColorTexture(ready and .10 or .08,ready and .25 or .14,ready and .27 or .16,.97)
    local message=M.status or "Prêt · lance un scan pour mémoriser les prix."
    if M.pending or M.job then message=M.status or reason
    elseif not ready then
        local count=M.db and M.db.count or 0
        local seconds=M:CooldownRemaining()
        if seconds>0 then
            message=count.." variantes mémorisées · "..string.format("prochain scan %d:%02d",math.floor(seconds/60),seconds%60)
            if M.db and M.db.lastScanPartial then message=message.." · partiel" end
            -- Keep refusals/timeouts visible even while the server cooldown is active.
            if M.status and (M.status:find("répondu") or M.status:find("refusé") or M.status:find("vide")) then message=M.status end
        else message=reason or message end
    end
    self.status:SetText("ForeverPilot · "..message)
    self.status:SetTextColor(.65,.87,.85)
end
function U:Attach()
    local parent=AuctionHouseFrame
    if not parent or self.button then return end
    -- The title bar and empty footer are separate from search, rows and sale controls.
    local f=CreateFrame("Button","ForeverPilotAuctionScanButton",parent);self.button=f
    -- Forever's title decoration renders above MEDIUM children, even at level 21.
    -- HIGH stays below tooltips/dialogs; the parent still controls visibility.
    f:SetFrameStrata("HIGH");f:SetFrameLevel(parent:GetFrameLevel()+20)
    f:SetSize(154,22);f:SetPoint("TOPRIGHT",parent,"TOPRIGHT",-36,-3)
    local bg=f:CreateTexture(nil,"BACKGROUND");bg:SetAllPoints(f);self.background=bg
    self.title=label(f,11);self.title:SetJustifyH("CENTER")
    local footer=CreateFrame("Frame","ForeverPilotAuctionScanStatus",parent);self.footer=footer
    footer:SetPoint("BOTTOMLEFT",parent,"BOTTOMLEFT",180,6)
    footer:SetPoint("BOTTOMRIGHT",parent,"BOTTOMRIGHT",-12,6);footer:SetHeight(20)
    self.status=label(footer,10)
    f:SetScript("OnClick",function()
        local ok,err=pcall(B.Market.Scan,B.Market)
        if not ok then self.lastError=tostring(err);B.Market:Cancel("Scan indisponible : voir /fpl debug.") end
        self:Refresh()
    end)
    f:SetScript("OnEnter",function()
        GameTooltip:SetOwner(f,"ANCHOR_BOTTOMRIGHT");GameTooltip:SetText("ForeverPilot · Scan HDV")
        GameTooltip:AddLine(B.Market.status or "Mémorise les prix des annonces actuellement proposées.",.8,.9,.9,true)
        GameTooltip:AddLine("Laisse l'HDV ouvert jusqu'à la fin. Les prix se consultent en survolant les objets.",.8,.9,.9,true)
        GameTooltip:Show()
    end)
    f:SetScript("OnLeave",function() GameTooltip:Hide() end)
    local elapsed=0
    f:SetScript("OnUpdate",function(_,dt)
        elapsed=elapsed+dt;if elapsed<1 then return end;elapsed=0;self:Refresh()
    end)
    parent:HookScript("OnShow",function()
        B.Market:Event("AUCTION_HOUSE_SHOW");self:Refresh()
    end)
    parent:HookScript("OnHide",function() B.Market:Event("AUCTION_HOUSE_CLOSED") end)
    if parent:IsShown() then B.Market:Event("AUCTION_HOUSE_SHOW") end
    self:Refresh()
end
function U:Init()
    if self.events then return end
    local events=CreateFrame("Frame");self.events=events
    events:RegisterEvent("ADDON_LOADED");events:RegisterEvent("AUCTION_HOUSE_SHOW")
    events:SetScript("OnEvent",function()
        self:Attach()
        -- The Blizzard load-on-demand frame may be created after our event handler.
        if not self.button then C_Timer.After(0,function() self:Attach() end) end
    end)
    self:Attach()
end
