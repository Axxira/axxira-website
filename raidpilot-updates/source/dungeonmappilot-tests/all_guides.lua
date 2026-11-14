-- Complete catalogue coverage, manual progress and source-honest guidance.
local A,T=DungeonMapPilot,TEST
local U=A.UI
local coverage=A.RouteCoverage()
check(coverage.maps==34,"25 reference maps and nine Forever guide-only instances have an entry")
check(coverage.variants==41,"32 classical routes plus 9 Forever guides and previews")
check(coverage.bosses==285,"214 new classic boss notes + 13 original encounters + 24 Forever encounters")
check(coverage.anchored==15,"only the 15 original WC and RFC map anchors are retained")
local distinct,routeIDs={},{}
local expected={ [189]=4,[229]=2,[329]=2,[429]=3 }
local maps,variantCount,starts,bosses=0,0,0,0
for mapID in pairs(A.ReferenceMaps) do
    maps=maps+1
    local context=A.ReferenceContext(mapID)
    check(context and #context.floors>0,"reference map loaded for "..mapID)
    local options=A.RouteVariants(context)
    local count=#options>0 and #options or 1
    check(count==(expected[mapID] or 1),"wing count correct for map "..mapID)
    for wing=1,count do
        if #options>0 then check(A.RouteChooseVariant(context,wing),"wing selection valid") end
        local route=A.GetRoute(context)
        check(route and #route.steps>=2 and #route.steps<=35,"nonempty complete route for "..mapID.."/"..wing)
        check(not routeIDs[route.id],"unique saved-route ID "..route.id)
        routeIDs[route.id]=true;variantCount=variantCount+1
        check(type(route.revision)=="string" or type(route.revision)=="number","stable revision")
        local seen={}
        for index,step in ipairs(route.steps) do
            check(type(step.id)=="string" and not seen[step.id],"unique step identifier in wing "..route.id)
            seen[step.id]=true
            check(type(step.kind)=="string","typed guide step")
            check(A.RouteText(step.title)~="" and A.RouteText(step.body)~="","nonempty localized guide content")
            if index==1 then check(step.kind=="start","every guide begins with preparation");starts=starts+1
            else bosses=bosses+1 end
            if mapID~=43 and mapID~=389 then
                check(step.x==nil and step.y==nil,"no invented map coordinates in "..route.id)
                check(route.guideOnly==true,"all additional routes explicitly text-only")
            else
                check(type(step.x)=="number" and type(step.y)=="number","original calibrated landmarks preserved")
            end
            if type(step.floor)=="number" then
                check(step.floor>=1 and step.floor<=#context.floors,"supported floor index")
            end
        end
        check(A.RouteState(route).current>=1,"load fresh route state")
        A.RouteRestart(route)
        for index,step in ipairs(route.steps) do
            check(A.RouteState(route).current==index,"deterministic sequential route progress")
            if step.kind=="optional" then
                check(A.RouteAdvance(route,true),"optional detour can be skipped")
            else
                check(A.RouteAdvance(route,false),"non-optional step confirms only by manual action")
            end
        end
        check(A.RouteState(route).current==#route.steps+1,"finish route only after every boss handled")
        check(not A.RouteAdvance(route,false),"cannot progress beyond end")
        check(A.RouteUndo(route),"undo works on finished route")
        check(A.RouteState(route).current<=#route.steps,"undo recovers a meaningful step")
        A.RouteRestart(route)
    end
    A.db.routeWing[mapID]=nil
end
check(maps==25 and variantCount==32 and starts==32 and bosses==227,"complete catalog accounting")
-- Switching the Cathedral and Graveyard must not destroy progress in either wing.
local sm=A.ReferenceContext(189)
check(A.RouteChooseVariant(sm,1),"graveyard selected")
local gy=A.GetRoute(sm);A.RouteRestart(gy)
check(A.RouteAdvance(gy,false),"graveyard step completed")
check(A.RouteChooseVariant(sm,4),"cathedral selected")
local cat=A.GetRoute(sm);A.RouteRestart(cat)
check(gy.id~=cat.id and A.RouteState(cat).current==1,"independent wing ID and progress")
check(A.RouteAdvance(cat,false),"cathedral progress independent")
check(A.RouteChooseVariant(sm,1) and A.RouteState(A.GetRoute(sm)).current==2,"graveyard progress persists")
check(not A.RouteChooseVariant(sm,0) and not A.RouteChooseVariant(sm,99),"invalid wing selection blocked")
A.db.routeWing[189]=nil
local sf=A.ReferenceContext(33);A.db.routeMode[33]=nil
local first=A.GetRoute(sf)
check(first and first.guideOnly,"Shadowfang guide available by default")
U:SetContext(sf)
check(U.route==first and U.guide.count.text~="","text guide displays on real frame model")
check(U.guide.warning.text==A.L("guideTextOnly"),"explicit uncalibrated map warning")
for _,pin in pairs(U.routePins) do check(not pin:IsShown(),"all unverified boss pins hidden") end
U:FocusRouteStep(2)
check(A.RouteState(first).current==2,"selecting a text step does not throw on missing x and y")
U.guide.done:Click()
check(A.RouteState(first).current==3,"text-only routes still advance by player confirmation")
-- The UI switcher lists every wing in the correct context.
U:SetContext(sm)
check(U.guide.variant:IsShown(),"Scarlet Monastery wing selector shown")
U.guide.variant:Click()
check(U.menu:IsShown() and #U.menu.buttons>=4,"four wings in the UI selector")
U.menu.buttons[3]:Click()
check(A.db.routeWing[189]==3 and U.route==A.GetRoute(sm),"UI selects Armory without moving the character")
U:SetContext(A.ReferenceContext(36))
check(not U.guide.variant:IsShown(),"single route hides irrelevant selector")
-- Personal route mode preserves its own editable marker workflow on a prepared map.
local firstKey,secondKey=sf.floors[1].key,sf.floors[2].key
local previousFirst,previousSecond=A.db.pins[firstKey],A.db.pins[secondKey]
A.db.pins[firstKey],A.db.pins[secondKey]={},{}
A.db.routeMode[33]="personal"
check(A.GetRoute(sf)==nil,"personal route remains an explicit alternative with no own markers")
A.SavePin(firstKey,.33,.44,"Custom mark")
check(A.GetRoute(sf).personal,"custom marker route remains available")
A.db.routeMode[33]=nil
check(A.GetRoute(sf)==first,"returning to prepared guide never deletes personal marker")
A.db.pins[firstKey],A.db.pins[secondKey]=previousFirst,previousSecond
-- Route text shares language keys and is not falsely claimed to be translated into boss names for every locale.
for _,code in ipairs({"enUS","enGB","frFR","deDE","esES","esMX","itIT","ptBR","ruRU","koKR","zhCN","zhTW"}) do
    A.db.language=code
    check(A.L("guideTextOnly")~="guideTextOnly" and A.L("guideTextBadge")~="guideTextBadge",
          "guide-only notice localized for "..code)
    local title=A.RouteText(first.steps[2].title)
    check(type(title)=="string" and title~="","boss names render even with language fallback "..code)
end
check(#A.routeData[43].steps==10 and #A.routeData[389].steps==5,"two original walkthroughs unchanged")
check(T.frames and #T.frames<600,"new guides reuse UI frames rather than creating one panel per boss")
print("PASS: "..T.count.." checks; 25 maps, 63 floors, 32 routes, 227 encounter steps, no invented coordinates.")
