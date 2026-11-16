-- Labels for the FastXP map-route toggle, in all supported client locales.
local B=ForeverPilotLeveling
local labels={
 enUS={"Map route: enabled","Map route: disabled"},
 frFR={"Tracé sur carte : actif","Tracé sur carte : désactivé"},
 deDE={"Kartenroute: aktiv","Kartenroute: aus"},
 esES={"Ruta en el mapa: activada","Ruta en el mapa: desactivada"},
 itIT={"Percorso sulla mappa: attivo","Percorso sulla mappa: disattivato"},
 ptBR={"Rota no mapa: ativada","Rota no mapa: desativada"},
 ruRU={"Маршрут на карте: включён","Маршрут на карте: выключен"},
 koKR={"지도 경로: 켜짐","지도 경로: 꺼짐"},
 zhCN={"地图路线：开启","地图路线：关闭"},
 zhTW={"地圖路線：開啟","地圖路線：關閉"}
}
labels.esMX=labels.esES
for locale,t in pairs(labels) do
 local map=B.locales and B.locales[locale]
 if type(map)=="table" then
  map["Tracé sur carte : actif"]=t[1]
  map["Tracé sur carte : désactivé"]=t[2]
 end
end
