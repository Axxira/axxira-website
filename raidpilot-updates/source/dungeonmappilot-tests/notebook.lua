-- Per-character boss notes and route resume: no automatic kills, chat or currency.
local A,T=DungeonMapPilot,TEST
local U=A.UI
T.combat=false
A.db.personalNotes={}
A.db.personalNoteHistory={}
local wc=A.GetRoute(A.ReferenceContext(43))
local sf=A.ReferenceContext(189)
A.RouteChooseVariant(sf,1)
local gy=A.GetRoute(sf)
A.RouteChooseVariant(sf,4)
local cathedral=A.GetRoute(sf)
check(gy and cathedral and gy.id~=cathedral.id,"different wings have unique note namespaces")
check(not A.NoteGet(gy,2) and not A.NoteCanUndo(gy,2),"no notes on fresh character")
check(A.NoteSet(gy,2,"Tank left; healer behind"),"save one personal boss note")
check(A.NoteGet(gy,2)=="Tank left; healer behind","saved text round-trips")
check(not A.NoteGet(cathedral,2),"other wings never inherit personal notes")
check(A.NoteSet(cathedral,2,"Dispel the curse"),"save separate wing note")
check(A.NoteGet(gy,2)=="Tank left; healer behind","same boss index in another wing does not overwrite")
check(A.NoteCanUndo(cathedral,2) and not A.NoteCanUndo(gy,2),"undo is scoped to last edited encounter")
check(A.NoteUndo(cathedral,2) and not A.NoteGet(cathedral,2),"undo removes new personal note")
check(A.NoteSet(gy,2,"Healer near pillar"),"editing an existing note")
check(A.NoteGet(gy,2)=="Healer near pillar","new note persisted")
check(A.NoteUndo(gy,2) and A.NoteGet(gy,2)=="Tank left; healer behind","undo restores exactly previous note")
check(A.NoteUndo(gy,2) and not A.NoteGet(gy,2),"second undo can restore the original empty note")
check(not A.NoteUndo(gy,2),"undo stops at the preceding edit belonging to another wing")
check(not A.NoteSet(gy,2,"|Hitem:1|h[bad]|h"),"chat/item markup rejected")
check(not A.NoteSet(gy,2,string.rep("A",201)),"overlength notes rejected")
check(not A.NoteSet(gy,0,"zero") and not A.NoteSet(gy,99,"invalid"),"invalid step indices rejected")
check(A.NoteSet(gy,2,"  Healer \t left  \n keep distance  "),"whitespace normalized safely")
check(A.NoteGet(gy,2)=="Healer left keep distance","stored note is single-line safe text")
T.combat=true
check(not A.NoteSet(gy,2,"During combat") and not A.NoteUndo(gy,2),"combat cannot change notes")
check(A.NoteGet(gy,2)=="Healer left keep distance","combat leaves saved note intact")
T.combat=false
A.RouteRestart(wc)
local state=A.RouteState(wc)
check(A.RouteNextReady(wc)==1,"fresh route starts at preparation")
check(A.RouteAdvance(wc,false),"first step completed manually")
check(A.RouteNextReady(wc)==2,"resume targets first remaining unfinished step")
check(A.RouteSelect(wc,7),"user may inspect another step")
local priorHistory=#state.history
local priorCount=A.RouteCounts(wc)
check(A.RouteNextReady(wc)==2,"resume ignores arbitrarily inspected cursor")
check(#state.history==priorHistory,"preview/resume query does not create undo checkpoints")
local bad={previewOnly=true,steps=wc.steps,revision=wc.revision,id="preview"}
check(A.RouteNextReady(bad)==nil,"announcement preview never produces actionable resume")
local blocked={id="blocked",revision="r1",steps={{id="one",requires={"missing"}}}}
check(A.RouteNextReady(blocked)==nil,"broken prerequisites do not yield a fake next action")
U:SetContext(A.ReferenceContext(43))
check(U.guide.notes and U.guide.resume,"Notes and Resume controls visible in guide")
check(U.guide.mode.width==125,"mode selector shares row with Resume")
check(U.guide.resume.enabled,"Resume enabled for unfinished accessible route")
U.guide.resume:Click()
check(A.RouteState(wc).current==2,"Resume explicitly chooses first undone step")
check(A.RouteCounts(wc)==priorCount and #state.history==priorHistory,
    "Resume never marks encounters complete or consumes undo history")
U:SetContext(sf)
A.RouteChooseVariant(sf,1)
U:UpdateGuide()
A.RouteSelect(gy,2);U:UpdateGuide()
check(U.guide.notes.enabled,"Notes available on selected encounter")
U.guide.notes:Click()
local f=U.noteDialog
check(f and f:IsShown() and f.input:GetText()=="Healer left keep distance","editor preloads existing note")
f.input:SetText("Personal strategy for this boss")
f.save:Click()
check(A.NoteGet(gy,2)=="Personal strategy for this boss" and not f:IsShown(),
      "save button writes local note and closes editor")
check(U.guide.notes:GetText():find("*",1,true),"saved note visibly marked with star")
U.guide.notes:Click()
check(f:IsShown() and f.undo.enabled,"undo available in reopened editor")
f.undo:Click()
check(A.NoteGet(gy,2)=="Healer left keep distance","UI Undo restores prior personal note")
f.clear:Click()
check(not A.NoteGet(gy,2),"UI Clear removes the correct boss note")
U.guide.notes:Click()
check(f.input:GetText()=="","cleared note no longer appears")
U:SetContext(A.ReferenceContext(33))
check(not f:IsShown(),"changing dungeon closes the note editor")
local results=A.EncounterSearch("Arugal",24)
check(#results==1 and U:PreviewBossResult(results[1]),"read-only boss finder can inspect an encounter")
local route=U.route;local index=results[1].stepIndex
local before=A.RouteState(route).current
local beforeDone=A.RouteCounts(route)
U.guide.notes:Click()
check(U.noteDialog.index==index and U.noteDialog.route.id==route.id,"notes target inspected boss, not unrelated cursor")
U.noteDialog.input:SetText("Stop the cast!")
U.noteDialog.save:Click()
check(A.NoteGet(route,index)=="Stop the cast!","notes persist on inspected boss")
check(A.RouteState(route).current==before and A.RouteCounts(route)==beforeDone,
    "adding note to search result never marks boss completed")
for _,code in ipairs({"enUS","enGB","frFR","deDE","esES","esMX","itIT","ptBR","ruRU","koKR","zhCN","zhTW"}) do
    A.db.language=code
    for _,key in ipairs({"bossNoteTitle","bossNoteSave","bossNoteClear","bossNoteUndo","resumeNextStep","resumeNextHelp"}) do
        check(A.L(key)~=key,"translated note and resume controls "..code..":"..key)
    end
end
check(#T.frames<700,"note editor reuses UI frames rather than recreating per boss")
print("PASS: "..T.count.." checks; per-boss notes, saved history, combat and resume.")
