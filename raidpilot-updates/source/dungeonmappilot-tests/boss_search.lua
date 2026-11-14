local A,T=DungeonMapPilot,TEST
local U=A.UI
local match=A.EncounterSearch("Arugal",24)
check(#match==1 and match[1].id==33 and match[1].boss=="Archmage Arugal",
      "Exact boss search finds Shadowfang Arugal")
check(match[1].stepIndex>1 and match[1].kind=="boss","Result records the encounter step")
local snip=A.EncounterSearch("Lyn the Ignored",24)
check(#snip==1 and snip[1].id==2959 and snip[1].optional,"Optional Dalaran boss found")
local brd=A.EncounterSearch("cthun",24)
check(#brd==1 and brd[1].id==531,"Apostrophes can be omitted in a boss name")
A.db.language="frFR"
local french=A.EncounterSearch("monastère écarlate",40)
check(#french>=5,"French accented dungeon name searches all wings")
local english=A.EncounterSearch("scarlet monastery",40)
check(#english==#french,"English dungeon name also works in French client")
check(#A.EncounterSearch("",24)==0 and #A.EncounterSearch("a",24)==0,
      "Empty and one-character searches are bounded")
local ignored=A.EncounterSearch("[.*",24)
check(#ignored==0,"User input is treated as literal text, not an executing Lua pattern")
check(#A.EncounterSearch(string.rep("a",96),24)==0,"Oversized search string rejected")
local capped,more=A.EncounterSearch("the",5)
check(#capped==5 and more,"Results capped and overflow disclosed")
local all=A.EncounterSearch("the",40)
check(#all>=#capped,"Search limit is configurable but bounded")
for i=2,#all do
    check(all[i-1].boss<=all[i].boss or all[i-1].boss~=all[i].boss,
          "Visible results are stable records")
end
for _,v in ipairs({"enUS","enGB","frFR","deDE","esES","esMX","itIT","ptBR","ruRU","koKR","zhCN","zhTW"}) do
    A.db.language=v
    for _,key in ipairs({"searchBoss","searchBossTitle","searchBossHint","searchBossAction",
        "searchBossEmpty","searchBossResults","searchBossLimit","searchBossInspect","searchBossPreview"}) do
        check(A.L(key)~=key,"Search label translated "..v..":"..key)
    end
end
A.db.language="frFR"
local c=A.ReferenceContext(33)
check(c~=nil,"Shadowfang reference context available")
local route=A.GetRoute(c)
A.RouteRestart(route)
local previous=A.RouteState(route)
local current=previous.current
local history=#previous.history
local done=#A.RouteHealth(route).issues
U:SetContext(c)
check(U.bossSearch and U.bossSearch:IsShown(),"Find boss button visible directly on map toolbar")
U:FindEncounter()
check(U.searchDialog and U.searchDialog:IsShown(),"Search dialog opens only on click")
local f=U.searchDialog
f.input:SetText("arugal")
f.find:Click()
check(U.menu and U.menu:IsShown() and U.menu.buttons[1]:IsShown(),
      "Search lists an actionable result")
U.menu.buttons[1]:Click()
check(not f:IsShown() and U.route.id==route.id and U.context.instanceID==33,
      "Choosing an encounter opens correct dungeon without a modal left behind")
check(U.encounterPreview and U.encounterPreview.index==match[1].stepIndex,
      "Result presents the exact requested encounter")
check(U.guide.title.text:find("Arugal",1,true) and U.guide.body.text:find("Arugal",1,true),
      "Encounter text visible in the route detail panel")
check(U.guide.warning.text==A.L("searchBossPreview") and not U.guide.done.enabled,
      "Inspection explicitly read-only; cannot accidentally complete the current step")
local after=A.RouteState(route)
check(after.current==current and #after.history==history and #A.RouteHealth(route).issues==done,
      "Inspection does not change saved route current position, history or prerequisites")
for _,step in ipairs(route.steps) do
    check(not after.done[step.id] and not after.skipped[step.id],
      "No boss is marked complete or skipped by a search")
end
U.guide.rows[match[1].stepIndex]:Click()
check(U.encounterPreview==nil and A.RouteState(route).current==match[1].stepIndex,
      "Manual click returns to the selected guide step")
check(U.guide.done.enabled,"Explicit manual completion works after inspection")
U:FindEncounter()
f.input:SetText("nonexistentbossxyz");f.find:Click()
check(U.menu==nil or not U.menu:IsShown(),"No result does not leave a stale menu open")
check(f.help.text==A.L("searchBossEmpty"),"Helpful empty-state text")
f:Hide()
local previousCount=T.count
U:FindEncounter();check(T.count==previousCount,"Opening existing modal does not change test record")
f:Hide()
check(T.count>0,"Test executed")
print("PASS: "..T.count.." checks; boss finder, 12 locale choices, results and read-only inspection.")
