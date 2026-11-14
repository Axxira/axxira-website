-- Strings for the FastXP route-first user interface.
local B=ForeverPilotLeveling
local keys={
 "Quête de l'itinéraire sans repère vérifié. Vérifie le journal et la carte.",
 "Atteins le niveau ",
 "OBJECTIF DE NIVEAU",
 "Étape de niveau : combats adaptés sur ton itinéraire.",
 "Entraîneur de classe",
 "Maître de vol",
 "Réparation",
 "Vendeur",
 "ÉTAPE DE SERVICE",
 "Pierre de foyer",
 "ÉTAPE DE TRAJET",
 "Continue l'itinéraire",
 "ITINÉRAIRE · ÉTAPE %d/%d",
 "PARCOURS RAPIDE",
 "Guide : %s · Étape %d/%d",
 "Itinéraire rapide : actif",
 "Itinéraire rapide : désactivé"
}
local values={
 enUS={
 "Route quest has no verified waypoint. Check your quest log and map.",
 "Reach level ","LEVEL GOAL","Leveling step: fight suitable enemies along your route.",
 "Class trainer","Flight master","Repair","Vendor","SERVICE STOP","Hearthstone","TRAVEL STEP",
 "Continue the route","ROUTE · STEP %d/%d","SPEED ROUTE","Guide: %s · Step %d/%d",
 "Speed route: enabled","Speed route: disabled"
 },
 frFR={
 "Quête de l'itinéraire sans repère vérifié. Vérifie le journal et la carte.",
 "Atteins le niveau ","OBJECTIF DE NIVEAU","Étape de niveau : combats adaptés sur ton itinéraire.",
 "Entraîneur de classe","Maître de vol","Réparation","Vendeur","ÉTAPE DE SERVICE","Pierre de foyer",
 "ÉTAPE DE TRAJET","Continue l'itinéraire","ITINÉRAIRE · ÉTAPE %d/%d","PARCOURS RAPIDE",
 "Guide : %s · Étape %d/%d","Itinéraire rapide : actif","Itinéraire rapide : désactivé"
 },
 deDE={
 "Quest ohne bestätigten Wegpunkt. Prüfe Questlog und Karte.",
 "Erreiche Stufe ","STUFENZIEL","Stufenziel: Bekämpfe geeignete Gegner auf deiner Route.",
 "Klassenlehrer","Flugmeister","Reparieren","Händler","SERVICEHALT","Ruhestein",
 "REISESCHRITT","Folge der Route","ROUTE · SCHRITT %d/%d","SCHNELLE ROUTE",
 "Guide: %s · Schritt %d/%d","Schnelle Route: aktiv","Schnelle Route: aus"
 },
 esES={
 "Misión sin punto de ruta confirmado. Consulta el diario y el mapa.",
 "Alcanza el nivel ","OBJETIVO DE NIVEL","Paso de nivel: combate enemigos adecuados por el camino.",
 "Instructor de clase","Maestro de vuelo","Reparación","Vendedor","PARADA DE SERVICIO",
 "Piedra de hogar","PASO DE VIAJE","Continúa la ruta","RUTA · PASO %d/%d","RUTA RÁPIDA",
 "Guía: %s · Paso %d/%d","Ruta rápida: activada","Ruta rápida: desactivada"
 },
 itIT={
 "Missione senza punto confermato. Controlla diario e mappa.",
 "Raggiungi il livello ","OBIETTIVO LIVELLO","Fase di livello: combatti nemici adatti sul percorso.",
 "Istruttore di classe","Maestro di volo","Riparazione","Mercante","SOSTA SERVIZI",
 "Pietra del ritorno","TAPPA DI VIAGGIO","Continua il percorso",
 "PERCORSO · TAPPA %d/%d","PERCORSO RAPIDO","Guida: %s · Tappa %d/%d",
 "Percorso rapido: attivo","Percorso rapido: disattivato"
 },
 ptBR={
 "Missão sem ponto de rota confirmado. Confira o diário e o mapa.",
 "Alcance o nível ","META DE NÍVEL","Etapa de nível: lute com inimigos adequados no caminho.",
 "Instrutor de classe","Mestre de voo","Reparo","Vendedor","PARADA DE SERVIÇO",
 "Pedra de Regresso","ETAPA DE VIAGEM","Continue a rota",
 "ROTA · ETAPA %d/%d","ROTA RÁPIDA","Guia: %s · Etapa %d/%d",
 "Rota rápida: ativada","Rota rápida: desativada"
 },
 ruRU={
 "Для задания нет проверенной точки. Проверь журнал и карту.",
 "Достигни уровня ","ЦЕЛЬ ПО УРОВНЮ","Этап прокачки: сражайся с подходящими врагами по пути.",
 "Наставник класса","Распорядитель полётов","Ремонт","Торговец","ОСТАНОВКА",
 "Камень возвращения","ЭТАП ПУТИ","Продолжай маршрут",
 "МАРШРУТ · ШАГ %d/%d","БЫСТРЫЙ МАРШРУТ","Гайд: %s · Шаг %d/%d",
 "Быстрый маршрут: включён","Быстрый маршрут: выключен"
 },
 koKR={
 "확인된 퀘스트 경로가 없습니다. 퀘스트 목록과 지도를 확인하세요.",
 "레벨 달성: ","레벨 목표","레벨 구간: 이동 중 적절한 적을 처치하세요.",
 "직업 상급자","비행 조련사","수리","상인","시설 방문","귀환석",
 "이동 단계","경로 계속","경로 · 단계 %d/%d","빠른 경로",
 "가이드: %s · 단계 %d/%d","빠른 경로: 켜짐","빠른 경로: 꺼짐"
 },
 zhCN={
 "任务没有已确认的路径点。请查看任务日志和地图。",
 "达到等级 ","等级目标","练级步骤：沿途击败适合等级的敌人。",
 "职业训练师","飞行管理员","修理","商人","服务站点","炉石","行程步骤",
 "继续路线","路线 · 步骤 %d/%d","快速路线","指南：%s · 步骤 %d/%d",
 "快速路线：开启","快速路线：关闭"
 },
 zhTW={
 "任務沒有已確認的路徑點。請查看任務日誌與地圖。",
 "達到等級 ","等級目標","練級步驟：沿途擊敗適合等級的敵人。",
 "職業訓練師","飛行管理員","修理","商人","服務站點","爐石","行程步驟",
 "繼續路線","路線 · 步驟 %d/%d","快速路線","指南：%s · 步驟 %d/%d",
 "快速路線：開啟","快速路線：關閉"
 }
}
values.esMX=values.esES
for code,rows in pairs(values) do
 local map=B.locales and B.locales[code]
 if type(map)=="table" then
  for i,key in ipairs(keys) do map[key]=rows[i] or values.enUS[i] end
 end
end
