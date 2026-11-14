-- Newer client-recorded encounter rosters for 5 WoW Forever dungeons (1.60.1.70888 and newer).
-- NOT corridor routes, NOT confirmed boss mechanics, NOT fake map coordinates or artwork.
-- Cross reference: https://forevermastery.com/en/guides/wow-forever-dungeons (updated 2026-11-12).
-- Community encounter data may disagree. Players should verify the actual game before a run.
local A = DungeonMapPilot
do
 local id=2970
 local original=A.routeVariants[id] and A.routeVariants[id][1]
 local meta=A.customGuideMeta and A.customGuideMeta[id]
 local catalog=A.catalog and A.catalog[id]
 if original and meta and catalog then
  meta.status="client-roster";meta.clientBuild="1.60.1.70888";meta.routeOrderVerified=false
  catalog.previewOnly=false;catalog.rosterOnly=true;catalog.guideOnly=true
  A.routeVariants[id] = {{id="forever:2970",revision="v2:roster-70888:2970",
   kind="forever-client-roster",guideOnly=true,rosterOnly=true,orderUnverified=true,previewOnly=false,
   clientBuild="1.60.1.70888",levelMin=original.levelMin,levelMax=original.levelMax,name=original.name,steps={
    {id="preparation",kind="start",title={en="Before the run",fr="Avant la sortie"},
      body={en="Underwater entrance off the Stranglethorn Vale coast, west of Grom'gol. Confirm water access and the party's exit plan. The following is a client encounter roster, not a navigable route.",fr="Entrée sous-marine au large de Strangleronce, à l'ouest de Grom'gol. Vérifiez l'accès à l'eau et le retour du groupe. La liste suivante vient du client, pas d'un chemin parcouru."}},
    {id="zul-alai",kind="boss",title={en="Zul'Alai",fr="Zul'Alai"},body={en="Client encounter listing: Zul'Alai. This index is not a verified kill order. Confirm its room, tactics and eligibility before the pull.",fr="Boss recensé dans le client : Zul'Alai. Cet index ne prouve ni l'ordre des combats ni le chemin. Vérifiez la salle, les mécaniques et l'accès avant d'engager."}},
    {id="var-taka",kind="boss",title={en="Var'Taka",fr="Var'Taka"},body={en="Client encounter listing: Var'Taka. This index is not a verified kill order. Confirm its room, tactics and eligibility before the pull.",fr="Boss recensé dans le client : Var'Taka. Cet index ne prouve ni l'ordre des combats ni le chemin. Vérifiez la salle, les mécaniques et l'accès avant d'engager."}},
    {id="captain-dreadrise",kind="boss",title={en="Captain Dreadrise",fr="Captain Dreadrise"},body={en="Client encounter listing: Captain Dreadrise. This index is not a verified kill order. Confirm its room, tactics and eligibility before the pull.",fr="Boss recensé dans le client : Captain Dreadrise. Cet index ne prouve ni l'ordre des combats ni le chemin. Vérifiez la salle, les mécaniques et l'accès avant d'engager."}},
    {id="deathless-guardians",kind="boss",title={en="Deathless Guardians",fr="Deathless Guardians"},body={en="Client encounter listing: Deathless Guardians. This index is not a verified kill order. Confirm its room, tactics and eligibility before the pull.",fr="Boss recensé dans le client : Deathless Guardians. Cet index ne prouve ni l'ordre des combats ni le chemin. Vérifiez la salle, les mécaniques et l'accès avant d'engager."}},
    {id="primeval-elemental",kind="boss",title={en="Primeval Elemental",fr="Primeval Elemental"},body={en="Client encounter listing: Primeval Elemental. This index is not a verified kill order. Confirm its room, tactics and eligibility before the pull.",fr="Boss recensé dans le client : Primeval Elemental. Cet index ne prouve ni l'ordre des combats ni le chemin. Vérifiez la salle, les mécaniques et l'accès avant d'engager."}},
    {id="min-loth-the-serpent",kind="boss",title={en="Min'loth the Serpent",fr="Min'loth the Serpent"},body={en="Client encounter listing: Min'loth the Serpent. This index is not a verified kill order. Confirm its room, tactics and eligibility before the pull.",fr="Boss recensé dans le client : Min'loth the Serpent. Cet index ne prouve ni l'ordre des combats ni le chemin. Vérifiez la salle, les mécaniques et l'accès avant d'engager."}},
   }}}
 end
end
do
 local id=3039
 local original=A.routeVariants[id] and A.routeVariants[id][1]
 local meta=A.customGuideMeta and A.customGuideMeta[id]
 local catalog=A.catalog and A.catalog[id]
 if original and meta and catalog then
  meta.status="client-roster";meta.clientBuild="1.60.1.70888";meta.routeOrderVerified=false
  catalog.previewOnly=false;catalog.rosterOnly=true;catalog.guideOnly=true
  A.routeVariants[id] = {{id="forever:3039",revision="v2:roster-70888:3039",
   kind="forever-client-roster",guideOnly=true,rosterOnly=true,orderUnverified=true,previewOnly=false,
   clientBuild="1.60.1.70888",levelMin=original.levelMin,levelMax=original.levelMax,name=original.name,steps={
    {id="preparation",kind="start",title={en="Before the run",fr="Avant la sortie"},
      body={en="Ogre stronghold in Riverglades. Confirm group size and the available entrance in the live client: published five-player guidance conflicts with one client party-size field. Encounter order is not a travel path.",fr="Bastion ogre dans Riverglades. Vérifiez la taille du groupe et l'entrée dans le jeu : une donnée client contredit les annonces pour cinq joueurs. L'ordre des boss n'est pas un cheminement."}},
    {id="slavecrusher-grox",kind="boss",title={en="Slavecrusher Grox",fr="Slavecrusher Grox"},body={en="Client encounter listing: Slavecrusher Grox. This index is not a verified kill order. Confirm its room, tactics and eligibility before the pull.",fr="Boss recensé dans le client : Slavecrusher Grox. Cet index ne prouve ni l'ordre des combats ni le chemin. Vérifiez la salle, les mécaniques et l'accès avant d'engager."}},
    {id="twilight-commander-zarmok",kind="boss",title={en="Twilight Commander Zarmok",fr="Twilight Commander Zarmok"},body={en="Client encounter listing: Twilight Commander Zarmok. This index is not a verified kill order. Confirm its room, tactics and eligibility before the pull.",fr="Boss recensé dans le client : Twilight Commander Zarmok. Cet index ne prouve ni l'ordre des combats ni le chemin. Vérifiez la salle, les mécaniques et l'accès avant d'engager."}},
    {id="handler-ashka",kind="boss",title={en="Handler Ashka",fr="Handler Ashka"},body={en="Client encounter listing: Handler Ashka. This index is not a verified kill order. Confirm its room, tactics and eligibility before the pull.",fr="Boss recensé dans le client : Handler Ashka. Cet index ne prouve ni l'ordre des combats ni le chemin. Vérifiez la salle, les mécaniques et l'accès avant d'engager."}},
    {id="nar-gurk",kind="boss",title={en="Nar'gurk",fr="Nar'gurk"},body={en="Client encounter listing: Nar'gurk. This index is not a verified kill order. Confirm its room, tactics and eligibility before the pull.",fr="Boss recensé dans le client : Nar'gurk. Cet index ne prouve ni l'ordre des combats ni le chemin. Vérifiez la salle, les mécaniques et l'accès avant d'engager."}},
    {id="overseer-mugg",kind="boss",title={en="Overseer Mugg",fr="Overseer Mugg"},body={en="Client encounter listing: Overseer Mugg. This index is not a verified kill order. Confirm its room, tactics and eligibility before the pull.",fr="Boss recensé dans le client : Overseer Mugg. Cet index ne prouve ni l'ordre des combats ni le chemin. Vérifiez la salle, les mécaniques et l'accès avant d'engager."}},
   }}}
 end
end
do
 local id=2994
 local original=A.routeVariants[id] and A.routeVariants[id][1]
 local meta=A.customGuideMeta and A.customGuideMeta[id]
 local catalog=A.catalog and A.catalog[id]
 if original and meta and catalog then
  meta.status="client-roster";meta.clientBuild="1.60.1.70888";meta.routeOrderVerified=false
  catalog.previewOnly=false;catalog.rosterOnly=true;catalog.guideOnly=true
  A.routeVariants[id] = {{id="forever:2994",revision="v2:roster-70888:2994",
   kind="forever-client-roster",guideOnly=true,rosterOnly=true,orderUnverified=true,previewOnly=false,
   clientBuild="1.60.1.70888",levelMin=original.levelMin,levelMax=original.levelMax,name=original.name,steps={
    {id="preparation",kind="start",title={en="Before the run",fr="Avant la sortie"},
      body={en="On Alcaz Island in Dustwallow Marsh. Defias and naga are fighting for the prison. Check faction quests and the route to the island; the client lists encounters but not verified navigation.",fr="Sur l'île d'Alcaz, dans les marécages d'Âprefange. Les Défias et les nagas se disputent la prison. Vérifiez les quêtes et l'accès à l'île ; le client énumère les boss sans itinéraire vérifié."}},
    {id="overseer-wrex",kind="boss",title={en="Overseer Wrex",fr="Overseer Wrex"},body={en="Client encounter listing: Overseer Wrex. This index is not a verified kill order. Confirm its room, tactics and eligibility before the pull.",fr="Boss recensé dans le client : Overseer Wrex. Cet index ne prouve ni l'ordre des combats ni le chemin. Vérifiez la salle, les mécaniques et l'accès avant d'engager."}},
    {id="tidemistress-ca-zass",kind="boss",title={en="Tidemistress Ca'zass",fr="Tidemistress Ca'zass"},body={en="Client encounter listing: Tidemistress Ca'zass. This index is not a verified kill order. Confirm its room, tactics and eligibility before the pull.",fr="Boss recensé dans le client : Tidemistress Ca'zass. Cet index ne prouve ni l'ordre des combats ni le chemin. Vérifiez la salle, les mécaniques et l'accès avant d'engager."}},
    {id="katerina-the-cleaner",kind="boss",title={en="Katerina the Cleaner",fr="Katerina the Cleaner"},body={en="Client encounter listing: Katerina the Cleaner. This index is not a verified kill order. Confirm its room, tactics and eligibility before the pull.",fr="Boss recensé dans le client : Katerina the Cleaner. Cet index ne prouve ni l'ordre des combats ni le chemin. Vérifiez la salle, les mécaniques et l'accès avant d'engager."}},
    {id="the-questioner",kind="boss",title={en="The Questioner",fr="The Questioner"},body={en="Client encounter listing: The Questioner. This index is not a verified kill order. Confirm its room, tactics and eligibility before the pull.",fr="Boss recensé dans le client : The Questioner. Cet index ne prouve ni l'ordre des combats ni le chemin. Vérifiez la salle, les mécaniques et l'accès avant d'engager."}},
    {id="blazeroar",kind="boss",title={en="Blazeroar",fr="Blazeroar"},body={en="Client encounter listing: Blazeroar. This index is not a verified kill order. Confirm its room, tactics and eligibility before the pull.",fr="Boss recensé dans le client : Blazeroar. Cet index ne prouve ni l'ordre des combats ni le chemin. Vérifiez la salle, les mécaniques et l'accès avant d'engager."}},
   }}}
 end
end
do
 local id=2974
 local original=A.routeVariants[id] and A.routeVariants[id][1]
 local meta=A.customGuideMeta and A.customGuideMeta[id]
 local catalog=A.catalog and A.catalog[id]
 if original and meta and catalog then
  meta.status="client-roster";meta.clientBuild="1.60.1.70888";meta.routeOrderVerified=false
  catalog.previewOnly=false;catalog.rosterOnly=true;catalog.guideOnly=true
  A.routeVariants[id] = {{id="forever:2974",revision="v2:roster-70888:2974",
   kind="forever-client-roster",guideOnly=true,rosterOnly=true,orderUnverified=true,previewOnly=false,
   clientBuild="1.60.1.70888",levelMin=original.levelMin,levelMax=original.levelMax,name=original.name,steps={
    {id="preparation",kind="start",title={en="Before the run",fr="Avant la sortie"},
      body={en="Corrupted furbolg stronghold in Azshara. Several branches may exist. Confirm the accessible entrance and whether the party intends to visit the Barrow Deeps connection before starting.",fr="Cité furbolg corrompue en Azshara. Plusieurs embranchements sont possibles. Vérifiez l'entrée et l'objectif du groupe, notamment la connexion éventuelle avec Barrow Deeps."}},
    {id="withermaw",kind="boss",title={en="Withermaw",fr="Withermaw"},body={en="Client encounter listing: Withermaw. This index is not a verified kill order. Confirm its room, tactics and eligibility before the pull.",fr="Boss recensé dans le client : Withermaw. Cet index ne prouve ni l'ordre des combats ni le chemin. Vérifiez la salle, les mécaniques et l'accès avant d'engager."}},
    {id="impaan-the-imprisoner",kind="boss",title={en="Impaan the Imprisoner",fr="Impaan the Imprisoner"},body={en="Client encounter listing: Impaan the Imprisoner. This index is not a verified kill order. Confirm its room, tactics and eligibility before the pull.",fr="Boss recensé dans le client : Impaan the Imprisoner. Cet index ne prouve ni l'ordre des combats ni le chemin. Vérifiez la salle, les mécaniques et l'accès avant d'engager."}},
    {id="elder-shaman-bloodmane",kind="boss",title={en="Elder Shaman Bloodmane",fr="Elder Shaman Bloodmane"},body={en="Client encounter listing: Elder Shaman Bloodmane. This index is not a verified kill order. Confirm its room, tactics and eligibility before the pull.",fr="Boss recensé dans le client : Elder Shaman Bloodmane. Cet index ne prouve ni l'ordre des combats ni le chemin. Vérifiez la salle, les mécaniques et l'accès avant d'engager."}},
    {id="bonesnout-the-unbroken",kind="boss",title={en="Bonesnout the Unbroken",fr="Bonesnout the Unbroken"},body={en="Client encounter listing: Bonesnout the Unbroken. This index is not a verified kill order. Confirm its room, tactics and eligibility before the pull.",fr="Boss recensé dans le client : Bonesnout the Unbroken. Cet index ne prouve ni l'ordre des combats ni le chemin. Vérifiez la salle, les mécaniques et l'accès avant d'engager."}},
    {id="varrox-bleakhoof",kind="boss",title={en="Varrox Bleakhoof",fr="Varrox Bleakhoof"},body={en="Client encounter listing: Varrox Bleakhoof. This index is not a verified kill order. Confirm its room, tactics and eligibility before the pull.",fr="Boss recensé dans le client : Varrox Bleakhoof. Cet index ne prouve ni l'ordre des combats ni le chemin. Vérifiez la salle, les mécaniques et l'accès avant d'engager."}},
    {id="blackmaw-behemoth",kind="boss",title={en="Blackmaw Behemoth",fr="Blackmaw Behemoth"},body={en="Client encounter listing: Blackmaw Behemoth. This index is not a verified kill order. Confirm its room, tactics and eligibility before the pull.",fr="Boss recensé dans le client : Blackmaw Behemoth. Cet index ne prouve ni l'ordre des combats ni le chemin. Vérifiez la salle, les mécaniques et l'accès avant d'engager."}},
    {id="unbound-spirit",kind="boss",title={en="Unbound Spirit",fr="Unbound Spirit"},body={en="Client encounter listing: Unbound Spirit. This index is not a verified kill order. Confirm its room, tactics and eligibility before the pull.",fr="Boss recensé dans le client : Unbound Spirit. Cet index ne prouve ni l'ordre des combats ni le chemin. Vérifiez la salle, les mécaniques et l'accès avant d'engager."}},
   }}}
 end
end
do
 local id=3001
 local original=A.routeVariants[id] and A.routeVariants[id][1]
 local meta=A.customGuideMeta and A.customGuideMeta[id]
 local catalog=A.catalog and A.catalog[id]
 if original and meta and catalog then
  meta.status="client-roster";meta.clientBuild="1.60.1.70888";meta.routeOrderVerified=false
  catalog.previewOnly=false;catalog.rosterOnly=true;catalog.guideOnly=true
  A.routeVariants[id] = {{id="forever:3001",revision="v2:roster-70888:3001",
   kind="forever-client-roster",guideOnly=true,rosterOnly=true,orderUnverified=true,previewOnly=false,
   clientBuild="1.60.1.70888",levelMin=original.levelMin,levelMax=original.levelMax,name=original.name,steps={
    {id="preparation",kind="start",title={en="Before the run",fr="Avant la sortie"},
      body={en="Titan facility in northern Un'Goro. Bring the power crystals needed for the consoles and check who carries them. The list is client encounter order, not an in-game route.",fr="Installation titanesque au nord d'Un'Goro. Prévoyez les cristaux nécessaires aux consoles et décidez qui les portera. La liste suit le journal du client, pas un itinéraire testé."}},
    {id="titanosaur",kind="boss",title={en="Titanosaur",fr="Titanosaur"},body={en="Client encounter listing: Titanosaur. This index is not a verified kill order. Confirm its room, tactics and eligibility before the pull.",fr="Boss recensé dans le client : Titanosaur. Cet index ne prouve ni l'ordre des combats ni le chemin. Vérifiez la salle, les mécaniques et l'accès avant d'engager."}},
    {id="ravasaur-hunters",kind="boss",title={en="Ravasaur Hunters",fr="Ravasaur Hunters"},body={en="Client encounter listing: Ravasaur Hunters. This index is not a verified kill order. Confirm its room, tactics and eligibility before the pull.",fr="Boss recensé dans le client : Ravasaur Hunters. Cet index ne prouve ni l'ordre des combats ni le chemin. Vérifiez la salle, les mécaniques et l'accès avant d'engager."}},
    {id="thunderhoof",kind="boss",title={en="Thunderhoof",fr="Thunderhoof"},body={en="Client encounter listing: Thunderhoof. This index is not a verified kill order. Confirm its room, tactics and eligibility before the pull.",fr="Boss recensé dans le client : Thunderhoof. Cet index ne prouve ni l'ordre des combats ni le chemin. Vérifiez la salle, les mécaniques et l'accès avant d'engager."}},
    {id="venomridge",kind="boss",title={en="Venomridge",fr="Venomridge"},body={en="Client encounter listing: Venomridge. This index is not a verified kill order. Confirm its room, tactics and eligibility before the pull.",fr="Boss recensé dans le client : Venomridge. Cet index ne prouve ni l'ordre des combats ni le chemin. Vérifiez la salle, les mécaniques et l'accès avant d'engager."}},
    {id="direhorn-matriarch",kind="boss",title={en="Direhorn Matriarch",fr="Direhorn Matriarch"},body={en="Client encounter listing: Direhorn Matriarch. This index is not a verified kill order. Confirm its room, tactics and eligibility before the pull.",fr="Boss recensé dans le client : Direhorn Matriarch. Cet index ne prouve ni l'ordre des combats ni le chemin. Vérifiez la salle, les mécaniques et l'accès avant d'engager."}},
    {id="cinder",kind="boss",title={en="Cinder",fr="Cinder"},body={en="Client roster: Cinder. Community reports say the Archival Panel may be needed to activate the encounter. Verify in game; this is not a mapped route.",fr="Liste du client : Cinder. Des témoignages indiquent que le Panneau d'archives peut déclencher la rencontre. Vérifiez en jeu ; aucune route n'est cartographiée."}},
    {id="king-op",kind="boss",title={en="King Op",fr="King Op"},body={en="Client encounter listing: King Op. This index is not a verified kill order. Confirm its room, tactics and eligibility before the pull.",fr="Boss recensé dans le client : King Op. Cet index ne prouve ni l'ordre des combats ni le chemin. Vérifiez la salle, les mécaniques et l'accès avant d'engager."}},
    {id="snowtalon",kind="boss",title={en="Snowtalon",fr="Snowtalon"},body={en="Client encounter listing: Snowtalon. This index is not a verified kill order. Confirm its room, tactics and eligibility before the pull.",fr="Boss recensé dans le client : Snowtalon. Cet index ne prouve ni l'ordre des combats ni le chemin. Vérifiez la salle, les mécaniques et l'accès avant d'engager."}},
    {id="bolt",kind="boss",title={en="Bolt",fr="Bolt"},body={en="Client encounter listing: Bolt. This index is not a verified kill order. Confirm its room, tactics and eligibility before the pull.",fr="Boss recensé dans le client : Bolt. Cet index ne prouve ni l'ordre des combats ni le chemin. Vérifiez la salle, les mécaniques et l'accès avant d'engager."}},
    {id="vitrix",kind="boss",title={en="Vitrix",fr="Vitrix"},body={en="Client encounter listing: Vitrix. This index is not a verified kill order. Confirm its room, tactics and eligibility before the pull.",fr="Boss recensé dans le client : Vitrix. Cet index ne prouve ni l'ordre des combats ni le chemin. Vérifiez la salle, les mécaniques et l'accès avant d'engager."}},
    {id="nanaya",kind="boss",title={en="Nanaya",fr="Nanaya"},body={en="Client encounter listing: Nanaya. This index is not a verified kill order. Confirm its room, tactics and eligibility before the pull.",fr="Boss recensé dans le client : Nanaya. Cet index ne prouve ni l'ordre des combats ni le chemin. Vérifiez la salle, les mécaniques et l'accès avant d'engager."}},
   }}}
 end
end
