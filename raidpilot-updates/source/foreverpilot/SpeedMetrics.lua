-- Local-only performance recorder for route optimization.
-- Data stay in this character's SavedVariables, without names or uploads.
local B=ForeverPilotLeveling
local M={};B.SpeedMetrics=M
local function sane(x)
    return type(x)=="number" and x==x and x>=0 and x<math.huge
end
function M:Data()
    if not B.char then return nil end
    local data=B.char.speedMetrics
    if type(data)~="table" or data.schema~=1 then
        data={schema=1,seconds=0,xp=0,travel=0,combat=0,dead=0,
            routeSeconds=0,switches=0,splits={},recoverySeconds=0}
        B.char.speedMetrics=data
    end
    if type(data.splits)~="table" then data.splits={} end
    return data
end
function M:XP(previous,current)
    if not previous or not current or not sane(previous.xp) or not sane(current.xp) then return 0 end
    if previous.level==current.level then return math.max(0,current.xp-previous.xp) end
    if previous.level+1==current.level and sane(previous.maxXP) then
        return math.max(0,previous.maxXP-previous.xp+current.xp)
    end
    return 0 -- Unsupported multi-level jump: do not invent XP.
end
function M:Finish(data)
    local step=self.step
    if not step or step.seconds<15 then self.step=nil;return end
    local splits=data.splits
    splits[#splits+1]={guide=step.guide,step=step.index,
        level=step.level,seconds=math.floor(step.seconds+.5),
        xp=math.floor(step.xp+.5),travel=math.floor(step.travel+.5),
        combat=math.floor(step.combat+.5),dead=math.floor(step.dead+.5)}
    while #splits>160 do table.remove(splits,1) end
    self.step=nil
end
function M:Observe(s,a)
    if not s or not sane(s.now) then return end
    local data=self:Data()
    if not data then return end
    local active=s.paceEligible and B.Mode()=="fast" and B.settings and B.settings.enabled
    local key=a and a.routeStep and a.routeGuideId and a.routeIndex and
        (tostring(a.routeGuideId)..":"..tostring(a.routeIndex)) or nil
    local old=self.last
    if old then
        local dt=s.now-old.now
        if old.active and active and sane(dt) and dt<=10 then
            local gain=self:XP(old,s)
            data.seconds=data.seconds+dt;data.xp=data.xp+gain
            if old.dead then
                data.dead=data.dead+dt
            elseif old.moving or old.taxi then
                data.travel=data.travel+dt
            elseif old.combat then
                data.combat=data.combat+dt
            end
            if old.routeKey then data.routeSeconds=data.routeSeconds+dt
            elseif old.recovery then data.recoverySeconds=data.recoverySeconds+dt end
            if self.step and self.step.key==old.routeKey and old.routeKey then
                self.step.seconds=self.step.seconds+dt
                self.step.xp=self.step.xp+gain
                if old.dead then self.step.dead=self.step.dead+dt
                elseif old.moving or old.taxi then self.step.travel=self.step.travel+dt
                elseif old.combat then self.step.combat=self.step.combat+dt end
            end
        end
        if old.routeKey~=key or not active then
            if old.active and active and old.routeKey and key and old.routeKey~=key then
                data.switches=data.switches+1
            end
            self:Finish(data)
        end
    end
    if key and active and (not self.step or self.step.key~=key) then
        self.step={key=key,guide=tostring(a.routeGuideId),index=a.routeIndex,
            level=s.level,seconds=0,xp=0,travel=0,combat=0,dead=0}
    end
    self.last={now=s.now,xp=s.xp,maxXP=s.maxXP,level=s.level,
        moving=s.moving or false,taxi=s.taxi or false,
        combat=s.combat or false,dead=s.dead or false,
        active=not not active,routeKey=key,recovery=a and a.kind=="recovery" or false}
end
function M:Flush()
    local data=self:Data()
    if data then self:Finish(data) end
    self.last=nil
end
function M:Summary()
    local d=self:Data()
    if not d then return nil end
    local seconds=d.seconds or 0
    local xp=d.xp or 0
    local rate=seconds>0 and xp*3600/seconds or 0
    return {seconds=seconds,xp=xp,rate=rate,
        travel=seconds>0 and (d.travel or 0)/seconds or 0,
        route=seconds>0 and (d.routeSeconds or 0)/seconds or 0,
        recovery=seconds>0 and (d.recoverySeconds or 0)/seconds or 0,
        switches=d.switches or 0,samples=#d.splits,
        sufficient=seconds>=900 and xp>0}
end
function M:SlowSteps(count)
    local d=self:Data()
    if not d then return {} end
    local rows={}
    for _,step in ipairs(d.splits) do
        if sane(step.seconds) and step.seconds>=120 then
            rows[#rows+1]=step
        end
    end
    table.sort(rows,function(a,b)
        local rateA=(a.xp or 0)/a.seconds
        local rateB=(b.xp or 0)/b.seconds
        if rateA~=rateB then return rateA<rateB end
        return a.seconds>b.seconds
    end)
    while #rows>(count or 5) do table.remove(rows) end
    return rows
end
function M:Report()
    local summary=self:Summary()
    if not summary then return end
    if summary.seconds<60 then
        B.Print(B.T("Mesures FastXP : joue au moins une minute pour obtenir un bilan."))
        return
    end
    B.Print(string.format(B.T("FastXP : %.0f XP/h sur %.1f min · déplacements %.0f%% · itinéraire %.0f%%"),
        summary.rate,summary.seconds/60,summary.travel*100,summary.route*100))
    B.Print(string.format(B.T("Temps de reprise %.0f%% · changements d'étape %d"),
        summary.recovery*100,summary.switches))
    if not summary.sufficient then
        B.Print(B.T("Échantillon insuffisant : vise 15 min de jeu actif avant de comparer les parcours."))
        return
    end
    for _,row in ipairs(self:SlowSteps(3)) do
        B.Print(string.format(B.T("Étape lente : %s · %d · %.0f XP en %.1f min"),
            tostring(row.guide),row.step or 0,row.xp or 0,row.seconds/60))
    end
end
function M:Reset()
    if B.char then B.char.speedMetrics=nil end
    self.step=nil;self.last=nil
end
