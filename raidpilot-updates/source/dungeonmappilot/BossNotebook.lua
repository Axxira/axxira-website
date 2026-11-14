-- DungeonMapPilot 0.5.3: personal notes and safe route resumption.
-- Notes are local per character, per route and per step; no gameplay automation.
local A=DungeonMapPilot
local MAX_BYTES=200
local MAX_SAVED=300
local HISTORY_LIMIT=12
local function identity(route,index)
    if type(route)~="table" or type(route.id)~="string" or
        #route.id<1 or #route.id>120 or type(route.steps)~="table" or
        #route.steps<1 or #route.steps>128 then return nil end
    if not A.ID(index) or index>#route.steps then return nil end
    local step=route.steps[index]
    if type(step)~="table" or type(step.id)~="string" or
        #step.id<1 or #step.id>120 or step.id:find("[%c|]") then return nil end
    return route.id,step.id
end
local function saved()
    if type(A.db)~="table" then return nil end
    if A.db.personalNotes==nil then A.db.personalNotes={} end
    if A.db.personalNoteHistory==nil then A.db.personalNoteHistory={} end
    if type(A.db.personalNotes)~="table" or type(A.db.personalNoteHistory)~="table" then return nil end
    return A.db.personalNotes,A.db.personalNoteHistory
end
function A.NoteGet(route,index)
    local id,key=identity(route,index)
    if not id or type(A.db)~="table" or type(A.db.personalNotes)~="table" then return nil end
    local all=A.db.personalNotes[id]
    if type(all)~="table" then return nil end
    local value=all[key]
    if type(value)~="string" or #value>MAX_BYTES or value:find("[%c|]") then return nil end
    return value~="" and value or nil
end
local function normalize(value)
    value=A.Public(value)
    if type(value)~="string" or #value>MAX_BYTES then return nil end
    value=value:gsub("[\r\n\t]"," "):gsub("%s+"," ")
    value=value:gsub("^%s+",""):gsub("%s+$","")
    if value:find("[%c|]") or #value>MAX_BYTES then return nil end
    return value
end
local function totalSaved(storage)
    local total=0;local seen=0
    for routeID,notes in pairs(storage) do
        seen=seen+1
        if seen>1000 or type(routeID)~="string" or type(notes)~="table" then return nil end
        for key,value in pairs(notes) do
            total=total+1
            if total>MAX_SAVED+1 or type(key)~="string" or type(value)~="string" then return nil end
        end
    end
    return total
end
function A.NoteSet(route,index,raw)
    if A.Call(InCombatLockdown)~=false then return false,"COMBAT" end
    local id,key=identity(route,index)
    local text=normalize(raw)
    if not id or not text then return false,"INVALID" end
    local notes,history=saved()
    if not notes then return false,"CORRUPT" end
    local count=totalSaved(notes)
    if not count then return false,"CORRUPT" end
    local branch=notes[id]
    if branch~=nil and type(branch)~="table" then return false,"CORRUPT" end
    local before=A.NoteGet(route,index)
    if (before or "")==text then return true end
    if text~="" and before==nil and count>=MAX_SAVED then return false,"LIMIT" end
    -- Bounded snapshots; no foreign notes are modified on failure.
    branch=branch or {}
    if text~="" then branch[key]=text else branch[key]=nil end
    if next(branch)==nil then notes[id]=nil else notes[id]=branch end
    history[#history+1]={route=id,key=key,before=before or "",after=text}
    while #history>HISTORY_LIMIT do table.remove(history,1) end
    return true
end
function A.NoteCanUndo(route,index)
    local id,key=identity(route,index)
    if not id or type(A.db)~="table" or type(A.db.personalNoteHistory)~="table" then return false end
    local h=A.db.personalNoteHistory[#A.db.personalNoteHistory]
    return type(h)=="table" and h.route==id and h.key==key and
        type(h.after)=="string" and (A.NoteGet(route,index) or "")==h.after
end
function A.NoteUndo(route,index)
    if A.Call(InCombatLockdown)~=false or not A.NoteCanUndo(route,index) then return false end
    local id,key=identity(route,index)
    local notes,history=saved()
    if not notes then return false end
    local h=history[#history]
    if type(h.before)~="string" or #h.before>MAX_BYTES then return false end
    local branch=notes[id] or {}
    if type(branch)~="table" then return false end
    branch[key]=h.before~="" and h.before or nil
    notes[id]=next(branch) and branch or nil
    table.remove(history)
    return true
end
function A.RouteNextReady(route)
    if type(route)~="table" or route.previewOnly or type(route.steps)~="table" then return nil end
    local state=A.RouteState(route)
    for i,step in ipairs(route.steps) do
        if not state.done[step.id] and not state.skipped[step.id] and
            #A.RouteMissing(route,step)==0 then return i end
    end
    return nil
end
