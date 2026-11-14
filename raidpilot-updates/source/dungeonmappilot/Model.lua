local A = DungeonMapPilot

-- Each tile is cropped at the true picture edge, not stretched to its padded size.
function A.TileLayout(width, height, tileWidth, tileHeight, textures)
    width, height, tileWidth, tileHeight = A.Number(width), A.Number(height), A.Number(tileWidth), A.Number(tileHeight)
    if not width or not height or not tileWidth or not tileHeight or width <= 0 or height <= 0 or tileWidth <= 0 or tileHeight <= 0 then return nil end
    local cols, rows = math.ceil(width / tileWidth), math.ceil(height / tileHeight)
    if cols * rows > 256 or type(textures) ~= "table" then return nil end
    local layout = {}
    for row = 0, rows - 1 do
        for col = 0, cols - 1 do
            local file = A.ID(textures[row * cols + col + 1])
            if not file then return nil end
            local w, h = math.min(tileWidth, width - col * tileWidth), math.min(tileHeight, height - row * tileHeight)
            layout[#layout + 1] = { file = file, x = col * tileWidth / width, y = row * tileHeight / height,
                w = w / width, h = h / height, u = w / tileWidth, v = h / tileHeight }
        end
    end
    return layout
end

function A.NativeFloor(id)
    if not A.ID(id) or not C_Map then return nil end
    local info = A.Call(C_Map.GetMapInfo, id)
    local dungeonType = Enum and Enum.UIMapType and Enum.UIMapType.Dungeon or 4
    if type(info) ~= "table" or A.Number(info.mapType) ~= dungeonType then return nil end
    local artID = A.Call(C_Map.GetMapArtID, id)
    local cache = A.nativeCache[id]
    if cache and cache.artID == artID then return cache end
    local layers = A.Call(C_Map.GetMapArtLayers, id)
    local layer = type(layers) == "table" and A.Public(layers[1])
    if type(layer) ~= "table" then return nil end
    local files = A.Call(C_Map.GetMapArtLayerTextures, id, 1)
    local tiles = A.TileLayout(layer.layerWidth, layer.layerHeight, layer.tileWidth, layer.tileHeight, files)
    if not tiles then return nil end
    local floor = { key = "native:" .. id, mapID = id, name = A.Text(info.name), tiles = tiles,
        aspect = layer.layerWidth / layer.layerHeight, artID = artID, source = "native" }
    A.nativeCache[id] = floor
    return floor
end

function A.NativeFloors(id)
    local current = A.NativeFloor(id)
    if not current then return nil end
    local group = A.Call(C_Map.GetMapGroupID, id)
    local members = group and A.Call(C_Map.GetMapGroupMembersInfo, group)
    local floors, seen = {}, {}
    if type(members) == "table" then
        for _, raw in ipairs(members) do
            local member = A.Public(raw)
            if type(member) == "table" then
                local floor = A.NativeFloor(A.ID(member.mapID))
                if floor and not seen[floor.mapID] then
                    floor.order = A.Number(member.relativeHeightIndex) or 0
                    floors[#floors + 1], seen[floor.mapID] = floor, true
                end
            end
        end
    end
    if not seen[id] then floors[#floors + 1] = current end
    table.sort(floors, function(a, b)
        if (a.order or 0) == (b.order or 0) then return a.mapID < b.mapID end
        return (a.order or 0) < (b.order or 0)
    end)
    local selected = 1
    for i, floor in ipairs(floors) do if floor.mapID == id then selected = i end end
    return floors, selected
end

function A.ReferenceContext(id)
    local data = A.ReferenceMaps[id]
    if not data then return nil end
    local floors = {}
    for i, ref in ipairs(data) do
        floors[i] = { key = "ref:" .. id .. ":" .. i, source = "reference", aspect = 1002 / 668,
            name = ref.name, index = i, instanceID = id, tiles = A.TileLayout(1002, 668, 256, 256, ref.tiles) }
    end
    local saved = A.Number(A.db.floors[id]) or 1
    return { instanceID = id, name = A.CatalogName(id), source = "reference", floors = floors,
        index = A.Clamp(math.floor(saved), 1, #floors), raid = A.catalog[id] and A.catalog[id].raid }
end

function A.GuideNameMatches(id,value)
    local data=A.customGuideMeta and A.customGuideMeta[id]
    if not data or type(value)~="string" then return false end
    local actual=value:lower():gsub("[%s%p]","")
    local en=data.en:lower():gsub("[%s%p]","")
    local fr=data.fr:lower():gsub("[%s%p]","")
    return actual==en or actual==fr
end
function A.GuideContext(id,liveName)
    local data=A.customGuideMeta and A.customGuideMeta[id]
    if not data then return nil end
    if liveName and not A.GuideNameMatches(id,liveName) then return nil end
    return {instanceID=id,name=A.CatalogName(id),source="guide-only",
       floors={},index=1,raid=false,fromLive=liveName~=nil}
end

function A.CurrentContext()
    local name, kind, _, _, _, _, _, instanceID = A.Call(GetInstanceInfo)
    if kind ~= "party" and kind ~= "raid" then return nil end
    instanceID = A.ID(instanceID)
    if not instanceID then return nil end
    if A.customGuideMeta and A.customGuideMeta[instanceID] and
        not A.GuideNameMatches(instanceID,name) then
        return {instanceID=instanceID,name=A.Text(name),floors={},source="unavailable",
            index=1,raid=kind=="raid"}
    end
    local best = C_Map and A.Call(C_Map.GetBestMapForUnit, "player")
    local floors, index = A.NativeFloors(best)
    if floors then return { instanceID = instanceID, name = A.Text(name), source = "native", floors = floors, index = index, raid = kind == "raid" } end
    return A.ReferenceContext(instanceID) or A.GuideContext(instanceID,name) or
        { instanceID = instanceID, name = A.Text(name), floors = {}, source = "unavailable", raid = kind == "raid" }
end

function A.FloorName(floor, index)
    if floor.source == "reference" then
        local key = "floor." .. floor.instanceID .. "." .. floor.index
        if A.HasText(key) then return A.L(key) end
    end
    return floor.name or A.L("floor", index or floor.index or 1)
end

function A.Position(floor, unit)
    -- A reference illustration has no calibrated coordinate transform. Never put GPS on it.
    if not floor or floor.source ~= "native" or not floor.mapID or not C_Map then return nil end
    if A.Call(C_Map.GetBestMapForUnit, unit or "player") ~= floor.mapID then return nil end
    local pos = A.Call(C_Map.GetPlayerMapPosition, floor.mapID, unit or "player")
    if not pos then return nil end
    local x, y = A.Call(function() return pos:GetXY() end)
    x, y = A.Number(x), A.Number(y)
    if not x or not y or x < 0 or y < 0 or x > 1 or y > 1 or (x == 0 and y == 0) then return nil end
    return x, y
end

function A.Status(context, floor, here)
    if not floor then return "unsupported" end
    if not here or here.instanceID ~= context.instanceID then return "preview" end
    if floor.source ~= "native" then return "noPosition" end
    if not here.floors[here.index] or here.floors[here.index].mapID ~= floor.mapID then return "otherFloor" end
    return A.Position(floor) and "live" or "noPosition"
end

function A.SafeLabel(text)
    text = (A.Text(text) or ""):gsub("|", ""):gsub("[%c]", " ")
    local chars = {}
    for ch in text:gmatch("[%z\1-\127\194-\244][\128-\191]*") do
        if #chars == 60 then break end
        chars[#chars + 1] = ch
    end
    return (table.concat(chars):gsub("^%s+", ""):gsub("%s+$", ""))
end

function A.Pins(key)
    if type(key) ~= "string" then return {} end
    local data = A.db.pins[key]
    if type(data) ~= "table" then data = {}; A.db.pins[key] = data end
    -- SavedVariables may be hand-edited or from a previous version.
    for i = #data, 1, -1 do
        local pin = data[i]
        if type(pin) ~= "table" or not A.Number(pin.x) or not A.Number(pin.y) or pin.x < 0 or pin.x > 1 or pin.y < 0 or pin.y > 1 or i > 40 then table.remove(data, i)
        else pin.label = A.SafeLabel(pin.label) end
    end
    return data
end

function A.SavePin(key, x, y, label, index)
    if type(key) ~= "string" or key == "" then return false end
    x, y = A.Number(x), A.Number(y)
    if not x or not y or x < 0 or x > 1 or y < 0 or y > 1 then return false end
    local pins = A.Pins(key)
    if index and not pins[index] then return false end
    if not index and #pins >= 40 then return false end
    label = A.SafeLabel(label)
    pins[index or #pins + 1] = { x = x, y = y, label = label ~= "" and label or A.L("pin") }
    return true
end

function A.DeletePin(key, index)
    local pins = A.Pins(key)
    if pins[index] then table.remove(pins, index); return true end
    return false
end

-- Viewport maths stays independent of WoW frames for zoom/pan regression tests.
function A.PanBounds(view, x, y)
    return A.Clamp(x, 0, math.max(0, view.width * view.zoom - view.width)),
        A.Clamp(y, 0, math.max(0, view.height * view.zoom - view.height))
end

function A.ZoomAt(view, zoom, cursorX, cursorY)
    zoom = A.Clamp(zoom, 1, 3)
    local x = (view.panX + cursorX) / view.zoom * zoom - cursorX
    local y = (view.panY + cursorY) / view.zoom * zoom - cursorY
    view.zoom = zoom
    view.panX, view.panY = A.PanBounds(view, x, y)
end

function A.CursorToMap(view, x, y)
    if x < 0 or y < 0 or x > view.width or y > view.height then return nil end
    return (x + view.panX) / (view.width * view.zoom), (y + view.panY) / (view.height * view.zoom)
end
