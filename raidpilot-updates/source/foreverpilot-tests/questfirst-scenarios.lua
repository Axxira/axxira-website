local B=ForeverPilotLeveling
local n={id=201,name="Old zone quest",level=19,raw={}}
B.Graph.nodes[201]=n
local s={now=100,level=30,map=1,log={[201]={title="Old zone quest",ready=false,objectives={{numRequired=5,numFulfilled=1}}}},count=1,limit=20}
local status=B.Journal:Classify(201,s)
assert(status.status=="fast","Accepted quest must be kept in speed route")
assert(status.outleveled==false,"Accepted lower-level quest must not be retired")
assert(B.Journal:Allows(n,s)==true,"Accepted quest must remain eligible")
local selected={q=201,key="complete:201",kind="complete"}
assert(B.Grind:Choose(s,selected)==selected,"Grind should not replace a quest")
local fallback=B.Planner:Fallback(s)
assert(fallback.kind=="recovery" and fallback.questReview and fallback.label=="Old zone quest","Unmapped held quest must not fall through to grind")
B.Recovery.IsSkipped=function(self,q)return q==201 end
assert(B.Journal:Allows(n,s)==false,"Explicitly skipped quest must remain skipped")
B.Recovery.IsSkipped=function()return false end
B.Planner.Signature=function()return "test" end
B.Planner.Batch=function(self,a)return a end
B.Planner.Candidates=function(self,state)
 return {
  {q=301,key="accept:301",kind="accept",score=50,travel=10,classPriority=false},
  {q=201,key="complete:201",kind="complete",score=1,travel=20,classPriority=false},
 }
end
local result=B.Planner:Plan(s)
assert(result.q==201,"Held quest must outrank a speculative new pickup")
B.Planner.current=nil;B.Planner.signature=nil
B.Planner.Candidates=function(self,state)
 return {
  {q=301,key="accept:301",kind="accept",score=50,travel=10,classPriority=false},
  {q=201,key="complete:201",kind="complete",score=1,travel=20,classPriority=false},
  {q=401,key="complete:401",kind="complete",score=5,travel=15,classPriority=true}
 }
end
result=B.Planner:Plan(s)
assert(result.q==401,"Class quest must remain first priority")
