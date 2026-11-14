-- Route recovery and performance report labels.
local B=ForeverPilotLeveling
local keys={
 "Étape réalisable à proximité; le guide principal reste inchangé.",
 "REPRISE INTELLIGENTE · OBJECTIF PROCHE",
 "Mesure : %.0f XP/h sur %.0f min · trajets %.0f%%",
 "Rapport de vitesse XP/h",
 "Mesures FastXP : joue au moins une minute pour obtenir un bilan.",
 "FastXP : %.0f XP/h sur %.1f min · déplacements %.0f%% · itinéraire %.0f%%",
 "Temps de reprise %.0f%% · changements d'étape %d",
 "Échantillon insuffisant : vise 15 min de jeu actif avant de comparer les parcours.",
 "Étape lente : %s · %d · %.0f XP en %.1f min"
}
local translations={
 enUS={
 "Nearby available step; the main guide remains unchanged.",
 "SMART RECOVERY · NEARBY GOAL",
 "Measured: %.0f XP/h over %.0f min · travel %.0f%%",
 "XP/hour report",
 "FastXP metrics: play at least one minute for a report.",
 "FastXP: %.0f XP/h over %.1f min · travel %.0f%% · route %.0f%%",
 "Recovery time %.0f%% · step switches %d",
 "Insufficient sample: play at least 15 active minutes before comparing routes.",
 "Slow step: %s · %d · %.0f XP in %.1f min"
 },
 frFR={
 "Étape réalisable à proximité; le guide principal reste inchangé.",
 "REPRISE INTELLIGENTE · OBJECTIF PROCHE",
 "Mesure : %.0f XP/h sur %.0f min · trajets %.0f%%",
 "Rapport de vitesse XP/h",
 "Mesures FastXP : joue au moins une minute pour obtenir un bilan.",
 "FastXP : %.0f XP/h sur %.1f min · déplacements %.0f%% · itinéraire %.0f%%",
 "Temps de reprise %.0f%% · changements d'étape %d",
 "Échantillon insuffisant : vise 15 min de jeu actif avant de comparer les parcours.",
 "Étape lente : %s · %d · %.0f XP en %.1f min"
 },
 deDE={
 "Nahegelegenes erreichbares Ziel; der Hauptguide bleibt unverändert.",
 "INTELLIGENTE FORTSETZUNG · NAHES ZIEL",
 "Gemessen: %.0f XP/h über %.0f Min. · Reise %.0f%%",
 "XP/Stunde-Bericht",
 "FastXP-Messung: Spiele mindestens eine Minute für einen Bericht.",
 "FastXP: %.0f XP/h über %.1f Min. · Reise %.0f%% · Route %.0f%%",
 "Wiederaufnahme %.0f%% · Schrittwechsel %d",
 "Zu wenige Daten: Spiele mindestens 15 aktive Minuten für Vergleiche.",
 "Langsamer Schritt: %s · %d · %.0f XP in %.1f Min."
 },
 esES={
 "Objetivo disponible cerca; la ruta principal sigue intacta.",
 "RECUPERACIÓN INTELIGENTE · OBJETIVO CERCANO",
 "Medido: %.0f XP/h en %.0f min · viaje %.0f%%",
 "Informe de XP/h",
 "Métricas FastXP: juega al menos un minuto para obtener un informe.",
 "FastXP: %.0f XP/h en %.1f min · viaje %.0f%% · ruta %.0f%%",
 "Recuperación %.0f%% · cambios de paso %d",
 "Muestra insuficiente: juega 15 minutos activos antes de comparar rutas.",
 "Paso lento: %s · %d · %.0f XP en %.1f min"
 },
 itIT={
 "Obiettivo vicino disponibile; la guida principale resta invariata.",
 "RIPRESA INTELLIGENTE · OBIETTIVO VICINO",
 "Misurato: %.0f XP/h in %.0f min · viaggio %.0f%%",
 "Rapporto XP/ora",
 "Metriche FastXP: gioca almeno un minuto per un rapporto.",
 "FastXP: %.0f XP/h in %.1f min · viaggio %.0f%% · percorso %.0f%%",
 "Recupero %.0f%% · cambi di tappa %d",
 "Campione insufficiente: gioca almeno 15 minuti attivi per confrontare.",
 "Tappa lenta: %s · %d · %.0f XP in %.1f min"
 },
 ptBR={
 "Objetivo próximo disponível; o guia principal não muda.",
 "RECUPERAÇÃO INTELIGENTE · META PRÓXIMA",
 "Medido: %.0f XP/h em %.0f min · viagem %.0f%%",
 "Relatório de XP/h",
 "Métricas FastXP: jogue pelo menos um minuto para gerar relatório.",
 "FastXP: %.0f XP/h em %.1f min · viagem %.0f%% · rota %.0f%%",
 "Recuperação %.0f%% · trocas de etapa %d",
 "Amostra insuficiente: jogue 15 minutos ativos antes de comparar rotas.",
 "Etapa lenta: %s · %d · %.0f XP em %.1f min"
 },
 ruRU={
 "Рядом есть доступный этап; основной маршрут не изменён.",
 "УМНОЕ ВОССТАНОВЛЕНИЕ · БЛИЖНЯЯ ЦЕЛЬ",
 "Измерено: %.0f XP/ч за %.0f мин · путь %.0f%%",
 "Отчёт по XP/ч",
 "FastXP: играйте хотя бы минуту для получения отчёта.",
 "FastXP: %.0f XP/ч за %.1f мин · путь %.0f%% · маршрут %.0f%%",
 "Восстановление %.0f%% · смен шагов %d",
 "Недостаточно данных: играйте не менее 15 минут для сравнения.",
 "Медленный шаг: %s · %d · %.0f XP за %.1f мин"
 },
 koKR={
 "가까운 진행 가능한 단계입니다. 기본 경로는 유지됩니다.",
 "스마트 복구 · 가까운 목표",
 "측정: %.0f XP/시간, %.0f분 · 이동 %.0f%%",
 "XP/시간 보고서",
 "FastXP 측정: 보고서를 보려면 1분 이상 플레이하세요.",
 "FastXP: %.0f XP/시간, %.1f분 · 이동 %.0f%% · 경로 %.0f%%",
 "복구 시간 %.0f%% · 단계 변경 %d회",
 "표본 부족: 비교하려면 15분 이상 플레이하세요.",
 "느린 단계: %s · %d · %.0f XP, %.1f분"
 },
 zhCN={
 "附近有可执行步骤；主指南保持不变。",
 "智能恢复 · 附近目标",
 "测得：%.0f XP/小时，%.0f 分钟 · 跑路 %.0f%%",
 "XP/小时报告",
 "FastXP 数据：至少游玩一分钟后查看报告。",
 "FastXP：%.0f XP/小时，%.1f 分钟 · 跑路 %.0f%% · 路线 %.0f%%",
 "恢复耗时 %.0f%% · 步骤切换 %d 次",
 "样本不足：至少有效游玩15分钟再比较路线。",
 "低效步骤：%s · %d · %.0f XP，%.1f 分钟"
 },
 zhTW={
 "附近有可執行步驟；主要指南保持不變。",
 "智慧恢復 · 附近目標",
 "測得：%.0f XP/小時，%.0f 分鐘 · 跑路 %.0f%%",
 "XP/小時報告",
 "FastXP 數據：至少遊玩一分鐘後查看報告。",
 "FastXP：%.0f XP/小時，%.1f 分鐘 · 跑路 %.0f%% · 路線 %.0f%%",
 "恢復耗時 %.0f%% · 步驟切換 %d 次",
 "樣本不足：至少有效遊玩15分鐘再比較路線。",
 "低效步驟：%s · %d · %.0f XP，%.1f 分鐘"
 }
}
translations.esMX=translations.esES
for locale,rows in pairs(translations) do
 local target=B.locales and B.locales[locale]
 if type(target)=="table" then
  for i,key in ipairs(keys) do target[key]=rows[i] or translations.enUS[i] end
 end
end
