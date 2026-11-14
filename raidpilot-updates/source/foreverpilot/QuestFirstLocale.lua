-- Quest-first guidance strings, available in every language supported by FastXP.
local B=ForeverPilotLeveling
local keys={
 "Quête déjà acceptée : ses objectifs sont prioritaires.",
 "Quête de niveau inférieur déjà prise : conservée dans le guidage.",
 "Quête active sans repère fiable. Consulte ses objectifs dans ton journal; aucun grind imposé.",
 "QUÊTE EN COURS · GUIDE À VÉRIFIER"
}
local language={
 enUS={"Quest already accepted: prioritize its objectives.",
  "Lower-level quest already accepted: kept in your guide.",
  "Active quest has no reliable waypoint. Check your quest log; grinding will not replace it.",
  "ACTIVE QUEST · VERIFY ROUTE"},
 frFR={"Quête déjà acceptée : ses objectifs sont prioritaires.",
  "Quête de niveau inférieur déjà prise : conservée dans le guidage.",
  "Quête active sans repère fiable. Consulte ses objectifs dans ton journal; aucun grind imposé.",
  "QUÊTE EN COURS · GUIDE À VÉRIFIER"},
 deDE={"Angenommene Quest: Ihre Ziele haben Vorrang.",
  "Angenommene Quest mit niedrigem Level bleibt im Guide.",
  "Aktive Quest ohne verlässlichen Wegpunkt. Prüfe dein Questlog; kein Grind statt Quests.",
  "AKTIVE QUEST · ROUTE PRÜFEN"},
 esES={"Misión aceptada: sus objetivos tienen prioridad.",
  "Misión de nivel inferior aceptada: se mantiene en la guía.",
  "Misión activa sin punto fiable. Consulta el registro; no se sustituye por farmeo.",
  "MISIÓN ACTIVA · VERIFICAR RUTA"},
 itIT={"Missione accettata: obiettivi prioritari.",
  "Missione di livello inferiore accettata: conservata nella guida.",
  "Missione attiva senza indicazioni affidabili. Consulta il registro; nessun farming imposto.",
  "MISSIONE ATTIVA · VERIFICA PERCORSO"},
 ptBR={"Missão aceita: objetivos prioritários.",
  "Missão de nível inferior aceita: mantida no guia.",
  "Missão ativa sem ponto confiável. Veja o diário; farm não substitui missões.",
  "MISSÃO ATIVA · VERIFICAR ROTA"},
 ruRU={"Принятое задание: цели в приоритете.",
  "Принятое задание низкого уровня сохранено в маршруте.",
  "Нет надёжной точки для задания. Проверь журнал; фарм не заменит задания.",
  "АКТИВНОЕ ЗАДАНИЕ · ПРОВЕРЬ МАРШРУТ"},
 koKR={"수락한 퀘스트의 목표를 우선합니다.",
  "이미 수락한 저레벨 퀘스트를 안내에 유지합니다.",
  "진행 중인 퀘스트의 위치가 불확실합니다. 퀘스트 목록을 확인하세요. 사냥으로 대체하지 않습니다.",
  "진행 중 퀘스트 · 경로 확인"},
 zhCN={"已接受任务：优先完成任务目标。",
  "已接受的低等级任务仍保留在指引中。",
  "进行中的任务没有可靠坐标。请查看任务日志，不会强制刷怪。",
  "进行中任务 · 核实路线"},
 zhTW={"已接受任務：優先完成任務目標。",
  "已接受的低等級任務仍保留在指引中。",
  "進行中的任務沒有可靠座標。請查看任務日誌，不會強制刷怪。",
  "進行中任務 · 確認路線"}
}
language.esMX=language.esES
for code,rows in pairs(language) do
 local target=B.locales[code]
 if type(target)=="table" then
  for i,key in ipairs(keys) do target[key]=rows[i] or language.enUS[i] or key end
 end
end
