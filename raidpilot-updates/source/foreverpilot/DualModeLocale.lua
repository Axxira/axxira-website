-- FastXp Dual Mode labels (all language preferences; English fallback for full guidance).
local B=ForeverPilotLeveling
local keys={
 "Mode FastXP abonné actif.","Mode Zone Explorer gratuit actif.",
 "FastXP abonné : route rapide et grind mesuré.",
 "Zone Explorer gratuit : quêtes recensées, zone par zone.",
 "Zone Explorer gratuit actif · FastXP rapide en abonnement",
 "MODE ZONE GRATUIT","FASTXP ROUTE RAPIDE","QUÊTES DE ZONE","ZONE SUIVANTE",
 "ZONE À DÉCOUVRIR · OFFRES À VÉRIFIER",
 "GRIND RAPIDE · RENDEMENT XP MESURÉ",
 "Nouvelle zone suggérée : consulte les quêtes réellement offertes au PNJ.",
 "Grind conseillé selon l'XP observée, puis retour aux quêtes si le rendement baisse.",
 "Mode gratuit : quête recensée dans la zone. Aucune suppression automatique.",
 "Quête de métier : prérequis à vérifier manuellement.",
 "Journal · quêtes zone par zone",
 "Déplacement de zone indisponible ici.",
 "Le guidage gratuit reste actif, zone par zone. Abonne-toi uniquement pour les itinéraires XP rapides et le grind intelligent.\nPortail et compagnon Windows ci-dessous."
}
local language={
 enUS={
 "FastXp subscription mode enabled.","Free Zone Explorer mode enabled.",
 "Subscriber FastXP: optimized routes and measured grinding.",
 "Free Zone Explorer: known quests, one zone at a time.",
 "Free Zone Explorer active · subscribe for faster FastXP routes",
 "FREE ZONE MODE","FASTXP SPEED ROUTE","ZONE QUESTS","NEXT ZONE",
 "EXPLORE THE NEXT ZONE · VERIFY QUEST OFFERS",
 "FAST GRIND · OBSERVED XP RATE",
 "Next zone suggested; confirm actual offers at the quest NPC.",
 "Based on observed XP, grind here; return to quests if the rate falls.",
 "Free zone mode: known quest, no automatic abandonment.",
 "Profession quest: check skill prerequisites manually.",
 "Quest journal · zone by zone",
 "Zone switch unavailable here.",
 "Free quest guidance remains available, zone by zone. Subscribe only for optimized XP routes and intelligent grinding.\nPortal and Windows companion are shown below."
 },
 frFR={
 "Mode FastXP abonné actif.","Mode Zone Explorer gratuit actif.",
 "FastXP abonné : route rapide et grind mesuré.",
 "Zone Explorer gratuit : quêtes recensées, zone par zone.",
 "Zone Explorer gratuit actif · FastXP rapide en abonnement",
 "MODE ZONE GRATUIT","FASTXP ROUTE RAPIDE","QUÊTES DE ZONE","ZONE SUIVANTE",
 "ZONE À DÉCOUVRIR · OFFRES À VÉRIFIER","GRIND RAPIDE · RENDEMENT XP MESURÉ",
 "Nouvelle zone suggérée : consulte les quêtes réellement offertes au PNJ.",
 "Grind conseillé selon l'XP observée, puis retour aux quêtes si le rendement baisse.",
 "Mode gratuit : quête recensée dans la zone. Aucune suppression automatique.",
 "Quête de métier : prérequis à vérifier manuellement.","Journal · quêtes zone par zone",
 "Déplacement de zone indisponible ici.",
 "Le guidage gratuit reste actif, zone par zone. Abonne-toi uniquement pour les itinéraires XP rapides et le grind intelligent.\nPortail et compagnon Windows ci-dessous."
 },
 deDE={"FastXP-Abo aktiv.","Kostenloser Zonenmodus aktiv.","Abo: schnelle Routen und gemessenes Farmen.","Gratis: Quests Gebiet für Gebiet.","Zonenmodus kostenlos · schnelle Routen im Abo","GRATIS-ZONENMODUS","FASTXP SCHNELLROUTE","GEBIETSQUESTS","NÄCHSTES GEBIET","NÄCHSTES GEBIET · ANGEBOTE PRÜFEN","SCHNELLES FARMEN · GEMESSENE XP-RATE"},
 esES={"FastXP suscrito activo.","Explorador de zonas gratis activo.","Suscripción: rutas rápidas y farmeo medido.","Gratis: misiones zona por zona.","Explorador gratis · rutas rápidas con suscripción","MODO ZONA GRATIS","RUTA RÁPIDA FASTXP","MISIONES DE ZONA","SIGUIENTE ZONA","EXPLORA ZONA · VERIFICA MISIONES","FARMEAR RÁPIDO · XP MEDIDA"},
 itIT={"Abbonamento FastXP attivo.","Esplorazione zone gratuita attiva.","Abbonamento: percorsi rapidi e farming misurato.","Gratis: missioni zona per zona.","Zone gratis · percorsi veloci con abbonamento","MODALITÀ ZONA GRATIS","PERCORSO FASTXP","MISSIONI DI ZONA","PROSSIMA ZONA","ESPLORA ZONA · CONTROLLA MISSIONI","FARM RAPIDO · XP MISURATI"},
 ptBR={"Assinatura FastXP ativa.","Explorador de zonas grátis ativo.","Assinante: rotas rápidas e farm medido.","Grátis: missões zona por zona.","Zonas grátis · rotas rápidas por assinatura","MODO ZONA GRÁTIS","ROTA RÁPIDA FASTXP","MISSÕES DE ZONA","PRÓXIMA ZONA","EXPLORAR ZONA · VERIFICAR MISSÕES","FARM RÁPIDO · XP MEDIDO"},
 ruRU={"Подписка FastXP активна.","Бесплатный режим зон активен.","Подписка: быстрые маршруты и замеренный фарм.","Бесплатно: задания по зонам.","Бесплатные зоны · быстрый маршрут по подписке","БЕСПЛАТНЫЕ ЗОНЫ","БЫСТРЫЙ МАРШРУТ","ЗАДАНИЯ ЗОНЫ","СЛЕДУЮЩАЯ ЗОНА","ПРОВЕРЬТЕ ЗАДАНИЯ В ЗОНЕ","БЫСТРЫЙ ФАРМ · ЗАМЕРЕННЫЙ ОПЫТ"},
 koKR={"FastXP 구독 모드 활성화","무료 지역 모드 활성화","구독: 빠른 경로와 측정된 사냥","무료: 지역별 퀘스트","무료 지역 탐색 · 빠른 경로는 구독","무료 지역 모드","FASTXP 빠른 경로","지역 퀘스트","다음 지역","다음 지역의 퀘스트 확인","빠른 사냥 · 실제 경험치 측정"},
 zhCN={"FastXP 订阅模式已启用","免费分区任务模式已启用","订阅：快速路线与实测刷怪","免费：逐个区域完成任务","免费区域任务 · 快速路线需订阅","免费区域模式","FASTXP 快速路线","区域任务","下一区域","前往区域 · 验证任务","高效刷怪 · 实测经验"},
 zhTW={"FastXP 訂閱模式已啟用","免費分區任務模式已啟用","訂閱：快速路線與實測刷怪","免費：逐個區域完成任務","免費區域任務 · 快速路線需訂閱","免費區域模式","FASTXP 快速路線","區域任務","下一區域","前往區域 · 驗證任務","高效刷怪 · 實測經驗"}
}
language.esMX=language.esES
local english=language.enUS
for code,translations in pairs(language) do
 local target=B.locales[code]
 if type(target)=="table" then
  for index,key in ipairs(keys) do
   target[key]=translations[index] or english[index] or key
  end
 end
end
