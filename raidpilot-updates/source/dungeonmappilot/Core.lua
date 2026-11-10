DungeonMapPilot = { version = "0.3.0", nativeCache = {} }
local A = DungeonMapPilot

function A.Public(value)
    if type(issecretvalue) == "function" then
        local ok, secret = pcall(issecretvalue, value)
        if not ok or secret then return nil end
    end
    return value
end

function A.Call(fn, ...)
    if type(fn) ~= "function" then return nil end
    local ok, a, b, c, d, e, f, g, h = pcall(fn, ...)
    if not ok then return nil end
    return A.Public(a), A.Public(b), A.Public(c), A.Public(d), A.Public(e), A.Public(f), A.Public(g), A.Public(h)
end

function A.Number(v)
    v = A.Public(v)
    if type(v) == "number" and v == v and v > -math.huge and v < math.huge then return v end
end

function A.ID(v)
    v = A.Number(v)
    if v and v > 0 and v == math.floor(v) then return v end
end

function A.Text(v)
    v = A.Public(v)
    if type(v) == "string" then return v end
end

function A.Clamp(v, lo, hi) return math.max(lo, math.min(hi, v)) end

function A.InitDB()
    local db = type(DungeonMapPilotCharDB) == "table" and DungeonMapPilotCharDB or {}
    DungeonMapPilotCharDB, A.db = db, db
    db.pins = type(db.pins) == "table" and db.pins or {}
    db.floors = type(db.floors) == "table" and db.floors or {}
    db.routes = type(db.routes) == "table" and db.routes or {}
    db.routeMode = type(db.routeMode) == "table" and db.routeMode or {}
    db.guideHidden = db.guideHidden == true
    if db.language ~= "fr" and db.language ~= "en" then db.language = "auto" end
    db.schema = 2
end

function A.Language()
    local value = A.db and A.db.language
    if value == "fr" or value == "en" then return value end
    return A.Call(GetLocale) == "frFR" and "fr" or "en"
end

function A.L(key, ...)
    local value = (A.locales[A.Language()] or {})[key] or A.locales.en[key] or key
    if select("#", ...) > 0 then return string.format(value, ...) end
    return value
end

function A.Print(key)
    local msg = "|cffE5BD73DungeonMapPilot|r · " .. A.L(key)
    if DEFAULT_CHAT_FRAME then DEFAULT_CHAT_FRAME:AddMessage(msg) end
end
