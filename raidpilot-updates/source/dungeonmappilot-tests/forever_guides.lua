-- Forever-specific guide-only records: four documented, five announced previews.
local A,T=DungeonMapPilot,TEST
local entries={
 [3065]={bosses=4},[2999]={bosses=7},[2998]={bosses=4},[2959]={bosses=9}}
local total,bosses=0,0
local all=A.RouteCoverage()
check(all.maps==34 and all.variants==41 and all.anchored==15,
      "legacy 4 documented guide identities remain under a larger catalogue")
for id,expected in pairs(entries) do
 total=total+1;bosses=bosses+expected.bosses
 local ctx=A.GuideContext(id)
 check(ctx and ctx.source=="guide-only" and #ctx.floors==0,"documented guide has no made-up art")
 check(A.ReferenceContext(id)==nil,"reference art not manufactured for new Forever map")
 check(A.CatalogName(id)~=A.L("unknown"),"documented dungeon name available")
 check(not A.GuideContext(id,"An unrelated instance"),"candidate ID validates live name")
 check(A.GuideContext(id,A.catalog[id].en)~=nil,"known client name accepted")
 local route=A.GetRoute(ctx)
 check(route and route.guideOnly and not route.previewOnly and #route.steps==expected.bosses+1,
       "previous documented boss roster preserved")
 check(#A.RouteVariants(ctx)==1,"single documented guide")
 check(A.RouteHealth(route)~=nil,"route audit remains functional")
 for _,step in ipairs(route.steps) do
    check(step.x==nil and step.y==nil and step.floor==nil,"no fabricated boss markers")
    check(type(step.title)=="table" and type(step.body)=="table" and A.RouteText(step.title)~="",
      "nonempty guide content")
 end
 local previous=A.RouteState(route).current
 check(A.RouteAdvance(route,false),"manual preparation confirmation still supported")
 check(A.RouteState(route).current==previous+1,"manual progress")
 check(A.RouteUndo(route),"undo works on previous guides")
end
check(total==4 and bosses==24,"four beta-documented rosters retained")
-- The game can report a custom ID from the client, but the name has to match.
T.kind="party";T.instance=3065;T.best=20
local current=A.CurrentContext()
check(current and current.source=="guide-only" and current.instanceID==3065 or
      current and current.source=="unavailable","game detection uses verified name instead of trusting candidate instance ID")
T.instance=43;T.best=20
local known=A.CurrentContext();check(known and known.source=="reference","original map detection preserved")
T.instance=3065
local guide=A.GuideContext(3065)
local U=A.UI
U:SetContext(guide)
check(U.route and U.route.id=="forever:3065","custom guide opens without map art")
check(not U.floor and not U.player:IsShown(),"no fabricated native location on missing art")
check(U.guide and U.guide.done.enabled,"documented text guide still supports manual confirmation")
U:SetContext(A.GuideContext(2970))
check(U.route and U.route.rosterOnly and not U.route.previewOnly,
      "newer client boss roster replaces outdated announcement")
check(U.guide.done.enabled and U.guide.restart.enabled,
      "the player may manually track boss encounters without automatic gameplay")
check(U.guide.warning.text==A.L("guideRosterWarning"),
      "unverified path/encounter order warning is visible")
check(U.empty:IsShown() and U.empty.text:find(A.L("guideNoArt"),1,true),
      "not a fabricated illustration")
-- When the addon displays native artwork, a legitimate custom matching name may use text guide;
-- guide coordinates stay nil and the renderer never invents GPS positions.
for _,code in ipairs({"enUS","enGB","frFR","deDE","esES","esMX","itIT","ptBR","ruRU","koKR","zhCN","zhTW"}) do
    A.db.language=code
    check(A.L("guideNoArt")~="guideNoArt" and A.L("guidePreviewOnly")~="guidePreviewOnly",
          "new preview warnings localized for "..code)
end
T.instance=43;T.best=20
print("PASS: "..T.count.." checks; nine Forever entries, four earlier guides, five newer client rosters; no manufactured map art.")
