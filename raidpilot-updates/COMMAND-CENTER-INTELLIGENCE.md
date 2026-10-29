# RaidPilot Command Center / LootPilot et Planque — première version

## Deux modes, une seule carte

- **Normal (Ctrl+Alt+I fermé)** : la carte de raid habituelle reste
  entièrement visible, sans barre ni grands panneaux interactifs.
  Le suivi CORTEX, les coéquipiers et les marqueurs continuent tels quels.
- **Interactif (Ctrl+Alt+I ouvert)** : carte centrale interactive inchangée
  avec barre de commandes à onglets Carte, Quêtes, Clés, Loot, Planque,
  Équipe, Options et Filtres.
- Les panneaux sont dockés sous la carte (jamais flottants au-dessus)
  et mutuellement exclusifs. Les préférences de zoom sont conservées.

## Sources et périmètre de la version

- JSON API tarkov.dev : catalogues locaux vérifiés des cartes, objets et
  quêtes réguliers/PvE ; niveaux/matériaux de planque chargés en lecture
  seule à la demande depuis /{mode}/hideout.
- Progression des quantités et niveaux locale, séparée pour PvE et PvP.
- Objets des quêtes suivies et matériaux du prochain niveau des modules
  de planque. Consultation facultative de toutes les quêtes publiées.
- Les objectifs à choix, optionnels ou ambigus ne sont pas additionnés
  comme des objets obligatoires.
- Le joueur indique lui-même quantités possédées et niveaux de planque :
  pas de scan automatique inventé, ni de modification du jeu.
- Recherche dans la base complète d'objets disponibles. Une absence de
  besoin connu NE prouve jamais qu'un objet peut être vendu.

## Limites reconnues

Il ne s'agit pas encore de toute la base tarkov.dev : restent notamment
les trocs, crafts, recettes alternatives, prix actualisés, bonus/conditions
complètes des stations et exigences spéciales d'équipement. Les quantités
peuvent être incomplètes après un patch ou pour un objectif non supporté.
La planque nécessite le réseau ou un cache local déjà téléchargé ;
les quêtes/objets restent consultables hors ligne.

## Sécurité et essais

- Le chargeur natif accepte uniquement les demandes depuis la page locale
  RaidPilot et les modes regular/pve.
- Pas de compte Tarkov, captures, ni inventaire du joueur transmis à l'API.
- Cache borné et lecture seule ; progresser est un choix du joueur.
- Tests JS/C# avec données simulées et vrais catalogues locaux ; essais
  visuels requis dans les raids avant de considérer la disposition finale.

