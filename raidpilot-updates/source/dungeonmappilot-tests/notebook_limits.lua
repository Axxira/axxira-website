-- Corruption, storage ceilings and concurrent-note edits; no protected API actions.
local A,T=DungeonMapPilot,TEST
T.combat=false
A.db.personalNotes={}
A.db.personalNoteHistory={}
local function synthetic(n)
    return {id="synthetic:"..n,revision="v1",steps={{id="step-one",kind="start",title="Example"}}}
end
for n=1,300 do
    local route=synthetic(n)
    check(A.NoteSet(route,1,"Personal note "..n),"bound slot "..n.." accepted")
end
check(not A.NoteSet(synthetic(301),1,"Limit"),"301st note refused without deleting existing data")
check(A.NoteGet(synthetic(1),1)=="Personal note 1","first note preserved at storage limit")
check(A.NoteGet(synthetic(300),1)=="Personal note 300","last accepted note preserved at limit")
check(#A.db.personalNoteHistory==12,"undo history bounded to 12 edits")
check(A.NoteSet(synthetic(1),1,"Personal note 1"),"re-saving unchanged text allowed at limit")
check(A.NoteSet(synthetic(1),1,"Updated 1"),"changing existing note allowed at capacity")
check(A.NoteGet(synthetic(1),1)=="Updated 1","updated note persisted")
local route=synthetic(1)
A.db.personalNotes[route.id]["step-one"]="Changed elsewhere"
check(not A.NoteCanUndo(route,1) and not A.NoteUndo(route,1),
      "undo refuses to overwrite concurrent note changes")
A.db.personalNotes="corrupt"
check(not A.NoteSet(route,1,"not valid") and A.NoteGet(route,1)==nil,
      "corrupted global saved variable fails closed rather than resetting notes")
A.db.personalNotes={}
A.db.personalNoteHistory={}
local badRoute={id="bad",steps={{id="|Hitem:123"}}}
check(not A.NoteSet(badRoute,1,"Blocked"),"invalid step identifiers cannot store notes")
check(not A.NoteSet(route,1,T.secret),"secret or protected text cannot be stored")
check(not A.NoteSet(route,1,string.rep("à",101)),"overlength multibyte text refused before splitting UTF-8")
check(A.NoteSet(route,1,"Rôle 1 : interruption"),"valid accented note allowed")
check(A.NoteGet(route,1)=="Rôle 1 : interruption","UTF-8 note roundtrips intact")
check(A.NoteSet(route,1,""),"notes can be cleared")
check(A.db.personalNotes[route.id]==nil,"empty route note branches cleaned")
check(T.frames and #T.frames<800,"stress test doesn't create UI frames per saved note")
print("PASS: "..T.count.." checks; 300 note limit, concurrent edit safety, non-ASCII and corruption.")
