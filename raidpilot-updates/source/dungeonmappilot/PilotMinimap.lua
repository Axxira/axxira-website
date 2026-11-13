-- Pilot minimap launchers: independent of PilotCenter and AtlasLoot.
-- No protected actions, automatic key bindings, inventory or combat operations.
local existing=_G.PilotMinimap
if type(existing)=="table" and existing.api==1 then return end
if existing~=nil then return end
local M={api=1,entries={}};_G.PilotMinimap=M
local function public(v)
    if type(issecretvalue)=="function" then local ok,hidden=pcall(issecretvalue,v);if not ok or hidden then return nil end end
    return v
end
local function call(fn,...)
    if type(fn)~="function" then return nil end
    local ok,a,b,c=pcall(fn,...);if ok then return public(a),public(b),public(c) end
end
local function number(v,lo,hi) v=public(v);return type(v)=="number" and v==v and v> -math.huge and v<math.huge and v>=(lo or -100000) and v<=(hi or 100000) and v or nil end
local function now() return number(call(GetTime),0,1000000000000) or 0 end
local function safe() return call(InCombatLockdown)==false end
local hints={
 enUS="Left click: open / close\nRight click: options\nDrag: move around the minimap",
 frFR="Clic gauche : ouvrir / fermer\nClic droit : options\nGlisser : déplacer autour de la minicarte",
 deDE="Linksklick: öffnen / schließen\nRechtsklick: Optionen\nZiehen: um die Minikarte bewegen",
 esES="Clic izquierdo: abrir / cerrar\nClic derecho: opciones\nArrastrar: mover alrededor del minimapa",
 itIT="Clic sinistro: apri / chiudi\nClic destro: opzioni\nTrascina: sposta attorno alla minimappa",
 ptBR="Clique esquerdo: abrir / fechar\nClique direito: opções\nArraste: mover ao redor do minimapa",
 ruRU="Левая кнопка: открыть / закрыть\nПравая кнопка: настройки\nПеретаскивание: вокруг миникарты",
 koKR="왼쪽 클릭: 열기 / 닫기\n오른쪽 클릭: 설정\n드래그: 미니맵 주위로 이동",
 zhCN="左键：打开 / 关闭\n右键：选项\n拖动：沿小地图移动",
 zhTW="左鍵：開啟 / 關閉\n右鍵：選項\n拖曳：沿小地圖移動"}
hints.enGB=hints.enUS;hints.esMX=hints.esES
function M:Radius()
    -- Read the shared public library setting; never modify AtlasLoot or LibDBIcon.
    if type(LibStub)=="table" and type(LibStub.GetLibrary)=="function" then
        local lib=call(LibStub.GetLibrary,LibStub,"LibDBIcon-1.0",true)
        if type(lib)=="table" then return number(lib.radius,0,100) or 5 end
    end
    return 5
end
function M:Offset(angle,width,height,shape,radius)
    angle=math.rad(number(angle,-10000,10000) or 160)
    local x,y=math.cos(angle),math.sin(angle)
    local w=(number(width,20,2000) or 140)/2+(number(radius,0,100) or 5)
    local h=(number(height,20,2000) or 140)/2+(number(radius,0,100) or 5)
    local square=shape=="SQUARE"
    if type(shape)=="string" and shape~="ROUND" and not square then
        local corner=(y>=0 and "TOP" or "BOTTOM")..(x>=0 and "RIGHT" or "LEFT")
        if shape:find("TRICORNER-",1,true)==1 then square=shape:sub(11)==corner
        elseif shape:find("CORNER-",1,true)==1 then square=shape:sub(8)~=corner
        elseif shape=="SIDE-LEFT" then square=x>=0
        elseif shape=="SIDE-RIGHT" then square=x<0
        elseif shape=="SIDE-TOP" then square=y<0
        elseif shape=="SIDE-BOTTOM" then square=y>=0 end
    end
    if square then local scale=math.max(math.abs(x),math.abs(y));x,y=x/scale,y/scale end
    return x*w,y*h
end
function M:Position(id)
    local e=self.entries[id];if not e or not e.button or not Minimap then return false end
    local s=call(e.config.Settings);if type(s)~="table" then return false end
    local x,y=self:Offset(s.angle or e.config.angle,call(Minimap.GetWidth,Minimap),call(Minimap.GetHeight,Minimap),call(GetMinimapShape),self:Radius())
    e.button:ClearAllPoints();e.button:SetPoint("CENTER",Minimap,"CENTER",x,y)
    return true
end
function M:Refresh(id)
    local e=self.entries[id];if not e or not Minimap then return nil end
    local c=e.config;local s=call(c.Settings);if type(s)~="table" then return nil end
    if not number(s.angle,-10000,10000) then s.angle=c.angle or 160 end
    if s.minimap==nil then s.minimap=true end
    local b=e.button
    if not b then
        b=CreateFrame("Button",c.frameName or (id.."MinimapButton"),Minimap);e.button=b
        b:SetSize(32,32);b:SetFrameStrata("MEDIUM");b:SetFrameLevel((number(call(Minimap.GetFrameLevel,Minimap),0,10000) or 0)+8)
        b:SetClampedToScreen(true);b:EnableMouse(true);b:RegisterForClicks("LeftButtonUp","RightButtonUp");b:RegisterForDrag("LeftButton")
        b.icon=b:CreateTexture(nil,"ARTWORK");b.icon:SetTexture(c.icon);b.icon:SetSize(21,21);b.icon:SetPoint("CENTER",-1,1);b.icon:SetTexCoord(.07,.93,.07,.93)
        if type(b.CreateMaskTexture)=="function" and type(b.icon.AddMaskTexture)=="function" then
            local mask=b:CreateMaskTexture(nil,"ARTWORK");mask:SetTexture("Interface\\CHARACTERFRAME\\TempPortraitAlphaMask","CLAMPTOBLACKADDITIVE","CLAMPTOBLACKADDITIVE")
            mask:SetAllPoints(b.icon);b.icon:AddMaskTexture(mask);b.mask=mask
        end
        b.border=b:CreateTexture(nil,"OVERLAY");b.border:SetTexture("Interface\\Minimap\\MiniMap-TrackingBorder");b.border:SetSize(54,54);b.border:SetPoint("TOPLEFT",0,0)
        b:SetHighlightTexture("Interface\\Minimap\\UI-Minimap-ZoomButton-Highlight","ADD")
        b:SetScript("OnClick",function(self,mouse)
            if self.dragging or now()<(self.suppressUntil or -1) or not safe() then return end
            if mouse~="LeftButton" and mouse~="RightButton" then return end
            if GameTooltip then GameTooltip:Hide() end
            local ok,err=pcall(c.Click,mouse)
            if not ok then e.lastError=tostring(err) end
        end)
        b:SetScript("OnDragStart",function(self)
            if not safe() or (c.ReadOnly and call(c.ReadOnly)) then return end
            self.dragging=true;if GameTooltip then GameTooltip:Hide() end
            self:SetScript("OnUpdate",function(button)
                if not safe() then button.dragging=nil;button:SetScript("OnUpdate",nil);return end
                local db=call(c.Settings);if type(db)~="table" then return end
                local x,y=call(GetCursorPosition);local cx,cy=call(Minimap.GetCenter,Minimap)
                local scale=number(call(Minimap.GetEffectiveScale,Minimap),.01,100)
                x,y,cx,cy=number(x),number(y),number(cx),number(cy)
                if not x or not y or not cx or not cy or not scale then return end
                local rx=(number(call(Minimap.GetWidth,Minimap),20,2000) or 140)/2+M:Radius()
                local ry=(number(call(Minimap.GetHeight,Minimap),20,2000) or 140)/2+M:Radius()
                x=(x/scale-cx)/rx;y=(y/scale-cy)/ry
                if math.abs(x)+math.abs(y)<.00001 then return end
                local a=x==0 and (y>=0 and math.pi/2 or -math.pi/2) or math.atan(y/x)+(x<0 and math.pi or 0)
                db.angle=math.deg(a)%360;M:Position(id)
            end)
        end)
        b:SetScript("OnDragStop",function(self) self.dragging=nil;self.suppressUntil=now()+.15;self:SetScript("OnUpdate",nil) end)
        b:SetScript("OnHide",function(self) self.dragging=nil;self:SetScript("OnUpdate",nil);if GameTooltip then GameTooltip:Hide() end end)
        b:SetScript("OnShow",function() M:Position(id) end)
        b:SetScript("OnEnter",function(self)
            if self.dragging or not GameTooltip then return end
            local language=c.Locale and call(c.Locale) or call(GetLocale)
            local hint=c.Hint and call(c.Hint) or hints[language] or hints.enUS
            GameTooltip:SetOwner(self,"ANCHOR_LEFT");GameTooltip:SetText(c.title or id)
            GameTooltip:AddLine(hint,.88,.85,.72,true);GameTooltip:Show()
        end)
        b:SetScript("OnLeave",function() if GameTooltip then GameTooltip:Hide() end end)
    end
    if not self.hookedMap and type(Minimap.HookScript)=="function" then
        self.hookedMap=Minimap;Minimap:HookScript("OnSizeChanged",function() for key in pairs(M.entries) do M:Position(key) end end)
    end
    self:Position(id);b:SetShown(s.minimap~=false)
    return b
end
function M:Register(id,config)
    if type(id)~="string" or type(config)~="table" or type(config.Settings)~="function" or type(config.Click)~="function" then return nil end
    if not self.entries[id] then self.entries[id]={config=config} end
    return self:Refresh(id)
end
local f=CreateFrame("Frame");M.eventFrame=f
for _,event in ipairs({"PLAYER_LOGIN","PLAYER_ENTERING_WORLD","ADDON_LOADED","UI_SCALE_CHANGED","DISPLAY_SIZE_CHANGED"}) do pcall(f.RegisterEvent,f,event) end
f:SetScript("OnEvent",function() for id in pairs(M.entries) do M:Refresh(id) end end)
