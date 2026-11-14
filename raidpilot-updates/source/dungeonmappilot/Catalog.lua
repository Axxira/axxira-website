local A = DungeonMapPilot
-- Instance IDs (GetInstanceInfo), never UI map IDs. Floor art: ReferenceMaps.lua.
A.catalog = {
    [33] = { en = "Shadowfang Keep", fr = "Donjon d'Ombrecroc" },
    [34] = { en = "The Stockade", fr = "La Prison" },
    [36] = { en = "The Deadmines", fr = "Les Mortemines" },
    [43] = { en = "Wailing Caverns", fr = "Cavernes des Lamentations" },
    [47] = { en = "Razorfen Kraul", fr = "Kraal de Tranchebauge" },
    [48] = { en = "Blackfathom Deeps", fr = "Profondeurs de Brassenoire" },
    [70] = { en = "Uldaman", fr = "Uldaman" },
    [90] = { en = "Gnomeregan", fr = "Gnomeregan" },
    [109] = { en = "Sunken Temple", fr = "Temple englouti" },
    [129] = { en = "Razorfen Downs", fr = "Souilles de Tranchebauge" },
    [189] = { en = "Scarlet Monastery", fr = "Monastère écarlate" },
    [209] = { en = "Zul'Farrak", fr = "Zul'Farrak" },
    [229] = { en = "Blackrock Spire", fr = "Pic Rochenoire" },
    [230] = { en = "Blackrock Depths", fr = "Profondeurs de Rochenoire" },
    [249] = { en = "Onyxia's Lair", fr = "Repaire d'Onyxia", raid = true },
    [289] = { en = "Scholomance", fr = "Scholomance" },
    [309] = { en = "Zul'Gurub", fr = "Zul'Gurub", raid = true },
    [329] = { en = "Stratholme", fr = "Stratholme" },
    [349] = { en = "Maraudon", fr = "Maraudon" },
    [389] = { en = "Ragefire Chasm", fr = "Gouffre de Ragefeu" },
    [409] = { en = "Molten Core", fr = "Cœur du Magma", raid = true },
    [429] = { en = "Dire Maul", fr = "Hache-tripes" },
    [469] = { en = "Blackwing Lair", fr = "Repaire de l'Aile noire", raid = true },
    [509] = { en = "Ruins of Ahn'Qiraj", fr = "Ruines d'Ahn'Qiraj", raid = true },
    [531] = { en = "Ahn'Qiraj Temple", fr = "Temple d'Ahn'Qiraj", raid = true },
}

function A.CatalogName(id)
    local item = A.catalog[id]
    if not item then return A.L("unknown") end
    local key="instance."..id
    if A.HasText(key) then return A.L(key) end
    return A.Language()=="frFR" and item.fr or item.en or A.L("unknown")
end

function A.CatalogList()
    local items = {}
    for id in pairs(A.catalog) do items[#items + 1] = { id = id, name = A.CatalogName(id) } end
    table.sort(items, function(a, b) return a.name < b.name end)
    return items
end

-- Names follow the reference illustrations, not assertions about Forever encounters.
A.floorNamesFR = {
    [33] = { "Cour", "Salle à manger", "Antre vacant", "Observatoire inférieur", "Observatoire supérieur", "Chambre de Godfrey", "Chemin de ronde" },
    [36] = { "Les Mortemines", "Crique du cuirassé" },
    [48] = { "Bassin d'Ask'Ar", "Sanctuaire de la Lune", "Bassin oublié" },
    [70] = { "Salle des gardiens", "Siège de Khaz'Goroth" },
    [90] = { "Salle des engrenages", "Dortoir", "Quai de lancement", "Cour des Brikabrok" },
    [189] = { "Cimetière", "Bibliothèque", "Armurerie", "Cathédrale" },
    [229] = { "Tazz'Alor", "Tunnels des toiles", "Cité d'Hordemar", "Salle de Main-Noire", "Antre d'Halycon", "Chambre de bataille", "Pic supérieur" },
    [230] = { "Bloc de détention", "Ville d'Ombreforge" },
    [289] = { "Reliquaire", "Chambre d'invocation", "Salle d'étude supérieure", "Bureau du directeur" },
    [329] = { "Place des Croisés", "Le Défi" },
    [349] = { "Cavernes de Maraudon", "Tombe de Zaetar" },
    [429] = { "Communs Gordok", "Jardins de la capitale", "Cour des Bien-nés", "Prison d'Immol'Thar", "Quartier de Crochebois", "Sanctuaire d'Eldretharr" },
    [469] = { "Garnison Gueule-de-dragon", "Salles du conflit", "Laboratoires cramoisis", "Repaire de Nefarian" },
    [531] = { "Souterrains de la ruche", "Portes du temple", "Caveau de C'Thun" },
}
