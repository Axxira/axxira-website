-- New user-facing labels are translated independently; boss names preserve client spelling.
local A=DungeonMapPilot
local keys={"guideRosterBadge","guideRosterWarning","guideBrowseRoster","guideMyLevel","guideAllMaps","guideRosterSource"}
local lines={
 enUS={"BOSSES","Client boss list, not a verified route or encounter order.","Boss list","At my level (%d) - %d dungeons","All maps and guides (%d)","Client encounters 1.60.1.70888; verify in game"},
 frFR={"BOSS","Liste des boss du client : ni trajet ni ordre de combat vérifiés.","Liste de boss","À mon niveau (%d) - %d donjons","Toutes les cartes et guides (%d)","Rencontres du client 1.60.1.70888 ; vérifier en jeu"},
 deDE={"BOSSE","Bossliste aus dem Client, keine bestätigte Route oder Kampfreihenfolge.","Bossliste","Auf meinem Level (%d) - %d Dungeons","Alle Karten und Guides (%d)","Client-Begegnungen 1.60.1.70888; im Spiel prüfen"},
 esES={"JEFES","Lista de jefes del cliente; recorrido y orden no verificados.","Lista de jefes","A mi nivel (%d) - %d mazmorras","Todos los mapas y guías (%d)","Encuentros del cliente 1.60.1.70888; comprueba en el juego"},
 esMX={"JEFES","Lista de jefes del cliente; recorrido y orden no verificados.","Lista de jefes","A mi nivel (%d) - %d calabozos","Todos los mapas y guías (%d)","Encuentros del cliente 1.60.1.70888; comprueba en el juego"},
 itIT={"BOSS","Elenco dei boss del client, non un percorso o ordine verificato.","Elenco boss","Al mio livello (%d) - %d dungeon","Tutte le mappe e guide (%d)","Incontri del client 1.60.1.70888; verifica in gioco"},
 ptBR={"CHEFES","Lista de chefes do cliente, sem rota ou ordem de combate verificada.","Lista de chefes","No meu nível (%d) - %d masmorras","Todos os mapas e guias (%d)","Encontros do cliente 1.60.1.70888; confira no jogo"},
 ruRU={"БОССЫ","Список боссов из клиента, не проверенный маршрут или порядок сражений.","Список боссов","Мой уровень (%d) - %d подземелий","Все карты и гайды (%d)","Данные клиента 1.60.1.70888; проверьте в игре"},
 koKR={"우두머리","클라이언트 우두머리 목록입니다. 경로와 전투 순서는 검증되지 않았습니다.","우두머리 목록","내 레벨 (%d) - 던전 %d개","전체 지도 및 가이드 (%d)","클라이언트 1.60.1.70888 정보, 게임에서 확인"},
 zhCN={"首领","客户端首领列表，路线与战斗顺序尚未验证。","首领列表","适合当前等级 (%d)：%d 个地下城","全部地图和指南 (%d)","客户端 1.60.1.70888 数据，请在游戏中确认"},
 zhTW={"首領","客戶端首領清單，路線與戰鬥順序尚未驗證。","首領清單","適合目前等級 (%d)：%d 個地城","所有地圖和指南 (%d)","客戶端 1.60.1.70888 資料，請在遊戲中確認"}
}
for code,values in pairs(lines) do
    local locale=A.NormalizeLocale(code)
    if locale then
        local strings=A.locales[locale] or {}
        for i,key in ipairs(keys) do strings[key]=values[i] end
        A.RegisterLocale(locale,strings)
    end
end
