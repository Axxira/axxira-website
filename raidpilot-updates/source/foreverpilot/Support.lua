-- Optional support UI, added at the author's explicit request. Local build.
-- No startup prompt, payment, browser launch, chat message or feature entitlement.
local addonName,namespace=...
local A=addonName=="ForeverPilot_Leveling" and _G.ForeverPilotLeveling
    or addonName=="DungeonMapPilot" and _G.DungeonMapPilot or namespace
if type(A)~="table" then return end
local S={api=2,buttons={},labels={}};A.Support=S
local config=A.SupportConfig or {}
local copy=unpack or table.unpack
local translations={
 enUS={optional="Entirely optional donation",body="Supporting development is entirely your choice. No perks are offered in return, and choosing not to donate does not change how this addon works.",missing="The creator's Buy Me a Coffee page has not been configured. No payment link is available.",copy="Select link (Ctrl+C)",hint="Copy this creator-page address into your browser. Nothing opens or pays automatically.",close="Close",unconfigured="Link not configured",selected="Link selected. Press Ctrl+C, then paste it into your browser."},
 frFR={optional="Don entièrement facultatif",body="Soutenir le développement est entièrement à ta discrétion. Aucun avantage n'est offert en échange et ne pas donner ne change rien au fonctionnement de cet addon.",missing="La page Buy Me a Coffee du créateur n'est pas encore configurée. Aucun lien de paiement n'est disponible.",copy="Sélectionner le lien (Ctrl+C)",hint="Copie cette adresse de créateur dans ton navigateur. Aucune ouverture ni aucun paiement automatique.",close="Fermer",unconfigured="Lien non configuré",selected="Lien sélectionné. Appuie sur Ctrl+C, puis colle-le dans ton navigateur."},
 deDE={optional="Völlig freiwillige Spende",body="Die Unterstützung der Entwicklung ist ganz freiwillig. Es gibt keine Gegenleistungen. Ohne Spende funktioniert dieses Addon unverändert.",missing="Die Buy-Me-a-Coffee-Seite des Entwicklers ist noch nicht eingerichtet. Kein Zahlungslink verfügbar.",copy="Link auswählen (Strg+C)",hint="Kopiere die Entwickleradresse in deinen Browser. Nichts wird automatisch geöffnet oder bezahlt.",close="Schließen",unconfigured="Link nicht eingerichtet",selected="Link markiert. Strg+C drücken und im Browser einfügen."},
 esES={optional="Donación totalmente opcional",body="Apoyar el desarrollo es decisión tuya. No se ofrecen ventajas a cambio y no donar no cambia el funcionamiento del addon.",missing="La página Buy Me a Coffee del creador aún no está configurada. No hay enlace de pago.",copy="Seleccionar enlace (Ctrl+C)",hint="Copia la dirección del creador en tu navegador. No se abre ni se paga nada automáticamente.",close="Cerrar",unconfigured="Enlace sin configurar",selected="Enlace seleccionado. Pulsa Ctrl+C y pégalo en el navegador."},
 itIT={optional="Donazione del tutto facoltativa",body="Sostenere lo sviluppo è una tua scelta. Non vengono offerti vantaggi e non donare non cambia il funzionamento dell'addon.",missing="La pagina Buy Me a Coffee del creatore non è ancora configurata. Nessun link di pagamento disponibile.",copy="Seleziona link (Ctrl+C)",hint="Copia l'indirizzo del creatore nel browser. Nessuna apertura o pagamento automatico.",close="Chiudi",unconfigured="Link non configurato",selected="Link selezionato. Premi Ctrl+C e incollalo nel browser."},
 ptBR={optional="Doação totalmente opcional",body="Apoiar o desenvolvimento é uma escolha sua. Não há vantagens em troca e não doar não altera o funcionamento deste addon.",missing="A página Buy Me a Coffee do criador ainda não foi configurada. Não há link de pagamento.",copy="Selecionar link (Ctrl+C)",hint="Copie o endereço do criador no navegador. Nada é aberto ou pago automaticamente.",close="Fechar",unconfigured="Link não configurado",selected="Link selecionado. Pressione Ctrl+C e cole no navegador."},
 ruRU={optional="Полностью добровольное пожертвование",body="Поддержка разработки — только ваше решение. Никаких преимуществ взамен нет. Отказ от пожертвования не меняет работу аддона.",missing="Страница автора на Buy Me a Coffee ещё не настроена. Платёжная ссылка недоступна.",copy="Выделить ссылку (Ctrl+C)",hint="Скопируйте адрес страницы автора в браузер. Ничего не открывается и не оплачивается автоматически.",close="Закрыть",unconfigured="Ссылка не настроена",selected="Ссылка выделена. Нажмите Ctrl+C и вставьте её в браузере."},
 koKR={optional="전적으로 자율적인 후원",body="개발 후원은 전적으로 선택 사항입니다. 후원 대가로 혜택을 제공하지 않으며 후원하지 않아도 애드온 작동에 영향이 없습니다.",missing="제작자의 Buy Me a Coffee 페이지가 아직 설정되지 않았습니다. 결제 링크가 없습니다.",copy="링크 선택 (Ctrl+C)",hint="제작자 페이지 주소를 브라우저에 복사하세요. 자동으로 열거나 결제하지 않습니다.",close="닫기",unconfigured="링크 미설정",selected="링크를 선택했습니다. Ctrl+C로 복사한 뒤 브라우저에 붙여넣으세요."},
 zhCN={optional="完全自愿的捐赠",body="是否支持开发完全由你决定。不提供任何回报或特权，不捐赠也不会影响插件功能。",missing="作者的Buy Me a Coffee页面尚未配置，目前没有支付链接。",copy="选择链接 (Ctrl+C)",hint="请将作者页面地址复制到浏览器中。不会自动打开或付款。",close="关闭",unconfigured="链接未配置",selected="链接已选中。按 Ctrl+C 复制，然后粘贴到浏览器。"},
 zhTW={optional="完全自願的捐贈",body="是否支持開發完全由你決定。不提供任何回報或特權，不捐贈也不會影響插件功能。",missing="作者的Buy Me a Coffee頁面尚未設定，目前沒有付款連結。",copy="選取連結 (Ctrl+C)",hint="請將作者頁面位址複製到瀏覽器中。不會自動開啟或付款。",close="關閉",unconfigured="連結未設定",selected="連結已選取。按 Ctrl+C 複製，再貼到瀏覽器。"}}
translations.enGB=translations.enUS;translations.esMX=translations.esES
S.locales=translations
local fonts={ruRU="Fonts\\FRIZQT___CYR.ttf",koKR="Fonts\\2002.ttf",zhCN="Fonts\\ARKai_T.ttf",zhTW="Fonts\\bKAI00M.ttf"}
local function public(v)
 if type(issecretvalue)=="function" then local ok,secret=pcall(issecretvalue,v);if not ok or secret then return nil end end
 return v
end
local function call(fn,...)
 if type(fn)~="function" then return nil end
 local ok,v=pcall(fn,...);if ok then return public(v) end
end
local function num(v,lo,hi) v=public(v);return type(v)=="number" and v==v and v>=lo and v<=hi and v or nil end
function S.Language()
 local code
 if type(A.GetLocale)=="function" then code=call(A.GetLocale)
 elseif type(A.Language)=="function" then code=call(A.Language)
 elseif type(A.Locale)=="string" then code=A.Locale
 elseif type(A.language)=="string" then code=A.language end
 code=public(code);if translations[code] then return code end
 return translations[call(GetLocale)] and call(GetLocale) or "enUS"
end
function S.Text(key) return (translations[S.Language()] or translations.enUS)[key] or translations.enUS[key] or key end
function S.ValidateURL(value)
 value=public(value)
 -- Accept only the PayPal.Me creator URL explicitly supplied by the addon owner.
 if value=="https://paypal.me/AddonWow" then return value end
 return nil
end
function S.URL()
 if type(config)~="table" or config.confirmed~=true then return nil end
 return S.ValidateURL(config.creatorURL)
end
local function font(label,size)
 local fallback=STANDARD_TEXT_FONT or "Fonts\\FRIZQT__.TTF"
 local ok,loaded=pcall(label.SetFont,label,fonts[S.Language()] or fallback,size,"")
 if not ok or loaded==false then pcall(label.SetFont,label,fallback,size,"") end
end
local function text(parent,w,h,x,y,size)
 local t=parent:CreateFontString(nil,"OVERLAY","GameFontHighlight")
 t:SetSize(w,h);t:SetPoint("TOPLEFT",x,y);t:SetJustifyH("LEFT");t:SetJustifyV("TOP");t:SetWordWrap(true)
 S.labels[#S.labels+1]={region=t,size=size};font(t,size);return t
end
local function button(parent,w,h,onClick)
 local b=CreateFrame("Button",nil,parent,"UIPanelButtonTemplate");b:SetSize(w,h)
 b.caption=text(b,w-12,h-4,6,-2,12);b.caption:SetJustifyH("CENTER");b.caption:SetJustifyV("MIDDLE")
 b:SetScript("OnClick",onClick);return b
end
function S.Close()
 if S.dialog then S.dialog:Hide() end
 S.owner=nil
end
function S.RefreshDialog()
 local f=S.dialog;if not f then return end
 local url=S.URL();f.title:SetText("Buy Me a Coffee · PayPal")
 f.subtitle:SetText(S.Text("optional"));f.body:SetText(S.Text("body"))
 f.hint:SetText(url and ("PayPal.Me · "..S.Text("hint")) or S.Text("missing"))
 f.copy.caption:SetText(S.Text(url and "copy" or "unconfigured"));f.copy:SetEnabled(url~=nil)
 f.close.caption:SetText(S.Text("close"))
 f.restoring=true;f.link:SetText(url or "");f.restoring=false;f.link:SetShown(url~=nil)
 for _,entry in ipairs(S.labels) do font(entry.region,entry.size) end
end
function S.Open(owner)
 if call(InCombatLockdown)~=false or not owner or call(owner.IsShown,owner)~=true then return false end
 S.owner=owner
 if not S.dialog then
  local f=CreateFrame("Frame",addonName.."SupportDialog",UIParent,BackdropTemplateMixin and "BackdropTemplate" or nil);S.dialog=f
  f:SetSize(600,340);f:SetPoint("CENTER");f:SetFrameStrata("FULLSCREEN_DIALOG");f:SetClampedToScreen(true);f:EnableMouse(true)
  f:SetBackdrop({bgFile="Interface\\Buttons\\WHITE8X8",edgeFile="Interface\\Buttons\\WHITE8X8",edgeSize=1})
  f:SetBackdropColor(.035,.05,.07,.99);f:SetBackdropBorderColor(.85,.68,.35,1)
  f.title=text(f,552,30,24,-20,22);f.title:SetTextColor(1,.83,.48)
  f.subtitle=text(f,552,36,24,-58,13);f.body=text(f,552,76,24,-101,13)
  f.hint=text(f,552,52,24,-182,12)
  f.link=CreateFrame("EditBox",nil,f,"InputBoxTemplate");f.link:SetSize(546,28);f.link:SetPoint("TOPLEFT",29,-240)
  f.link:SetAutoFocus(false);f.link:SetFontObject("GameFontHighlight");f.link:SetMaxLetters(200)
  f.link:SetScript("OnEscapePressed",S.Close)
  f.link:SetScript("OnEditFocusGained",function(e)
   if S.URL() then e:HighlightText() end
  end)
  f.link:SetScript("OnTextChanged",function(e)
   if f.restoring then return end
   local original=S.URL() or ""
   if e:GetText()~=original then f.restoring=true;e:SetText(original);f.restoring=false end
  end)
  f.copy=button(f,332,30,function()
   if call(InCombatLockdown)~=false or not S.URL() then return end
   S.RefreshDialog();f.link:SetFocus();f.link:HighlightText()
   f.hint:SetText("PayPal.Me - "..S.Text("selected"))
  end);f.copy:SetPoint("BOTTOMLEFT",24,20)
  f.close=button(f,190,30,S.Close);f.close:SetPoint("BOTTOMRIGHT",-24,20)
  f:SetScript("OnShow",function()
   if S.URL() then f.link:SetFocus();f.link:HighlightText() end
  end)
  f:SetScript("OnHide",function() f.link:ClearFocus();S.owner=nil end)
  if UISpecialFrames then UISpecialFrames[#UISpecialFrames+1]=addonName.."SupportDialog" end
  f:Hide()
 end
 S.owner=owner
 local width=num(call(UIParent.GetWidth,UIParent),100,100000) or 1920
 local height=num(call(UIParent.GetHeight,UIParent),100,100000) or 1080
 S.dialog:SetScale(math.max(.25,math.min(1,(width-24)/600,(height-24)/340)))
 S.RefreshDialog();S.dialog:Show();return true
end
function S.Attach(parent,options)
 if not parent or type(parent.CreateTexture)~="function" then return nil end
 if S.buttons[parent] then return S.buttons[parent] end
 options=options or {}
 local width=options.width or 194;local height=options.height or 27
 local b=button(parent,width,height,function() S.Open(parent) end);S.buttons[parent]=b
 b:SetPoint(options.point or "TOPRIGHT",parent,options.relativePoint or options.point or "TOPRIGHT",options.x or -18,options.y or -48)
 b:SetFrameLevel((num(call(parent.GetFrameLevel,parent),0,100000) or 0)+5)
 b.icon=b:CreateTexture(nil,"ARTWORK");b.icon:SetTexture("Interface\\Icons\\INV_Drink_03");b.icon:SetSize(18,18);b.icon:SetPoint("LEFT",7,0)
 b.caption:ClearAllPoints();b.caption:SetPoint("LEFT",29,0);b.caption:SetSize(width-35,height-4);b.caption:SetText("Buy Me a Coffee");b.caption:SetTextColor(1,.85,.50)
 local function update() b:SetShown(call(InCombatLockdown)==false);font(b.caption,12) end
 b:SetScript("OnEnter",function()
  font(b.caption,12)
  if GameTooltip then GameTooltip:SetOwner(b,"ANCHOR_BOTTOM");GameTooltip:SetText("Buy Me a Coffee");GameTooltip:AddLine(S.Text("optional"),.93,.83,.61,true);GameTooltip:AddLine(S.Text(S.URL() and "hint" or "missing"),.8,.85,.9,true);GameTooltip:Show() end
 end)
 b:SetScript("OnLeave",function() if GameTooltip then GameTooltip:Hide() end end)
 if parent.HookScript then
  parent:HookScript("OnShow",update)
  parent:HookScript("OnHide",function() if S.owner==parent then S.Close() end end)
 end
 update();return b
end
local events=CreateFrame("Frame");S.events=events
for _,event in ipairs({"PLAYER_REGEN_DISABLED","PLAYER_REGEN_ENABLED"}) do pcall(events.RegisterEvent,events,event) end
events:SetScript("OnEvent",function(_,event)
 if event=="PLAYER_REGEN_DISABLED" then S.Close() end
 for _,b in pairs(S.buttons) do b:SetShown(call(InCombatLockdown)==false) end
end)
