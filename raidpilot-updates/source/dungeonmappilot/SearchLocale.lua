-- Search interface captions; boss names remain original names from the guide.
local A=DungeonMapPilot
local labels={
 enUS={"Find boss","Find a boss or encounter","Search a boss name, or a dungeon. Matches never complete a step.","Search encounters","No results. Try a boss name or another dungeon.","%d results","More results exist. Refine the search.","Inspect","Read-only preview. Your saved route and defeated boss steps remain unchanged."},
 frFR={"Chercher un boss","Rechercher un boss ou une rencontre","Tapez un boss ou un donjon. Une recherche ne termine aucune étape.","Chercher les rencontres","Aucun résultat. Essayez un autre boss ou donjon.","%d résultats","D'autres résultats existent. Précisez la recherche.","Examiner","Aperçu en lecture seule. Le parcours sauvegardé et les boss terminés restent inchangés."},
 deDE={"Boss suchen","Boss oder Begegnung suchen","Bossname oder Dungeon eingeben. Suche erledigt keine Schritte.","Begegnungen suchen","Keine Ergebnisse. Anderen Boss oder Dungeon versuchen.","%d Ergebnisse","Weitere Ergebnisse vorhanden. Suche eingrenzen.","Ansehen","Nur Vorschau. Gespeicherter Fortschritt bleibt unverändert."},
 esES={"Buscar jefe","Buscar un jefe o encuentro","Escribe un jefe o mazmorra. Buscar no completa pasos.","Buscar encuentros","Sin resultados. Prueba otro jefe o mazmorra.","%d resultados","Hay más resultados. Refina la búsqueda.","Examinar","Vista de solo lectura. El progreso guardado no cambia."},
 esMX={"Buscar jefe","Buscar un jefe o encuentro","Escribe un jefe o calabozo. Buscar no completa pasos.","Buscar encuentros","Sin resultados. Prueba otro jefe o calabozo.","%d resultados","Hay más resultados. Refina la búsqueda.","Examinar","Vista de solo lectura. El progreso guardado no cambia."},
 itIT={"Cerca boss","Cerca boss o incontro","Digita un boss o dungeon. La ricerca non completa passaggi.","Cerca incontri","Nessun risultato. Prova un altro boss o dungeon.","%d risultati","Altri risultati presenti. Restringi la ricerca.","Esamina","Anteprima di sola lettura. Il progresso salvato non cambia."},
 ptBR={"Buscar chefe","Buscar chefe ou encontro","Digite chefe ou masmorra. A busca não conclui etapas.","Buscar encontros","Nenhum resultado. Tente outro chefe ou masmorra.","%d resultados","Há mais resultados. Refine a busca.","Examinar","Prévia de leitura. Seu progresso salvo não muda."},
 ruRU={"Найти босса","Поиск босса или встречи","Введите босса или подземелье. Поиск не завершает этапы.","Найти встречи","Нет результатов. Попробуйте другое имя.","%d результатов","Есть другие результаты. Уточните поиск.","Просмотр","Только просмотр. Сохранённый прогресс не меняется."},
 koKR={"보스 검색","우두머리 또는 전투 검색","보스나 던전 이름을 입력하세요. 검색은 진행도를 바꾸지 않습니다.","전투 검색","결과가 없습니다. 다른 이름으로 검색하세요.","결과 %d개","결과가 더 있습니다. 검색어를 구체화하세요.","보기","읽기 전용 미리보기입니다. 저장한 진행도는 변경되지 않습니다."},
 zhCN={"查找首领","搜索首领或战斗","输入首领或地下城名称。搜索不会完成任何步骤。","搜索战斗","没有结果，请尝试其他名称。","%d 个结果","还有更多结果，请缩小搜索范围。","查看","仅供预览，已保存的路线和首领进度不会改变。"},
 zhTW={"查找首領","搜尋首領或戰鬥","輸入首領或地城名稱。搜尋不會完成任何步驟。","搜尋戰鬥","沒有結果，請嘗試其他名稱。","%d 個結果","還有更多結果，請縮小搜尋範圍。","檢視","僅供預覽，已儲存的路線與首領進度不會改變。"}
}
local keys={"searchBoss","searchBossTitle","searchBossHint","searchBossAction","searchBossEmpty",
            "searchBossResults","searchBossLimit","searchBossInspect","searchBossPreview"}
for code,values in pairs(labels) do
    local canonical=A.NormalizeLocale(code)
    if canonical then
        local t=A.locales[canonical] or {}
        for i,key in ipairs(keys) do t[key]=values[i] end
        A.RegisterLocale(canonical,t)
    end
end
