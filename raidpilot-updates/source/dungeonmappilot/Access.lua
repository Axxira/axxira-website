local A = DungeonMapPilot
-- Subscription edition: the companion supplies DungeonMapPilotAccess from verified billing.
A.distribution = "raidpilot-test"
A.subscriptionURL = "https://dungeonmappilot-subscriptions.tyronixstudio.workers.dev"

function A.HasAccess()
    if A.distribution == "raidpilot-test" then return true end
    local license = A.Public(_G.DungeonMapPilotAccess)
    if type(license) ~= "table" then return false end
    local expiry = A.Number(license.expires)
    local now = A.Number(A.Call(time))
    return A.Text(license.environment) == "live" and A.Text(license.product) == "dungeon_maps"
        and A.Public(license.dungeonMaps) == true and expiry ~= nil and now ~= nil and expiry > now
end
