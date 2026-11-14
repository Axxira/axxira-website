-- Local UI wording; free-form notes are never translated or sent anywhere.
local A=DungeonMapPilot
local names={"bossNoteButton","bossNoteTitle","bossNoteHelp","bossNoteSave",
             "bossNoteClear","bossNoteUndo","bossNoteEmpty","bossNoteBlocked",
             "bossNoteUndone","bossNotePersonal","resumeNextStep","resumeNextHelp"}
local translations={
enUS={"Notes","Personal boss note","Write up to 200 characters for this encounter.",
      "Save","Clear","Undo note","No personal note for this encounter.",
      "Unable to change this note. Leave combat and check the text.",
      "Previous note restored.","Stored locally for this character and this boss.",
      "Resume","Show the first unfinished and available step. Does not mark anything completed."},
frFR={"Notes","Note personnelle du boss","Écrivez jusqu'à 200 caractères pour cette rencontre.",
      "Enregistrer","Effacer","Annuler note","Aucune note personnelle pour ce boss.",
      "Impossible de modifier la note. Quittez le combat et vérifiez le texte.",
      "Note précédente rétablie.","Enregistrée localement pour ce personnage et ce boss.",
      "Reprendre","Afficher la première étape inachevée et accessible, sans rien valider."},
deDE={"Notizen","Persönliche Bossnotiz","Bis zu 200 Zeichen für diese Begegnung.",
      "Speichern","Löschen","Notiz rückgängig","Keine persönliche Notiz für diesen Boss.",
      "Notiz kann nicht geändert werden. Kampf beenden und Text prüfen.",
      "Vorherige Notiz wiederhergestellt.","Lokal für diesen Charakter und Boss gespeichert.",
      "Fortsetzen","Ersten offenen und verfügbaren Schritt anzeigen, ohne ihn abzuschließen."},
esES={"Notas","Nota personal del jefe","Hasta 200 caracteres para este encuentro.",
      "Guardar","Borrar","Deshacer nota","No hay notas personales para este jefe.",
      "No se pudo cambiar la nota. Sal del combate y comprueba el texto.",
      "Nota anterior restaurada.","Guardada localmente para este personaje y jefe.",
      "Continuar","Mostrar el primer paso disponible pendiente sin marcarlo como completado."},
esMX={"Notas","Nota personal del jefe","Hasta 200 caracteres para este encuentro.",
      "Guardar","Borrar","Deshacer nota","No hay notas personales para este jefe.",
      "No se pudo cambiar la nota. Sal del combate y comprueba el texto.",
      "Nota anterior restaurada.","Guardada localmente para este personaje y jefe.",
      "Continuar","Mostrar el primer paso disponible pendiente sin completarlo."},
itIT={"Note","Nota personale boss","Fino a 200 caratteri per questo incontro.",
      "Salva","Cancella","Annulla nota","Nessuna nota personale per questo boss.",
      "Impossibile modificare la nota. Esci dal combattimento e controlla il testo.",
      "Nota precedente ripristinata.","Salvata localmente per il personaggio e il boss.",
      "Riprendi","Mostra il primo passaggio disponibile incompleto, senza completarlo."},
ptBR={"Notas","Nota pessoal do chefe","Até 200 caracteres para este encontro.",
      "Salvar","Limpar","Desfazer nota","Não há nota pessoal para este chefe.",
      "Não foi possível alterar a nota. Saia de combate e confira o texto.",
      "Nota anterior restaurada.","Salva localmente para este personagem e chefe.",
      "Retomar","Exibe a primeira etapa pendente e disponível sem marcar como concluída."},
ruRU={"Заметки","Личная заметка о боссе","До 200 символов для этой встречи.",
      "Сохранить","Очистить","Отменить","Нет личной заметки об этом боссе.",
      "Нельзя изменить заметку. Выйдите из боя и проверьте текст.",
      "Предыдущая заметка восстановлена.","Хранится локально для этого персонажа и босса.",
      "Продолжить","Первый доступный незавершённый шаг без автоматического завершения."},
koKR={"메모","개인 우두머리 메모","이 전투에 대해 최대 200자까지 기록할 수 있습니다.",
      "저장","지우기","메모 취소","이 우두머리에 대한 개인 메모가 없습니다.",
      "메모를 변경할 수 없습니다. 전투를 끝낸 후 텍스트를 확인하세요.",
      "이전 메모가 복원되었습니다.","이 캐릭터와 우두머리용으로 로컬 저장됩니다.",
      "이어하기","완료하지 않은 첫 번째 진행 가능한 단계를 표시합니다. 자동 완료되지 않습니다."},
zhCN={"笔记","首领个人笔记","为此次战斗记录不超过 200 字。",
      "保存","清除","撤销笔记","没有此首领的个人笔记。",
      "无法修改笔记。请脱离战斗并检查文字。",
      "已恢复上一条笔记。","仅保存在本角色和首领的本地数据中。",
      "继续","显示第一个尚未完成且可进行的步骤，不会自动完成。"},
zhTW={"筆記","首領個人筆記","為此次戰鬥記錄不超過 200 字。",
      "儲存","清除","復原筆記","沒有此首領的個人筆記。",
      "無法修改筆記。請脫離戰鬥並檢查文字。",
      "已還原上一則筆記。","僅儲存在此角色與首領的本機資料中。",
      "繼續","顯示第一個尚未完成且可進行的步驟，不會自動完成。"}
}
for code,values in pairs(translations) do
    local canonical=A.NormalizeLocale(code)
    if canonical then
        local text=A.locales[canonical] or {}
        for i,key in ipairs(names) do text[key]=values[i] end
        A.RegisterLocale(canonical,text)
    end
end
