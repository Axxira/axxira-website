-- User-facing controls. Guidance and equipment logic stay in their own modules.
local B=ForeverPilotLeveling
local O={tab="general"};B.Options=O
local function text(f,size,x,y,w,h,value,muted)
    local t=f:CreateFontString(nil,"OVERLAY");t:SetFont(STANDARD_TEXT_FONT,size,"")
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
-- Settings remain reachable even when guidance is hidden, unavailable or stopped.
-- This entry point must not depend on the equipment/auction module initializing.
function O:SyncEntryPoints()
    if not B.settings then return end
    local guide=B.UI and B.UI.frame
    if not self.shortcut then
        self.shortcut=button(UIParent,"ForeversPilot FastXp · Options",0,0,244,function() self:Toggle() end)
        self.shortcut:SetFrameStrata("MEDIUM");self.shortcut:SetClampedToScreen(true)
    end
    if guide and not self.headerButton then
        self.headerButton=button(guide,"Options",244,3,86,function() self:Toggle() end)
        self.headerButton:SetScript("OnEnter",function()
            GameTooltip:SetOwner(self.headerButton,"ANCHOR_BOTTOM");GameTooltip:SetText("Options · Français / English")
            GameTooltip:AddLine(B.T("Langue, profil, taille et aide."),.8,.9,.9,true);GameTooltip:Show()
        end)
        self.headerButton:HookScript("OnLeave",function() GameTooltip:Hide() end)
        guide:HookScript("OnShow",function() self:SyncEntryPoints() end)
        guide:HookScript("OnHide",function() self:SyncEntryPoints() end)
    end
    self.shortcut:ClearAllPoints()
    self.shortcut:SetPoint(B.settings.point or "TOP",UIParent,B.settings.point or "TOP",B.settings.x or 0,B.settings.y or -90)
    self.shortcut:SetScale(B.settings.scale or 1)
    self.shortcut:SetShown(not guide or not guide:IsShown())
end
function O:Render()
    if not self.frame then return end
    for key,p in pairs(self.pages) do p:SetShown(key==self.tab);self.tabs[key]:Paint(key==self.tab) end
    local chosen=self.pendingLanguage or B.settings.language or "auto"
    for key,b in pairs(self.languages) do b:Paint(chosen==key) end
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
    local free=B.distribution=="raidpilot-test"
    local active=B.HasPremium()
    self.accessTitle:SetText(free and B.T("Édition RaidPilot : tout est gratuit") or active and B.T("ForeverPilot Premium actif") or B.T("Activer Fast XP et le suivi de classe"))
    self.accessDetails:SetText(free and B.T("Toutes les fonctions sont incluses dans ton édition de test. Aucun abonnement ni connexion ne sont nécessaires.") or active and B.T("Ton accès est chargé. Le compagnon vérifie le renouvellement lorsque tu l'ouvres. Garde-le ouvert pour actualiser l'accès.") or B.T("1. Ouvre le portail ci-dessous.\n2. Abonne-toi sur PayPal et garde ta clé personnelle.\n3. Ouvre le compagnon Windows, colle ta clé, puis reconnecte-toi à WoW."))
    self.accessLink:SetShown(not free);self.copyLink:SetShown(not free)
end
function O:MaybeWelcome()
    if not B.char or not B.settings or not B.settings.enabled or B.char.welcomeSeen or B.Call(InCombatLockdown)==true or B.Call(UnitIsDeadOrGhost,"player")==true then return end
    self:Toggle(B.premiumBlocked and "subscription" or "general")
    B.char.welcomeSeen=true
end
function O:Create()
    if self.frame then return end
    local f=CreateFrame("Frame","ForeverPilotOptionsFrame",UIParent);self.frame=f
    f:SetSize(520,550);f:SetPoint("CENTER",UIParent,"CENTER",0,0);f:SetClampedToScreen(true)
    f:SetFrameStrata("DIALOG");f:EnableMouse(true)
    local bg=f:CreateTexture(nil,"BACKGROUND");bg:SetAllPoints(f);bg:SetColorTexture(.035,.06,.08,.99)
    local accent=f:CreateTexture(nil,"ARTWORK");accent:SetPoint("TOPLEFT",f,"TOPLEFT",0,0);accent:SetSize(520,2);accent:SetColorTexture(.4,.85,.8,1)
    text(f,17,20,18,410,24,"ForeversPilot FastXp")
    text(f,11,20,43,440,17,B.T("Réglages, profil et outils au même endroit."),true)
    button(f,"×",467,16,32,function() f:Hide() end)
    self.pages={};self.tabs={}
    for i,entry in ipairs({{"general",B.T("Général")},{"tools",B.T("Outils")},{"help",B.T("Aide")},{"subscription",B.T("Abonnement")}}) do
        local key=entry[1]
        self.tabs[key]=button(f,entry[2],20+(i-1)*122,74,114,function() self.tab=key;self:Render() end)
        local p=CreateFrame("Frame",nil,f);p:SetPoint("TOPLEFT",f,"TOPLEFT",0,0);p:SetSize(520,550);self.pages[key]=p
    end
    local p=self.pages.general
    text(p,13,20,124,480,20,"Langue / Language")
    self.languages={}
    for i,entry in ipairs({{"fr","Français"},{"en","English"},{"auto",B.T("Auto · langue du jeu")}}) do
        local key=entry[1]
        self.languages[key]=button(p,entry[2],20+(i-1)*164,152,152,function() self.pendingLanguage=key;self:Render() end)
    end
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
    text(p,13,20,124,480,22,B.T("L'essentiel, sans encombrer la flèche."))
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
    self.resume=button(p,B.T("Reprendre les quêtes écartées"),20,446,480,function() B.ResumeSkipped("all");self:Render() end)
    text(p,11,20,487,480,35,B.T("Passer met une quête de côté sans l'abandonner dans ton journal."),true)
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
    self.accessTitle=text(p,16,20,132,480,50)
    self.accessDetails=text(p,12,20,198,480,148,"",true)
    self.accessLink=CreateFrame("EditBox",nil,p,"InputBoxTemplate");self.accessLink:SetPoint("TOPLEFT",p,"TOPLEFT",25,-365);self.accessLink:SetSize(470,30)
    self.accessLink:SetAutoFocus(false);self.accessLink:SetFontObject(ChatFontNormal);self.accessLink:SetText(B.subscriptionURL)
    self.accessLink:SetScript("OnEscapePressed",function(box) box:ClearFocus() end)
    self.copyLink=button(p,B.T("Sélectionner le lien (Ctrl+C)"),20,410,480,function() self.accessLink:SetText(B.subscriptionURL);self.accessLink:SetFocus();self.accessLink:HighlightText() end)
    text(p,11,20,465,480,45,B.T("Les outils HDV et layers restent accessibles. Ding fournit le guide de base."),true)
    text(f,10,20,526,480,16,"ForeverPilot "..B.version.."  ·  Français / English",true)
    if UISpecialFrames then table.insert(UISpecialFrames,"ForeverPilotOptionsFrame") end
    for _,event in ipairs({"PLAYER_REGEN_ENABLED","PLAYER_REGEN_DISABLED","PLAYER_TALENT_UPDATE","PLAYER_LEVEL_UP"}) do pcall(f.RegisterEvent,f,event) end
    f:SetScript("OnEvent",function() if f:IsShown() then self:Render() end end)
    f:Hide()
end
function O:Toggle(tab)
    self:Create()
    if self.frame:IsShown() and not tab then self.frame:Hide();return end
    if B.Advisor.frame then B.Advisor.frame:Hide() end
    if B.Advisor.profileFrame then B.Advisor.profileFrame:Hide() end
    if B.LayerUI and B.LayerUI.frame then B.LayerUI.frame:Hide() end
    self.tab=tab or "general";self.pendingLanguage=B.settings.language or "auto"
    self:Render();self.frame:Show()
end
