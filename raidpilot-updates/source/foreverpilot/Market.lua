-- Prices are observed buyouts, not sale guarantees. Never buys, sells or cancels auctions.
local B=ForeverPilotLeveling
local M={maxRows=200000,maxEntries=12000};B.Market=M
local function num(v) return B.Items:Number(v) end
local function field(t,k) return B.Items:Field(t,k) end
function M:Init()
    -- Native character scope avoids mixing regions, rulesets or factions.
    if type(B.char.market)~="table" then B.char.market={prices={}} end
    self.db=B.char.market;self.db.prices=self.db.prices or {}
end
function M:RefreshViews()
    if B.AuctionUI then B.AuctionUI:Refresh() end
    if B.Advisor then B.Advisor:RefreshIfShown() end
end
function M:SetStatus(message)
    self.status=message;self:RefreshViews()
end
function M:CooldownRemaining()
    local remaining=self.requestAt and (900-GetTime()+self.requestAt) or 0
    local at=self.db and num(self.db.lastRequestAt or self.db.lastScan)
    if at then remaining=math.max(remaining,900-time()+at) end
    return math.min(900,math.max(0,math.ceil(remaining)))
end
function M:Availability()
    if not B.settings or not B.settings.enabled then return false,B.T("Scan désactivé"),B.T("Active ForeverPilot pour scanner l'HDV.") end
    if not self.open then return false,B.T("Scanner HDV"),B.T("Ouvre l'hôtel des ventes pour lancer un scan.") end
    if self.pending then return false,B.T("Attente du serveur…"),B.T("Scan demandé · attente du serveur…") end
    if self.job then return false,B.T("Scan · ")..math.floor(100*self.job.index/math.max(1,self.job.n)).." %",self.status end
    if B.Call(InCombatLockdown)==true then return false,B.T("Attends la fin du combat"),B.T("Attends la fin du combat.") end
    local A=C_AuctionHouse
    if not (A and type(A.ReplicateItems)=="function" and type(A.GetReplicateItemInfo)=="function"
        and type(A.GetReplicateItemLink)=="function" and type(A.GetNumReplicateItems)=="function") then
        return false,B.T("Scan indisponible"),B.T("Scan global indisponible ; les recherches HDV peuvent encore alimenter les prix.")
    end
    local seconds=self:CooldownRemaining()
    if seconds>0 then
        local clock=string.format("%d:%02d",math.floor(seconds/60),seconds%60)
        return false,B.T("Scan dans ")..clock,B.T("Prochain scan dans ")..clock..B.T(". Les recherches restent enregistrées.")
    end
    if type(A.IsThrottledMessageSystemReady)=="function" and B.Call(A.IsThrottledMessageSystemReady)~=true then
        return false,B.T("HDV occupé…"),B.T("HDV occupé : attends quelques secondes puis réessaie.")
    end
    return true,B.T("Scanner HDV")
end
function M:Key(link)
    link=B.Items:Text(link);if not link then return end
    local raw=link:match("item:([%d:%-]+)");if not raw then return end
    local fields={};for part in (raw..":"):gmatch("(.-):") do fields[#fields+1]=part end
    local id=tonumber(fields[1]);if not id or id<=0 then return end
    -- Preserve enchants, gems, random suffix and bonus/context fields. Strip only
    -- the unique instance ID and viewer level/spec, which do not describe a variant.
    local variant={};for i=2,7 do variant[#variant+1]=tostring(tonumber(fields[i]) or 0) end
    local tail={};for i=11,#fields do tail[#tail+1]=fields[i]~="" and fields[i] or "0" end
    while #tail>0 and tail[#tail]=="0" do table.remove(tail) end
    return id..":"..table.concat(variant,":")..":"..table.concat(tail,":"),id
end
function M:Money(copper)
    copper=num(copper);if not copper or copper<0 then return "?" end
    copper=math.floor(copper)
    local coins=B.Call(C_CurrencyInfo and C_CurrencyInfo.GetCoinTextureString or GetCoinTextureString,copper,14)
    if type(coins)=="string" and coins~="" then return coins end
    copper=math.floor(copper);local g=math.floor(copper/10000);local s=math.floor(copper/100)%100;local c=copper%100
    local parts={}
    for _,coin in ipairs({{g,"Gold"},{s,"Silver"},{c,"Copper"}}) do
        if coin[1]>0 or (copper==0 and coin[2]=="Copper") then
            parts[#parts+1]=coin[1].."|TInterface\\MoneyFrame\\UI-"..coin[2].."Icon:14:14:2:0|t"
        end
    end
    return table.concat(parts," ")
end
function M:Age(at)
    local age=math.max(0,time()-(num(at) or 0))
    if age<60 then return B.T("à l'instant") elseif age<3600 then return B.T("il y a ")..math.floor(age/60)..B.T(" min")
    elseif age<86400 then return B.T("il y a ")..math.floor(age/3600)..B.T(" h") else return B.T("il y a ")..math.floor(age/86400)..B.T(" j") end
end
function M:Get(link)
    if not self.db then return end
    local key=self:Key(link);local p=key and self.db.prices[key]
    if p and num(p.price) and p.price>0 and num(p.at) then return p,time()-p.at>86400 end
end
function M:Add(target,link,buyout,count)
    local key=self:Key(link);buyout,count=num(buyout),num(count)
    if not key or not buyout or buyout<=0 or not count or count<=0 then return false end
    local price=math.floor(buyout/count);if price<1 then return false end
    local p=target[key]
    if p then p.price=math.min(p.price,price);p.listings=p.listings+1;p.quantity=p.quantity+count
    else target[key]={price=price,listings=1,quantity=count} end
    return true
end
function M:Commit(batch,source,partial,at)
    if not self.db then self:Init() end
    local n=0
    for key,p in pairs(batch) do
        p.at=at or time();p.source=source;p.partial=partial==true;self.db.prices[key]=p;n=n+1
    end
    -- Keep recent entries, not an unbounded history on every scan.
    local ordered={};for key,p in pairs(self.db.prices) do ordered[#ordered+1]={key=key,at=num(p.at) or 0} end
    if #ordered>self.maxEntries then
        table.sort(ordered,function(a,b) if a.at==b.at then return a.key<b.key end;return a.at>b.at end)
        for i=self.maxEntries+1,#ordered do self.db.prices[ordered[i].key]=nil end
    end
    self.db.lastObservation=time();self.db.count=math.min(#ordered,self.maxEntries)
    self:RefreshViews()
    return n
end
function M:Cancel(message)
    self.pending=nil;self.job=nil;self.generation=(self.generation or 0)+1
    if message then self.status=message end
    self:RefreshViews()
end
function M:Scan()
    local A=C_AuctionHouse
    local ready,_,reason=self:Availability()
    if not ready then self:SetStatus(reason);return false end
    if not self.db then self:Init() end
    local previous=self.db.lastRequestAt
    self.pending=true;self.requestAt=GetTime();self.generation=(self.generation or 0)+1
    self.db.lastRequestAt=time()
    local generation=self.generation
    local ok=pcall(A.ReplicateItems)
    if not ok then self.db.lastRequestAt=previous;self.requestAt=nil;self:Cancel(B.T("Le serveur a refusé le scan. Réessaie plus tard."));return false end
    self:SetStatus(B.T("Scan demandé · attente du serveur…"))
    C_Timer.After(40,function()
        if self.generation==generation and self.pending then
            self:Cancel(B.T("Le serveur n'a pas répondu. Aucun nouveau prix enregistré."))
        end
    end)
    return true
end
function M:Replicated()
    if not self.open or not self.pending or not (B.settings and B.settings.enabled) then return end
    local n=num(B.Call(C_AuctionHouse.GetNumReplicateItems))
    if not n or n<=0 then self:Cancel(B.T("Scan vide ou indisponible ; derniers prix conservés."));return end
    self.pending=nil
    self.job={n=math.min(n,self.maxRows),total=n,index=0,prices={},at=time(),generation=self.generation,skipped=0}
    self:Chunk()
end
function M:Chunk()
    local job=self.job
    if not job or not self.open or not B.settings.enabled then return end
    local A=C_AuctionHouse;local last=math.min(job.n-1,job.index+249)
    for index=job.index,last do
        local row=B.Call(function() return {A.GetReplicateItemInfo(index)} end)
        local link=B.Call(A.GetReplicateItemLink,index)
        if not row or not self:Add(job.prices,link,field(row,10),field(row,3)) then job.skipped=job.skipped+1 end
    end
    job.index=last+1
    self.status=B.T("Lecture du scan · ")..math.floor(100*job.index/job.n).." %"
    if job.index>=job.n then
        local partial=job.total>job.n or job.skipped>0
        local n=self:Commit(job.prices,B.T("Scan HDV"),partial,job.at)
        self.job=nil;self.db.lastScan=job.at;self.db.lastScanPartial=partial;self.db.lastScanCount=n
        self.status=n..B.T(" variantes enregistrées")..(partial and B.T(" · résultats partiels") or "")..B.T(". Prix affichés = annonces, pas ventes garanties.")
        B.Print(self.status)
    else
        C_Timer.After(.05,function() if self.job==job then B.Call(self.Chunk,self) end end)
    end
    self:RefreshViews()
end
function M:Observe(kind,key)
    if not self.open or not self.db then return end
    local A=C_AuctionHouse;if not A then return end
    local commodity=kind=="commodity"
    if commodity and not num(key) then return end
    if not commodity and type(B.Public(key))~="table" then return end
    local n=num(B.Call(commodity and A.GetNumCommoditySearchResults or A.GetNumItemSearchResults,key))
    if not n or n<=0 then return end
    local batch={};local missing=false
    for i=1,math.min(n,500) do
        local r=B.Call(commodity and A.GetCommoditySearchResultInfo or A.GetItemSearchResultInfo,key,i)
        local link=commodity and ("item:"..key) or field(r,"itemLink")
        local count=num(field(r,"quantity"));local price=num(field(r,commodity and "unitPrice" or "buyoutAmount"))
        if commodity and price and count then price=price*count end
        if not self:Add(batch,link,price,count) then missing=true end
    end
    local full=B.Call(commodity and A.HasFullCommoditySearchResults or A.HasFullItemSearchResults,key)==true
    self:Commit(batch,B.T("Recherche HDV"),not full or missing or n>500)
end
function M:Event(event,arg)
    if event=="AUCTION_HOUSE_CLOSED" then self.open=false;self:Cancel(B.T("HDV fermé · derniers prix conservés."));return end
    if not B.settings or not B.settings.enabled then return end
    if event=="AUCTION_HOUSE_SHOW" then
        self.open=true
        local count=self.db and self.db.count or 0
        self:SetStatus(count>0 and (count..B.T(" variantes mémorisées · survole un objet pour son prix.")) or B.T("Prêt · lance un scan ou recherche un objet à l'HDV."))
    elseif event=="REPLICATE_ITEM_LIST_UPDATE" then self:Replicated()
    elseif event=="COMMODITY_SEARCH_RESULTS_UPDATED" or event=="COMMODITY_SEARCH_RESULTS_ADDED" then self:Observe("commodity",B.Public(arg))
    elseif event=="ITEM_SEARCH_RESULTS_UPDATED" or event=="ITEM_SEARCH_RESULTS_ADDED" then self:Observe("item",B.Public(arg)) end
end
