local A, T = DungeonMapPilot, TEST
T.events.ADDON_LOADED.scripts.OnEvent(nil, "ADDON_LOADED", "DungeonMapPilot")
check(A.db == DungeonMapPilotCharDB, "per-character storage")
check(A.Language() == "frFR", "French automatic")
check(A.Number(T.secret) == nil and A.Number(0/0) == nil and A.ID(-1) == nil, "protected/invalid numbers rejected")
check(A.Call(function() error("API unavailable") end) == nil, "API failure guarded")
for k in pairs(A.locales.enUS) do check(A.locales.frFR[k] ~= nil, "French key " .. k) end
for k in pairs(A.locales.frFR) do check(A.locales.enUS[k] ~= nil, "English key " .. k) end

local count, floorCount = 0, 0
for id, ref in pairs(A.ReferenceMaps) do
    count = count + 1
    local c = A.ReferenceContext(id)
    check(A.catalog[id] ~= nil, "catalog name " .. id)
    for _, floor in ipairs(c.floors) do
        floorCount = floorCount + 1
        check(#floor.tiles == 12, "twelve tiles")
        local sum = 0
        for _, tile in ipairs(floor.tiles) do sum = sum + tile.w * tile.h end
        check(near(sum, 1), "tiles cover exact full area")
        check(near(floor.tiles[12].u, 234 / 256) and near(floor.tiles[12].v, 156 / 256), "edge crop")
        check(A.Position(floor) == nil, "no fabricated reference GPS")
    end
end
check(count == 25, "25 reference instances")
check(A.TileLayout(0, 10, 1, 1, {}) == nil, "invalid dimensions")
check(A.TileLayout(1000000, 1000000, 1, 1, {}) == nil, "bounded tile count")
check(A.TileLayout(1002, 668, 256, 256, { 1 }) == nil, "partial tile set rejected")
A.db.floors[43] = math.huge; check(A.ReferenceContext(43).index == 1, "corrupt saved floor")
A.db.floors[33] = 2.6; check(A.ReferenceContext(33).index == 2, "saved floor integer")

local c = A.CurrentContext()
check(c.source == "native" and #c.floors == 2 and c.index == 1, "native maps deduplicated and sorted")
local floor = c.floors[1]
check(A.Status(c, floor, c) == "live", "native position")
T.noPos = true; check(A.Status(c, floor, c) == "noPosition", "missing position")
T.noPos = false; T.px = T.secret; check(A.Position(floor) == nil, "secret coordinate")
T.px = 0; T.py = 0; check(A.Position(floor) == nil, "zero sentinel")
T.px = 1.1; check(A.Position(floor) == nil, "out of range")
T.px = 0.4; T.py = 0.6; T.best = 9002
check(A.Position(floor) == nil, "no GPS on wrong floor")
check(A.Status(c, floor, A.CurrentContext()) == "otherFloor", "wrong floor explained")
T.best = 20
c = A.CurrentContext(); check(c.source == "reference", "outdoor UI map cannot masquerade as dungeon")
check(A.Status(c, c.floors[1], c) == "noPosition", "reference clearly static")
T.instance = 99999; c = A.CurrentContext(); check(c.source == "unavailable" and #c.floors == 0, "unknown instance honest")
T.kind = "none"; check(A.CurrentContext() == nil, "outside instance")
T.kind = "party"; T.instance = 43

local key = "ref:43:1"
check(A.SavePin(key, 0.2, 0.3, "Porte nord"), "create pin")
check(A.Pins(key)[1].label == "Porte nord", "label saved")
check(A.SavePin(key, 0.2, 0.3, "Sortie", 1) and #A.Pins(key) == 1, "edit replaces")
check(not A.SavePin(key, -1, 0.5, "bad") and not A.SavePin(nil, 0.1, 0.1, "bad"), "invalid pin rejected")
check(#A.Pins("native:43") == 0, "no cross-coordinate pin leakage")
for i = 2, 40 do check(A.SavePin(key, 0.1, 0.1, "repère"), "add pin") end
check(not A.SavePin(key, 0.1, 0.1, "too much"), "pin cap")
check(A.DeletePin(key, 2) and #A.Pins(key) == 39, "delete one pin")
check(A.SafeLabel("  été\n|cffff0000  "):find("|") == nil, "chat formatting stripped")
local former = DungeonMapPilotCharDB
DungeonMapPilotCharDB = {}; A.InitDB(); check(#A.Pins(key) == 0, "other character isolated")
DungeonMapPilotCharDB = former; A.InitDB(); check(#A.Pins(key) == 39, "first character preserved")
A.db.pins[key] = {}

local view = { width = 800, height = 500, zoom = 1, panX = 0, panY = 0 }
A.ZoomAt(view, 2, 400, 250)
local x, y = A.CursorToMap(view, 400, 250)
check(near(x, 0.5) and near(y, 0.5), "cursor anchor preserved while zooming")
view.panX, view.panY = A.PanBounds(view, 10000, -500)
check(view.panX == 800 and view.panY == 0, "pan clamped")
A.ZoomAt(view, 1, 400, 250); check(view.panX == 0 and view.panY == 0, "zoom reset full map")
check(A.CursorToMap(view, -1, 0) == nil, "outside click ignored")

check(A.HasAccess(), "test edition free")
A.distribution = "public"
ForeverPilotAccess = { environment = "live", fastXP = true, classGuidance = true, expires = 2000 }
check(not A.HasAccess(), "leveling purchase never unlocks separate Maps product")
DungeonMapPilotAccess = { environment = "sandbox", product = "dungeon_maps", dungeonMaps = true, expires = 2000 }
check(not A.HasAccess(), "sandbox cannot unlock public")
DungeonMapPilotAccess.environment = "live"; check(A.HasAccess(), "dedicated active maps access")
DungeonMapPilotAccess.expires = 1000; check(not A.HasAccess(), "expiry")
DungeonMapPilotAccess.expires = 2000; DungeonMapPilotAccess.product = "fast_xp"; check(not A.HasAccess(), "wrong product")
DungeonMapPilotAccess = nil; A.distribution = "raidpilot-test"; check(A.HasAccess(), "free offline forever")

T.events.PLAYER_LOGIN.scripts.OnEvent(nil, "PLAYER_LOGIN")
check(A.worldButton ~= nil, "native map access button")
T.combat = true; A.Toggle(); check(A.UI.frame == nil, "no intrusive opening in combat")
T.combat = false; SlashCmdList.DUNGEONMAPPILOT("")
local U = A.UI
check(U.frame:IsShown() and U.floor.source == "reference", "opens current static dungeon")
check(not U.player:IsShown() and U.status.text == A.L("noPosition"), "no false player marker")
check(#U.tiles == 12 and near(U.tiles[12].width, U.view.width * 234/1002), "render tile crop scale")
U:EditPin(0.4, 0.2); U.editor.input:SetText("Sortie"); U.editor.save:Click()
check(#A.Pins(key) == 1 and U.pins[1]:IsShown(), "UI creates personal pin")
U.pins[1]:Click(); U.editor.delete:Click(); check(#A.Pins(key) == 0, "UI removes selected pin")
U.follow = false; U:SetContext(A.ReferenceContext(33)); U:SelectFloor(2, false)
check(U.context.index == 2 and A.db.floors[33] == 2, "manual floor saved")
U:RefreshCurrent(); check(U.context.instanceID == 33, "preview not stolen by auto detection")
U:ChooseMap(); check(U.menu:IsShown() and #U.menu.buttons == 25, "all instances in browser")
U.menu:Hide(); A.db.language = "en"; U:Localize(); check(U.browse.text == "Maps", "English UI")
A.db.language = "fr"; U:Localize(); check(U.current.text == "Lieu actuel", "French UI")
T.best = 9001; U:Current(); check(U.player:IsShown(), "true native GPS drawn")
T.noPos = true; U:UpdatePosition(); check(not U.player:IsShown(), "stale GPS cleared immediately")
T.noPos = false; T.best = 9002; U:RefreshCurrent(); check(U.floor.mapID == 9002, "automatic native floor transition")
T.kind = "none"; U:RefreshCurrent(); check(U.floor == nil and not U.player:IsShown(), "exit clears dungeon and GPS")
A.Toggle(); check(not U.frame:IsShown(), "toggle closes")
SlashCmdList.DUNGEONMAPPILOT("diagnostic"); check(A.db.diagnostic.source == "outside", "bounded diagnostic")
A.db.lastReference = 43; A.Toggle(); check(U.context.instanceID == 43 and not U.player:IsShown(), "outside reopens remembered map without GPS"); A.Toggle()
A.distribution = "public"; A.Toggle()
check(U.accessFrame:IsShown() and not U.frame:IsShown(), "public missing access explains activation without exposing paid map")
check(U.accessFrame.url:GetText():find("dungeonmappilot%-subscriptions") ~= nil, "activation points only to Maps portal")
U.accessFrame.close:Click(); A.db.language = "en"; A.Toggle(); check(U.accessFrame.TitleText.text == "Activate DungeonMapPilot", "English activation help")
A.distribution = "raidpilot-test"; A.Toggle(); check(not U.accessFrame:IsShown() and U.frame:IsShown(), "free test edition bypasses activation offline")

-- Route data must be internally coherent and completely translated.
local routeCount=0
for instanceID,route in pairs(A.routeData) do
    routeCount=routeCount+1
    local context=A.ReferenceContext(instanceID)
    local seen={}
    for i,step in ipairs(route.steps) do
        check(not seen[step.id],"unique route step ID"); seen[step.id]=i
        check(context.floors[step.floor] and step.x>=0 and step.x<=1 and step.y>=0 and step.y<=1,"reference anchor valid")
        check(type(step.title.fr)=="string" and #step.title.fr>0 and type(step.title.en)=="string" and #step.title.en>0,"translated step title")
        check(type(step.body.fr)=="string" and #step.body.fr>0 and type(step.body.en)=="string" and #step.body.en>0,"translated step directions")
        for _,required in ipairs(step.requires or {}) do check(seen[required] and seen[required]<i,"prerequisite exists earlier in route") end
    end
end
check(routeCount==2,"two prepared routes, not 25")
check(A.GetRoute(nil)==nil and A.GetRoute({source="native",instanceID=43})==nil,"reference guide never projected onto native map")
local wc=A.GetRoute(A.ReferenceContext(43))
local state=A.RouteRestart(wc)
check(A.RouteSelect(wc,9) and #A.RouteMissing(wc,wc.steps[9])==4,"return requires all four Fanglords")
check(not A.RouteAdvance(wc,false) and state.current==9 and not state.done.disciple,"cannot confirm locked escort")
check(not A.RouteSelect(wc,0) and not A.RouteSelect(wc,99) and not A.RouteSelect(wc,0/0),"invalid selection leaves state")
A.RouteSelect(wc,10); check(not A.RouteAdvance(wc,false),"Mutanus requires escort")
A.RouteSelect(wc,2); check(not A.RouteAdvance(wc,true),"required boss cannot be skipped")
check(not state.done.anacondra,"clicking a marker does not complete it")
for _,index in ipairs({2,3,5,7}) do A.RouteSelect(wc,index); check(A.RouteAdvance(wc,false),"confirm a Fanglord") end
A.RouteSelect(wc,9); check(#A.RouteMissing(wc,wc.steps[9])==0 and A.RouteAdvance(wc,false),"four confirmations unlock return")
check(state.current==10 and A.RouteAdvance(wc,false) and state.current==1,"completion returns to first pending step")
A.RouteAdvance(wc,false); check(state.current==4,"advance skips already confirmed bosses")
check(A.RouteAdvance(wc,true) and state.current==6,"skip first optional boss")
check(A.RouteAdvance(wc,true) and state.current==8,"skip second optional boss")
check(A.RouteAdvance(wc,true) and state.current==11,"all processed route finishes")
local confirmed,total,skipped=A.RouteCounts(wc)
check(confirmed==7 and total==10 and skipped==3,"skipped steps are not reported as confirmed")
check(not A.RouteAdvance(wc,false),"finished route stays finished")
A.InitDB(); check(A.RouteState(wc).current==11,"route progress survives initialization")
local savedCharacter=DungeonMapPilotCharDB
DungeonMapPilotCharDB={}; A.InitDB(); check(A.RouteState(wc).current==1,"new character has own route")
DungeonMapPilotCharDB=savedCharacter; A.InitDB(); check(A.RouteState(wc).current==11,"original character progress retained")
state=A.RouteState(wc); state.current=math.huge; state.done.anacondra="false"; state.skipped.cobrahn=true
check(A.RouteState(wc).current==1 and not state.done.anacondra and not state.skipped.cobrahn,"corrupt flags cannot certify or skip required boss")
local rfc=A.GetRoute(A.ReferenceContext(389)); A.RouteAdvance(rfc,false)
A.RouteRestart(wc); check(A.RouteState(rfc).current==2,"restart changes only the selected dungeon")

-- Real interactions, including sparse marker tables (start and return share an anchor).
A.db.language="fr"; U:SetContext(A.ReferenceContext(43)); U:Localize()
check(U.guide.heading.text=="PARCOURS" and U.routePins[1].number.text=="D","French route and departure")
check(U.guide.done.text=="Étape terminée" and not U.guide.skip.enabled,"French primary action and required start")
check(U.routePins[10]:IsShown() and U.routePins[9]==nil,"duplicate anchor leaves a sparse pin table")
U.guideToggle:Click()
check(not U.guide:IsShown() and U.frame:GetWidth()==920,"compact map hides route sidebar")
for _,pin in pairs(U.routePins) do check(not pin:IsShown(),"all sparse route pins hidden") end
U.guideToggle:Click(); check(U.guide:IsShown() and U.routePins[10]:IsShown(),"route can be restored")
U.guide.rows[9]:Click()
check(U.guide.done.text==A.L("showRequired") and A.RouteState(wc).current==9,"selecting return explains unmet prerequisite")
check(U.routePins[9]:IsShown() and not U.routePins[1]:IsShown(),"return replaces start badge at shared anchor")
U.guide.done:Click(); check(A.RouteState(wc).current==2 and not A.RouteState(wc).done.disciple,"required-objective button navigates without confirming")
U.guide.done:Click(); check(A.RouteState(wc).done.anacondra and A.RouteState(wc).current==3,"primary button confirms and advances")
U:FocusRouteStep(4); U.guide.skip:Click(); check(A.RouteState(wc).skipped.kresh and A.RouteState(wc).current==5,"optional Skip updates route")
A.db.language="en"; U:Localize()
check(U.guide.heading.text=="ROUTE" and U.guide.done.text=="Step completed","English guide controls")
check(U.guide.title.text:find("Lord Pythas",1,true) and U.guide.body.text:find("eastern",1,true),"English objectives and directions")
check(U.guide.count.text:find("1 skipped",1,true),"English counter distinguishes skipped")
U:FocusRouteStep(1); check(U.routePins[1].number.text=="S","English Start badge")
U.guide.restart:Click(); check(A.RouteState(wc).done.anacondra and U.guide.restart.text==A.L("confirmRestart"),"restart needs second click")
U.guide.restart:Click(); check(not A.RouteState(wc).done.anacondra and A.RouteState(wc).current==1,"confirmed restart clears only route progress")
T.kind="party"; T.best=9001; U:Current()
check(U.route==nil,"native map has no reference route")
for _,pin in pairs(U.routePins) do check(not pin:IsShown(),"no reference pins leak to native map") end

-- Personal ordered objectives across floors, editing, translation, and reset isolation.
local context=A.ReferenceContext(33); local firstKey=context.floors[1].key; local secondKey=context.floors[2].key
A.db.pins[firstKey]={}; A.db.pins[secondKey]={}
check(A.GetRoute(context)==nil,"unprepared map has no invented route")
A.SavePin(firstKey,.2,.3,"Départ perso"); A.SavePin(secondKey,.7,.6,"Porte finale")
U:SetContext(context); U:FocusRouteStep(1)
check(U.route.personal and #U.route.steps==2 and U.context.index==1,"personal route selects correct starting floor")
local personal=U.route; U.guide.done:Click()
check(U.context.index==2 and A.RouteState(U.route).current==2,"next personal objective changes displayed floor")
check(not U.routePins[1]:IsShown() and U.routePins[2]:IsShown(),"only current floor's objectives displayed")
U.routePins[2]:Click("RightButton")
check(U.editor:IsShown() and U.editor.index==1 and U.editor.key==secondKey,"right-click edits exact personal marker")
U.editor.input:SetText("Sortie est"); U.editor.save:Click()
check(A.RouteState(U.route).current==1 and A.RouteState(U.route).done["1:1"]==nil,"edited personal route invalidates old completion")
check(#A.Pins(firstKey)==1 and #A.Pins(secondKey)==1 and A.RouteState(rfc).current==2,"personal route edit preserves pins and other dungeon progress")
A.db.language="fr"; U:Localize(); check(U.guide.body.text:find("Rejoignez",1,true),"personal directions switch to French")
U.guide.restart:Click(); U.guide.restart:Click()
check(#A.Pins(firstKey)==1 and #A.Pins(secondKey)==1,"new run does not delete map markers")
A.db.routeMode[43]="personal"; check(A.GetRoute(A.ReferenceContext(43))==nil,"personal mode does not silently substitute prepared route")
A.SavePin(key,.1,.1,"Mon départ"); check(A.GetRoute(A.ReferenceContext(43)).personal,"custom route available on prepared map")
A.db.routeMode[43]=nil; check(A.GetRoute(A.ReferenceContext(43))==wc,"switching back preserves prepared route identity")
A.RouteRestart(wc); U:SetContext(A.ReferenceContext(43)); U:FocusRouteStep(2)
A.ZoomAt(U.view,2,400,250); U:ApplyView()
check(near(U.routePins[2].point[4],wc.steps[2].x*U.view.width*2),"route badges remain attached during zoom")
check(not U.player:IsShown(),"guide never creates a fake player pawn")
-- Actual RouteUI buttons, with exact selection and restart recovery.
A.RouteRestart(wc);U:SetContext(A.ReferenceContext(43));U:FocusRouteStep(1)
local firstID=wc.steps[1].id
U.guide.done:Click();check(A.RouteState(wc).done[firstID] and U.guide.undo.enabled,"step completion enables undo")
local afterCompletion=A.RouteState(wc).current
U.guide.undo:Click();check(A.RouteState(wc).current==1 and not A.RouteState(wc).done[firstID],"undo button restores selection and completion")
U.guide.done:Click();U.guide.restart:Click();U.guide.restart:Click()
check(A.RouteState(wc).current==1 and not A.RouteState(wc).done[firstID],"restart still requires confirmation")
U.guide.undo:Click();check(A.RouteState(wc).current==afterCompletion and A.RouteState(wc).done[firstID],"undo recovers confirmed restart")
for _,code in ipairs({"enUS","enGB","frFR","deDE","esES","esMX","itIT","ptBR","ruRU","koKR","zhCN","zhTW"}) do
 A.db.language=code;U:Localize();check(U.guide.undo.text==A.L("undoRoute") and U.guide.undo.text~="undoRoute","undo localized "..code)
end


print("PASS " .. T.count .. " checks; " .. count .. " reference instances / " .. floorCount .. " floors. UI uses frame doubles.")
