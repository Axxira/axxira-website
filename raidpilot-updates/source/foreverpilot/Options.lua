-- User-facing controls. Guidance and equipment logic stay in their own modules.
local B=ForeverPilotLeveling
local O={tab="general"};B.Options=O
local function text(f,size,x,y,w,h,value,muted)
    local t=f:CreateFontString(nil,"OVERLAY");B.SetUIFont(t,size)
    t:SetPoint("TOPLEFT",f,"TOPLEFT",x,-y);t:SetSize(w,h);t:SetJustifyH("LEFT");t:SetJustifyV("TOP");t:SetWordWrap(true)
    if muted then t:SetTextColor(.67,.76,.81) else t:SetTextColor(.91,.95,.97) end
    t:SetText(value or "");return t
end
local function line(f,y)
    local t=f:CreateTexture(nil,"ARTWORK");t:SetPoint("TOPLEFT",f,"TOPLEFT",20,-y);t:SetSize(480,1);t:SetColorTexture(.25,.39,.44,.5)
end
local function button(f,label,x,y,w,fn)
    local b=CreateFrame("Button",nil,f);b:SetPoint("TOPLEFT",f,"TOPLEFT",x,-y);b:SetSize(w,30)
    b.bg=b:CreateTexture(nil,"BACKGROUND");b.bg:SetAllPoints(b)
    b.label=text(b,11,8,8,w-16,16,label);b.label:SetJustifyH("CENTER")
    function b:Paint(selected,enabled)
        self.selected=selected;self.active=enabled~=false;self:SetEnabled(self.active)
        if selected then self.bg:SetColorTexture(.12,.36,.38,1)
        else self.bg:SetColorTexture(.10,.17,.21,1) end
        self.label:SetTextColor(self.active and .87 or .45,self.active and .94 or .53,self.active and .96 or .57)
    end
    b:Paint(false)
    b:SetScript("OnEnter",function() if b.active then b.bg:SetColorTexture(.19,.35,.39,1) end end)
    b:SetScript("OnLeave",function() b:Paint(b.selected,b.active) end)
    b:SetScript("OnClick",fn);return b
end
function O:CloseFor(fn)
    self.frame:Hide();GameTooltip:Hide();fn()
end
-- One entry below the arrow. It never competes with Items/Options in the header.
-- The test edition and existing subscribers get a quiet status, not an upsell.
function O:SyncPremiumButton()
    local b=self.premiumButton;if not b then return end
    local state=B.distribution=="raidpilot-test" and "included" or B.HasPremium() and "active" or "upgrade"
    local label=state=="included" and B.T("PRO inclus") or state=="active" and B.T("PRO actif") or B.T("VERSION PRO")
    b.state=state;b.label:SetText(label)
    local warm=state=="upgrade"
    b.bg:SetColorTexture(warm and (b.hovered and .43 or .29) or .055,
        warm and .035 or (b.hovered and .27 or .19),warm and .065 or .20,1)
    b.label:SetTextColor(1,warm and .91 or .95,warm and .91 or .94)
    for _,edge in ipairs(b.edges) do edge:SetColorTexture(warm and .98 or .30,warm and .26 or .68,warm and .32 or .65,.9) end
    b.glow:SetShown(warm)
    if not warm then b.phase=0 end
    b:SetShown(true)
end
function O:CreatePremiumButton(guide)
    if self.premiumButton then return end
    local b=button(guide,"",6,115,72,function()
        if GameTooltip then GameTooltip:Hide() end
        self:Toggle("subscription")
    end)
    self.premiumButton=b;b:SetHeight(22);b.phase=0;b.elapsed=0;b.refresh=0
    b.label:SetAllPoints(b);b.label:SetJustifyV("MIDDLE");b.label:SetWordWrap(false);B.SetUIFont(b.label,10)
    local function edge(parent,x,y,w,h,alpha)
        local t=parent:CreateTexture(nil,"BACKGROUND");t:SetPoint("TOPLEFT",parent,"TOPLEFT",x,y);t:SetSize(w,h)
        t:SetColorTexture(.95,.12,.20,alpha);return t
    end
    b.edges={edge(b,0,0,72,1,.9),edge(b,0,-21,72,1,.9),edge(b,0,0,1,22,.9),edge(b,71,0,1,22,.9)}
    b.glow=CreateFrame("Frame",nil,b);b.glow:SetAllPoints(b);b.glow:EnableMouse(false)
    for i=1,3 do
        local a=(4-i)*.17
        edge(b.glow,-i,i,72+2*i,1,a);edge(b.glow,-i,-21-i,72+2*i,1,a)
        edge(b.glow,-i,i,1,22+2*i,a);edge(b.glow,71+i,i,1,22+2*i,a)
    end
    b.glow:SetAlpha(.3)
    b:SetScript("OnEnter",function()
        b.hovered=true;self:SyncPremiumButton()
        if not GameTooltip then return end
        GameTooltip:SetOwner(b,"ANCHOR_BOTTOM")
        if b.state=="upgrade" then
            GameTooltip:SetText(B.T("Passe à FastXP Pro"))
            GameTooltip:AddLine(B.T("Itinéraires XP optimisés et conseils adaptés à ta classe et ta spécialisation."),.88,.93,.96,true)
            GameTooltip:AddLine(B.T("Découvre l'offre et le tarif dans le portail d'abonnement."),1,.72,.68,true)
            GameTooltip:AddLine(B.T("Déjà abonné ? Active ta clé existante, sans repayer."),.72,.82,.85,true)
        else
            GameTooltip:SetText(B.T(b.state=="included" and "PRO inclus · édition RaidPilot" or "FastXP Pro actif"))
            GameTooltip:AddLine(B.T(b.state=="included" and "Toutes les fonctions sont incluses. Aucun abonnement nécessaire." or "Consulte ton accès et gère ton abonnement."),.80,.91,.91,true)
        end
        GameTooltip:Show()
    end)
    b:SetScript("OnLeave",function() b.hovered=false;self:SyncPremiumButton();if GameTooltip then GameTooltip:Hide() end end)
    b:SetScript("OnHide",function() b.hovered=false;b.phase=0;b.glow:SetAlpha(.3) end)
    b:SetScript("OnUpdate",function(_,dt)
        b.refresh=b.refresh+dt;b.elapsed=b.elapsed+dt
        if b.refresh>=1 then b.refresh=0;self:SyncPremiumButton() end
        if b.elapsed<.05 then return end
        local elapsed=b.elapsed;b.elapsed=0
        if b.state~="upgrade" then return end
        -- A slow four-second glow cycle; static during combat, pause or hover.
        if b.hovered or B.settings.paused or B.Call(InCombatLockdown)==true then
            b.glow:SetAlpha(b.hovered and .8 or .3);return
        end
        b.phase=(b.phase+elapsed)%4
        b.glow:SetAlpha(.35+.25*(.5-.5*math.cos(b.phase*math.pi/2)))
    end)
    self:SyncPremiumButton()
end
-- Settings remain reachable even when guidance is hidden, unavailable or stopped.
-- This entry point must not depend on the equipment/auction module initializing.
function O:SyncEntryPoints()
    if not B.settings then return end
    if self.SyncMinimap then self:SyncMinimap() end
    local guide=B.UI and B.UI.frame
    if not self.shortcut then
        self.shortcut=button(UIParent,"ForeversPilot FastXp · "..B.T("Options"),0,0,244,function() self:Toggle() end)
        self.shortcut:SetFrameStrata("MEDIUM");self.shortcut:SetClampedToScreen(true)
    end
    if guide and not self.headerButton then
        self.headerButton=button(guide,B.T("Options"),252,5,94,function() self:Toggle() end)
        self.headerButton:SetHeight(26);self.headerButton.label:SetAllPoints(self.headerButton)
        self.headerButton.label:SetJustifyV("MIDDLE");B.SetUIFont(self.headerButton.label,12)
        self.headerButton:SetScript("OnEnter",function()
            GameTooltip:SetOwner(self.headerButton,"ANCHOR_BOTTOM");GameTooltip:SetText(B.T("Options"))
            GameTooltip:AddLine(B.T("Langue, profil, taille et aide."),.8,.9,.9,true);GameTooltip:Show()
        end)
        self.headerButton:HookScript("OnLeave",function() GameTooltip:Hide() end)
        guide:HookScript("OnShow",function() self:SyncEntryPoints() end)
        guide:HookScript("OnHide",function() self:SyncEntryPoints() end)
    end
    if guide then self:CreatePremiumButton(guide);self:SyncPremiumButton() end
    self.shortcut:ClearAllPoints()
    self.shortcut:SetPoint(B.settings.point or "TOP",UIParent,B.settings.point or "TOP",B.settings.x or 0,B.settings.y or -90)
    self.shortcut:SetScale(B.settings.scale or 1)
    self.shortcut:SetShown(not self.minimapButton and (not guide or not guide:IsShown()))
end
function O:Render()
    if not self.frame then return end
    for key,p in pairs(self.pages) do p:SetShown(key==self.tab);self.tabs[key]:Paint(key==self.tab) end
    local chosen=self.pendingLanguage or B.settings.language or "auto"
    for key,b in pairs(self.languages) do b:Paint(chosen==key) end
    self.languageButton.label:SetText(B.LanguageLabel(chosen).."  >")
    if self.tab~="general" and self.languageMenu then self.languageMenu:Hide() end
    local changed=chosen~=(B.settings.language or "auto")
    local combat=B.Call(InCombatLockdown)==true
    self.apply:Paint(changed,changed and not combat)
    self.languageHint:SetText(combat and B.T("Changement disponible après le combat.") or changed and
        B.T("Appliquer recharge l'interface. Aucun choix de quête ou de profil n'est modifié.") or
        B.T("Auto suit la langue du jeu. Les noms de quêtes et d'objets restent ceux du jeu."))
    local _,_,profile,reason=B.Items:Profile()
    self.profile:SetText(profile or B.T("Choisis ta spécialisation"))
    self.profileNote:SetText(reason and B.T("Spécialisation à préciser pour des conseils adaptés.") or B.T("Les conseils d'objets suivent ce profil."))
    self.pause.label:SetText(B.settings.paused and B.T("Reprendre le guidage") or B.T("Mettre en pause"))
    self.pause:Paint(B.settings.paused)
    for scale,b in pairs(self.sizes) do b:Paint(math.abs((B.settings.scale or 1)-scale)<.01) end
    self.lock.label:SetText(B.settings.locked and B.T("Position : verrouillée") or B.T("Position : déplaçable"));self.lock:Paint(B.settings.locked)
    self.layerShortcut.label:SetText(B.settings.layerShortcut and B.T("Raccourci layer : affiché") or B.T("Raccourci layer : masqué"));self.layerShortcut:Paint(B.settings.layerShortcut)
    local canResume=B.Recovery and B.Recovery:LastSkipped()~=nil
    self.resume:Paint(false,canResume)
    if self.speedRouteToggle then
        local fast=B.Mode()=="fast"
        local on=fast and B.settings.speedRoute~=false
        self.speedRouteToggle.label:SetText(on and B.T("Itinéraire rapide : actif") or B.T("Itinéraire rapide : désactivé"))
        self.speedRouteToggle:Paint(on,fast)
    end
    if self.mapRouteToggle then
        local fast=B.Mode()=="fast" and B.EmbeddedEngine and B.EmbeddedEngine:Runtime()~=nil
        local on=fast and B.settings.mapRoute~=false
        self.mapRouteToggle.label:SetText(on and B.T("Tracé sur carte : actif") or B.T("Tracé sur carte : désactivé"))
        self.mapRouteToggle:Paint(on,fast)
    end
    local free=B.distribution=="raidpilot-test"
    local active=B.HasPremium()
    if self.modeCaption then
        self.modeCaption:SetText(active and B.T("FastXP abonné : route rapide et grind mesuré.") or
            B.T("Zone Explorer gratuit : quêtes recensées, zone par zone."))
    end
    self.accessTitle:SetText(free and B.T("Édition RaidPilot : tout est gratuit") or active and B.T("ForeverPilot Premium actif") or B.T("Zone Explorer gratuit actif · FastXP rapide en abonnement"))
    self.accessDetails:SetText(free and B.T("Toutes les fonctions sont incluses dans ton édition de test. Aucun abonnement ni connexion ne sont nécessaires.") or active and B.T("Après chaque renouvellement payé, récupère un nouveau code sur le portail. Ton accès déjà payé reste reconnu.") or B.T("Active Pro directement ici, sans programme à installer. Paie sur le portail, copie le code d’activation, puis colle-le ci-dessous."))
    self.accessLink:SetShown(not free);self.copyLink:SetShown(not free)
    if self.accessCode then
        self.accessCode:SetShown(not free);self.accessCodeLabel:SetShown(not free);self.activateCode:SetShown(not free)
        self.activateCode:Paint(false,not combat and B.Access~=nil)
        local imported=B.Access and B.Access:Current()
        self.accessResult:SetText(self.accessMessage or (imported and string.format(B.T("Valable jusqu’au %s · renouveler le code dans le portail"),date("%Y-%m-%d",imported.expires))) or "")
        self.accessResult:SetShown(not free)
    end
    self:SyncPremiumButton()
end
function O:MaybeWelcome()
    if not B.char or not B.settings or not B.settings.enabled or B.char.welcomeSeen or B.Call(InCombatLockdown)==true or B.Call(UnitIsDeadOrGhost,"player")==true then return end
    self:Toggle(B.premiumBlocked and "subscription" or "general")
    B.char.welcomeSeen=true
end
function O:Create()
    if self.frame then return end
    local f=CreateFrame("Frame","ForeverPilotOptionsFrame",UIParent);self.frame=f
    f:SetSize(520,615);f:SetPoint("CENTER",UIParent,"CENTER",0,0);f:SetClampedToScreen(true)
    f:SetFrameStrata("DIALOG");f:EnableMouse(true)
    local bg=f:CreateTexture(nil,"BACKGROUND");bg:SetAllPoints(f);bg:SetColorTexture(.035,.06,.08,.99)
    local accent=f:CreateTexture(nil,"ARTWORK");accent:SetPoint("TOPLEFT",f,"TOPLEFT",0,0);accent:SetSize(520,2);accent:SetColorTexture(.4,.85,.8,1)
    text(f,17,20,18,410,24,"ForeversPilot FastXp")
    self.modeCaption=text(f,11,20,43,440,17,B.T("Zone Explorer gratuit : quêtes recensées, zone par zone."),true)
    button(f,"×",467,16,32,function() f:Hide() end)
    self.pages={};self.tabs={}
    for i,entry in ipairs({{"general",B.T("Général")},{"tools",B.T("Outils")},{"help",B.T("Aide")},{"subscription",B.T("Abonnement")}}) do
        local key=entry[1]
        self.tabs[key]=button(f,entry[2],20+(i-1)*122,74,114,function() self.tab=key;self:Render() end)
        local p=CreateFrame("Frame",nil,f);p:SetPoint("TOPLEFT",f,"TOPLEFT",0,0);p:SetSize(520,550);self.pages[key]=p
    end
    local p=self.pages.general
    text(p,13,20,124,480,20,B.T("Langue"))
    self.languages={}
    self.languageButton=button(p,"",20,152,480,function()
        self.languageMenu:SetShown(not self.languageMenu:IsShown())
    end)
    local languageMenu=CreateFrame("Frame",nil,p)
    self.languageMenu=languageMenu;languageMenu:SetSize(480,150)
    languageMenu:SetPoint("TOPLEFT",self.languageButton,"BOTTOMLEFT",0,-3)
    languageMenu:SetFrameLevel(f:GetFrameLevel()+20);languageMenu:EnableMouse(true)
    local languageBackground=languageMenu:CreateTexture(nil,"BACKGROUND")
    languageBackground:SetAllPoints(languageMenu);languageBackground:SetColorTexture(.035,.06,.08,1)
    for i,entry in ipairs(B.LanguageChoices()) do
        local key=entry[1]
        self.languages[key]=button(languageMenu,entry[2],6+((i-1)%3)*158,6+math.floor((i-1)/3)*35,152,function()
            self.pendingLanguage=key;languageMenu:Hide();self:Render()
        end)
    end
    languageMenu:Hide()
    f:HookScript("OnHide",function() languageMenu:Hide() end)
    self.languageHint=text(p,11,20,198,306,42,"",true)
    self.apply=button(p,B.T("Appliquer la langue"),348,195,152,function()
        if B.Call(InCombatLockdown)==true then self:Render();return end
        if self.pendingLanguage and self.pendingLanguage~=B.settings.language then B.SetLanguage(self.pendingLanguage) end
    end)
    line(p,248)
    text(p,13,20,266,300,20,B.T("Mon personnage"))
    self.profile=text(p,12,20,293,322,32)
    self.profileNote=text(p,10,20,328,480,20,"",true)
    button(p,B.T("Modifier le profil"),348,288,152,function() self:CloseFor(function() B.Advisor:ToggleProfile() end) end)
    line(p,360)
    self.pause=button(p,"",20,377,200,function() B.settings.paused=not B.settings.paused;B.dirty=true;self:Render() end)
    text(p,11,236,380,264,34,B.T("La prochaine étape se choisit automatiquement."),true)
    text(p,13,20,428,200,20,B.T("Taille du panneau"))
    self.sizes={}
    for i,scale in ipairs({.8,1,1.2}) do
        local n=scale;self.sizes[n]=button(p,math.floor(n*100+.5).." %",284+(i-1)*74,421,68,function()
            B.settings.scale=n;B.UI.frame:SetScale(n);self:Render()
        end)
    end
    self.lock=button(p,"",20,465,230,function() B.settings.locked=not B.settings.locked;self:Render() end)
    self.layerShortcut=button(p,"",270,465,230,function()
        B.settings.layerShortcut=not B.settings.layerShortcut;if B.LayerUI then B.LayerUI:Refresh() end;self:Render()
    end)
    p=self.pages.tools
    self.mapRouteToggle=button(p,"",20,124,230,function()
        if B.Mode()~="fast" then return end
        B.settings.mapRoute=not (B.settings.mapRoute~=false)
        B.SetEnabled(B.settings.enabled~=false)
        B.Guard(B.Update);self:Render()
    end)
    self.speedRouteToggle=button(p,"",270,124,230,function()
        if B.Mode()~="fast" then return end
        B.settings.speedRoute=not (B.settings.speedRoute~=false)
        if B.SpeedRoute then B.SpeedRoute:Reset() end
        B.Guard(B.Update);self:Render()
    end)
    local cards={
        {B.T("Équipement"),B.T("Upgrade oui/non, gains et pertes selon ta spécialisation."),function() B.Advisor:Toggle("gear") end},
        {B.T("Hôtel des ventes"),B.T("Lance un scan à l'HDV, puis consulte les prix dans tes sacs."),function() B.Advisor:Toggle("market") end},
        {B.T("Changer de layer"),B.T("Ouvre ChangeLayer ou prépare une demande dans Général."),function() B.LayerUI:Toggle() end},
    }
    for i,entry in ipairs(cards) do
        local action=entry[3];local y=166+(i-1)*88
        button(p,entry[1],20,y,210,function() self:CloseFor(action) end)
        text(p,11,246,y+1,254,48,entry[2],true)
    end
    line(p,426)
    self.resume=button(p,B.T("Reprendre les quêtes écartées"),20,446,230,function() B.ResumeSkipped("all");self:Render() end)
    self.review=button(p,B.T("REC_OPEN"),270,446,230,function() self:CloseFor(function() B.RecoveryUI:Open() end) end)
    text(p,11,20,487,240,35,B.T("Passer met une quête de côté sans l'abandonner dans ton journal."),true)
    button(p,B.T("Rapport de vitesse XP/h"),270,484,230,function()
        if B.SpeedMetrics then B.SpeedMetrics:Report() end
    end)
    p=self.pages.help
    local tips={
        {B.T("1. Suis la flèche"),B.T("Va au point indiqué et termine l'objectif. ForeverPilot réévalue la suite quand tu progresses.")},
        {B.T("2. Prends les quêtes indiquées"),B.T("Ouvre le dialogue du PNJ : le guidage vérifie les quêtes offertes et celles de ton journal.")},
        {B.T("3. Termine la boucle"),B.T("NE RENTRE PAS ENCORE : continue les objectifs proches. RETOUR HUB : rapporte les quêtes terminées.")},
        {B.T("4. Une quête trop difficile ?"),B.T("Clique Passer. Tu peux reprendre les quêtes écartées dans Options > Outils.")},
    }
    for i,entry in ipairs(tips) do
        local y=124+(i-1)*78;text(p,13,20,y,480,22,entry[1]);text(p,11,20,y+27,480,42,entry[2],true)
    end
    line(p,442)
    button(p,B.T("Vérifier les addons"),20,462,230,function() self:CloseFor(function() B.Advisor:Toggle("addons") end) end)
    button(p,B.T("Recalculer le guidage"),270,462,230,function()
        B.Recovery:Reset();B.Planner.current=nil;B.Guard(B.Update);self:Render()
    end)
    p=self.pages.subscription
    self.accessTitle=text(p,16,20,124,480,44)
    self.accessDetails=text(p,12,20,174,480,68,"",true)
    self.accessLink=CreateFrame("EditBox",nil,p,"InputBoxTemplate");self.accessLink:SetPoint("TOPLEFT",p,"TOPLEFT",25,-250);self.accessLink:SetSize(470,28)
    self.accessLink:SetAutoFocus(false);self.accessLink:SetFontObject(ChatFontNormal);self.accessLink:SetText(B.subscriptionURL)
    self.accessLink:SetScript("OnEscapePressed",function(box) box:ClearFocus() end)
    self.copyLink=button(p,B.T("1. Ouvrir le portail : sélectionner le lien"),20,286,480,function() self.accessLink:SetText(B.subscriptionURL);self.accessLink:SetFocus();self.accessLink:HighlightText() end)
    self.accessCodeLabel=text(p,12,20,330,480,20,B.T("2. Colle ton code d’activation"))
    self.accessCode=CreateFrame("EditBox",nil,p,"InputBoxTemplate")
    self.accessCode:SetPoint("TOPLEFT",p,"TOPLEFT",25,-360);self.accessCode:SetSize(470,32)
    self.accessCode:SetAutoFocus(false);self.accessCode:SetFontObject(ChatFontNormal)
    self.accessCode:SetMultiLine(false);self.accessCode:SetMaxLetters(2048)
    self.accessCode:SetScript("OnEscapePressed",function(box) box:ClearFocus() end)
    self.activateCode=button(p,B.T("Activer Pro"),20,410,480,function()
        if not B.Access then return end
        local ok,reason=B.Access:Import(self.accessCode:GetText())
        local messages={expired="Code expiré. Récupère le code de ta période payée sur le portail.",combat="Attends la fin du combat pour activer Pro.",older="Ton code actuel reste valable plus longtemps. Il est conservé."}
        self.accessMessage=B.T(ok and "Code reconnu. Pro est actif." or messages[reason] or "Code non reconnu. Copie le code complet commençant par FPA1, pas ta clé personnelle.")
        if ok then self.accessCode:SetText("");self.accessCode:ClearFocus() end
        self:Render()
    end)
    self.accessResult=text(p,11,20,450,480,48,"",true)
    text(f,10,20,526,480,16,"ForeverPilot "..B.version.."  ·  "..B.LanguageLabel(B.language),true)
    if UISpecialFrames then table.insert(UISpecialFrames,"ForeverPilotOptionsFrame") end
    for _,event in ipairs({"PLAYER_REGEN_ENABLED","PLAYER_REGEN_DISABLED","PLAYER_TALENT_UPDATE","PLAYER_LEVEL_UP"}) do pcall(f.RegisterEvent,f,event) end
    f:SetScript("OnEvent",function() if f:IsShown() then self:Render() end end)
    if B.Support then
        self.donationCaption=text(f,11,20,566,250,24,B.Support.Text("optional"),true)
        B.Support.Attach(f,{
            point="BOTTOMRIGHT",relativePoint="BOTTOMRIGHT",x=-18,y=17,width=222,height=30})
    end
    f:Hide()
end
function O:Toggle(tab)
    self:Create()
    if self.frame:IsShown() and not tab then self.frame:Hide();return end
    if B.Advisor.frame then B.Advisor.frame:Hide() end
    if B.Advisor.profileFrame then B.Advisor.profileFrame:Hide() end
    if B.LayerUI and B.LayerUI.frame then B.LayerUI.frame:Hide() end
    self.tab=self.pages[tab] and tab or "general";self.pendingLanguage=B.settings.language or "auto"
    self:Render();self.frame:Show()
end
