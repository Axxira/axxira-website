ForeverPilotLeveling={
  T=function(x)return x end, LocalizeLabels=function()end,
  Mode=function()return "fast" end, Now=function()return 100 end,
  char={},Trace=function()end,
  Graph={nodes={},classQuests={},Node=function(self,q)return self.nodes[q] end,LocalOpportunity=function()return false end},
  Recovery={IsSkipped=function()return false end},
  Progression={Evaluate=function()return {retire=true,questLevel=19} end,Describe=function()return "old" end},
  Travel={Prepare=function()end},
  FlightPaths={Choose=function(self,s,a)return a end},
  Needs={Choose=function(self,s,a)return a end},
  Journey={Choose=function(self,w,actions,s)return w end},
  Loop={Choose=function(self,actions,s)return actions[1] end},
}
