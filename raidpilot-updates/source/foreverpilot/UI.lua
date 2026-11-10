local B=ForeverPilotLeveling
local U={width=344,height=132};B.UI=U
local verbs={accept=B.T("PRENDS"),complete=B.T("TERMINE"),turnin=B.T("RENDS"),grind=B.T("XP D'APPOINT"),corpse=B.T("RETOUR AU CORPS"),done=B.T("NIVEAU 60"),recovery=B.T("RÉORIENTATION"),service=B.T("FAIS UNE HALTE"),rest=B.T("RÉCUPÈRE")}
local colors={normal={0.40,0.85,0.80},turnin={0.94,0.76,0.43},recovery={1,0.65,0.36},pause={0.60,0.65,0.72}}
local travelVerbs={fly=B.T("PRENDS LE VOL"),hearth=B.T("PIERRE DE FOYER"),boat=B.T("PRENDS LE BATEAU"),zeppelin=B.T("PRENDS LE ZEPPELIN"),tram=B.T("PRENDS LE TRAM"),passage=B.T("SUIS LE PASSAGE"),teleport=B.T("TÉLÉPORTATION")}
B.LocalizeLabels(verbs);B.LocalizeLabels(travelVerbs)
local arrowTexture="Interface\\AddOns\\Ding\\Media\\Textures\\arrow_halo_gold"
local mapTexture="Interface\\Icons\\INV_Misc_Map_01"
function U:Symbol(texture)
    if self.symbol~=texture then self.arrow:SetTexture(texture);self.symbol=texture end
    self.arrow:SetRotation(0);self.arrow:Show()
end
local function font(f,size,x,y,width,height)
    local t=f:CreateFontString(nil,"OVERLAY");t:SetFont(STANDARD_TEXT_FONT,size,"")
    t:SetPoint("TOPLEFT",f,"TOPLEFT",x,-y);t:SetWidth(width);t:SetHeight(height or size+4)
    t:SetJustifyH("LEFT");t:SetJustifyV("TOP");t:SetTextColor(0.94,0.96,0.98)
    t:SetWordWrap(false)
    return t
end
local function rect(f,x,y,w,h,r,g,b,a)
    local t=f:CreateTexture(nil,"ARTWORK");t:SetColorTexture(r,g,b,a)
    t:SetPoint("TOPLEFT",f,"TOPLEFT",x,-y);t:SetSize(w,h);return t
end
function U:View(a,s)
    local v={verb=verbs[a.kind] or "CONTINUE",title=a.node and a.node.name or a.label or "",detail=a.objective and a.objective.text or a.note or "",color=colors.normal}
    v.tag=a.confidence=="LIVE" and "LIVE" or a.confidence=="RECOVERY" and B.T("ADAPTATION") or B.T("GUIDAGE")
    if a.levelFit and a.levelFit.questLevel then v.verb=v.verb..B.T(" · NIV. ")..a.levelFit.questLevel end
    if a.reward then v.verb=(verbs[a.kind] or "")..B.T(" · BUTIN BLEU À COMPARER") end
    local log=a.q and s.log[a.q];local have,need=0,0
    for _,o in ipairs(log and log.objectives or {}) do
        local total=o.numRequired or 1;need=need+total
        have=have+math.min(total,o.numFulfilled or (o.finished and total or 0))
    end
    v.fraction=need>0 and math.max(0,math.min(1,have/need)) or (a.kind=="turnin" and 1 or 0)
    v.footer=a.kind=="turnin" and B.T("RETOUR HUB") or a.kind=="complete" and B.T("NE RENTRE PAS ENCORE") or B.T("BOUCLE EN COURS")
    local tripCount=a.tripIDs and #a.tripIDs or a.plan and a.plan.quests or 0
    if tripCount>1 then v.footer=v.footer.."  ·  "..tripCount..B.T(" quêtes") end
    if v.detail=="" and a.point and a.point.label then v.detail=B.T("Parle à ")..a.point.label end
    if a.kind=="turnin" then v.color=colors.turnin end
    if a.kind=="grind" then v.footer=B.T("Quêtes réévaluées à chaque progression") end
    if a.kind=="rest" then v.footer=B.T("La boucle t'attend");v.color=colors.turnin end
    if a.kind=="service" then v.footer=B.T("Reprise des quêtes après cette halte");v.color=colors.turnin end
    if a.kind=="recovery" then v.title=B.T("Une nouvelle direction");v.footer=B.T("Ouvre un PNJ de quêtes à proximité");v.color=colors.recovery end
    if a.kind=="corpse" then v.title=s.ghost and B.T("Rejoins ton corps") or B.T("Reviens quand tu es prêt");v.footer=B.T("La boucle reprend après résurrection") end
    if a.kind=="done" then v.title=B.T("L'aventure continue.");v.detail=B.T("Niveau 60 atteint");v.footer=B.T("Objectif accompli") end
    if a.itemStart then v.verb=B.T("OUVRE L'OBJET") end
    if a.collection then v.footer=string.format(B.T("AVANT DE PARTIR · %d/%d quêtes prises"),a.collection.confirmed,a.collection.total) end
    if a.levelFit and a.levelFit.keep=="prerequisite" then v.footer=B.T("PRÉREQUIS POUR LA SUITE")
    elseif a.levelFit and a.levelFit.keep=="almostDone" then v.footer=B.T("PRESQUE FINI · PUIS ON AVANCE") end
    if a.outing then
        if a.collection then v.footer=string.format(B.T("PRÉPARATION · %d/%d prises"),a.collection.confirmed,a.collection.total)
        elseif a.outing.nearby then v.footer=B.T("REMETS ICI · PUIS ON CONTINUE")
        elseif a.outing.remaining>0 then v.footer=string.format(B.T("RESTE EN ZONE · %d à terminer"),a.outing.remaining)
        else v.footer=string.format(B.T("RETOUR HUB · %d à rendre"),a.outing.ready) end
    end
    if B.Recovery.notice and B.Recovery.notice.untilAt>s.now then v.footer=B.Recovery.notice.text;v.color=colors.recovery end
    if B.settings.paused then v.tag="PAUSE";v.footer=B.T("Clic droit pour reprendre");v.color=colors.pause end
    local badge,badgeColor=B.Journal:Badge()
    if badge then v.tag=badge;v.tagColor=badgeColor end
    if a.classPriority then
        v.tag=B.T("CLASSE PRIORITAIRE");v.tagColor={0.74,0.66,1}
        if not a.outing then v.footer=B.Journal:IsClass(a.node) and B.T("PROGRESSION DE CLASSE") or B.T("PRÉREQUIS DE CLASSE") end
    end
    if a.service=="flight" then
        v.tag=B.T("TRAJET AÉRIEN");v.tagColor=colors.turnin;v.verb=B.T("PARLE AU MAÎTRE DE VOL")
        v.footer=B.T("À DÉCOUVRIR AVANT DE PARTIR")
    end
    if B.settings.paused then v.tag="PAUSE";v.footer=B.T("Clic droit pour reprendre");v.tagColor=colors.pause end
    return v
end
function U:Tooltip()
    local a=self.current;if not a then return end
    GameTooltip:SetOwner(self.frame,"ANCHOR_BOTTOM");GameTooltip:SetText(a.node and a.node.name or "ForeverPilot")
    B.Journal:Tooltip()
    local levelNote=B.Progression:Describe(a.levelFit)
    if levelNote then GameTooltip:AddLine(levelNote,0.94,0.76,0.43,true) end
    if self.routeHint then GameTooltip:AddLine(self.routeHint,0.8,0.9,0.94,true) end
    if a.savedTravel then GameTooltip:AddLine(string.format(B.T("En finissant ici : environ %d min de trajet évitées (estimation)."),math.max(1,math.floor(a.savedTravel/60))),.4,.85,.8,true) end
    if a.reward then
        GameTooltip:AddLine(B.T("Récompense observée : ")..a.reward.link,.4,.65,1,true)
        GameTooltip:AddLine(B.T("Statistiques utiles pour ")..(a.reward.profile or "")..B.T(". Compare les effets avant de choisir."),.75,.83,.95,true)
        for i,d in ipairs(a.reward.deltas or {}) do if i<=3 then GameTooltip:AddLine(d.text,.75,.83,.95,true) end end
    end
    if self.view.detail~="" then GameTooltip:AddLine(self.view.detail,1,1,1,true) end
    if a.note and a.note~=self.view.detail then GameTooltip:AddLine(a.note,0.8,0.85,0.90,true) end
    if a.tripIDs then
        GameTooltip:AddLine(B.T("Quêtes de cette sortie"),0.40,0.85,0.80,true)
        for _,q in ipairs(a.tripIDs) do
            local row=self.state.log[q];local n=B.Graph:Node(q)
            local status=row and (row.ready and B.T("À rendre") or B.T("Dans le journal")) or B.T("À prendre")
            GameTooltip:AddLine(status.." · "..(n and n.name or tostring(q)),0.78,0.83,0.89,true)
        end
    elseif a.plan then
        GameTooltip:AddLine(string.format(B.T("Boucle proposée · %d quête(s) · ~%d min"),a.plan.quests,math.ceil(a.plan.seconds/60)),0.40,0.85,0.80,true)
        for i,t in ipairs(a.plan.steps) do if i<=6 then GameTooltip:AddLine(i..". "..(verbs[t.kind] or t.kind).." "..t.name,0.78,0.83,0.89,true) end end
    end
    local rate,eta=B.State:Pace()
    if rate then GameTooltip:AddLine(string.format(B.T("%.0f XP/h · Prochain niveau ~%d min"),rate,math.ceil((eta or 0)/60)),0.9,0.78,0.5,true) end
    GameTooltip:AddLine(B.T("Source : ")..a.confidence,0.62,0.68,0.76,true)
    GameTooltip:AddLine(B.T("Glisser : déplacer · Clic droit : pause"),0.62,0.68,0.76)
    GameTooltip:AddLine(B.T("Passer : écarter la quête · Clic droit sur Passer : reprendre"),0.62,0.68,0.76,true)
    GameTooltip:AddLine(B.T("/fpl why : détails"),0.62,0.68,0.76)
    GameTooltip:Show()
end
function U:Create()
    if self.frame then return end
    local f=CreateFrame("Frame","ForeverPilotBrainFrame",UIParent)
    self.frame=f;f:SetSize(self.width,self.height);f:SetScale(B.settings.scale or 1)
    f:SetPoint(B.settings.point or "TOP",UIParent,B.settings.point or "TOP",B.settings.x or 0,B.settings.y or -90)
    f:SetMovable(true);f:EnableMouse(true);f:RegisterForDrag("LeftButton");f:SetClampedToScreen(true)
    local bg=f:CreateTexture(nil,"BACKGROUND");bg:SetPoint("TOPLEFT",f,"TOPLEFT",-12,12);bg:SetSize(368,156)
    bg:SetTexture("Interface\\AddOns\\ForeverPilot_Leveling\\Media\\Panel")
    bg:SetTexCoord(0,368/512,0,156/256)
    self.accent=rect(f,14,29,316,1,0.40,0.85,0.80,0.22)
    self.header=font(f,10,14,12,150,14);self.header:SetText("ForeversPilot FastXp");self.header:SetTextColor(0.73,0.81,0.87)
    self.tag=font(f,9,190,12,140,14);self.tag:SetJustifyH("RIGHT")
    -- Confidence remains in the tooltip; the header is reserved for clear controls.
    self.tag:Hide()
    self.arrow=f:CreateTexture(nil,"ARTWORK");self.arrow:SetSize(42,42);self.arrow:SetPoint("TOPLEFT",f,"TOPLEFT",24,-39)
    self.arrow:SetTexture("Interface\\AddOns\\Ding\\Media\\Textures\\arrow_halo_gold")
    self.distance=font(f,11,9,86,72,16);self.distance:SetJustifyH("CENTER")
    rect(f,83,40,1,57,0.45,0.55,0.64,0.18)
    self.verb=font(f,10,97,38,233,14)
    self.action=font(f,15,97,54,233,35);self.action:SetWordWrap(true)
    self.progress=font(f,11,97,92,233,15);self.progress:SetTextColor(0.72,0.79,0.85)
    self.track=rect(f,14,106,316,2,0.17,0.24,0.30,0.7)
    self.bar=rect(f,14,106,1,2,0.40,0.85,0.80,0.8)
    self.loop=font(f,10,14,115,316,14)
    self.skip=CreateFrame("Button",nil,f);self.skip:SetPoint("TOPLEFT",f,"TOPLEFT",266,-111);self.skip:SetSize(64,19)
    self.skip:RegisterForClicks("LeftButtonUp","RightButtonUp")
    local skipBG=self.skip:CreateTexture(nil,"BACKGROUND");skipBG:SetAllPoints(self.skip);skipBG:SetColorTexture(.17,.25,.30,.95)
    self.skipLabel=font(self.skip,10,0,4,64,14);self.skipLabel:SetJustifyH("CENTER");self.skipLabel:SetText(B.T("Passer"))
    self.skip:SetScript("OnEnter",function()
        skipBG:SetColorTexture(.25,.36,.42,1)
        if self.reportUnavailable then
            GameTooltip:SetOwner(self.skip,"ANCHOR_BOTTOM");GameTooltip:SetText(B.T("PNJ sans quête ?"))
            GameTooltip:AddLine(B.T("Clic gauche : signaler l'absence et recalculer. Aucun abandon. Clic droit : mettre la quête de côté durablement."),.85,.9,.94,true)
            GameTooltip:Show();return
        end
        GameTooltip:SetOwner(self.skip,"ANCHOR_BOTTOM");GameTooltip:SetText(B.T("Passer l'étape"))
        GameTooltip:AddLine(B.T("Quête trop difficile en solo ? Clic gauche : mets-la de côté et recalcule le guidage."),.85,.9,.94,true)
        GameTooltip:AddLine(B.T("Elle reste dans ton journal et ne revient pas automatiquement. Les haltes et camps d'XP sont reportés de 5 min."),.7,.78,.84,true)
        local _,last=B.Recovery:LastSkipped()
        if last then GameTooltip:AddLine(B.T("Clic droit : réactiver ")..last.name,.94,.76,.43,true) end
        GameTooltip:AddLine(B.T("/fpl reprendre tout : réactiver les quêtes écartées"),.62,.68,.76,true);GameTooltip:Show()
    end)
    self.skip:SetScript("OnLeave",function() skipBG:SetColorTexture(.17,.25,.30,.95);GameTooltip:Hide() end)
    self.skip:SetScript("OnClick",function(_,button)
        GameTooltip:Hide()
        if self.reportUnavailable then
            if button=="RightButton" then B.SkipCurrent() else B.ReportUnavailable() end
        elseif button=="RightButton" or not B.Recovery:CanSkip(B.Planner.current) then B.ResumeSkipped() else B.SkipCurrent() end
    end)
    f:SetScript("OnDragStart",function() if not B.settings.locked then f:StartMoving() end end)
    f:SetScript("OnDragStop",function() f:StopMovingOrSizing();local p,_,_,x,y=f:GetPoint();B.settings.point=p;B.settings.x=x;B.settings.y=y end)
    f:SetScript("OnMouseUp",function(_,button) if button=="RightButton" then B.settings.paused=not B.settings.paused;B.dirty=true end end)
    f:SetScript("OnEnter",function() self:Tooltip() end)
    f:SetScript("OnLeave",function() GameTooltip:Hide() end)
    f:SetShown(B.settings.shown~=false)
    if B.Options then B.Options:SyncEntryPoints() end
end
function U:Render(a,s)
    if not self.frame then return end
    self.current=a;self.state=s;self.view=self:View(a,s)
    local v=self.view;local c=v.color
    local tc=v.tagColor or c
    self.tag:SetText(v.tag);self.tag:SetTextColor(tc[1],tc[2],tc[3])
    self.action:SetText(v.title);self.verb:SetText(v.verb);self.verb:SetTextColor(c[1],c[2],c[3])
    self.progress:SetText(v.detail);self.loop:SetText(v.footer);self.loop:SetTextColor(c[1],c[2],c[3])
    local canSkip=B.Recovery:CanSkip(a);local showSkip=canSkip or B.Recovery:LastSkipped()~=nil
    self.reportUnavailable=B.Recovery:CanReportUnavailable(a,s)
    self.skip:SetShown(showSkip);self.skipLabel:SetText(self.reportUnavailable and B.T("Absent ?") or canSkip and B.T("Passer") or B.T("Reprendre"));self.loop:SetWidth(showSkip and 244 or 316)
    self.bar:SetWidth(math.max(1,316*v.fraction));self.bar:SetShown(v.fraction>0);self.bar:SetColorTexture(c[1],c[2],c[3],0.8)
end
function U:Tick()
    local a=self.current;if not a or not self.frame:IsShown() then return end
    local s=self.state
    if B.settings.paused then self.arrow:Hide();self.distance:SetText("Pause");return end
    local p=a.point
    if not p then self.arrow:Hide();self.distance:SetText(a.kind=="done" and "60" or "—");B.Navigation:Mark(nil);return end
    local leg=not a.walkOnly and B.Travel:Leg(p) or nil
    -- A hearthstone/teleport is an action performed here, not an arrow pointing
    -- across disconnected world coordinates toward its remote destination.
    if leg and not leg.point and (leg.kind=="hearth" or leg.kind=="teleport") then
        local icon=leg.kind=="hearth" and B.Call(Ding.Compat.ItemIcon,6948) or (leg.spell and B.Call(Ding.Compat.SpellIcon,leg.spell))
        self:Symbol(icon or mapTexture);self.distance:SetText(B.T("À utiliser"))
        self.verb:SetText(travelVerbs[leg.kind]);self.progress:SetText(leg.hint or "")
        self.routeHint=leg.hint;B.Navigation:Mark(nil);return
    end
    local target=leg and leg.point or p
    B.Travel:World(target);B.Navigation:Mark(target)
    self.routeHint=leg and leg.hint or Ding.Compat.MapName(target.map)
    self.verb:SetText(leg and travelVerbs[leg.kind] or self.view.verb)
    if leg and travelVerbs[leg.kind] then self.progress:SetText(leg.hint or self.view.detail) else self.progress:SetText(self.view.detail) end
    local continent,wx,wy=Ding.Compat.GetPlayerWorld()
    if continent and continent==target.continent and wx and target.wx then
        local dx,dy=target.wx-wx,target.wy-wy;local distance=math.sqrt(dx*dx+dy*dy)
        local facing=Ding.Compat.GetFacing()
        if facing then self:Symbol(arrowTexture);self.arrow:SetRotation(Ding.Arrow.Bearing(dx,dy)-facing)
        else self:Symbol(mapTexture);self.progress:SetText(B.T("Orientation indisponible · consulte la carte")) end
        self.distance:SetText(distance<12 and B.T("Sur place") or distance>=1000 and string.format("%.1f k yd",distance/1000) or string.format("%.0f yd",distance))
    else
        self:Symbol(mapTexture);self.distance:SetText(B.T("Carte"))
        self.verb:SetText(B.T("TRAJET À CONFIRMER"))
        self.progress:SetText(leg and leg.hint or B.T("Aucun passage connu · recalcul en cours"))
        self.routeHint=leg and leg.hint or B.T("Aucun passage connu vers cette destination.")
        if not leg and s then B.Navigation:Mark(nil);B.Travel.at=nil;B.Planner.signature=nil;B.dirty=true end
    end
end
