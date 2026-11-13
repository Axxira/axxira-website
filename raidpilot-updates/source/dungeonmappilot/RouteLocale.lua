local A = DungeonMapPilot

for code,values in pairs({
    enUS={"Undo","Unknown prerequisite: route must be checked"},frFR={"Annuler","Prérequis inconnu : parcours à vérifier"},
    deDE={"Rückgängig","Unbekannte Voraussetzung: Route prüfen"},esES={"Deshacer","Requisito desconocido: revisar la ruta"},esMX={"Deshacer","Requisito desconocido: revisar la ruta"},
    itIT={"Annulla","Requisito sconosciuto: controlla il percorso"},ptBR={"Desfazer","Pré-requisito desconhecido: verifique a rota"},
    ruRU={"Отменить","Неизвестное условие: проверьте маршрут"},koKR={"실행 취소","알 수 없는 선행 조건: 경로 확인 필요"},
    zhCN={"撤销","未知前置条件：需要检查路线"},zhTW={"復原","未知前置條件：需要檢查路線"}
}) do local t=A.locales[A.NormalizeLocale(code)] or {};t.undoRoute=values[1];t.routeInvalidRequirement=values[2];A.RegisterLocale(code,t) end
