-- Source-checked quest and access preparation for two related dungeons.
-- These tips do not alter boss IDs, route revisions, navigation or saved completion.
local A=DungeonMapPilot
local sf=A.routeVariants and A.routeVariants[33]
if sf and sf[1] and sf[1].steps then
    for _,step in ipairs(sf[1].steps) do
        if step.id=="archmage-arugal" then
            step.body.en="Before leaving Arugal's chamber, check for the Black Tome. Beta guides report that it helps unlock the optional Lyn the Ignored encounter in the City of Dalaran. Verify the steps in the live game."
            step.body.fr="Avant de quitter la chambre d'Arugal, vérifiez le Livre noir. Des guides bêta indiquent qu'il permet de débloquer Lyn the Ignored, un boss facultatif de la Cité de Dalaran. Confirmez ces étapes dans le jeu."
        end
    end
end
local dalaran=A.routeVariants and A.routeVariants[2959]
if dalaran and dalaran[1] and dalaran[1].steps then
    local steps=dalaran[1].steps
    if steps[1] and steps[1].id=="preparation" then
        steps[1].body.en="Access checklist: Alliance begins 'An Alarming Request' from Emissary Jacques in Southshore. Horde begins 'Key to the City' and 'Prison Break-In' from Magus Wordeen Voidglare in Tarren Mill. Finish your faction's access chain and use the sewers. Confirm the group's access before starting."
        steps[1].body.fr="Accès : Alliance, commencez « An Alarming Request » auprès de l'émissaire Jacques à Austrivage. Horde, commencez « Key to the City » et « Prison Break-In » auprès de Magus Wordeen Voidglare à Moulin-de-Tarren. Terminez la chaîne de votre faction et rejoignez les égouts. Vérifiez l'accès du groupe avant la sortie."
    end
    for _,step in ipairs(steps) do
        if step.id=="lyn-the-ignored" then
            step.body.en="Optional secret boss (beta): get the Black Tome in Shadowfang Keep, use it near the ley-line focus in Ambermill to receive the Tome of Dalaran, then look for a purple rune in the sewers past Atrexis. Verify current live requirements."
            step.body.fr="Boss secret facultatif (bêta) : récupérez le Livre noir au Donjon d'Ombrecroc, utilisez-le près du foyer des lignes telluriques à Moulin-de-l'Ambre pour obtenir le Tome de Dalaran, puis cherchez une rune violette dans les égouts après Atrexis. Vérifiez les conditions actuelles en jeu."
        end
    end
end
