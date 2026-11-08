local B=ForeverPilotLeveling
local initialized=false
function B.SetEnabled(enabled)
    B.settings.enabled=enabled
    if enabled then
        Ding.AutoQuest.Wanted=B.Wanted
        B.Navigation:Install()
        Ding.db.ui.autoAccept=true;Ding.db.ui.autoTurnin=true
        Ding.db.panel.shown=false;Ding.db.arrow.shown=false
        if Ding.Panel.frame then Ding.Panel.frame:Hide() end
        if Ding.Arrow.frame then Ding.Arrow.frame:Hide() end
        B.UI.frame:SetShown(B.settings.shown~=false);B.dirty=true
    else
        if B.Advisor and B.Advisor.frame then B.Advisor.frame:Hide() end
        if B.Market then B.Market:Cancel() end
        B.Validator:Close()
        Ding.AutoQuest.Wanted=B.originalWanted
        B.Navigation:Restore();B.UI.frame:Hide()
        if B.dingPrefs then
            Ding.db.panel.shown=B.dingPrefs.panel;Ding.db.arrow.shown=B.dingPrefs.arrow
            Ding.db.ui.autoAccept=B.dingPrefs.autoAccept;Ding.db.ui.autoTurnin=B.dingPrefs.autoTurnin
            Ding.Panel:Update()
        end
    end
end
function B.Guard(fn)
    local ok,err=pcall(fn)
    if not ok then
        B.errors=(B.errors or 0)+1;B.lastError=tostring(err)
        B.Trace("error",err)
        if B.errors==1 then B.Print("Erreur capturée : "..tostring(err)) end
        -- Stop the brain after repeated faults and restore Ding's original guidance.
        if B.errors>=3 and B.settings and not B.failed then
            B.failed=true
            if B.dingPrefs then B.SetEnabled(false) else B.settings.enabled=false end
            B.Print("Brain suspendu après erreurs. Guide Ding restauré. /fpl debug")
        end
    end
    return ok
end
function B.Wanted()
    local accept,turnin,ahead={},{},{}
    if not B.settings or not B.settings.enabled or B.settings.paused then return accept,turnin,ahead end
    local s=B.State:Refresh();if not s then return accept,turnin,ahead end
    local current=B.Planner.current;local planned={}
    for _,q in ipairs(current and (current.tripIDs or current.plan and current.plan.ids) or {}) do planned[q]=true end
    if current and current.kind=="accept" then planned[current.q]=true end
    -- Only collect quests belonging to the selected loop. Nearby candidates
    -- can be alternatives, and accepting all of them would fill the log.
    for _,a in ipairs(B.Planner.actions or {}) do
        if not B.Recovery:IsSkipped(a.q) then
            if a.kind=="accept" and planned[a.q] and not s.log[a.q] and not s:IsDone(a.q) and not a.node.manual and not a.node.profession and a.travel<120 and s.count<s.limit then accept[a.q]=true end
            if a.kind=="turnin" then turnin[a.q]=true end
        end
    end
    return accept,turnin,ahead
end
function B.Update()
    if not initialized or not B.settings.enabled then return end
    local started=B.Call(debugprofilestop)
    local s=B.State:Refresh()
    B.Journal:Refresh(s,true)
    B.Learning:Tick(s,B.Planner.current)
    B.Recovery:Tick(s,B.Planner.current)
    if not B.settings.paused or not B.Planner.current then
        local a=B.Planner:Plan(s);B.UI:Render(a,s)
        B.char.last={at=time(),version=B.version,level=s.level,map=s.map,quest=a.q,kind=a.kind,key=a.key,logCount=s.count}
    else
        B.UI:Render(B.Planner.current,s)
    end
    B.UI:Tick();B.dirty=false
    local finished=B.Call(debugprofilestop)
    if started and finished then
        B.updateMS=finished-started;B.maxUpdateMS=math.max(B.maxUpdateMS or 0,B.updateMS)
    end
end
local function init()
    if initialized then return end
    B.InitDB()
    if not (Ding and Ding.Data and Ding.Data.available) then B.Print("Données Ding indisponibles.");return end
    B.Graph:Build();B.UI:Create()
    if B.Advisor then
        local ok,err=pcall(B.Advisor.Init,B.Advisor)
        if not ok then B.Advisor.lastError=tostring(err);B.Print("Conseils objets indisponibles : /fpl debug") end
    end
    B.originalWanted=Ding.AutoQuest.Wanted
    B.dingPrefs={panel=Ding.db.panel.shown,arrow=Ding.db.arrow.shown,autoAccept=Ding.db.ui.autoAccept,autoTurnin=Ding.db.ui.autoTurnin}
    B.SetEnabled(B.settings.enabled~=false)
    initialized=true;B.Update();B.Print("Brain v"..B.version.." chargé : "..B.Graph.count.." quêtes. /fpl why")
end
local f=CreateFrame("Frame")
for _,ev in ipairs({"PLAYER_LOGIN","PLAYER_ENTERING_WORLD","PLAYER_XP_UPDATE","PLAYER_LEVEL_UP","QUEST_LOG_UPDATE","QUEST_ACCEPTED","QUEST_TURNED_IN","QUEST_REMOVED","QUEST_WATCH_UPDATE","ZONE_CHANGED_NEW_AREA","BAG_UPDATE_DELAYED","GOSSIP_SHOW","GOSSIP_CLOSED","QUEST_GREETING","QUEST_DETAIL","QUEST_PROGRESS","QUEST_COMPLETE","QUEST_FINISHED","PLAYER_DEAD","PLAYER_ALIVE","PLAYER_UNGHOST","TAXIMAP_OPENED","HEARTHSTONE_BOUND","PLAYER_LOGOUT"}) do pcall(f.RegisterEvent,f,ev) end
for _,ev in ipairs({"MERCHANT_CLOSED","UPDATE_INVENTORY_DURABILITY","UNIT_HEALTH","UNIT_POWER_UPDATE"}) do pcall(f.RegisterEvent,f,ev) end
f:SetScript("OnEvent",function(_,event,...)
    local q,xp=...
    if (event=="UNIT_HEALTH" or event=="UNIT_POWER_UPDATE") and q~="player" then return end
    B.Guard(function()
        if event=="PLAYER_LOGIN" then C_Timer.After(1,function() B.Guard(init) end);return end
        if not initialized then return end
        if event=="PLAYER_LOGOUT" then
            B.Snapshot()
            if B.dingPrefs then
                Ding.db.panel.shown=B.dingPrefs.panel;Ding.db.arrow.shown=B.dingPrefs.arrow
                Ding.db.ui.autoAccept=B.dingPrefs.autoAccept;Ding.db.ui.autoTurnin=B.dingPrefs.autoTurnin
            end
            return
        end
        if not B.settings.enabled then return end
        if event=="QUEST_TURNED_IN" then
            if type(q)=="number" then
                B.char.completed[q]=true;B.Learning:TurnIn(q,xp)
                B.Knowledge:TurnIn(q,xp,B.State.current or B.State:Refresh(),Ding.Compat.NpcUnitID())
            end
            B.Recovery:Invalidate(q)
        elseif event=="QUEST_DETAIL" then
            local id=Ding.Compat.DialogQuestID();local npc=Ding.Compat.NpcUnitID()
            if id then B.Knowledge:Offer(id,npc,B.State:Refresh(),B.Call(GetTitleText)) end
        elseif event=="QUEST_PROGRESS" or event=="QUEST_COMPLETE" then
            local id=Ding.Compat.DialogQuestID()
            if id then B.Knowledge:Position(id,"turnin",Ding.Compat.NpcUnitID(),B.State:Refresh()) end
        elseif event=="PLAYER_LEVEL_UP" then B.Recovery:Invalidate("level")
        elseif event=="GOSSIP_SHOW" then B.Validator:Open("gossip")
        elseif event=="QUEST_GREETING" then B.Validator:Open("greeting")
        elseif event=="GOSSIP_CLOSED" or event=="QUEST_FINISHED" then B.Validator:Close()
        elseif event=="PLAYER_DEAD" then B.Learning:Death(B.Planner.current)
        elseif event=="MERCHANT_CLOSED" then B.Needs:MerchantClosed()
        elseif event=="UPDATE_INVENTORY_DURABILITY" then B.State.durabilityAt=nil
        end
        if event=="QUEST_WATCH_UPDATE" or event=="QUEST_TURNED_IN" or event=="PLAYER_XP_UPDATE" then B.Learning:Progress() end
        if event=="QUEST_ACCEPTED" or event=="QUEST_REMOVED" then B.Recovery:Invalidate(xp or q or "quest") end
        if event=="ZONE_CHANGED_NEW_AREA" or event=="TAXIMAP_OPENED" or event=="HEARTHSTONE_BOUND" then B.Travel.at=nil end
        B.dirty=true
    end)
end)
function B.Snapshot()
    local s=B.State.current;if not s then return end
    local snapshot={version=B.version,at=time(),level=s.level,map=s.map,x=s.x,y=s.y,log=B.Copy(s.log),done=B.Copy(s.done),errors=B.errors or 0,lastError=B.lastError,updateMS=B.updateMS,maxUpdateMS=B.maxUpdateMS,blocked=B.Copy(B.Planner.blocked),knowledge=B.Knowledge.key,loop=B.Copy(B.Planner.current and B.Planner.current.plan),choices={}}
    for i,a in ipairs(B.Planner.actions or {}) do if i<=12 then snapshot.choices[i]={q=a.q,kind=a.kind,name=a.node.name,score=a.score,travel=a.travel,point=B.Copy(a.point),levelFit=B.Copy(a.levelFit)} end end
    B.char.diagnostic=snapshot
    snapshot.trip=B.Copy(B.char.trip)
    snapshot.journal=B.Copy(B.Journal.current)
    snapshot.storage=B.char.storage;snapshot.guid=B.char.guid
    snapshot.advisorError=B.Advisor and B.Advisor.lastError
    snapshot.skipped=B.Copy(B.char.skipped)
end
function B.SkipCurrent()
    B.Guard(function()
        if B.Recovery:Defer(B.Planner.current) then B.Update()
        else B.Print("Aucune quête ou étape à passer.") end
    end)
end
function B.ResumeSkipped(q)
    B.Guard(function()
        if B.Recovery:Resume(q) then B.Update()
        else B.Print("Aucune quête écartée à reprendre.") end
    end)
end
local elapsed,arrow=0,0
f:SetScript("OnUpdate",function(_,dt)
    if not initialized or not B.settings.enabled then return end
    elapsed=elapsed+dt;arrow=arrow+dt
    if elapsed>=(B.dirty and 0.3 or 3) then elapsed=0;B.Guard(B.Update) end
    if arrow>=0.1 then arrow=0;B.Guard(function() B.UI:Tick() end) end
end)
SLASH_FOREVERPILOTLEVELING1="/fpl"
SlashCmdList.FOREVERPILOTLEVELING=function(msg)
    msg=(msg or ""):lower():match("^%s*(.-)%s*$")
    if not initialized then B.Print("Chargement en attente de Ding.");return end
    if msg=="" then B.settings.shown=not B.UI.frame:IsShown();B.UI.frame:SetShown(B.settings.shown)
    elseif msg=="skip" or msg=="next" or msg=="passer" then B.SkipCurrent()
    elseif msg=="reprendre" or msg=="unskip" then B.ResumeSkipped()
    elseif msg=="reprendre tout" or msg=="unskip all" then B.ResumeSkipped("all")
    elseif msg:match("^reprendre%s+%d+$") then B.ResumeSkipped(tonumber(msg:match("(%d+)$")))
    elseif msg=="resync" or msg=="reset" then B.Recovery:Reset();B.Planner.current=nil;B.Guard(B.Update)
    elseif msg=="pause" or msg=="auto" then B.settings.paused=not B.settings.paused;B.dirty=true
    elseif msg=="guide" then Ding.Panel:Toggle()
    elseif msg=="journal" then B.Journal:Print(B.State:Refresh())
    elseif msg=="objets" or msg=="gear" then B.Advisor:Toggle("gear")
    elseif msg=="hdv" then B.Advisor:Toggle("market")
    elseif msg=="addons" then B.Advisor:Toggle("addons")
    elseif msg=="scan" then B.Market:Scan();B.Print(B.Market.status);B.Advisor:RefreshIfShown()
    elseif msg:match("^profil%s+") then
        local key=msg:match("^profil%s+(%S+)$")
        if B.Items:SetProfile(key) then B.Print("Profil objets : "..key);B.Advisor:RefreshIfShown()
        else B.Print("Profils : auto, feral, tank, soin, caster, melee, agile, hybride, chasseur") end
    elseif msg:match("^size%s+") then
        local scale=tonumber(msg:match("^size%s+([%d%.]+)$"))
        if scale then B.settings.scale=math.max(0.8,math.min(1.5,scale));B.UI.frame:SetScale(B.settings.scale) end
    elseif msg=="lock" then B.settings.locked=not B.settings.locked;B.Print(B.settings.locked and "Position verrouillée." or "Panneau déplaçable.")
    elseif msg=="why" then
        local a=B.Planner.current
        B.Print(a and (a.key.." | "..a.confidence.." | "..(a.note or "")) or "Pas de décision")
        local levelNote=a and B.Progression:Describe(a.levelFit);if levelNote then B.Print(levelNote) end
        if a and a.plan then
            B.Print(string.format("Boucle : %d quête(s), environ %d min (estimation)",a.plan.quests,math.ceil(a.plan.seconds/60)))
            for i,t in ipairs(a.plan.steps) do if i<=8 then B.Print(i..". "..t.kind.." · "..t.name) end end
        end
        for i,c in ipairs(B.Planner.actions or {}) do if i<=5 then B.Print(string.format("%d. %s %s | score %.2f | trajet ~%.0fs",i,c.kind,c.node.name,c.score,c.travel)) end end
    elseif msg=="debug" then
        B.Print("v"..B.version.." Ding="..tostring(Ding~=nil).." DB="..tostring(Ding.Data.available).." nodes="..B.Graph.count.." errors="..(B.errors or 0).." profil="..tostring(B.char.storage))
        if B.lastError then B.Print(B.lastError) end
        if B.Advisor and B.Advisor.lastError then B.Print("Objets : "..B.Advisor.lastError) end
        for reason,n in pairs(B.Planner.blocked or {}) do B.Print(reason.."="..n) end
    elseif msg=="off" then B.SetEnabled(false);B.Print("Brain désactivé. Guide Ding restauré.")
    elseif msg=="on" then B.errors=0;B.failed=nil;B.SetEnabled(true);B.Guard(B.Update);B.Print("Brain activé.")
    else B.Print("/fpl | objets | hdv | scan | addons | profil auto | journal | why | skip | reprendre [tout] | resync | pause | size 1 | lock | guide | debug | on | off") end
end
