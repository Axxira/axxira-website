# DungeonMapPilot — 0.3.0 — bêta / beta

Carte de donjons et raids conçue pour se repérer, dans une fenêtre au style du jeu.
Version bêta bilingue. Deux parcours guidés de référence sont inclus : Cavernes des Lamentations et Ragefeu. La couverture complète des instances reste en vérification.

## Utilisation

- Ouvrir avec `/dmp` ou `/dungeonmappilot`, ou **Donjons / raids** au-dessus de la carte du monde.
  Les anciennes commandes `/fpmap` et `/fpmaps` restent reconnues.
- **Lieu actuel** : détecter le donjon/raid et revenir au suivi de son étage si le jeu le fournit.
- **Cartes** : consulter les 25 donjons/raids du catalogue, même depuis l'extérieur.
- Choisir l'étage avec son nom ou les flèches. Un choix manuel reste sélectionné jusqu'à **Lieu actuel**.
- Molette : zoom autour du curseur. Glisser : déplacer. **Recentrer** : retrouver le plan entier.
- Maj-clic : créer un repère personnel. Cliquer sur l'étoile : modifier ou supprimer ce repère.
- **Langue** : automatique selon le client, français ou anglais. Déplacer la fenêtre par son cadre.
- **Parcours** : sélectionnez un objectif numéroté pour lire la direction à prendre.
  **Étape terminée** confirme l'action et sélectionne la suivante. **Passer** concerne les étapes facultatives.
- **Masquer le parcours** replie le panneau et ses numéros pour garder le plan seul.
- Échap ferme la carte. Aucun raccourci M du jeu n'est remplacé.

Repères, langue, fenêtre, progression du parcours et choix d'étages sont enregistrés séparément pour chaque personnage.
40 repères maximum par étage. Aucun message envoyé au groupe, aucune télémétrie réseau.

## Parcours expliqués, français et anglais

Deux parcours de référence sont préparés : **Cavernes des Lamentations** et **Gouffre de Ragefeu**.
Le départ porte la lettre D en français, S en anglais; les objectifs suivants sont numérotés.
Chaque étape a une consigne courte. Les repères sont approximatifs, pas des coordonnées du joueur
ni une ligne de cheminement certifiée dans les couloirs. Aucun déplacement automatique.

Dans les Cavernes, Kresh, Skum et Verdan peuvent être passés pour l'escorte finale. Le retour au
disciple exige la confirmation des quatre chefs serpents; Mutanus vient après ce retour.
Cliquer sur un numéro consulte l'étape sans la déclarer terminée. Le bouton principal vous renvoie
au premier prérequis manquant si nécessaire. Les étapes passées sont comptées séparément des
étapes confirmées. **Nouvelle sortie** demande un second clic et efface uniquement la progression
de ce parcours, sans supprimer vos repères ni toucher aux autres donjons.

Sur les autres plans de référence, posez vos repères par Maj-clic dans l'ordre souhaité. Ils deviennent
un parcours personnel, regroupé par étage puis par ordre de création. Le menu du panneau permet aussi
de préférer ce parcours aux deux parcours préparés. Clic gauche sur un numéro : lire l'étape;
clic droit sur un numéro personnel : modifier son repère. Une modification des repères remet la
progression de ce parcours personnel à zéro afin de ne pas considérer un nouvel objectif comme terminé.
Le changement d'étage du plan suit l'étape sélectionnée; ce n'est pas une détection du joueur.

La progression est **manuelle** : le jeu ne fournit pas de position exploitable dans les Cavernes
sur le client testé. Le guide n'annonce jamais qu'un boss est vaincu sans votre confirmation.
Les deux parcours sont contrôlés par simulation, pas encore parcourus intégralement en jeu.

## English — using the map and route guide

Open `/dmp`, select **Language → English** (or **Auto** for the client language), and choose a map.
Use the mouse wheel to zoom, drag to pan, and **Reset view** to show the entire floor.
**Current location** returns to the detected dungeon; **Maps** opens the 25-instance catalogue.
The guide, objective names, directions, buttons and help are available in French and English.

Prepared reference routes cover **Wailing Caverns** and **Ragefire Chasm**. **S** marks the start;
numbered objectives have short directions. Click a number to read it, then **Step completed** when
you have actually finished it. **Skip** applies only to optional steps. Wailing Caverns requires all
four Fanglords to be confirmed before the disciple's escort can be confirmed. Skipped and confirmed
steps are counted separately. **Hide route** collapses the guide. **New run** requires a second click
and resets only this route's progress, keeping personal markers and other dungeon progress.

Other reference maps can use your own markers: Shift-click to create the start and objectives in order.
Select **My markers in order** to use them instead of a prepared route. Objectives are grouped by
floor and creation order. Right-click a personal numbered marker to edit it. Editing markers resets
that personal route's progress. All settings and progress are stored separately for each character.

These are approximate landmarks and manually confirmed steps, not GPS or automatic movement.
In the tested Wailing Caverns client, no usable player coordinates are exposed. A player dot is shown
only on a native map when the game supplies valid coordinates for that exact floor. The catalogue
contains 25 instances / 63 floor plans; this does not mean all layouts or routes have been verified.
Only two prepared routes are included. They have not yet been fully walked in game.

Close WoW completely before installing the `DungeonMapPilot` folder into `Interface/AddOns`, then
restart through Battle.net. The addon is standalone: Ding is not required. The RaidPilot testing edition
is free and works offline. The separate requested offer is CAD 5.99/month; the public edition requires a separate subscription and its Windows activation companion.

## Position et couverture : ce qui est réellement implémenté

Deux sources sont clairement distinguées :

1. **Carte du jeu** : si C_Map fournit une vraie carte d'instance avec illustrations, celle-ci est utilisée.
   Le repère du joueur ne s'affiche que si la carte demandée est exactement son étage et si le jeu renvoie
   une position exploitable. Il disparaît dès que la position n'est plus disponible.
2. **Plan de référence** : 25 instances, 63 plans d'étage, au moyen d'identifiants d'illustrations déjà
   présents dans le client. Pas de GPS ni d'étage automatique sur ces plans non calibrés. Le choix d'étage
   est manuel. Les repères personnels restent utilisables. Les cartes de référence ne sont pas assimilées
   à des cartes natives et leurs repères ne se mélangent pas.

Les illustrations de référence proviennent de versions WoW différentes. Ragefeu et les Cavernes des
Lamentations s'affichent dans le client testé. La correspondance exacte de tous les couloirs et étages
avec Forever reste **non validée**. Certains plans
peuvent différer du terrain actuel, notamment les donjons remaniés. Cette version affiche donc
« Plan de référence · à vérifier en jeu ». Elle ne certifie pas 25 donjons jouables cartographiés.

Ni nouvelles instances exclusives à Forever ni Naxxramas ajoutés sans données vérifiées.
Seuls les deux parcours documentés ci-dessus sont préparés. Aucun suivi de groupe ou déplacement automatique.

Essai dans les Cavernes des Lamentations le 10 novembre 2026 : le donjon est bien reconnu (43),
mais le client ne fournit ni carte native courante ni coordonnées X/Y/Z du joueur. La minimap ne
fournit pas non plus d'identifiant de carte exploitable. Le pion du joueur ne peut donc pas être
positionné sur ce plan; ce n'est pas un réglage d'affichage à activer. Les repères manuels restent distincts.

## Installation et essais

Fermer complètement WoW avant de placer le dossier `DungeonMapPilot` dans `Interface/AddOns`.
Relancer via Battle.net et activer **DungeonMapPilot**. Addon autonome : aucun Ding requis.
Ne pas remplacer les dossiers `ForeverPilot`, `ForeverPilot_Leveling` ou `ForeverPilot_Farm`.
L'alpha locale/RaidPilot est entièrement gratuite et fonctionne sans compte ni connexion au service.

Les tests automatisés se lancent depuis le projet avec `node work/DungeonMapPilot/tests/run.cjs`.
Ils chargent tous les fichiers dans l'ordre du TOC, testent les erreurs d'API, valeurs protégées,
projection, étages, zoom, repères, langues, isolation des droits et interactions avec des objets UI simulés,
ainsi que les prérequis du parcours, les étapes facultatives, les changements de personnage et la remise à zéro.
Ils ne remplacent pas le rendu dans WoW, un parcours réel ou une mesure de performance en raid.

Vérifications en jeu restant à effectuer :

- Chargement sans erreur Lua; ouverture/fermeture, langues, zoom, glisser, saisie et suppression d'un repère.
- Ragefeu puis Cavernes des Lamentations : couloirs, intersections et limites réelles du plan.
- Une instance à plusieurs étages puis un raid : ordre des plans et changements d'étage.
- `/dmp diagnostic` relève les capacités sans nom de personnage ni identifiant de compte.
- Réentrée, `/reload`, changement de personnage, coexistence avec la carte et les autres addons.
- Petite résolution, différentes échelles d'interface et absence d'illustration.

## Offre séparée demandée

Offre séparée choisie : 5,99 $ CA par mois. Renouvellement mensuel automatique.
`Access.lua` prévoit le droit `dungeon_maps`, indépendant de Fast XP et du suivi de classe.
La construction locale `raidpilot-test` reste gratuite sans expiration. Un ancien droit de leveling
ne débloque jamais l'édition publique Cartes. L'ancien service et les abonnements existants sont inchangés.

Le portail et le compagnon distincts sont raccordés. Paiement fictif de 5,99 CAD, activation signée
reconnue par le compagnon, annulation conservant la période payée puis remboursement retirant
l'accès ont été vérifiés dans PayPal Sandbox. Aucun argent réel utilisé. La disponibilité des nouvelles souscriptions est indiquée sur le portail; la couverture en instance reste en vérification.
Portail : https://dungeonmappilot-subscriptions.tyronixstudio.workers.dev.
Installez le compagnon depuis ce portail, importez votre clé, choisissez _forever_, puis actualisez votre accès avec WoW fermé. Activez DungeonMapPilot_Access et DungeonMapPilot au redémarrage. Actualisez au moins tous les sept jours; une licence ne dépasse jamais la période payée.
Le code Lua reste lisible/modifiable; ce contrôle n'est pas une protection inviolable.
La politique Blizzard exige des addons gratuits et interdit leurs fonctions payantes : la faisabilité
technique du raccordement n'établit pas l'autorisation de commercialiser.

## Sources et droits

- Tables de références adaptées d'[OpenMap](https://github.com/openmap-world/openmap/blob/d6cdee5569f5ac458b87ae5908730ee4c73e60d5/OpenMap/Data/Forever/DungeonMaps.lua),
  commit `d6cdee5569f5ac458b87ae5908730ee4c73e60d5`, licence MIT jointe dans `LICENSE-OpenMap.txt`.
- Aucun fichier d'illustration Blizzard distribué : seulement des identifiants de ressources client.
- API : [interface Forever du client](https://github.com/Gethe/wow-ui-source/blob/forever/Interface/AddOns/Blizzard_APIDocumentationGenerated/MapDocumentation.lua).
- [Politique Blizzard sur les addons](https://us.forums.blizzard.com/en/wow/t/ui-add-on-development-policy/24534).
- HiddenMaps consulté uniquement pour sa description publique; aucun code ou dessin réutilisé.
- Informations de parcours consultées : [guide des Cavernes, Icy Veins](https://www.icy-veins.com/wow-classic/wailing-caverns-dungeon-guide),
  [Cavernes, WoW Forever Info](https://wowforeverinfo.com/guides/wailing-caverns/) et
  [Ragefeu, WoW Forever Info](https://wowforeverinfo.com/guides/ragefire-chasm/).
  Consignes FR/EN rédigées pour cet addon; aucun code, dessin ou tracé de ces guides distribué.
- Aucun contenu Zygor, RestedXP ou Polestar lu/cop​​ié.
