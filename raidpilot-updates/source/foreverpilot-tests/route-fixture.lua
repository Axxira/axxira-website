TEST={resumes=0,replans=0}
local B={}
ForeverPilotLeveling=B
B.mode="fast";B.settings={enabled=true}
B.Mode=function()return B.mode end
B.T=function(x)return x end
B.Now=function()return 100 end
B.Call=function(fn,...)
 if type(fn)~="function" then return nil end
 local ok,a,b,c,d=pcall(fn,...)
 if ok then return a,b,c,d end
 TEST.lastErr=tostring(a)
 return nil
end
B.List=function(v) if type(v)=="table" then return v elseif type(v)=="number" then return {v} end return {} end
B.Trace=function(self,kind,message)end
local nodes={
 [1110]={id=1110,name="Rocket Car Parts",level=33},
 [339]={id=339,name="Chapter I",level=40},
 [97328]={id=97328,name="Anything of Value",level=35},
}
B.Graph={
 Node=function(self,q)return nodes[q]end,
 Locations=function(self,n,kind)
  if kind=="complete" then return {{points={{map=1441,x=65,y=74,npc=113}}}} end
  if kind=="turnin" then return {{map=1434,x=26.35,y=73.56,npc=4631}} end
  return {{map=1441,x=63,y=52,npc=112}}
 end
}
B.Planner={actions={},FastEligible=function(self,a,s)
 local row=s.log[a.q]; return not row or (row.level or 0)<=s.level+3 end}
B.Recovery={skipped={},
 IsSkipped=function(self,q)return self.skipped[q] or false end,
 IsBlocked=function(self,q)return false end,
 Replan=function() TEST.replans=TEST.replans+1 end}
B.Validator={Eligible=function()return true end}
B.Travel={
 Nearest=function(self,pts,s)return pts[1] end,
 Reachable=function()return true end,
 Seconds=function(self,p,s)return 43 end
}
local guide={id="h33",pack="ding",title="33-34 Thousand Needles",minLevel=33,maxLevel=34,steps={
 {accept={1110}},
 {complete={1110}},
 {go={1441,60,55},text="Take the trail"},
 {level=34,patrol={{1441,55,70}}},
 {accept={999}},
 {complete={339}},
}}
local E={guide=nil,index=1}
Ding={Data={available=true},Engine=E,
 SuggestGuide=function(self,p)
  if p=="ding" then return guide end
 end}
function E:Resume(id)
 TEST.resumes=TEST.resumes+1;self.guide=guide;self.index=1
 return true
end
function E:CurrentStep()
 return self.guide and self.guide.steps[self.index]
end
function E:StepCandidates(step,index)
 if step.level then return {{map=1441,x=54,y=70}} end
 return {{map=1441,x=99,y=99}} -- intentionally wrong for active quest
end
function E:NearestPoint(points) return points[1] end
function E:Describe(step)
 local lines={}
 for _,key in ipairs({"turnin","complete","accept"}) do
  for _,q in ipairs(step[key] or {}) do
   lines[#lines+1]={quest=q,action=key,text=key.." "..tostring(q)}
  end
 end
 return lines
end
function E:Skip()
 self.index=self.index+1
end
TEST.state={now=100,level=33,map=1441,log={},done={},count=0,limit=20,dead=false,
 IsDone=function(self,q)return self.done[q] end}
