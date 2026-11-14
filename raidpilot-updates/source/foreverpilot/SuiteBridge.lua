-- Optional PilotCenter integration. Reading never buys, sells, learns or advances.
local registry = _G.PilotSuiteBridge
if registry == nil then registry = {api=1, providers={}}; _G.PilotSuiteBridge=registry end
if type(registry)~="table" or registry.api~=1 or type(registry.providers)~="table" then return end
local function combat()
    if type(InCombatLockdown)~="function" then return true end
    local ok,value=pcall(InCombatLockdown)
    if not ok then return true end
    if type(issecretvalue)=="function" then
        local checked,hidden=pcall(issecretvalue,value)
        if not checked or hidden then return true end
    end
    return value~=false
end
local A = ForeverPilotLeveling
registry.providers.ForeverPilot_Leveling={api=1,version=A.version,
 Snapshot=function()
    if not A.settings or not A.char then return {ready=false} end
    local now=A.Call(time) or 0
    local blocked=A.RecoveryUI:List("unavailable",now)
    local skipped=A.RecoveryUI:List("skipped",now)
    local state=A.State.current
    local current=A.Planner.current
    return {ready=state~=nil,enabled=A.settings.enabled~=false,paused=A.settings.paused==true,
        access=true,premium=A.HasPremium(),edition=A.Mode(),errors=A.errors or 0,failed=A.failed==true,
        unavailable=#blocked,skipped=#skipped,questCount=state and state.count,questLimit=state and state.limit,
        sampledAt=state and state.now,quest=current and current.node and current.node.name,
        action=current and current.kind,confidence=current and current.confidence}
 end,
 Open=function(action)
    if combat() or not A.settings then return false end
    if action=="recovery" then A.RecoveryUI:Open()
    elseif action=="open" or action=="options" then
        if not A.UI.frame or action=="options" then A.Options:Toggle()
        else A.settings.shown=true;A.UI.frame:Show() end
    else return false end
    return true
 end}
