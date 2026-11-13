local A = DungeonMapPilot

function A.AttachWorldButton()
    if A.worldButton or not WorldMapFrame then return end
    local b = CreateFrame("Button", nil, WorldMapFrame, "UIPanelButtonTemplate")
    A.worldButton = b; b:SetSize(142, 23); b:SetPoint("BOTTOMRIGHT", WorldMapFrame, "TOPRIGHT", -48, 1)
    b:SetText(A.L("worldButton")); b:SetScript("OnClick", A.Toggle)
end

local events = CreateFrame("Frame")
events:RegisterEvent("ADDON_LOADED")
events:RegisterEvent("PLAYER_LOGIN")
events:RegisterEvent("PLAYER_ENTERING_WORLD")
events:RegisterEvent("ZONE_CHANGED_NEW_AREA")
events:SetScript("OnEvent", function(_, event, name)
    if event == "ADDON_LOADED" and name == "DungeonMapPilot" then
        A.InitDB()
        if A.SyncMinimap then A.SyncMinimap() end
        SLASH_DUNGEONMAPPILOT1 = "/dmp"
        SLASH_DUNGEONMAPPILOT2 = "/dungeonmappilot"
        SLASH_DUNGEONMAPPILOT3 = "/fpmap"
        SLASH_DUNGEONMAPPILOT4 = "/fpmaps"
        SlashCmdList.DUNGEONMAPPILOT = function(msg)
            if msg == "diagnostic" then
                local c = A.CurrentContext()
                A.db.diagnostic = { version = A.version, instanceID = c and c.instanceID,
                    source = c and c.source or "outside", nativeMapID = C_Map and A.Call(C_Map.GetBestMapForUnit, "player"),
                    floorCount = c and #c.floors or 0, position = c and c.floors[c.index or 1] and A.Position(c.floors[c.index or 1]) ~= nil or false }
                A.Print("diag")
                for k, v in pairs(A.db.diagnostic) do DEFAULT_CHAT_FRAME:AddMessage(k .. " : " .. tostring(v)) end
            else A.Toggle() end
        end
    elseif event == "ADDON_LOADED" and name == "Blizzard_WorldMap" then A.AttachWorldButton()
    elseif event == "PLAYER_LOGIN" then A.AttachWorldButton()
    elseif A.UI.frame and A.UI.frame:IsShown() then A.UI:RefreshCurrent() end
end)
