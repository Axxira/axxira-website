# ForeverPilot Brain v1.4.1

Assistant de leveling dynamique pour WoW Forever. Nécessite **Ding** activé.
L'installation locale se trouve dans `_forever_/Interface/AddOns/ForeverPilot_Leveling`.

Le moteur lit le personnage et son journal, choisit une action, puis recalcule après une
progression, un dialogue PNJ ou un changement de zone. Le joueur se déplace, interagit
et combat lui-même. La flèche indique une destination, pas un chemin qui contourne
automatiquement les obstacles.

## Utilisation

### Passer une quête trop difficile (1.4.1)

Le bouton **Passer**, en bas à droite du cadre de la flèche, met la quête actuelle
de côté et recalcule immédiatement le guidage. La quête reste dans le journal,
avec sa progression. Elle n'est plus proposée, prise ou rendue automatiquement
avant réactivation, même après un rechargement, un gain de niveau ou `/fpl resync`.
Cette préférence est propre au personnage. Le journal ForeverPilot la marque
« MISE DE CÔTÉ » et continue d'identifier les quêtes de classe.

Clic droit sur **Passer** : réactiver la dernière quête écartée. `/fpl reprendre`
fait la même chose ; `/fpl reprendre tout` réactive toutes les quêtes écartées.
`/fpl journal` donne aussi la commande permettant de reprendre une quête précise.
Réactiver une quête la rend à nouveau candidate ; cela ne force pas sa priorité.
Le bouton devient **Reprendre** si aucune étape ne peut être passée et qu'une
quête a été écartée. Les haltes et camps d'XP d'appoint sont reportés de cinq
minutes seulement. Le retour au corps ne peut pas être passé.

### Objets et hôtel des ventes (1.4)

Le bouton **Objets** ouvre un panneau facultatif avec trois onglets : Équipement,
Prix HDV et Addons. La flèche et le panneau de navigation gardent leur taille.
Le survol d'un objet affiche la comparaison et les derniers prix observés.

- **Équipement** : profils indicatifs selon la classe, modifiables selon le rôle
  (farouche, tank, soins, lanceur de sorts, etc.). Le mode auto choisit un profil
  de leveling par classe ; il ne lit pas encore la répartition des talents.
  Le druide utilise par défaut le profil polyvalent. Choisir Farouche pour ce rôle.
- Une amélioration estimée indique un gain de score de statistiques, pas un gain
  de DPS mesuré. Les armes, effets spéciaux, ensembles, objets uniques, statistiques
  non prises en charge et restrictions inconnues restent « À comparer ».
  Les données non chargées/protégées ne sont pas assimilées à des statistiques nulles.
  Les deux mains sont comptées pour une arme à deux mains ; les anneaux sont comparés
  aux deux emplacements. Aucun objet n'est équipé ou vendu par ces modules.
- **HDV** : lancer un scan auprès du commissaire-priseur, ou effectuer une recherche
  dans l'interface habituelle. Les prix unitaires sont mémorisés avec leur source,
  leur âge et un indicateur de résultats partiels. Ils restent consultables ailleurs.
  Aucun flux de prix en direct hors HDV : « ANCIEN » apparaît après 24 heures.
- Les prix sont des minimums d'annonces observées, pas des ventes réalisées ni une
  estimation garantie. Les variantes (affixes, enchantements, gemmes, bonus) restent
  distinctes. Le stockage est propre au personnage, sans mélange entre ses serveurs.
  Les objets liés ne peuvent pas être vendus à l'HDV.
- Le scan respecte un délai local de 15 minutes, les signaux d'attente du client,
  un délai de réponse de 40 secondes et un traitement par petits lots. Fermer l'HDV
  annule la lecture en cours sans publier un scan incomplet. Plafonds : 200 000 lignes
  par scan (signalées partielles si dépassées), 12 000 variantes conservées.
- **Addons** : inventaire de quelques compléments pertinents et de leur chargement.
  Aucun addon supplémentaire n'est requis pour ces deux fonctions. Ding, Polestar,
  TankPilot et l'ancien bridge ForeverPilot ne sont pas modifiés par cette livraison.

Commandes : `/fpl objets`, `/fpl hdv`, `/fpl scan`, `/fpl addons`,
`/fpl profil auto` (ou `feral`, `tank`, `soin`, `caster`, `melee`, `agile`, `hybride`, `chasseur`).

### Guidage

- Suivre l'action dominante et la flèche. Les objectifs terminés sont retirés des choix.
- Le niveau de la quête apparaît à côté de l'action. À chaque gain de niveau, les
  objectifs dépassés quittent la sortie en cours et une nouvelle boucle est calculée.
- `NE RENTRE PAS ENCORE` : le moteur recommande encore un objectif sur le terrain.
- `RETOUR HUB` : une remise de quête est l'action choisie.
- `AVANT DE PARTIR · 1/2 quêtes prises` : le journal confirme une prise sur deux;
  prendre la quête indiquée avant de partir vers les objectifs.
- Ouvrir les PNJ normalement. Ding accepte/remet les quêtes sélectionnées par Brain,
  lorsqu'elles sont effectivement proposées. Maintenir Maj suspend cette automatisation.
- Les récompenses à plusieurs choix et les quêtes marquées manuelles restent à choisir.
- Une quête démarrée par un objet déjà possédé indique l'objet à ouvrir dans les sacs.
- Glisser le panneau pour le déplacer. Clic droit pour mettre en pause/reprendre.
- Survoler le panneau pour voir la boucle proposée, les estimations et la source des données.
- Le panneau fait 344 × 132 pixels à l'échelle normale, avec une seule action dominante.
- Le compteur `À ABANDONNER` signale les quêtes prises hors parcours rapide.
  Survoler le panneau ou taper `/fpl journal` donne les noms et les raisons.
- `CLASSE · À CONSERVER` protège les quêtes de classe même hors parcours rapide.
  Les prérequis utiles sont aussi protégés. Une quête inconnue reste `À VÉRIFIER · CONSERVER`.
  Aucun abandon automatique : le joueur décide dans son journal de quêtes.

| Commande | Effet |
|---|---|
| `/fpl` | Afficher/masquer le panneau |
| `/fpl pause` | Suspendre/reprendre le moteur et son auto-acceptation |
| `/fpl skip` | Différer cette proposition cinq minutes, puis recalculer |
| `/fpl resync` | Effacer les exclusions temporaires et recalculer |
| `/fpl why` | Voir les cinq meilleures propositions et leur trajet estimé |
| `/fpl journal` | Conseils pour toutes les quêtes prises : parcours, prérequis, classe ou abandon |
| `/fpl size 1.2` | Régler la taille du panneau entre 0.8 et 1.5 |
| `/fpl lock` | Verrouiller/déverrouiller sa position |
| `/fpl debug` | Version, erreurs et raisons de rejet |
| `/fpl guide` | Ouvrir le guide Ding |
| `/fpl off` | Désactiver Brain et restaurer le guidage Ding |
| `/fpl on` | Réactiver Brain |

## Ce qui décide

L'éligibilité tient compte du journal, de son espace libre, de l'historique, du niveau,
des prérequis ET/OU, des prérequis devant rester actifs, des restrictions connues et
des objets démarrant les quêtes. Une offre réelle du PNJ prime sur les restrictions
théoriques; la faisabilité de ses objectifs reste contrôlée avant de la sélectionner.
Deux observations concordantes d'une absence au bon PNJ mettent la quête en attente.
Le temps seul ne relance pas un déplacement vers ce PNJ. Un changement de niveau,
de prérequis, une nouvelle offre réelle ou un `/fpl resync` permet de réévaluer la quête.
Une réponse API absente ne constitue pas un refus. Rendre une quête sans rapport
n'efface plus les absences déjà constatées.

Le moteur compare des boucles comprenant prise, objectifs et remise des quêtes. Il
comptabilise l'XP à la remise et vérifie la capacité du journal à chaque prise prévue.
Il peut entrelacer les objectifs de plusieurs quêtes avant de revenir au hub. La recherche
est volontairement limitée (10 quêtes candidates, 8 étapes, 10 branches conservées)
pour rester compatible avec un addon en jeu; elle ne garantit pas l'optimum mondial.
Le score compare XP estimée, temps de trajet, travail restant, risque et suites débloquées.
Une sortie de quatre quêtes au maximum est conservée malgré les fluctuations de score.
Les quêtes prévues au même hub sont confirmées dans le journal avant le départ;
atteindre un PNJ ou ouvrir son dialogue ne compte pas comme une acceptation.
Une quête réellement indisponible quitte la sortie et les autres restent faisables.
Le mode rapide écarte aussi les quêtes ou les cibles connues dépassées d'au moins
trois niveaux. Ce seuil est une règle de sélection, pas une affirmation que ces
monstres ne donnent plus d'XP. Le niveau de quête fourni par le journal prime sur
celui de la base. Parmi les sources d'un même objectif, les ennemis adaptés passent
avant les camps trop bas, tout en conservant les sources dont le niveau est inconnu.
Les remises prêtes, les quêtes de classe, les objectifs à au moins 80 % (ou déjà
commencés avec au plus deux unités restantes) et les prérequis d'une suite disponible
adaptée au niveau restent admissibles. La raison apparaît dans le panneau ou au survol.
Un prérequis utile seulement plus tard reste protégé dans le journal sans retenir
la flèche. Une branche alternative déjà satisfaite ne force pas l'ancien prérequis.
Les propositions locales sont incluses dans la comparaison complète des boucles;
un transfert entre continents reste moins prioritaire quand du travail local adapté
est disponible. Les estimations de transport restent approximatives.
Les objectifs restants passent avant un retour éloigné, sauf journal plein. Une cible
valide reste stable pendant le trajet et les objectifs terminés sont réévalués.
L'acceptation automatique est limitée aux quêtes de cette sortie, sans les quêtes manuelles.
Le parcours rapide utilise les routes de leveling Ding adaptées au personnage, les
extensions Horde et leurs prérequis récursifs. Le catalogue de quêtes par zone n'est
pas en lui-même une sélection rapide. Les quêtes hors sélection sont signalées et
écartées de la flèche, y compris si elles sont déjà prêtes à rendre : elles peuvent
toujours être accomplies ou rendues manuellement. Les introductions aux métiers
« Camping 101 » sont optionnelles et signalées à abandonner pour le leveling rapide.
Les quêtes de classe sont reconnues par les guides de classe et les restrictions de
classe exportées par Ding. Elles sont conservées même hors sélection, sans imposer
un détour à la flèche. Un avertissement est donné à la prise ou au changement de
catégorie, différé hors combat; il n'est pas répété à chaque recalcul.
Les PNJ amicaux à qui parler ne sont plus rejetés à cause de leur niveau.
Le transport utilise le réseau Ding : vols, pierre de foyer et passages connus. Les trajets
futurs de la boucle restent des estimations prudentes, sans réutiliser une pierre de foyer
comme si son temps de recharge n'existait pas.
Une destination sans liaison connue est exclue : l'absence de trajet n'est plus remplacée
par un coût arbitraire de trente minutes. Cela s'applique aussi aux objectifs avant une
prise de quête, aux remises planifiées, aux services et aux camps d'appoint.
Une sortie déjà engagée vers une destination inaccessible est recalculée. La flèche
indique le quai ou le passage local quand le transport est connu. La pierre de foyer
affiche une consigne d'utilisation; un trajet temporairement inconnu affiche une
explication et déclenche une réévaluation au lieu d'un simple espace vide « Trajet ».

Les temps de travail observés par catégorie et tranche de niveau ajustent les estimations.
La pause et l'absence du joueur sont exclues de cet apprentissage. Les positions des PNJ
réellement rencontrés et les récompenses XP observées sont mémorisées localement par
royaume, version du client et faction. Les XP ne sont réutilisées qu'au niveau observé.
Les positions expirent après 30 jours. Une ancienne offre ne prouve jamais une disponibilité
actuelle, et l'addon n'invente pas les objectifs d'une quête inconnue de la base.

Après trois minutes d'activité sans progrès près d'un objectif (quatre pour les objets
à récupérer), le moteur attend la sortie du combat et cherche un autre camp ou une autre
action. Une pause ou une simple attente immobile ne déclenche pas cette récupération.
Un camp écarté redevient candidat après dix minutes. Cette détection concerne les objectifs;
elle ne détecte pas tous les obstacles du terrain pendant un trajet.

Les sacs presque pleins et l'usure de l'équipement peuvent déclencher une halte chez un
marchand/réparateur connu. Les besoins urgents passent avant les petits détours. Une vie
ou un mana très bas déclenchent une consigne de récupération, suivie de la reprise des
quêtes. Le joueur vend, répare, mange et boit lui-même. `/fpl skip` permet de différer une
halte; fermer le marchand évite d'y être retenu si l'on ne souhaite rien vendre.
Lorsque le client protège la vie ou le mana, ces besoins restent inconnus : l'addon
continue de guider les quêtes sans calculer à partir de ces valeurs.

Les quêtes ont priorité sur l'XP d'appoint. Si aucune action de quête n'est faisable,
le moteur cherche un camp de monstres normaux adapté au niveau dans le graphe, sans
dépendre de l'ancienne étape du guide. Sans coordonnées fiables il l'indique explicitement.

## Signification de la confiance

- `LIVE` : offre ou état confirmé par le jeu.
- `LIVE / POSITION DB` : quête active confirmée, position tirée de la base.
- `DATABASE` : proposition issue des données, à confirmer auprès du PNJ.
- `OBSERVÉ / À CONFIRMER` : position observée auparavant, disponibilité à vérifier.
- `RECOVERY` : prérequis ou solution de récupération.

Ces mentions décrivent la provenance de la décision. Ce ne sont pas des probabilités
statistiques. La base Ding disponible n'exporte pas les XP ni toutes les exclusivités.
Les XP non observées sont donc des estimations. Les quêtes propres au serveur et absentes
de Ding n'ont pas de coordonnées inventées.

## Organisation du code

Les 20 modules `Bootstrap`, `Knowledge`, `State`, `Graph`, `Journal`, `Progression`, `Recovery`, `Validator`,
`Travel`, `Needs`, `Learning`, `Loop`, `Journey`, `Planner`, `Navigation`, `UI`, `Items`, `Market`, `Advisor` et `Main`
sont chargés dans l'ordre du fichier TOC. Le monolithe v0.7
éventuellement encore présent dans le dossier local n'est plus chargé.

Depuis v1.1.1, la progression, l'apprentissage des durées, les exclusions et les diagnostics
sont dans `ForeverPilotLevelingCharDB`, une sauvegarde que WoW isole par personnage.
Deux personnages ayant le même prénom et le même royaume ne partagent donc pas ces données.
Les réglages d'interface et les observations de la base serveur restent dans
`ForeverPilotLevelingDB.brain`. L'ancien dictionnaire `brain.characters` est conservé
comme archive, sans réutiliser son historique potentiellement mélangé. Les nouveaux profils
relisent les quêtes déjà accomplies depuis l'API du jeu; les anciens temps personnels
ne sont pas attribués arbitrairement à l'un des personnages.
Un diagnostic compact est enregistré à la
déconnexion/rechargement : journal, décisions, raisons de rejet et erreurs du moteur.
Après trois erreurs capturées, Brain se suspend et restaure Ding.

## État de validation

Les tests locaux utilisent les vraies données Ding, un client simulé et les 20 modules.
Le journal Skyborne niveau 12 est reproduit avec ses quatre quêtes niveau 8–9 :
le passage 11→12 doit libérer la sortie, choisir une quête locale adaptée et rester
stable après déplacement et rechargement. Ce test échoue sur v1.2.1. Les exceptions
de progression, les niveaux inconnus et les niveaux observés en jeu sont testés.
Le cas suivant, sur Zephras Isle après Aid For The Refugees, vérifie le rejet de
Border Crossings quand aucun transport ne mène au continent. Il échoue sur v1.3.0.
Le banc d'essai général suppose les transports disponibles; ce cas dédié simule
explicitement leur absence et teste le quai, la pierre de foyer et le retour de la flèche.
Le cas Skyborne niveau 6 reproduit le journal observé en jeu : conversation avec
l'aubergiste après Illaya, quête de minage et offres absentes. Il échoue sur v1.1.1
et passe avec la correction. Les prises non confirmées et les variations de score
sont également testées pour empêcher un départ prématuré et les changements de cible.
Les conseils du journal couvrent les quêtes de classe, les quêtes inconnues, les
prérequis récursifs, le retrait d'une quête, la cohérence de la flèche et les notifications.
Ils couvrent les transitions, les nouvelles boucles, les besoins, les observations, la
récupération et le démarrage/arrêt de l'interface. Le parcours simulé 1–60 est optimiste :
objectifs accomplis instantanément, XP de quête estimée, aucune preuve de disponibilité
serveur. Il détecte des impasses logiques; il ne certifie ni un temps de leveling ni toutes
les quêtes du serveur. Voir le bilan joint pour les résultats des tests locaux et en jeu.

## Sources et licence

Code de Brain : GPL-3.0-or-later, voir `LICENSE_GPLv3.txt`.
Routes, données, transport, adaptateurs WoW et texture de flèche : dépendance Ding,
utilisée par ses interfaces publiques. Aucun fichier Ding n'est modifié sur disque.
Les données Ding proviennent de Questie/QuestieDB selon `Ding/DB/CREDITS.md` (GPLv3).
La texture de flèche est référencée dans l'installation Ding, pas redistribuée ici.
Aucune route Zygor ou RestedXP n'est incluse.
L'ancien addon séparé `ForeverPilot` reste indépendant et n'est pas écrasé.

Les nouveaux modules utilisent les interfaces publiques de Blizzard, vérifiées dans
les sources du client Forever :
- [Objets](https://github.com/Gethe/wow-ui-source/blob/forever/Interface/AddOns/Blizzard_APIDocumentationGenerated/ItemDocumentation.lua)
- [Éligibilité](https://github.com/Gethe/wow-ui-source/blob/forever/Interface/AddOns/Blizzard_APIDocumentationGenerated/PlayerInfoDocumentation.lua)
- [HDV](https://github.com/Gethe/wow-ui-source/blob/forever/Interface/AddOns/Blizzard_APIDocumentationGenerated/AuctionHouseDocumentation.lua)
- [Infobulles](https://github.com/Gethe/wow-ui-source/blob/forever/Interface/AddOns/Blizzard_SharedXMLGame/Tooltip/TooltipUtil.lua)

Tests ciblés objets/HDV : `node tests/run.cjs --items` ; suite : `node tests/run.cjs --ui`.
Ils nécessitent Fengari dans `runtime/node_modules` et l'installation locale de Ding.
Les réponses HDV des tests sont simulées. Un scan réel doit être validé auprès d'un
commissaire-priseur ; ces tests ne certifient pas les performances du marché en jeu.
