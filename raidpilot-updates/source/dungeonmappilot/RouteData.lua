local A = DungeonMapPilot
-- Original short instructions. Landmark anchors are approximate on the reference art,
-- not world coordinates. Sources and validation limits are recorded in README.md.
local function step(id, x, y, fr, en, bodyFR, bodyEN, kind, requires)
    return { id=id, floor=1, x=x, y=y, title={fr=fr,en=en}, body={fr=bodyFR,en=bodyEN}, kind=kind or "boss", requires=requires }
end
A.routeData = {
    [43] = { id="wc-classic", revision=1, steps={
        step("entrance", .46,.60, "Départ · le disciple", "Start · the disciple",
            "Rassemblez le groupe au portail. Repérez le disciple : vous reviendrez ici pour l'escorte.",
            "Gather at the portal. Remember the disciple's location: you will return here for the escort.", "start"),
        step("anacondra", .20,.39, "Dame Anacondra", "Lady Anacondra",
            "Explorez les plateformes de la première grande salle, côté ouest. Son emplacement peut varier.",
            "Check the platforms in the first large cavern, on the west side. Her spawn can vary."),
        step("cobrahn", .16,.56, "Seigneur Cobrahn", "Lord Cobrahn",
            "Poursuivez dans la branche ouest jusqu'au bout du chemin montant.",
            "Continue along the western branch to the end of the rising path."),
        step("kresh", .31,.42, "Kresh", "Kresh",
            "Revenez au cours d'eau central. Cette tortue patrouille; ne la cherchez pas à un point fixe.",
            "Return to the central stream. This turtle patrols; do not search a single fixed spot.", "optional"),
        step("pythas", .62,.53, "Seigneur Pythas", "Lord Pythas",
            "Prenez la branche est. Dégagez les groupes proches avant d'engager le chef.",
            "Take the eastern branch. Clear nearby packs before engaging the leader."),
        step("skum", .62,.73, "Skum", "Skum",
            "Continuez vers le sud-est dans le passage sinueux. Combat facultatif pour l'escorte finale.",
            "Continue southeast through the winding passage. This fight is optional for the final escort.", "optional"),
        step("serpentis", .55,.46, "Seigneur Serpentis", "Lord Serpentis",
            "Suivez le passage montant vers la salle de Serpentis. Achevez les quatre chefs serpents.",
            "Follow the rising passage toward Serpentis's chamber. Finish all four Fanglords."),
        step("verdan", .39,.34, "Verdan l'Immortel", "Verdan the Everliving",
            "Après Serpentis, rejoignez Verdan près de la cascade. Boss facultatif; gardez le groupe réuni.",
            "After Serpentis, reach Verdan near the waterfall. Optional boss; keep the group together.", "optional"),
        step("disciple", .46,.60, "Retour au disciple", "Return to the disciple",
            "Revenez au portail par le trajet dégagé. Parlez au disciple une fois les quatre chefs vaincus.",
            "Return to the portal along the cleared route. Speak to the disciple after defeating all four leaders.", "return", {"anacondra","cobrahn","pythas","serpentis"}),
        step("mutanus", .34,.13, "Escorte · Mutanus", "Escort · Mutanus",
            "Suivez et protégez le disciple jusqu'à Naralex. Éliminez les vagues puis Mutanus.",
            "Follow and protect the disciple to Naralex. Defeat the waves, then Mutanus.", "event", {"disciple"}),
    } },
    [389] = { id="rfc-classic", revision=1, steps={
        step("entrance", .62,.07, "Départ · portail", "Start · portal",
            "Préparez le groupe à l'entrée puis descendez le tunnel. Restez sur les passages rocheux.",
            "Prepare the party at the entrance, then descend the tunnel. Stay on the rocky paths.", "start"),
        step("oggleflint", .67,.64, "Lorgnesilex", "Oggleflint",
            "Prenez la branche des troggs. Vérifiez le corps de Maur si vous avez sa quête.",
            "Take the trogg branch. Check Maur's body if you have his quest."),
        step("taragaman", .36,.58, "Taragaman l'Affameur", "Taragaman the Hungerer",
            "Rejoignez la grande salle de lave et sa plateforme centrale. Éloignez-vous des rebords.",
            "Reach the large lava chamber and its central platform. Stay away from the edges."),
        step("jergosh", .38,.88, "Jergosh l'Invocateur", "Jergosh the Invoker",
            "Continuez au-delà de Taragaman vers les cultistes. Dégagez les gardes avant le boss.",
            "Continue beyond Taragaman toward the cultists. Clear the guards before the boss."),
        step("bazzalan", .28,.79, "Bazzalan", "Bazzalan",
            "Prenez la rampe voisine pour atteindre Bazzalan. Vérifiez vos objectifs avant de repartir.",
            "Take the nearby ramp to reach Bazzalan. Check your objectives before leaving."),
    } },
}

-- Display keys are separate from the stable route/step identifiers used by progress.
for instanceID, route in pairs(A.routeData) do
    for _, routeStep in ipairs(route.steps) do
        local key = "route." .. instanceID .. "." .. routeStep.id
        routeStep.title.localeKey = key .. ".title"
        routeStep.body.localeKey = key .. ".body"
    end
end
