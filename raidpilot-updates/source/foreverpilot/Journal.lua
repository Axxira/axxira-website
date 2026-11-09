-- Advice only: never select/abandon quests or alter the player's quest log.
local B=ForeverPilotLeveling
local J={};B.Journal=J
local classBits={1,2,4,8,16,64,128,256,1024}
local labels={abandon=B.T("À ABANDONNER"),class=B.T("CLASSE · À CONSERVER"),prerequisite=B.T("PRÉREQUIS · À CONSERVER"),fast=B.T("PARCOURS RAPIDE"),unknown=B.T("À VÉRIFIER · CONSERVER"),skipped=B.T("MISE DE CÔTÉ · CONSERVÉE")}
B.LocalizeLabels(labels)
local colors={abandon={1,0.65,0.36},class={0.74,0.66,1},prerequisite={0.40,0.85,0.80},fast={0.40,0.85,0.80},unknown={0.94,0.76,0.43},skipped={0.69,0.76,0.84}}
local order={class=1,abandon=2,skipped=3,unknown=4,prerequisite=5,fast=6}
function J:IsClass(n)
    if not n then return false end
    if B.Graph.classQuests[n.id] then return true end
    local mask=n.raw and n.raw.requiredClasses
    if type(mask)~="number" or mask<=0 then return false end
    local total=0
    for _,bit in ipairs(classBits) do if math.floor(mask/bit)%2==1 then total=total+1 end end
    -- A mask permitting all Classic classes is not a class quest.
    return total>0 and total<#classBits
end
function J:Classify(q,s)
    local n=B.Graph:Node(q);local row=s and s.log[q]
    local status,reason
    if self:IsClass(n) then
        status="class";reason=B.T("Prioritaire pour ta classe dès qu'elle est disponible et faisable en solo. À conserver.")
    elseif n and n.classPrerequisite then
        status="prerequisite";reason=B.T("Prérequis d'une quête de classe : à conserver.")
    elseif n and n.fastPrerequisite then
        status="prerequisite";reason=B.T("Débloque une quête du parcours rapide.")
    elseif n and n.profession then
        status="abandon";reason=B.T("Quête de métier optionnelle, exclue du leveling rapide.")
    elseif n and n.fast then
        status="fast";reason=B.T("Incluse dans le parcours de leveling de ton personnage.")
    elseif n and B.Graph:LocalOpportunity(n,s) then
        status="fast";reason=B.T("Quête de zone ajoutée : objectifs solo adaptés à ton niveau, regroupés dans la sortie.")
    elseif not n then
        status="unknown";reason=B.T("Données manquantes : impossible d'exclure une quête de classe ou un prérequis.")
    else
        status="abandon";reason=B.T("Hors parcours rapide et sans prérequis utile connu.")
    end
    local outleveled=false
    if row and n and (status=="fast" or status=="prerequisite") then
        local fit=B.Progression:Evaluate(n,row.ready and "turnin" or "complete",s)
        if fit.retire then
            outleveled=true
            if n.classPrerequisite or B.Progression:Unlock(n,s,true) then
                status="prerequisite";reason=B.T("Trop basse pour la boucle actuelle, mais prérequis utile pour la suite : conserve-la.")
            else
                status="abandon";reason=B.T("Niveau dépassé : ")..(fit.questLevel or "?")..B.T(" pour ton niveau ")..s.level..B.T(", sans suite utile connue.")
            end
        elseif fit.keep then reason=B.Progression:Describe(fit) end
    end
    local label=labels[status]
    if B.Recovery:IsSkipped(q) then
        label=self:IsClass(n) and B.T("CLASSE · MISE DE CÔTÉ") or labels.skipped
        reason=(self:IsClass(n) and B.T("Quête de classe à conserver. ") or n and n.fastPrerequisite and B.T("Prérequis à conserver. ") or "")..B.T("Écartée du guidage à ta demande. /fpl reprendre ")..q..B.T(" pour la réactiver.")
        status="skipped"
    end
    return {q=q,name=row and row.title or n and n.name or (B.T("Quête ")..q),status=status,label=label,reason=reason,color=colors[status],outleveled=outleveled}
end
function J:Allows(n,s)
    local row=self:Classify(n.id,s)
    if row.status=="skipped" then return false,"skipped" end
    if row.outleveled then return false,"outleveled" end
    if row.status=="abandon" then return false,"outsideFastRoute" end
    return true
end
function J:Refresh(s,notify)
    local report={rows={},byQuest={},abandon=0,class=0,unknown=0}
    for q in pairs(s.log) do
        local row=self:Classify(q,s)
        report.rows[#report.rows+1]=row;report.byQuest[q]=row
        if report[row.status]~=nil then report[row.status]=report[row.status]+1 end
    end
    table.sort(report.rows,function(a,b)
        if order[a.status]~=order[b.status] then return order[a.status]<order[b.status] end
        return a.q<b.q
    end)
    self.current=report
    if notify and not s.combat then
        local seen={}
        for _,row in ipairs(report.rows) do
            seen[row.q]=row.status
            if (not self.seen or self.seen[row.q]~=row.status) and (row.status=="abandon" or row.status=="class" or row.status=="unknown") then
                self:PrintRow(row)
            end
        end
        self.seen=seen
    end
    return report
end
function J:PrintRow(row)
    local hex=row.status=="abandon" and "ffae5c" or row.status=="class" and "bda8ff" or "65d9cc"
    B.Print("|cff"..hex..row.label.."|r · "..row.name.." — "..row.reason)
end
function J:Print(s)
    local report=self:Refresh(s,false)
    B.Print(B.T("Journal · parcours rapide (conseils, aucun abandon automatique)"))
    for _,row in ipairs(report.rows) do self:PrintRow(row) end
    if #report.rows==0 then B.Print(B.T("Aucune quête dans le journal.")) end
end
function J:Badge()
    local r=self.current;if not r then return end
    if r.class>0 then return r.class..B.T(" DE CLASSE"),colors.class end
    if r.abandon>0 then return r.abandon..B.T(" À ABANDONNER"),colors.abandon end
    if r.unknown>0 then return r.unknown..B.T(" À VÉRIFIER"),colors.unknown end
end
function J:Tooltip()
    local r=self.current;if not r or #r.rows==0 then return end
    GameTooltip:AddLine(B.T("Journal · parcours rapide"),0.94,0.96,0.98,true)
    for i,row in ipairs(r.rows) do if i<=8 then
        GameTooltip:AddLine(row.label.." · "..row.name,row.color[1],row.color[2],row.color[3],true)
        GameTooltip:AddLine(row.reason,0.72,0.79,0.85,true)
    end end
    GameTooltip:AddLine(B.T("/fpl journal : liste complète · abandon manuel"),0.62,0.68,0.76,true)
end
