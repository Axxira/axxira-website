-- ForeverPilot Brain. GPL-3.0-or-later. Uses Ding's public data APIs.
local B = { version = "1.10.1", modules = {}, dirty = true }
_G.ForeverPilotLeveling = B
-- The CurseForge packager changes this one marker. RaidPilot always remains free.
B.distribution = "raidpilot-test"
B.subscriptionURL = "https://foreverpilot-subscriptions.tyronixstudio.workers.dev"
function B.HasPremium(feature)
    if B.distribution=="raidpilot-test" then return true end
    local a=_G.ForeverPilotAccess
    if type(a)~="table" or a.environment~="live" or type(a.expires)~="number" or not(a.expires>time()) then return false end
    return feature=="class" and a.classGuidance==true or feature~="class" and a.fastXP==true
end
function B.List(v) if type(v)=="table" then return v elseif type(v)=="number" then return {v} end return {} end
function B.Copy(v)
    if type(v)~="table" then return v end
    local t={} for k,x in pairs(v) do t[k]=B.Copy(x) end return t
end
function B.Call(fn, ...)
    if type(fn)~="function" then return nil end
    local ok,a,b,c,d=pcall(fn, ...) if ok then return B.Public(a),B.Public(b),B.Public(c),B.Public(d) end
end
function B.Public(value)
    -- Forever may return protected values even outside combat. Unknown state
    -- must remain unknown; attempting arithmetic on it disables the addon.
    if type(issecretvalue)=="function" then
        local ok,secret=pcall(issecretvalue,value)
        if not ok or secret then return nil end
    end
    return value
end
function B.Now() return GetTime() end
function B.Print(s) if DEFAULT_CHAT_FRAME then DEFAULT_CHAT_FRAME:AddMessage("|cff48d7e8ForeversPilot FastXp|r "..s) end end
function B.Trace(kind, message)
    if not B.char then return end
    local t=B.char.trace; t[#t+1]={at=time(),kind=kind,message=tostring(message)}
    if #t>80 then table.remove(t,1) end
end
function B.InitDB()
    B.InitLocale()
    ForeverPilotLevelingDB = type(ForeverPilotLevelingDB)=="table" and ForeverPilotLevelingDB or {}
    B.db=ForeverPilotLevelingDB
    B.db.brain=B.db.brain or {characters={},shown=true,enabled=true}
    B.settings=B.db.brain; B.settings.characters=B.settings.characters or {}
    if B.settings.enabled==nil then B.settings.enabled=true end
    if B.settings.shown==nil then B.settings.shown=true end
    if B.settings.language==nil then B.settings.language=B.language or "fr" end
    -- Forever permits characters with the same first name on the same realm.
    -- WoW's per-character saved-variable scope is the identity boundary, even
    -- when UnitGUID is absent/protected. Never reuse the legacy name-keyed
    -- profiles: their history may already contain several different people.
    -- Keep settings.characters untouched as an archive of that old history.
    ForeverPilotLevelingCharDB = type(ForeverPilotLevelingCharDB)=="table" and ForeverPilotLevelingCharDB or {}
    B.char=ForeverPilotLevelingCharDB
    for _,k in ipairs({"completed","unavailable","learning","trace","deferred","skipped","flightDiscoveries"}) do
        if type(B.char[k])~="table" then B.char[k]={} end
    end
    B.char.storage="character";B.char.schema=1
    B.char.guid=B.Call(UnitGUID,"player") or B.char.guid -- diagnostic only, never a lookup key
    B.char.createdAt=B.char.createdAt or time()
    if B.Knowledge then B.Knowledge:Init() end
    B.db.loadedVersion=B.version; B.db.loadedAt=time()
end
