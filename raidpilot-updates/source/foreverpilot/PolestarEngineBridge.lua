-- FastXP HUD adapter for the self-contained bundled routing engine.
-- The original internal engine owns guide progress, data and nav calculations;
-- FastXP owns the sole visible guide panel. Player quest state remains live.
local B=ForeverPilotLeveling
local R={};B.EmbeddedEngine=R
local questKinds={accept="accept",turnin="turnin",complete="complete",
    kill="complete",collect="complete",interact="complete",use="complete",
    craft="complete",escort="complete"}
local serviceKinds={train=true,buy=true,vendor=true,repair=true,
    fly=true,hearth=true,sethome=true,rest=true}
local function finite(x)
    return type(x)=="number" and x==x and x<math.huge and x>-math.huge
end
local function validPoint(p)
    return type(p)=="table" and type(p.map)=="number"
        and finite(p.x) and finite(p.y)
        and p.x>=0 and p.x<=100 and p.y>=0 and p.y<=100
end
function R:Runtime()
    local ns=_G.ForeverPilot_Engine
    return type(ns)=="table" and type(ns.Engine)=="table" and
        type(ns.Nav)=="table" and ns or nil
end
function R:Enabled(s)
    return s and B.settings and B.settings.enabled and
        B.settings.embeddedEngine~=false and B.Mode()=="fast"
        and self:Runtime()~=nil and not s.dead and not s.taxi
end
function R:RuntimeReady(ns)
    if not (ns and ns.Engine.GetGuide and ns.Engine.GetStepView and
        ns.Engine.GetLiveStep and ns.Engine.GetWaypoint) then return false end
    -- The copied support engine is private and its original UI is hidden.
    if type(ns.Hidden)=="function" and not ns.Hidden() and
        type(ns.SetHidden)=="function" then B.Call(ns.SetHidden,true) end
    return true
end
function R:Guide(s,ns)
    local E=ns.Engine
    local guide=B.Call(E.GetGuide)
    if not guide and not s.combat and type(E.StartGuide)=="function" and
        (not self.lastStart or s.now-self.lastStart>45) then
        self.lastStart=s.now
        B.Call(E.StartGuide)
        guide=B.Call(E.GetGuide)
    end
    return guide
end
function R:Objective(s,view)
    if not view or type(view.goals)~="table" then return nil end
    local candidates={}
    for index,g in ipairs(view.goals) do
        if type(g)=="table" and g.status=="active" and
            g.t~="note" then
            local kind=questKinds[g.t]
            local q=g.quest
            local row=type(q)=="number" and s.log and s.log[q] or nil
            local possible=true
            if q and kind=="accept" then possible=not row and not s:IsDone(q)
            elseif q and kind=="turnin" then possible=row and row.ready
            elseif q and kind=="complete" then possible=row and not row.ready end
            if possible then
                candidates[#candidates+1]={i=index,goal=g,kind=kind}
            end
        end
    end
    if #candidates==0 then return nil end
    local marked=type(view.current)=="number" and view.current or
        type(view.current)=="table" and view.current.index
    if marked then
        for _,entry in ipairs(candidates) do
            if marked==entry.i then return entry end
        end
        -- Do not second-guess the native engine's focused objective.
        -- An unavailable quest is not permission to redirect to another NPC.
        return nil
    end
    return candidates[1]
end
function R:Choose(s,fallback)
    if not self:Enabled(s) then return fallback end
    if fallback and (fallback.kind=="corpse" or fallback.kind=="done" or
        fallback.kind=="rest") then return fallback end
    local ns=self:Runtime()
    if not self:RuntimeReady(ns) then return fallback end
    -- Use the character's verified discovered flight masters before route selection.
    if B.FlightSync then B.FlightSync:Sync(ns,s) end
    local guide=self:Guide(s,ns)
    if not guide then return fallback end
    local E=ns.Engine
    local index=B.Call(E.GetLiveStep) or B.Call(E.GetStepIndex)
    if type(index)~="number" then return fallback end
    local view=B.Call(E.GetStepView,index)
    if type(view)~="table" then return fallback end
    local entry=self:Objective(s,view)
    local goal=entry and entry.goal
    local kind=entry and entry.kind or "zone"
    if not entry and type(view.goals)=="table" then
        for _,g in ipairs(view.goals) do
            if g.t=="level" then kind="grind";break end
            if serviceKinds[g.t] then kind="service" end
        end
    end
    local q=goal and goal.quest
    local label=goal and goal.text or view.title or
        (goal and goal.subject) or B.T("Suis ton parcours")
    local wp=B.Call(E.GetWaypoint)
    local nav=B.Call(ns.Nav.GetTarget)
    local target=(validPoint(nav) and nav)
        or (validPoint(wp) and wp)
        or (goal and validPoint(goal) and goal)
    -- When available, Blizzard's marker for the same held quest is the
    -- authoritative final location. Keep native travel legs for transport.
    local marker=B.QuestMarker and B.QuestMarker:Point({q=q,kind=kind,point=target},s,s.now)
    if marker then target=marker end
    local node=q and B.Graph and B.Graph:Node(q)
    if node and node.name then label=node.name end
    local currentKey="embedded:"..tostring(guide.id)..":"..
        tostring(index)..":"..tostring(entry and entry.i or kind)
    local action={
        key=currentKey,kind=kind,q=q,node=node,
        label=label,note=goal and (goal.detail or goal.note) or view.note,
        point=target,confidence="EMBEDDED ENGINE",batch={},
        routeStep=true,routeGuide=guide.title or guide.id,
        routeGuideId=guide.id,routeIndex=index,
        routeTotal=#(guide.steps or {}),routeSource="bundled",
        embedded=true,embeddedGoal=entry and entry.i,
        questReview=not target and q~=nil
    }
    if target and B.Travel and B.Travel.Seconds then
        action.travel=B.Call(B.Travel.Seconds,B.Travel,target,s)
    end
    self.currentKey=currentKey
    self.last={guide=guide.id,index=index,at=s.now}
    return action
end
function R:Tick(ui,action)
    if not (action and action.embedded and ui and ui.arrow and ui.distance) then return false end
    local now=GetTime and GetTime() or 0
    if self.lastActionKey~=action.key then
        self.lastActionKey=action.key
        self.lastGoodAt=nil
        self.lastAngle=nil
        self.pendingAngle=nil
    end
    local ns=self:Runtime()
    if not ns or not ns.Nav or type(ns.Nav.Sample)~="function" then
        ui.arrow:Hide()
        ui.distance:SetText("—")
        return true
    end
    local state,dist,angle=B.Call(ns.Nav.Sample)
    -- Same quest, same map pin, same arrow. Do not override flight/boat
    -- instructions with a distant quest marker.
    if state~="instruction" and state~="instance" and state~="taxi" and
        B.QuestMarker and B.QuestMarker:Tick(ui,action,ui.state,ns,now) then
        self.lastGoodAt=now
        return true
    end
    -- A brief missing native sample is NOT permission to use Ding's
    -- unrelated destination. Retain the previous bearing for 0.6 seconds.
    if state==nil or state=="none" or state=="unknown" or
        state=="far" or state=="nopos" then
        if self.lastGoodAt and now-self.lastGoodAt<0.6 then return true end
        if state=="far" or state=="nopos" then
            ui:Symbol("Interface\\Icons\\INV_Misc_Map_01")
            ui.distance:SetText(B.T("Carte"))
        else
            ui.arrow:Hide()
            ui.distance:SetText("—")
        end
        return true
    end
    if state=="instance" or state=="taxi" then
        ui.arrow:Hide()
        ui.distance:SetText("—")
        return true
    end
    self.lastGoodAt=now
    if state=="instruction" then
        local t=ns.Nav.travel
        local leg=t and B.Call(t.CurrentLeg)
        local kind=leg and leg.kind
        -- Transport instructions are actions, not a walking bearing:
        -- show the actual transport icon instead of an empty HUD.
        local icons={
            hearth="Interface\\Icons\\INV_Misc_Rune_01",
            teleport="Interface\\Icons\\Spell_Arcane_TeleportOrgrimmar",
            fly="Interface\\Icons\\Ability_Mount_Wyvern_01",
            boat="Interface\\Icons\\INV_Misc_Map_01",
            zeppelin="Interface\\Icons\\INV_Misc_Map_01",
            tram="Interface\\Icons\\INV_Misc_Map_01",
            portal="Interface\\Icons\\Spell_Arcane_PortalOrgrimmar"
        }
        ui:Symbol(icons[kind] or "Interface\\Icons\\INV_Misc_Map_01")
        ui.distance:SetText(kind=="fly" and B.T("PRENDS LE VOL")
            or kind=="hearth" and B.T("PIERRE DE FOYER") or B.T("À utiliser"))
        if ui.verb then
            if kind=="fly" then ui.verb:SetText(B.T("PRENDS LE VOL"))
            elseif kind=="hearth" then ui.verb:SetText(B.T("PIERRE DE FOYER"))
            elseif kind=="boat" then ui.verb:SetText(B.T("PRENDS LE BATEAU"))
            elseif kind=="zeppelin" then ui.verb:SetText(B.T("PRENDS LE ZEPPELIN"))
            elseif kind=="tram" then ui.verb:SetText(B.T("PRENDS LE TRAM")) end
        end
        local label=t and B.Call(t.CurrentLabel)
        if label then
            ui.routeHint=label
            if ui.progress then ui.progress:SetText(label) end
        end
        return true
    end
    if state=="far" or state=="nopos" then
        ui:Symbol("Interface\\Icons\\INV_Misc_Map_01")
        ui.distance:SetText(B.T("Carte"))
        return true
    end
    if state=="arrived" or state=="search" then
        ui:Symbol("Interface\\AddOns\\Ding\\Media\\Textures\\arrow_halo_gold")
        ui.distance:SetText(B.T("Sur place"))
        return true
    end
    if finite(angle) then
        -- Drop a single-frame bearing spike while the navigation graph
        -- recalculates. A sustained turn is accepted on the next sample.
        if self.lastAngle and self.lastBearingAt and now-self.lastBearingAt<0.3 then
            local function gap(a,b)
                return math.abs((a-b+math.pi)%(2*math.pi)-math.pi)
            end
            if gap(angle,self.lastAngle)>1.25 then
                if self.pendingAngle and gap(angle,self.pendingAngle)<0.6 then
                    self.pendingAngle=nil
                else
                    self.pendingAngle=angle
                    angle=self.lastAngle
                end
            else
                self.pendingAngle=nil
            end
        end
        ui:Symbol("Interface\\AddOns\\Ding\\Media\\Textures\\arrow_halo_gold")
        ui.arrow:SetRotation(angle)
        self.lastAngle=angle
        self.lastBearingAt=now
    else
        self.lastAngle=nil
        self.pendingAngle=nil
        ui:Symbol("Interface\\Icons\\INV_Misc_Map_01")
    end
    if finite(dist) and dist>=0 then
        ui.distance:SetText(dist>=1000 and
            string.format(B.T("%.1f k yd"),dist/1000) or
            string.format(B.T("%.0f yd"),dist))
    else ui.distance:SetText(B.T("Trajet")) end
    local travel=ns.Nav.travel
    local legs,index
    if type(ns.Nav.GetRoute)=="function" then
        legs,index=B.Call(ns.Nav.GetRoute)
    end
    if state=="travel" and type(legs)=="table" and type(index)=="number" then
        local leg=legs[index]
        local following=legs[index+1]
        if leg and leg.kind=="walk" and following and following.kind=="fly"
            and ui.verb then ui.verb:SetText(B.T("PARLE AU MAÎTRE DE VOL")) end
    end
    local label=travel and B.Call(travel.CurrentLabel)
    if label and ui.progress then ui.progress:SetText(label) end
    return true
end
function R:Skip(action)
    if not (action and action.embedded) then return false end
    local ns=self:Runtime()
    local E=ns and ns.Engine
    if not (E and type(E.Skip)=="function") then return false end
    local before=B.Call(E.GetLiveStep)
    B.Call(E.Skip)
    local after=B.Call(E.GetLiveStep)
    if before==after then return false end
    B.Recovery:Replan()
    return true
end
