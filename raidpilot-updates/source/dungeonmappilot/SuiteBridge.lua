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
local A = DungeonMapPilot
registry.providers.DungeonMapPilot={api=1,version=A.version,
 Snapshot=function()
    if not A.db then return {ready=false} end
    local r=A.UI.route
    local s=r and A.db.routes[r.id]
    local done,skipped=0,0
    if r and type(s)=="table" and s.revision==r.revision then
        for _,step in ipairs(r.steps) do
            if type(s.done)=="table" and s.done[step.id]==true then done=done+1
            elseif type(s.skipped)=="table" and s.skipped[step.id]==true then skipped=skipped+1 end
        end
    end
    return {ready=true,selected=r~=nil,total=r and #r.steps or 0,done=done,skipped=skipped,
        personal=r and r.personal==true,visible=A.UI.frame and A.UI.frame:IsShown() or false}
 end,
 Open=function(action)
    if combat() or not A.db or action~="open" then return false end
    if not A.UI.frame or not A.UI.frame:IsShown() then A.Toggle() end
    return true
 end}
