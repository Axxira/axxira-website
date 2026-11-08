local B=ForeverPilotLeveling
local L={};B.Learning=L
function L:Tick(s,a)
    local now=s.now; local dt=self.last and math.min(3,now-self.last) or 0;self.last=now
    if not a or not a.q or s.dead or s.taxi or B.settings.paused or B.Call(UnitIsAFK,"player") then self.wx,self.wy=nil,nil;return end
    -- Count active guidance only; a paused/AFK player must not poison estimates.
    local d=a.point and B.Travel:Distance(a.point,s)
    local moving=self.wx and s.wx and ((s.wx-self.wx)^2+(s.wy-self.wy)^2)>1
    self.wx,self.wy=s.wx,s.wy
    if moving or s.combat or (d and d<40 and s.now-(self.progressAt or 0)<60) then
        local r=B.char.learning[a.q] or {seconds=0,deaths=0};B.char.learning[a.q]=r
        r.seconds=(r.seconds or 0)+dt
        if a.kind=="complete" and d and d<250 then r.workSeconds=(r.workSeconds or 0)+dt end
    end
end
function L:Progress() self.progressAt=B.Now() end
function L:Death(a)
    if not B.settings.enabled or B.settings.paused then return end
    if a and a.q then local r=B.char.learning[a.q] or {};B.char.learning[a.q]=r;r.deaths=(r.deaths or 0)+1 end
end
function L:TurnIn(q,xp)
    local r=B.char.learning[q] or {};B.char.learning[q]=r
    r.xp=tonumber(xp) or r.xp;r.samples=(r.samples or 0)+1
    if (r.workSeconds or 0)>=15 then
        r.duration=math.max(30,r.workSeconds)
        local key=self:Bucket(q)
        B.char.pace=B.char.pace or {}
        local bucket=B.char.pace[key] or {samples=0,seconds=300}
        bucket.seconds=bucket.samples==0 and r.duration or bucket.seconds*0.75+r.duration*0.25
        bucket.samples=bucket.samples+1;B.char.pace[key]=bucket
    end
    r.seconds=0;r.workSeconds=0
end
function L:Bucket(q)
    local n=B.Graph:Node(q);local category="delivery"
    if n then
        for _,target in ipairs(B.Graph:Locations(n,"complete")) do
            local kind=target.kind or "other"
            if category=="delivery" then category=kind elseif category~=kind then category="mixed";break end
        end
    end
    return math.floor((n and math.max(1,n.level) or 1)/10)..":"..category
end
function L:Duration(q,default)
    local r=B.char.learning[q]
    if r and r.samples and r.duration then return r.duration end
    local bucket=B.char.pace and B.char.pace[self:Bucket(q)]
    if bucket then
        local weight=math.min(0.8,bucket.samples/5)
        return math.max(60,math.min(1800,default*(1-weight)+bucket.seconds*weight))
    end
    return default
end
