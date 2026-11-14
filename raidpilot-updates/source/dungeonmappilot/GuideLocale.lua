-- Additional non-deceptive labels for guide-only and announcement previews.
local A = DungeonMapPilot
A.guideExtraLocales={
 ["enUS"]={"No map illustration available for this dungeon. The guide is text only.","Announced content: encounters and playable route not yet verified.","Guide without map","Announced - preview only"},
 ["frFR"]={"Aucune illustration disponible pour ce donjon. Le guide est uniquement textuel.","Contenu annoncé : rencontres et parcours jouable pas encore vérifiés.","Guide sans carte","Annoncé - aperçu seulement"},
 ["deDE"]={"Keine Kartenillustration verfügbar. Diese Anleitung enthält nur Text.","Angekündigter Inhalt: Bosse und Spielroute sind noch nicht geprüft.","Anleitung ohne Karte","Angekündigt - nur Vorschau"},
 ["esES"]={"No hay mapa ilustrado disponible. Esta guía contiene solo texto.","Contenido anunciado: encuentros y ruta jugable sin verificar.","Guía sin mapa","Anunciado - solo avance"},
 ["esMX"]={"No hay mapa ilustrado disponible. Esta guía contiene solo texto.","Contenido anunciado: encuentros y ruta jugable sin verificar.","Guía sin mapa","Anunciado - solo avance"},
 ["itIT"]={"Non è disponibile una mappa illustrata. Questa guida è solo testuale.","Contenuto annunciato: incontri e percorso non ancora verificati.","Guida senza mappa","Annunciato - solo anteprima"},
 ["ptBR"]={"Mapa ilustrado indisponível. Este guia contém apenas texto.","Conteúdo anunciado: encontros e rota jogável ainda não verificados.","Guia sem mapa","Anunciado - prévia"},
 ["ruRU"]={"Карта этого подземелья недоступна. Руководство содержит только текст.","Анонсировано: боссы и маршрут ещё не проверены.","Гид без карты","Анонсировано - обзор"},
 ["koKR"]={"던전 지도 이미지가 없습니다. 텍스트 안내만 제공됩니다.","예고된 콘텐츠: 보스와 진행 경로가 아직 검증되지 않았습니다.","지도 없는 안내","예고됨 - 미리보기"},
 ["zhCN"]={"该地下城没有地图插图，仅提供文字指南。","已公布内容：首领和可用路线尚未验证。","无地图的指南","已公布 - 仅预览"},
 ["zhTW"]={"此地城沒有地圖插圖，僅提供文字指南。","已公布內容：首領和可用路線尚未驗證。","無地圖的指南","已公布 - 僅預覽"},
}
-- Common route captions localized independently of boss names.
for code,values in pairs({
  ["enUS"]={"Text only","Text guide: unverified map markers. Check the route in game.","Select a wing"},
  ["frFR"]={"Texte","Guide texte : repères non vérifiés. Vérifiez les directions en jeu.","Choisir une aile"},
  ["deDE"]={"Text","Textanleitung: Kartenmarkierungen ungeprüft. Weg im Spiel prüfen.","Flügel wählen"},
  ["esES"]={"Texto","Guía de texto: marcas sin verificar. Comprueba la ruta en el juego.","Elegir ala"},
  ["esMX"]={"Texto","Guía textual: marcas sin verificar. Comprueba la ruta en el juego.","Elegir ala"},
  ["itIT"]={"Testo","Guida testuale: segnaposti non verificati. Controlla il percorso nel gioco.","Scegli ala"},
  ["ptBR"]={"Texto","Guia textual: marcadores não verificados. Confira a rota no jogo.","Escolher ala"},
  ["ruRU"]={"Текст","Текстовый маршрут: точки на карте не проверены. Сверьте путь в игре.","Выбрать крыло"},
  ["koKR"]={"텍스트","텍스트 가이드: 지도 위치가 검증되지 않았습니다. 게임 내에서 확인하세요.","구역 선택"},
  ["zhCN"]={"文字","文字路线：地图标记未经验证。请在游戏内确认路线。","选择区域"},
  ["zhTW"]={"文字","文字路線：地圖標記尚未驗證。請在遊戲內確認路線。","選擇區域"},
}) do
  local canonical=A.NormalizeLocale(code)
  if canonical then
    local t=A.locales[canonical] or {}
    t.guideTextBadge=values[1]
    t.guideTextOnly=values[2]
    t.guideWing=values[3]
    t.guideNoArt=(A.guideExtraLocales[canonical] or A.guideExtraLocales.enUS)[1]
    t.guidePreviewOnly=(A.guideExtraLocales[canonical] or A.guideExtraLocales.enUS)[2]
    t.guideBrowseText=(A.guideExtraLocales[canonical] or A.guideExtraLocales.enUS)[3]
    t.guideBrowsePreview=(A.guideExtraLocales[canonical] or A.guideExtraLocales.enUS)[4]
    A.RegisterLocale(canonical,t)
  end
end
