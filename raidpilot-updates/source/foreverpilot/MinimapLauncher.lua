local A=ForeverPilotLeveling
function A.Options:SyncMinimap()
    if not PilotMinimap then return end
    self.minimapButton=PilotMinimap:Register("ForeverPilot_Leveling",{
        title="ForeversPilot FastXp",angle=260,icon="Interface\\Icons\\Ability_Hunter_Pathfinding",
        Settings=function()
            if not A.char then return nil end
            if type(A.char.pilotMinimap)~="table" then A.char.pilotMinimap={angle=260,minimap=true} end
            return A.char.pilotMinimap
        end,
        Click=function(mouse)
            if not A.settings then return end
            if mouse=="RightButton" or not A.UI.frame then A.Options:Toggle()
            else A.settings.shown=not A.UI.frame:IsShown();A.UI.frame:SetShown(A.settings.shown) end
        end})
    return self.minimapButton
end
A.Options:SyncMinimap()
