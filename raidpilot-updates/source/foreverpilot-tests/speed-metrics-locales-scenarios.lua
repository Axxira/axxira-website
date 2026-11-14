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
local langs={"enUS","frFR","deDE","esES","esMX","itIT","ptBR","ruRU","koKR","zhCN","zhTW"}
local function formats(s)
    local withoutEscapes=s:gsub("%%%%","")
    local tokens={}
    for fmt in withoutEscapes:gmatch("%%[%d%.%-+ #]*[sdf]") do
        tokens[#tokens+1]=fmt:sub(-1)
    end
    local _,literal=s:gsub("%%%%","")
    return table.concat(tokens,",")..":"..literal
end
local total=0
for _,code in ipairs(langs) do
    local map=B.locales[code]
    assert(map,"missing locale "..code)
    for _,key in ipairs(keys) do
        local text=map[key]
        assert(type(text)=="string" and #text>0,code.." missing text "..key)
        assert(formats(key)==formats(text),code.." format mismatch: "..key)
        total=total+1
    end
end
return "PASS metrics localization: "..total.." strings with matching format placeholders"