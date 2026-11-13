local A=DungeonMapPilot
function A.SyncMinimap()
    if not PilotMinimap then return end
    A.minimapButton=PilotMinimap:Register("DungeonMapPilot",{
        title="DungeonMapPilot",angle=295,icon="Interface\\Icons\\INV_Misc_Map02",Locale=A.Language,
        Settings=function()
            if not A.db then return nil end
            if type(A.db.pilotMinimap)~="table" then A.db.pilotMinimap={angle=295,minimap=true} end
            return A.db.pilotMinimap
        end,
        Click=function(mouse)
            if mouse=="RightButton" then
                if not A.UI.frame or not A.UI.frame:IsShown() then A.Toggle() end
                if A.UI.frame and A.UI.frame:IsShown() then A.UI:ChooseMap() end
            else A.Toggle() end
        end})
    return A.minimapButton
end
A.SyncMinimap()
