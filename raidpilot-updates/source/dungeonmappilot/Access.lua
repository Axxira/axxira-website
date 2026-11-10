local A = DungeonMapPilot
-- Local/RaidPilot builds always stay free. Public builds require a separate Maps entitlement.
A.distribution = "raidpilot-test"

function A.HasAccess()
    if A.distribution == "raidpilot-test" then return true end
    local license = A.Public(_G.DungeonMapPilotAccess)
    if type(license) ~= "table" then return false end
    local expiry = A.Number(license.expires)
    local now = A.Number(A.Call(time))
    return A.Text(license.environment) == "live" and A.Text(license.product) == "dungeon_maps"
        and A.Public(license.dungeonMaps) == true and expiry ~= nil and now ~= nil and expiry > now
end
