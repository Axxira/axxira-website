-- Five new client rosters and a level-aware catalogue, without claiming map GPS.
local A,T=DungeonMapPilot,TEST
local expected={
 [2970]={"Zul'Alai","Var'Taka","Captain Dreadrise","Deathless Guardians","Primeval Elemental","Min'loth the Serpent"},
 [3039]={"Slavecrusher Grox","Twilight Commander Zarmok","Handler Ashka","Nar'gurk","Overseer Mugg"},
 [2994]={"Overseer Wrex","Tidemistress Ca'zass","Katerina the Cleaner","The Questioner","Blazeroar"},
 [2974]={"Withermaw","Impaan the Imprisoner","Elder Shaman Bloodmane","Bonesnout the Unbroken","Varrox Bleakhoof","Blackmaw Behemoth","Unbound Spirit"},
 [3001]={"Titanosaur","Ravasaur Hunters","Thunderhoof","Venomridge","Direhorn Matriarch","Cinder","King Op","Snowtalon","Bolt","Vitrix","Nanaya"}
}
local coverage=A.RouteCoverage()
check(coverage.maps==34 and coverage.variants==41 and coverage.bosses==285 and coverage.anchored==15,
      "all guide entries and 58 custom boss encounters without any new false map markers")
local total=0
for id,bosses in pairs(expected) do
    total=total+1
    local meta=A.customGuideMeta[id]
    check(meta and meta.status=="client-roster" and meta.clientBuild=="1.60.1.70888" and not meta.routeOrderVerified,
          "newer client data provenance and uncertainty explicit")
    local cat=A.catalog[id]
    check(cat and cat.rosterOnly and not cat.previewOnly and cat.guideOnly,"list visible as a roster, not obsolete placeholder")
    local context=A.GuideContext(id)
    check(context and context.source=="guide-only" and #context.floors==0 and A.ReferenceContext(id)==nil,
          "all 5 new rosters avoid invented illustration")
    check(A.GuideContext(id,"Wrong dungeon") == nil,"provisional ID requires matching name for live detection")
    local route=A.GetRoute(context)
    check(route and route.rosterOnly and route.orderUnverified and not route.previewOnly,
          "manual checklist is not a certified route")
    check(#route.steps==#bosses+1 and route.steps[1].kind=="start","source lists all bosses plus preparation")
    for i,name in ipairs(bosses) do
        local step=route.steps[i+1]
        check(A.RouteText(step.title)==name,"client boss name retained in source order")
        check(step.kind=="boss" and step.x==nil and step.y==nil and step.floor==nil,
              "no unsupported boss positions or fake optional status")
        check(A.RouteText(step.body):find(name,1,true)~=nil,"each encounter has context")
    end
    A.RouteRestart(route)
    check(A.RouteAdvance(route,false),"manual confirmation allowed after viewing roster")
    check(A.RouteUndo(route) and A.RouteState(route).current==1,"undo safely restores manual state")
    local health=A.RouteHealth(route)
    check(health and health.remaining==#bosses+1 and health.invalid==0,"route index has no invalid steps")
end
check(total==5 and #A.CatalogList()==34,"all 34 records preserved and five client-roster updates")
-- The four earlier documented Forever rosters retain their step IDs and independent progress.
for _,id in ipairs({3065,2999,2998,2959}) do
    local ctx=A.GuideContext(id)
    local r=A.GetRoute(ctx)
    check(r and r.guideOnly and not r.rosterOnly and not r.previewOnly,
        "previous documented walk-through preserved")
end
local l13=A.CatalogAtLevel(13)
local found={}
for _,row in ipairs(l13) do found[row.id]=true end
check(found[389] and found[3065],"level 13 recommends Ragefire and Hall of Thanes")
check(not found[531] and not found[409],"level filter never recommends level 60 raids to level 13")
local l40=A.CatalogAtLevel(40)
found={}
for _,row in ipairs(l40) do
    found[row.id]=true
    check(not A.catalog[row.id].raid,"dungeon recommendations exclude raids")
    local min,max=A.CatalogLevelRange(row.id)
    check(min<=40 and max>=40,"every level match in range")
end
check(found[3039],"level 40 includes Krol'dok")
check(#A.CatalogAtLevel(0)==0 and #A.CatalogAtLevel(61)==0 and #A.CatalogAtLevel(0/0)==0,
      "unavailable/invalid player level cannot invent a recommendation")
local oldLevel=UnitLevel
UnitLevel=function() return 40 end
check(A.PlayerDungeonLevel()==40,"live client level is read without persisting")
local U=A.UI
if U.menu then U.menu:Hide() end
U.mapBrowseMode="all"
U:ChooseMap()
check(U.menu:IsShown() and U.menu.buttons[1].text:find("40",1,true),"At my level control immediately visible in map menu")
U.menu.buttons[1]:Click()
local selected={}
for _,row in ipairs(U.menu.buttons) do
    if row:IsShown() then selected[#selected+1]=row end
end
check(#selected==#l40+1,"one-click level filter actually limits displayed dungeon choices")
check(selected[1].text:find("34",1,true),"one-click All Maps restores full coverage")
selected[1]:Click()
local shown=0
for _,row in ipairs(U.menu.buttons) do if row:IsShown() then shown=shown+1 end end
check(shown==35,"all 34 entries and one optional level-filter action shown")
U.menu:Hide()
UnitLevel=function() return T.secret end
check(A.PlayerDungeonLevel()==nil,"protected level not interpreted")
U.mapBrowseMode="level";U:ChooseMap()
shown=0
for _,row in ipairs(U.menu.buttons) do if row:IsShown() then shown=shown+1 end end
check(shown==34 and U.mapBrowseMode=="all","hidden level safely falls back to all maps")
U.menu:Hide();UnitLevel=oldLevel
for _,code in ipairs({"enUS","enGB","frFR","deDE","esES","esMX","itIT","ptBR","ruRU","koKR","zhCN","zhTW"}) do
 A.db.language=code
 for _,key in ipairs({"guideRosterBadge","guideRosterWarning","guideBrowseRoster","guideMyLevel","guideAllMaps","guideRosterSource"}) do
    check(A.L(key)~=key,"new roster/catalog label translated "..code..":"..key)
 end
 check(A.L("guideMyLevel",40,8):find("40",1,true)~=nil,"locale level message has valid formatting")
end
check(#A.routeData[43].steps==10 and #A.routeData[389].steps==5,"original anchored routes unchanged")
print("PASS: "..T.count.." checks; 34 entries, 41 guide variants, 285 encounter steps, 5 new rosters, level finder.")
