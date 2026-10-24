# RaidPilot Overlay 0.11.3 (Windows)

## Flashs de quêtes et extractions ralentis — 0.11.3

Les marqueurs clignotants sont maintenant beaucoup plus calmes : quêtes 1,8 s, extractions confirmées 1,8 s et extraction actuellement guidée 1,4 s. Le cercle pulsant autour des extractions suit le même rythme. La cible active reste volontairement légèrement plus rapide afin de rester identifiable sans produire un flash agressif.

## Taille de carte adaptative et réglable — 0.11.2

RaidPilot adapte maintenant automatiquement la taille de l'overlay à la surface utile de l'écran. Sur un écran plus petit, le mode **Auto** réduit l'ensemble pour éviter qu'il occupe une portion excessive de l'affichage. Le menu de l'icône RaidPilot contient aussi **Taille de la carte** avec Auto, 50/60/70/80/90/100/110/120 %, ainsi que des commandes **−5 % / +5 %**. Le réglage est conservé dans les préférences locales.

L'ensemble complet est redimensionné proportionnellement, y compris la carte et les panneaux RaidPilot. Une taille demandée qui dépasserait la surface utile du moniteur est automatiquement limitée afin que l'overlay reste entièrement visible. Le coin supérieur droit reste l'ancrage et les changements de DPI/moniteur recalculent la taille de façon sûre.

## Escouade temps réel — 0.11.0

RaidPilot peut maintenant créer ou rejoindre une escouade depuis l’icône de notification. Le créateur obtient un code privé du type `RP-ABCD-EFGH-JKLM-NPQR`. Les teammates n’ont pas besoin de l’overlay complet : le petit **RaidPilot Beacon** suffit. Chaque Beacon utilise ses propres captures F8 et son suivi local puis transmet uniquement le nom choisi, la carte, X/Y/Z, le cap et l’incertitude au relais TLS `wss://raidpilot-team-relay.tyronixstudio.workers.dev/ws`. Les captures d’écran, touches et mouvements de souris ne sont jamais transmis.

Les pions d’escouade sont projetés localement dans Leaflet, possèdent une couleur stable et affichent le nom, la distance horizontale et un indicateur vertical ▲/▼ lorsque l’écart d’altitude est significatif. Une position sans mise à jour devient visuellement périmée après 2,5 s et disparaît après 8 s. Le rendu accepte jusqu’à 12 membres et masque les poses d’une autre carte. Le code d’escouade agit comme un secret d’accès : il doit uniquement être partagé avec les membres de l’équipe.

## Point joueur 100 % local — 0.10.2

Le marqueur de position visible est maintenant projeté directement par RaidPilot depuis la pose locale F8 + suivi temps réel, via l'instance Leaflet déjà chargée par tarkov.dev. Il ne dépend plus de `socket.tarkov.dev`, d'une session distante ni de `Ctrl+Alt+G`. Le partage tarkov.dev reste facultatif et ne sert plus qu'à envoyer une copie de la position vers le service distant. Le marqueur local est restauré après une navigation/rechargement de carte, refuse les poses d'un ancien raid ou d'une autre carte, suit le pan/zoom de Leaflet et masque le marqueur distant éventuel pour éviter les doublons.

## Correctif de fiabilité du chrono — 0.10.1

La lecture du temps du panneau `O` a été durcie à partir de captures EFT réelles : cadrage serré supplémentaire, filtrage des colonnes parasites, lecture spécifique du chrono rouge, sélection non ambiguë de la séquence `H:MM:SS` et repli OCR compact borné. Le jeu de validation local du 19 octobre 2026 contient 45 captures ; les 14 captures où le panneau d'extraction est reconnu donnent maintenant 14/14 lectures du chrono, contre 6/13 avant le correctif initial. Ce résultat décrit ce jeu de captures précis et n'est pas une garantie universelle pour toute résolution ou future interface EFT. Les valeurs contradictoires restent refusées plutôt qu'inventées.

Disposition 0.8.9 : ensemble ancré dans le coin supérieur droit, à 16 pixels logiques des bords utiles de l’écran. De haut en bas : extraction (72 px), carte (832 x 340 px), puis quêtes proches (96 px uniquement lorsqu’une aide est pertinente). L’apparition des quêtes agrandit la fenêtre vers le bas sans déplacer ni réduire la carte. Les calculs GPS, quêtes et extractions ne changent pas.

Application native Windows x64 en .NET 10 WinForms et WebView2. À 100 % de DPI, l’ensemble mesure 832 x 412 px, ou 832 x 508 px lorsqu’une quête proche est affichée. Le positionnement en haut à droite et les marges s’adaptent au DPI Windows et au moniteur de Tarkov. Le programme conserve son mode sans bordure et sans prise de focus.

L'overlay est volontairement bord à bord : aucune barre RaidPilot ni interface tarkov.dev n'est dessinée sur la carte. Le filtre local conserve seulement le fond cartographique, les noms de lieux (`.map-area-label`), les extractions (`.extract-icon`), la position/orientation (`.marker`), les zones possibles de boss (`spawn_boss`), une sélection stricte d'emplacements dédiés aux objets rares et les objectifs des quêtes reconnues par RaidPilot. Le guidage d’extraction est placé sous la carte; seul le temps de raid restant reste dans son coin supérieur gauche. Les caches, les pools de loot génériques, les autres quêtes, les apparitions PMC/Scav, le BTR, les contrôles, badges, popups et bannières sont masqués. Le déplacement et le zoom à la souris restent disponibles en mode interactif.

## Commandes

- `Ctrl+Alt+M` : afficher ou masquer l'overlay avec `RegisterHotKey`.
- `Ctrl+Alt+I` : basculer entre le mode interactif et le mode click-through.
- `Ctrl+Alt+G` : afficher ou masquer le point du joueur transmis à tarkov.dev (le guidage local fonctionne sans cette option).
- `Ctrl+Alt+E` : choisir manuellement une extraction de secours tant que le panneau `O` n'a pas encore été reconnu.
- Icône de notification : ouvrir sans focus, interagir, rendre la carte visible ou invisible dans un stream Discord, afficher l'aide de lecture des quêtes, choisir manuellement une carte, choisir le type de raid PMC/Scav, afficher la touche de position détectée, activer/désactiver le GPS ou le suivi fluide, masquer ou quitter.

Le mode click-through applique `WS_EX_TRANSPARENT | WS_EX_NOACTIVATE`, renvoie `HTTRANSPARENT` au hit-test et refuse l'activation souris. Le suivi fluide reçoit les deltas souris et les transitions des seules touches de contrôle configurées via Raw Input quand la fenêtre Tarkov est au premier plan. La seule entrée éventuellement envoyée est le raccourci `MakeScreenshot` déjà configuré par l'utilisateur, uniquement pour le recalage au repos; aucune donnée n'est injectée dans le processus du jeu.

## Détection locale de la carte

Au démarrage, RaidPilot cherche le dossier `Logs` d'EFT uniquement via les emplacements du registre et quelques chemins d'installation usuels ; il ne scanne jamais le disque. S'il trouve le journal `application.log` de la session la plus récente, il examine au maximum sa dernière tranche de 1 Mio pour permettre un lancement en plein raid, retient seulement la dernière carte reconnue, puis ne lit que les octets ajoutés au fichier. La carte déjà détectée devient directement la première navigation Web au lieu de charger Customs avant elle. Une réconciliation légère toutes les 1,5 seconde retrouve aussi le dernier journal et sa carte si Windows a perdu une notification lors du changement de session.

Deux marqueurs publics sont reconnus : `scene preset path:maps/...bundle` et `TRACE-NetworkGameCreate ... Location: ...`. Chaque ligne est transitoire : aucun identifiant, aucune ligne brute et aucun autre contenu du journal n'est conservé, affiché ou envoyé. Seul le nom normalisé de la carte reste en mémoire afin d'ouvrir la route tarkov.dev correspondante. Au changement de raid, une navigation devenue fausse est remplacée et les événements d'un ancien watcher sont ignorés. Un F8 pris juste avant que la nouvelle ligne de carte arrive est gardé brièvement, puis appliqué une seule fois à la nouvelle session avec le mouvement effectué depuis la capture. Si le journal est absent ou inaccessible, RaidPilot ne signale pas d'erreur ; le sous-menu **Choisir la carte** de l'icône de notification reste disponible.

## Rendu épuré et ressources tarkov.dev

`InjectedBehavior.js` est enregistré avant la première navigation. Il fusionne les préférences `savedMapSettings` pour rendre visibles les noms, les extractions, la position et la couche `spawn_boss`, choisit le style SVG quand il existe, puis désactive les autres groupes Leaflet. Dans `Spawns`, la liste blanche CSS laisse passer uniquement les crânes de boss. Toutes les couches `Loose Loot` restent désactivées : RaidPilot inspecte leurs métadonnées locales, exige un emplacement dédié à un seul objet et ajoute individuellement uniquement les marqueurs d'une liste stricte (LEDX, GPU, Bitcoin, Intel, technologies militaires rares, cartes Labs et clés marquées). Le lien public `/item/{normalizedName}` sert d'identité stable, avec l'identifiant d'image comme repli. La liste blanche dessine ces marqueurs avec un contour doré; les caches, le loot ordinaire et les pools génériques restent invisibles. Ces marqueurs indiquent un emplacement possible, jamais la présence garantie d'un objet pendant le raid. Un observateur DOM idempotent traite aussi les couches ajoutées plus tard. Les couches de quêtes restent désactivées : RaidPilot ajoute individuellement à la carte uniquement les marqueurs dont l'identifiant public correspond à une quête lue sur la capture.

RaidPilot n'embarque et ne redistribue aucun SVG, tuile ou image de carte. La page et ses ressources restent chargées à l'exécution depuis `https://tarkov.dev` et `https://assets.tarkov.dev`; un crédit minuscule intégré au bord du fond reprend aussi l'auteur exposé par tarkov.dev quand il est disponible. Le comportement a été vérifié contre [tarkov.dev au commit `97ff6e7`](https://github.com/the-hideout/tarkov-dev/tree/97ff6e7a43d03c10f3f7a3165c8dbcdf2193d6b9), dont le code source est publié sous [licence MIT](https://github.com/the-hideout/tarkov-dev/blob/97ff6e7a43d03c10f3f7a3165c8dbcdf2193d6b9/LICENSE). Les noms de lieux affichés sont créés par le site à partir de `mapData.labels`; ils ne sont pas copiés dans RaidPilot.

## Guidage local et point tarkov.dev facultatif

Le watcher de captures, l'OCR, la flèche et le clignotement démarrent automatiquement et restent entièrement locaux. Le WebSocket est indépendant et inactif au démarrage. Pour afficher aussi le point du joueur sur la carte tarkov.dev :

1. ouvrir le menu de l'icône RaidPilot dans la zone de notification ;
2. cliquer **Afficher mon point sur tarkov.dev…** ;
3. accepter le dialogue qui décrit précisément les données transmises.

Après accord, RaidPilot génère en mémoire un identifiant aléatoire cryptographique de 16 caractères, arme un marqueur limité à l'instance WebView dans `sessionStorage`, ouvre la carte avec `?connection=<id>`, puis relit l'identifiant depuis le `localStorage` du WebView pour confirmer que tarkov.dev a accepté la session. L'identifiant n'est ni affiché ni écrit dans les journaux. Il est supprimé de `localStorage` à la désactivation et à la sortie ; au démarrage de chaque nouveau document, il est également supprimé si le marqueur de session n'est pas armé. La désactivation masque uniquement le point distant : la surveillance locale, la flèche et les sorties restent actives.

Le profil WebView2 `UserData` est placé à côté de l'exécutable et contient les données ordinaires du navigateur. Ce dossier est créé au premier lancement et doit être exclu des archives ZIP. Si le dossier de l'exécutable n'est pas inscriptible, le profil est placé dans `%LocalAppData%\RaidPilot\WebView2`.

Le client se connecte ensuite à :

```text
wss://socket.tarkov.dev?sessionid=<id>-rp
```

RaidPilot lit aussi en lecture seule `Control.ini` pour connaître l'entrée `MakeScreenshot`, les touches de déplacement, posture et visée, leurs modes bascule/maintien, ainsi que les sensibilités normale/visée. Il ne modifie jamais ce fichier. Si la touche par défaut `SysReq`/Impr. écran est encore utilisée, le menu recommande `F8`; une fois ce réglage unique effectué dans Tarkov, l'utilisateur appuie une première fois sur F8 et le recalage au repos peut ensuite réutiliser ce même raccourci.

Pour chaque nouveau nom de capture reconnu, il envoie le message compatible avec le protocole open source TarkovMonitor :

```json
{
  "type": "command",
  "sessionID": "<id>",
  "data": {
    "type": "playerPosition",
    "map": "<carte normalisée>",
    "position": { "x": 0, "y": 0, "z": 0 },
    "rotation": 0
  }
}
```

Le client répond également aux messages `ping` par `pong`.

## Precision V3 — capture Windows GPU optionnelle (expérimentale)

Precision V3 peut alimenter le suivi optique 160×90 avec **Windows.Graphics.Capture + Direct3D11** au lieu de GDI. Cette voie est **désactivée par défaut**. Pour l'activer, le joueur coche **Capteur GPU WGC/Direct3D11 (expérimental, capture visible)** dans l'icône de notification de RaidPilot ou de Beacon. Le capteur cible uniquement la fenêtre Tarkov reconnue au premier plan, respecte l'indicateur de capture Windows et ne transmet aucune image au serveur d'escouade.

Si Windows refuse la capture, si le composant natif manque ou si aucune image valide n'arrive, RaidPilot utilise automatiquement la voie GDI déjà disponible. Le suivi historique 64×36 et les recalages F8 restent indépendants. La sélection peut être désactivée immédiatement depuis le même menu; elle n'est pas conservée au prochain lancement. Les deux composantes de l'équipe utilisent toujours le relais Cloudflare existant, sans modifier leurs codes privés.

**Limites :** cette capture n'est ni un GPS direct ni un SLAM métrique complet. Les tests de compilation et le test réseau ne peuvent pas confirmer que Tarkov autorise WGC sur une configuration particulière. Il faudra mesurer l'erreur réellement observée avant F8 et l'impact sur les FPS, tout en vérifiant les conditions applicables au jeu.


## Zoom mémorisé sur toutes les cartes

Le zoom choisi à la molette ou avec les boutons +/− est enregistré comme **facteur relatif à la vue de base**. Il se réapplique sur la carte suivante et après la fermeture ou la mise à jour de RaidPilot, quelle que soit la résolution des illustrations. Les cartes locales et la vue tarkov.dev en ligne partagent la préférence. La valeur est conservée dans `%LocalAppData%\RaidPilot\overlay-preferences.json`, indépendamment du Launcher.

Le bouton **Ajuster / Vue entière** remet le zoom à 100 % et enregistre ce choix. Les changements de carte, de raid, d'étage, le suivi du joueur et les recadrages automatiques ne doivent jamais écraser le zoom manuel. Les préférences existantes de taille de fenêtre, position, filtres, style et calibrations sont conservées.

## Suivi hybride en temps réel

Après la première ancre exacte, RaidPilot extrapole la pose en continu à 20 Hz à partir des touches avant/arrière/gauche/droite, du sprint, de la marche lente et des deltas horizontaux de la souris. Chaque transition clavier reçoit un horodatage monotone dans un tampon RAM borné : même un tap complet entre deux ticks contribue selon sa durée et les actions bascule ne le perdent plus. Le tampon est remis à zéro à chaque perte de premier plan; aucune frappe d'une autre application ne peut être rejouée dans RaidPilot. La lecture est aussi suspendue quand le curseur Tarkov est visible dans un menu. Les entrées restent en mémoire, ne sont jamais journalisées et ne sont jamais envoyées; seul le résultat `x/y/z/yaw` alimente la carte et la flèche.

La pose est intégrée à 20 Hz pour garder les touches et les rotations réactives; le marqueur tarkov.dev et le HUD local sont publiés à 10 Hz, sans file d'attente accumulée. Un clone persistant interpole seulement le point et son orientation sur 110 ms; les grands recalages s'appliquent immédiatement et la carte ne se recentre que près de ses bords. L'estimation tient compte de l'accélération et du freinage, des vitesses avant/arrière/latérale, du réglage continu à la molette, de CapsLock, de l'accroupissement, de la position allongée et ignore le yaw de free-look.

Une petite trame 64×36 lue directement dans la fenêtre EFT à 10 Hz sert de contrôle visuel local : elle n'est jamais téléversée ni enregistrée. Cette capture ne démarre qu'après la première ancre exacte. Le contrôle de mouvement peu coûteux reste régulier, tandis que la recherche plus lourde de points texturés n'est lancée que lorsqu'un arrêt possible doit être confirmé. RaidPilot vérifie alors les correspondances aller-retour et exige un consensus spatial avant de publier un sens de rotation visuelle. Les variations globales de luminosité, le HUD, l'arme et un objet mobile local sont écartés. Une capture noire, uniforme, figée, contradictoire ou ambiguë est ignorée. Après quatre trames stables et seulement si la capture a déjà montré du mouvement, la translation est arrêtée afin de ne plus faire marcher le point à travers un mur. Un nouvel appui libère cet arrêt immédiatement. La carte est visible dans les captures d'écran et les streams Discord par défaut. La case **Visible dans le stream Discord** du menu de notification permet de la masquer à nouveau et le choix est conservé dans `%LocalAppData%\RaidPilot\overlay-preferences.json`. Ce choix ne contamine pas le contrôle visuel : celui-ci lit directement la fenêtre EFT plutôt que l'écran composé avec l'overlay.

Une capture de recalage automatique, déclenchée au repos sur la vue libre du jeu, peut aussi créer en mémoire une signature compacte du décor. Les F8 manuels avec les panneaux `O` ou `TÂCHES` servent à l'OCR mais ne deviennent jamais des repères visuels. Lorsque le joueur est immobile et que trois petites trames fraîches et espacées retrouvent sans ambiguïté le même repère sur la même carte, à proximité de la pose estimée et avec un cap voisin, RaidPilot réapplique automatiquement cette ancre. Une image uniforme, un lieu ressemblant, un autre étage, un second candidat trop proche, une commande de mouvement ou une capture figée annulent le recalage. Seules les signatures restent en RAM et elles sont bornées à 64 repères par carte et 16 cartes; les captures complètes ne sont pas copiées dans cette mémoire et les repères disparaissent à la fermeture de l'application.

L'étiquette `LIVE ≈N m` montre la dérive estimée; ce n'est pas une borne garantie. Quand elle atteint environ 3,5 m, le mode **Recalage exact automatique au repos** attend au moins 0,9 seconde sans mouvement, rotation, visée, tir ni saut, puis envoie le raccourci `MakeScreenshot` lu dans `Control.ini`. Il ne le fait jamais pendant une course ou un combat actif. RaidPilot mémorise les 256 dernières poses avec leur horloge monotone et relève aussi l'instant de l'appui manuel. Lorsque le nom du PNG arrive, l'ancre absolue est appliquée à l'instant historique de la capture, puis le déplacement et la rotation effectués depuis sont conservés et corrigés dans le nouveau repère. Le point ne revient donc plus en arrière si le joueur a déjà recommencé à avancer. Le PNG automatique, qui n'est pas utilisé par l'OCR, est supprimé trois secondes plus tard seulement s'il est l'unique nouveau fichier relié sans ambiguïté à la demande. Une capture manuelle concurrente est conservée et traitée normalement. Le menu de l'icône permet de désactiver ce mode et de conserver un F8 entièrement manuel. Chaque nouvelle ancre remet l'erreur passée à sa valeur minimale et ajuste progressivement la sensibilité de rotation et les vitesses. Les cinq valeurs apprises sont validées puis conservées dans `%LocalAppData%\RaidPilot\realtime-calibration.json`; elles survivent aux mises à jour sans enregistrer la trajectoire ni les commandes. Le fichier contient aussi l'empreinte des deux sensibilités source de `Control.ini` et est automatiquement ignoré si l'une d'elles change.

Le suivi reste une estimation entre les ancres : un escalier, le poids, la stamina, une blessure ou une scène non capturable peuvent encore faire dériver la position. À 64×36, les déplacements visuels inférieurs à un pixel sont quantifiés et une caméra monoculaire ne donne pas à elle seule l'échelle métrique. Les entrées, les ancres F8, les repères reconnus et l'incertitude restent donc fusionnés de façon prudente; aucune lecture visuelle ambiguë ne remplace une coordonnée exacte.

Une évolution future pourra remplacer la petite capture GDI par [Windows.Graphics.Capture](https://learn.microsoft.com/windows/apps/develop/media-authoring-processing/screen-capture) ou la [Desktop Duplication API](https://learn.microsoft.com/windows/win32/direct3ddxgi/desktop-dup-api) pour augmenter la fréquence et la résolution sans capture CPU. L'estimateur actuel reste volontairement léger et sans dépendance native supplémentaire.

## Guidage automatique vers une extraction

Après un recalage F8, RaidPilot affiche une flèche relative à la direction du joueur, le nom de la sortie et sa distance. Il charge uniquement les coordonnées publiques depuis `https://json.tarkov.dev/regular/maps` ainsi que les noms français et anglais depuis `maps_fr` et `maps_en`; aucune donnée personnelle n'est ajoutée à ces requêtes. Chaque réponse JSON est limitée, validée puis conservée comme dernière copie saine dans `%LocalAppData%\RaidPilot\catalog-cache`; si le service de catalogues répond temporairement mal après un premier chargement réussi, les extractions et quêtes connues restent disponibles depuis ce cache local. Ce cache ne contient ni la page, ni les tuiles, ni les images de tarkov.dev : l'affichage de la carte demande toujours que ces ressources Web soient accessibles et un premier démarrage entièrement hors ligne n'est pas pris en charge.

1. Au début du raid, appuyer deux fois sur `O` pour laisser le panneau complet des extractions visible.
2. Pendant que le panneau est visible, appuyer sur `F8` une fois. RaidPilot attend que le PNG soit entièrement écrit, rogne uniquement sa zone supérieure droite, puis utilise le moteur OCR local de Windows.
3. La même lecture récupère le temps exact affiché par Tarkov et démarre le petit compte à rebours en haut à gauche. Aucune capture supplémentaire n'est faite. Il reste à `--:--` tant qu'une lecture fiable du panneau n'a pas réussi, puis devient jaune sous dix minutes et rouge sous cinq minutes. Un autre `O` deux fois puis `F8` le resynchronise.
4. Les noms reconnus sont comparés aux alias français et anglais de la carte. Seules les lignes `EXFIL` numérotées sont retenues : les `TRANSIT`, les lignes `EXFIL??` et les sorties absentes du panneau ne sont jamais ajoutés. Une lecture partielle affiche immédiatement les EXFIL déjà reconnues et les F8 suivants peuvent compléter la liste.
4. Les sorties réellement attribuées au raid restent seules visibles et clignotent sur la carte. La flèche préfère automatiquement la sortie reconnue la plus proche sans condition. Si la carte n'en offre aucune — notamment pour un PMC sur Réserve — elle pointe la meilleure EXFIL reconnue et affiche `⚠ CONDITION` au lieu de supprimer tout le guidage.

Si le panneau n'est pas reconnu de façon fiable, aucune sortie n'est inventée : la lecture reste armée pour le prochain F8 et le statut rappelle `O deux fois puis F8`. `Ctrl+Alt+E` reste disponible comme secours manuel avant une reconnaissance réussie.

Le type de raid est **PMC** par défaut et peut être changé en **Scav** depuis l'icône RaidPilot dans la zone de notification. La liste est réinitialisée à chaque nouvelle session, même lorsqu'un nouveau raid reprend la même carte, ainsi qu'à chaque changement de carte ou de type de raid.

Au premier passage dans une zone inconnue, la flèche reste explicitement en mode `CAP DIRECT`. Pendant les raids, RaidPilot construit localement un graphe borné à partir des segments réellement parcourus : aucune proximité entre deux couloirs, deux raids ou deux étages ne crée un passage. Lorsqu'un chemin dirigé de confiance moyenne ou élevée rejoint une extraction confirmée à moins de 1,5 m du trajet réellement observé, un tracé cyan apparaît sur la carte, la cible automatique préfère l'extraction réellement atteignable et la flèche vise le prochain point du chemin plutôt que l'extraction à travers un mur. La même limite prudente s'applique au raccord entre le joueur et le début du chemin. Les changements d'étage ne sont utilisés qu'après avoir observé la transition. Un tracé moyen est pointillé et le HUD affiche `CHEMIN APPRIS`; tout raccord non appris ou toute dérive supérieure à 4 m conserve le mode `CAP DIRECT` et demande un nouveau F8.

Le graphe est conservé uniquement sur l'ordinateur dans `%LocalAppData%\RaidPilot\learned-routes-v1.json`, avec des limites de 16 cartes, 4096 nœuds et 16384 arêtes par carte. Il s'améliore au fil des trajets réellement effectués, reste séparé par carte et ne restaure jamais une session de raid active. Supprimer ce fichier remet simplement l'apprentissage des chemins à zéro.

## Objectifs de quêtes par capture

Dans le raid, ouvrir **TAB → TÂCHES** afin que les titres soient visibles, puis appuyer sur la touche de capture de position (`F8` recommandé). RaidPilot lit localement la partie gauche et centrale du PNG avec les moteurs OCR français et anglais, limite la comparaison aux quêtes qui ont un objectif géolocalisé sur la carte active, puis fait clignoter leurs marqueurs en violet directement sur la carte. Aucune liste ni panneau supplémentaire ne recouvre la carte.

Une correspondance exacte est privilégiée; une lecture approchée n'est retenue que si elle est suffisamment proche et sans autre candidate ambiguë. Si la liste de tâches défile, une autre capture après défilement complète les quêtes déjà reconnues pour le raid. Les quêtes sans coordonnée publique exploitable ne produisent volontairement aucun marqueur. Le mode PvE visible dans la capture sélectionne automatiquement le catalogue PvE et synchronise le mode de la carte intégrée; une seule actualisation de la carte peut alors se produire. Le mode détecté reste stable pour les F8 suivants du raid même si le petit filigrane PvE est moins lisible.

Le PNG et le texte OCR brut restent sur l'ordinateur. RaidPilot télécharge seulement les catalogues publics `tasks`, `tasks_fr`, `tasks_en` et `maps` depuis `json.tarkov.dev`; seuls les identifiants publics des quêtes reconnues sont transmis à la page tarkov.dev intégrée pour sélectionner ses marqueurs existants.

Sources de compatibilité :

- [TarkovMonitor `SocketClient.cs`](https://github.com/the-hideout/TarkovMonitor/blob/master/TarkovMonitor/SocketClient.cs)
- [tarkov.dev `remote-websocket.mjs`](https://github.com/the-hideout/tarkov-dev/blob/main/src/modules/remote-websocket.mjs)
- [tarkov.dev connexion par paramètre `connection`](https://github.com/the-hideout/tarkov-dev/blob/main/src/App.jsx)

## Position et panneau par capture

Le seul dossier observé est :

```text
%USERPROFILE%\Documents\Escape from Tarkov\Screenshots
```

À chaque lancement du processus, RaidPilot nettoie en arrière-plan les anciens PNG qui existaient déjà dans ce dossier exact. Une marge de sécurité de trois secondes préserve une capture créée presque en même temps que l'ouverture. Il ne parcourt aucun sous-dossier, ne touche à aucune autre extension et ne suit pas un lien ou une jonction. La liste est figée au début du lancement : un nouveau PNG créé par le raid en cours ne peut pas être ajouté au nettoyage. Un fichier momentanément verrouillé est réessayé un petit nombre de fois puis conservé. Cette purge est définitive; une capture manuelle reste disponible pendant la session, mais sera supprimée à un lancement suivant si elle se trouve encore dans ce dossier et n'est plus dans cette marge de sécurité.

`FileSystemWatcher` est activé sans attendre la fin de ce nettoyage et reçoit uniquement les créations/renommages de fichiers `*.png`. Le nom fournit `x/y/z` et le quaternion `qx/qy/qz/qw`; le quaternion est normalisé et converti en yaw Unity avant envoi. RaidPilot ouvre aussi le nouveau PNG en partage de lecture et attend que sa taille soit stable. Il lit d'abord la zone supérieure droite lorsque le panneau `O` est visible afin de reconnaître les extractions et de synchroniser le temps de raid; la lecture des quêtes attend que ce traitement prioritaire soit terminé. Les lectures OCR sont sérialisées pour éviter plusieurs moteurs lourds en parallèle. Le compte à rebours retire le délai de lecture du PNG grâce à l'instant monotone de la capture, puis avance localement sans refaire de capture. Pendant la session, seule une capture de recalage automatique corrélée sans ambiguïté peut être effacée immédiatement. Les pixels et les lignes OCR brutes ne sont ni téléversés ni enregistrés; seuls les identifiants publics retenus sont appliqués aux marqueurs de la carte Web intégrée.

Si la première utilisation crée le dossier entre deux sondages, RaidPilot rattrape au maximum le PNG valide le plus récent créé depuis l'activation, ce qui évite de perdre le premier recalage. Les notifications de nouveaux fichiers restent actives pendant la purge de lancement.

Exemple de nom pris en charge :

```text
2026-01-03[16-20]_166.52, 17.87, 7.16_0.03673, 0.94566, -0.11506, 0.30189_14.37 (0).png
```

La seule entrée envoyée par l'application est la touche de capture déjà choisie par l'utilisateur, lorsque le recalage au repos est activé et que les conditions d'inactivité sont réunies. Sa seule lecture du journal local est la détection de carte bornée et append-only décrite plus haut ; aucune ligne brute n'est transmise. Les entrées de mouvement utilisées par le suivi fluide sont traitées uniquement en mémoire pendant que Tarkov est au premier plan.

## Compiler, tester et publier

Prérequis : SDK .NET 10, runtime Microsoft Edge WebView2 et accès à NuGet.

```powershell
dotnet restore .\RaidPilot.Overlay.csproj
dotnet build .\RaidPilot.Overlay.csproj -c Release
dotnet run --project .\Tests\RaidPilot.Overlay.Tests.csproj -c Release
dotnet publish .\RaidPilot.Overlay.csproj -c Release -r win-x64 --self-contained false -o .\publish
dotnet publish .\RaidPilot.Overlay.csproj -c Release -r win-x64 --self-contained true -o .\publish-self-contained
```

## Limites connues

- Le filtre dépend des classes et clés de couches publiques actuelles de tarkov.dev ; une refonte du site peut demander une mise à jour. La liste blanche masque par défaut toute nouvelle classe de marqueur inconnue.
- Le service `socket.tarkov.dev` n'offre pas d'accusé de réception pour chaque position. Le statut de l'icône de notification confirme l'envoi WebSocket, pas le rendu de la carte.
- Les raccourcis globaux peuvent déjà être réservés par un autre logiciel. Le menu de notification reste utilisable.
- tarkov.dev ne publie pas les extractions attribuées au raid courant. RaidPilot dépend donc de la lisibilité du panneau `O`; si l'OCR n'est pas assez fiable, il ne présente aucune sortie comme garantie et réessaie au prochain F8.
- Une quête n'est affichée que si son titre visible est reconnu et si tarkov.dev publie au moins une position pour son objectif sur la carte active. Une quête sans position publique ou un titre masqué par le défilement demande une autre capture et peut rester absente.
- Un chemin ne peut être affiché qu'après que RaidPilot a observé ses segments dans le bon sens. Une première visite, un spawn éloigné du réseau appris, un passage jamais emprunté ou une confiance trop faible conservent donc le repli `CAP DIRECT`; le logiciel n'invente jamais un raccourci à travers un obstacle.
- Une fenêtre always-on-top séparée n'est pas garantie au-dessus d'un jeu en plein écran exclusif. Le mode fenêtré sans bordure est le cas compatible.
- L'application démarre sur Customs avant la première détection. Les nouveaux identifiants de cartes ajoutés par EFT pourront demander une mise à jour du mapping ; la sélection manuelle reste disponible.
- La génération du nom enrichi dépend de la fonction de capture intégrée à Escape from Tarkov ; une capture Windows ordinaire ne fournit pas ces coordonnées.
- Entre deux ancres, le suivi hybride reste estimatif. L'odométrie basse résolution et les repères reconnus réduisent la dérive, mais les pixels monoculaires ne donnent pas seuls une distance métrique et ne remplacent pas les coordonnées exactes du nom de capture.
- Le cache local concerne uniquement les catalogues JSON d'extractions et de quêtes déjà téléchargés. Il ne rend pas la page, les tuiles ni les images de tarkov.dev disponibles hors ligne.

## Aide contextuelle des quêtes proches — 0.8.8

Une bande légère apparaît **en dessous** de la carte lorsque le personnage estimé
approche d'un objectif d'une quête déjà reconnue via le parcours TÂCHES existant.
Elle montre le titre, un repère/distance estimée, le texte réel de l'objectif en
français (anglais signalé en repli), et les conditions structurées disponibles.
Les données viennent des mêmes catalogues `json.tarkov.dev/{mode}/tasks`,
`tasks_fr`, `tasks_en` et `maps` que la page https://tarkov.dev/tasks.
Aucun catalogue de toutes les quêtes n'est traité comme la liste du joueur.
Aucune nouvelle capture, lecture OCR périodique ou entrée de jeu n'est ajoutée.

La détection entre à 55 m et sort à 75 m pour limiter le clignotement. Elle utilise
le contour public des zones lorsqu'il existe et les positions possibles sinon.
Elle nécessite une pose rafraîchie depuis moins de 2 s, une dérive estimée de 8 m
au plus et le jeu au premier plan avec le suivi actif. La hauteur conservée par
le GPS ne confirme pas un étage : le panneau le précise. Ni la proximité ni le
nom de quête reconnu ne prouvent une étape terminée; `Progression non vérifiée`
reste affiché. La pièce exacte n'est pas inventée si absente des données.

Le panneau est évalué 2 fois par seconde, sans requête réseau à chaque déplacement.
La quête la plus proche est privilégiée avec une marge de stabilité; le nombre
d'autres objectifs proches est indiqué. Le menu de notification permet de
masquer `Aide des quêtes proches` ou `Masquer cet objectif pour ce raid` sans
marquer la quête comme accomplie. Les exclusions sont remises à zéro au raid suivant.

Les 96 pixels logiques ajoutés vers le bas ne réduisent ni ne déplacent la carte
832×340 ni la bande d'extraction de 72 pixels située au-dessus. Le panneau se
rétracte quand il n'est pas utile. La logique GPS et les captures natives de
recalage existantes ne sont pas modifiées par cette fonction.

## Niveaux automatiques — 0.9.0

Le module choisit automatiquement une couche d'étage, de garage, de bunker ou de
souterrain parmi les couches réellement chargées par la carte tarkov.dev. Il lit
leurs plages de hauteur et leurs limites X/Z publiques, plutôt que d'appliquer
une hauteur unique à toute la carte. Aucune image supplémentaire n'est embarquée.
Le lecteur et l'évaluation s'activent au lancement et se réinitialisent par raid.
Aucun bouton d'étage n'est requis; `Niveaux automatiques` permet seulement de
mettre cette fonction en pause. Le point distant tarkov.dev n'est pas requis.

L'altitude vient des observations NATIVE ou des repères visuels déjà reconnus
par le moteur GPS existant. Ce module n'ajoute aucune capture, aucun OCR, aucune
pression de touche ni lecture mémoire de Tarkov. La cadence de décision est de
2 Hz; cela ne transforme PAS la hauteur conservée du GPS en mesure verticale à 2 Hz.
Un même relevé conserve son âge réel. À réception d'une nouvelle altitude récente,
la couche correspondante est sélectionnée; aux frontières ou en cas de plages
contradictoires, aucun nouvel étage n'est inventé. Une observation proche et âgée
d'au plus 4 s est indiquée `Altitude reçue` ou `Repère reconnu`. Ensuite la vue
est signalée `Dernier niveau · estimé`; une altitude périmée ou un déplacement
important entraîne un retour prudent à la vue de base. Sortir horizontalement
d'un secteur borné retire automatiquement son ancien étage. Une nouvelle partie
ne réutilise jamais l'altitude de la précédente.

Le petit état AUTO est placé à droite de la bande d'extraction en haut, hors de
la carte. La surface cartographique et la section de quête en bas sont conservées.
Les marqueurs d'objets rares et de quêtes clairement situés à un autre niveau
peuvent être masqués quand l'observation est fraîche; une extraction attribuée
reste visible, atténuée si elle appartient à un autre niveau. En cas de doute,
le filtre supplémentaire est retiré. La description contextuelle de quête ne
constitue toujours pas une preuve d'étage, de progression ou d'accessibilité.

Catalogue contrôlé le 19 octobre 2026 : 43 couches supplémentaires sur 9 cartes
(Reserve, Factory, Interchange, The Lab, Shoreline, Customs, Ground Zero, Streets
of Tarkov et Icebreaker). Woods, Lighthouse, Terminal et The Labyrinth n'exposent
pas de couche supplémentaire dans ce catalogue; leur vue publiée reste utilisable.
Les plages et les ressources proviennent de tarkov.dev. Les noms de niveaux sont
ceux du site; aucun plan de sous-sol absent des sources n'est fabriqué.

LIMITE : la montée ou descente instantanée dans un escalier ne peut pas être
confirmée entre deux observations de hauteur. Cette livraison automatise la
sélection des niveaux disponibles; elle ne résout pas la mesure verticale continue.
Les captures natives de recalage préexistantes restent un mécanisme distinct;
leur réglage n'est pas changé et aucun essai en raid n'est lancé par la validation.

Tests : sélection numérique/ambiguïtés/péremption, observation native, inspection
du catalogue public et essais de rendu isolés. Les positions de ces derniers sont
synthétiques et n'ont jamais été envoyées au jeu ou à une session de point distant.

Les raccourcis de la mise à jour passent `--preserve-screenshots` : ils empêchent le nettoyage des captures au démarrage de ce processus, sans changer les paramètres système ou supprimer de fichier.

## Correction du chrono — 0.9.1

Cause identifiée : `RecognizeRaidExtractionsAsync` quittait immédiatement lorsque
`_extractionPanelComplete` était vrai, en envoyant la capture suivante uniquement
vers la lecture des quêtes. Une première lecture du chrono ratée n'était donc plus
récupérable après reconnaissance complète des extractions. La lecture du temps
attendait aussi la résolution du catalogue des sorties avant de pouvoir démarrer.

La nouvelle décision `RaidPanelReadPlan` garde une nouvelle capture manuelle du
panneau O admissible pour le chrono, même avec la liste EXFIL déjà complète. La
synchronisation est lancée avant le téléchargement éventuel des sorties. Une
capture sans rang EXFIL suit toujours le parcours des quêtes; elle n'est pas
interprétée arbitrairement comme un chrono. La lecture OCR reste sérialisée.
Ce correctif ne crée aucune capture ni entrée dans le jeu.

Après une lecture réussie, le chrono s'écoule sur l'horloge monotone locale. Un
rafraîchissement hôte chaque seconde réapplique cet état dans le navigateur,
sans OCR ni acquisition d'image, pour récupérer un nœud recréé ou un affichage
retardé. Les messages ordonnés empêchent une ancienne remise à zéro d'effacer
une mesure plus récente. Une capture identique n'est pas synchronisée deux fois.
Les valeurs OCR contradictoires de même priorité sont refusées plutôt que
choisies arbitrairement. Un échec ultérieur conserve le dernier chrono sain.

L'indicateur affiche NON LU, LECTURE, À CONFIRMER ou SYNCHRO. Le menu Chrono donne
l'état, le nombre de tentatives de lecture et le dernier acquittement d'affichage.
Ces diagnostics restent en RAM et ne contiennent aucune image, ligne OCR brute
ou donnée du compte de jeu. Aucune durée de raid standard n'est supposée.

La première synchronisation dépend toujours de la lisibilité du panneau du jeu.
Le correctif supprime un blocage logique; il ne garantit pas 100 % de réussite
OCR ni une meilleure fluidité en raid sans mesure. Le scénario initial raté puis
récupéré est validé avec des données simulées, pas par une nouvelle capture EFT.
Dans le navigateur de test hors écran, le callback JavaScript seul pouvait être
retardé; le test de reprise applique le rafraîchissement hôte pour vérifier le
rattrapage du temps réellement écoulé.

## Moteur Précision — 0.10.0

Les déplacements ne sont plus réduits uniquement à une moyenne par tick de
50 ms. `PrecisionMotionIntegrator` utilise les transitions clavier déjà reçues
et un petit historique de rotation en RAM. Les séquences sont intégrées par
sous-pas de 1 ms en présence de rotations/transitions et de 5 ms en mouvement
stable. Ce sont des pas de CALCUL, pas des acquisitions du jeu à 1000 Hz.
Le timer de l'overlay, l'acquisition d'images et la cadence de rendu ne sont pas
augmentés. Au-delà du budget de 512 sous-pas ou avec un historique incomplet,
un repli explicite utilise le moteur précédent, sans intégrer deux fois le pas.

Une inversion exactement opposée freine désormais jusqu'à zéro avant de
réaccélérer. La physique du freinage ne dépend plus accidentellement du tick
lorsque la vitesse passe par zéro. Les moyennes de deux touches opposées ou de
deux rotations qui s'annulent ne constituent plus une preuve d'inactivité.
Cela empêche ces cas d'autoriser à tort un recalage automatique au repos.

L'élan est annulé à la perte de contrôle/au changement de fenêtre. Lors d'une
interruption non observée, le point ne saute pas sur une trajectoire inventée :
son incertitude augmente et le segment n'est pas appris comme déplacement sûr.
La hauteur Y reste inchangée entre les observations verticales existantes.

La calibration est confirmée sur au moins trois segments admissibles, bornée
à sept échantillons par paramètre, filtrée par médiane et dispersion. Chaque
correction reste limitée à 3 %. Les trajets courbes, les marches arrière/latérales,
les postures réduites, les changements verticaux et les séquences interrompues
ne retouchent pas les vitesses nominales. Un segment mêlant plusieurs allures
ne modifie plus les trois vitesses à la fois. Les mélanges hip/ADS et rotations
qui se compensent ne retouchent pas une sensibilité isolée. Les anciens réglages
appris sont conservés; les nouveaux sont sauvegardés par le mécanisme existant.

Le menu `RaidPilot 0.10.0 — Précision GPS` indique l'écart horizontal et angulaire
AVANT les recalages natifs reçus, la médiane récente, les replis et le coût du
calcul. Il garde au plus 32 couples d'erreurs, en RAM uniquement, et les remet
à zéro au changement de session. Ces points de contrôle ne prouvent pas la
précision entre deux observations. Aucun historique clavier/souris ou emplacement
brut n'est enregistré par ce diagnostic. Le menu `Suivi précis — séquences courtes`
permet de revenir au chemin d'intégration précédent pour comparer, sans couper
le GPS ni déplacer le point sur une ancienne ancre.

Le correctif de chrono 0.9.1 et les niveaux automatiques 0.9.0 restent inclus.
La disposition supérieure droite, extraction/carte/quêtes, n'est pas modifiée.
Cette version n'ajoute ni capture native, ni OCR, ni bibliothèque, ni accès à la
mémoire du jeu. Le réglage existant de recalage automatique reste inchangé.

Limites : les timings de réception reflètent la boucle de messages actuelle;
les transitions d'allure/posture restent évaluées au tick de contrôle. La vitesse
réelle peut différer avec stamina, charge, blessure, terrain et collisions non
observées. Les images monoculaires et les commandes ne fournissent pas à elles
seules une position absolue continue. Les résultats des bancs de simulation ne
sont pas des mesures de précision en raid ni des garanties de fluidité du jeu.


## Precision V4 (expérimental) — 0.14.2.22

Le menu de notification propose désormais **Precision V4 — fusion prudente + atlas** (désactivé par défaut en attente d'un benchmark F8 réel). Il combine les reconnaisseurs de keyframes, la validation temporelle déjà présente dans V2, un filtre d'innovation et une fusion pondérée par incertitude pour empêcher des sauts erronés; l'odométrie V2/V3 et les estimations F8 continuent à fonctionner.

Les reconnaissances incompatibles avec l'étage sont rejetées. Un filtre de polygones navigables **vérifiés** est implémenté et testé, mais aucun polygone de murs n'est chargé par défaut : les limites de carte et les plans ne constituent pas un navmesh autoritatif. Ne pas confondre cette garde avec une collision réelle du jeu. Le diagnostic affiche V4, les refus, la calibration protégée, et les médianes des écarts F8 de plusieurs durées. La précision réelle doit être vérifiée dans EFT sur des trajets comparables avec mode V4 activé/désactivé. Ne jamais capturer de données secrètes ni lire la mémoire du jeu.


Le menu **Exporter rapport comparatif F8 (.json)** produit uniquement les écarts, durées, incertitudes du modèle et compteurs de correction (32 intervalles maximum). Il n'exporte ni captures, ni coordonnées, ni identifiants. L'export est déclenché explicitement par le joueur, sans collecte en réseau. Le diagnostic V4 conserve ses compteurs pendant le raid, y compris après un nouvel F8.


## RaidPilot 0.14.2.24 / Launcher 0.2.15 — fiabilité

- Les mesures d'écart F8 sont classées strictement par mode (V1/V2/V4) : un F8 après un changement de mode ne peut pas compter comme preuve comparative du mode précédent.
- Un repère visuel V4 partiellement pondéré n'efface pas l'âge de la dernière ancre F8. Un waypoint issu d'une relocalisation visuelle V4 reste une estimation; il ne devient pas un point « exact » dans les routes apprises.
- Le Launcher écrit un journal local atomique avant une transaction Overlay/Beacon; en cas d'arrêt Windows, il vérifie les empreintes de l'ancienne version et restaure la paire au prochain démarrage plutôt que mélanger les binaires.
- Le Launcher reprend une interruption HTTP par requête Range quand le serveur le permet, vérifie la plage de reprise et repart de zéro si le serveur renvoie une réponse complète; une SHA-256 sur l'archive entière reste obligatoire.
- Le canal Stable officiel refuse les packages hors du dépôt officiel ou issus de publications différentes. Ce contrôle ne remplace **pas** une signature du manifeste ou du code par l'éditeur.
- Le pipeline Windows vérifie les composants distribués et lance réellement le Launcher en mode `--verify-bundle` avant de créer les archives.

**À valider en raid :** erreur de position réelle face aux F8 successifs sur plusieurs cartes/résolutions/postures, performance GPU selon PC et compatibilité de l'escouade avec plusieurs personnes en PvE. **À fournir avant distribution commerciale :** autorisations des auteurs des cartes non commerciales / à droits incertains, et certificat de signature des exécutables si l'on veut un package authentifié côté Windows. Ces éléments ne peuvent pas être réputés complets à partir des tests automatisés.


## 0.14.2.25 — Exact F8 même pendant le déplacement (facultatif)

Le moteur actuel utilise un mouvement à l'estime entre deux captures natives, ce qui ne peut pas garantir une position métrique exacte quand EFT varie la vitesse selon le terrain, le poids et les équipements. La version 0.14.2.25 ajoute donc une **source exacte répétée** : le menu de notification propose « **Recalage F8 pendant déplacement · 4 s (expérimental)** », initialement **désactivé**. Le joueur choisit volontairement de l'activer; la préférence locale survit aux raids et aux mises à jour.

- Conditions strictes : jeu EFT actif au premier plan, raid en cours, mouvement détecté, ni visée/tir/free-look, première capture exacte déjà validée, dossier de captures détecté et touche « Screenshot » EFT **simple** dans F6 à F12 (par exemple F8). La touche est lue dans la configuration du joueur, jamais supposée. Au moins 4 secondes entre captures et après la dernière ancre exacte.
- Le logiciel emploie l'envoi de touche Win32 déjà présent dans le projet. **Aucun accès à la mémoire du jeu, paquet réseau ou code EFT**. Le moteur garde l'historique entre le moment de l'image et sa détection pour replacer correctement le joueur à la capture, sans le renvoyer artificiellement en arrière.
- **Pas de calibration ni d'apprentissage de keyframes lors d'une capture automatique en mouvement** : elle sert uniquement de position exacte. Les fichiers n'appartenant pas à la requête sont préservés; le mécanisme existant supprime prudemment les captures automatiques identifiées.
- Diagnostic du menu GPS : demandes de recalage mobile et captures effectivement reconnues. Une demande sans réception doit rester un échec visible, jamais une réussite simulée.
- Précaution : l'envoi automatique d'une touche peut afficher un message de capture dans EFT et affecter les performances. Les règles d'utilisation de Battlestate concernant les macros et logiciels tiers peuvent évoluer. Utiliser ce mode à ses propres risques, seulement après avoir vérifié ses permissions; il est **facultatif**. Les tests de CI montrent le comportement prévu, pas un F8 capturé à travers l'anticheat ou une précision certifiée en PvE.
- Si le joueur préfère ne pas utiliser les entrées automatiques, la capture **F8 manuelle reste entièrement opérationnelle**. Un raccourci Screenshot facile à utiliser tout en bougeant permet un recalage rapide.


## 0.14.2.26 — QuestPilot, gestion des quêtes et balises numérotées

RaidPilot ajoute un planificateur de quêtes **local** sous le bouton « 🎯 Mes quêtes » (visible lorsque le mode interaction Ctrl+Alt+I est actif). Pour une quête qui comporte plusieurs objectifs géolocalisés distincts, par exemple trois balises, les repères ont **la couleur stable de la quête** et portent **1, 2, 3** dans l'ordre des objectifs fourni par le catalogue tarkov.dev. Les étapes cochées par le joueur montrent une coche atténuée; elles ne sont pas déclarées accomplies par EFT.

- Ouvrir « Mes quêtes » : rechercher dans le catalogue PvE/PvP, activer les quêtes voulues; les sélections restent enregistrées dans le stockage local WebView2. L'ancien parcours TAB → TÂCHES → F8 reste disponible pour ajouter automatiquement des quêtes reconnues. Retirer volontairement une quête est mémorisé et un nouvel OCR ne la réactive pas.
- Les tâches et les couleurs restent disponibles au raid suivant et au changement de carte. Le seul mode PvE/PvP actif est utilisé à la fois par la carte locale et le plan de quêtes; les sélections PvE/PvP sont distinctes.
- La section « Étapes sur cette carte » affiche les objectifs sélectionnés, leur numéro, une description source, les exigences publiées et une case **« fait selon moi »** pour chaque étape. Le joueur peut choisir l'étape cible. La checklist de préparation montre les conditions structurées connues de clé, balise, objet ou équipement; pas d'inventaire automatiquement reconnu.
- Un HUD très compact sur la carte montre l'étape cible avec une **distance indicative** seulement si la pose locale est récente et son incertitude estimée ≤8 m. Une différence de niveau/obstacle n'est pas prise pour une route garantie. L'ordre conseillé est un ordre simple de proximité, pas un itinéraire garanti praticable.
- **Étapes obligatoires vs lieux possibles :** trois objectifs indépendants reçoivent 1/2/3. Plusieurs emplacements possibles attachés à un **seul** objectif conservent son numéro unique et affichent une indication de lieu possible, sans inventer trois validations.
- La numérotation et la liste ne sont jamais reconstruites à partir de l'ordre de proximité : elles suivent l'ordre des objectifs du catalogue, et leur couleur reste liée à l'identifiant de quête même si d'autres quêtes sont ajoutées.

**Limites connues :** le suivi des étapes cochées est déclaratif et local, l'OCR peut manquer les quêtes hors écran, et les positions publiques ne décrivent pas toujours une pièce/étage ou une zone navigable. Le partage automatique du plan entre membres de l'escouade et l'accès autorisé à la progression du profil Tarkov nécessiteront une intégration distincte; ils ne sont pas simulés. Les droits des cartes HD et les essais en raid réel restent à valider.


## 0.14.2.27 — Journal de quêtes EFT et partage simple des plans

### Suivi des quêtes (sans promesse de détecter chaque étape)

RaidPilot surveille localement, en lecture seule, le fichier EFT le plus récent dont le nom contient `push-notifications` ou `notifications` dans le dossier des journaux déjà découvert pour le suivi de la carte. La lecture commence en fin de fichier à l'ouverture (pas de réécriture rétroactive des anciennes quêtes), suit les ajouts, accepte les JSON multi-lignes, et ne garde **que** les notifications de quête de type `new_message`, `message.type=10/11/12`, dont `templateId` commence par **un identifiant de quête de 24 caractères hexadécimaux** et dont `dt` est une date raisonnable.

- 10 : quête acceptée; propose l'ajout à la liste locale si le joueur ne l'a pas exclue.
- 11 : quête échouée; signale le statut, sans altérer les cases des étapes.
- 12 : **quête remise au marchand**; QuestPilot la classe comme `Journal EFT`, et non comme un F8 ou un succès deviné. Cette source est distincte des étapes que le joueur coche manuellement.
- La seule source de mode de jeu est un événement explicite `Session mode: Pve/Pvp/PvpSeason` dans les journaux d'application. Si le mode est inconnu, les événements attendent en mémoire et ne sont pas attribués au profil PvE ou PvP par supposition. Les profils de progression restent distincts.
- Les événements bruts et messages de discussion ne sont ni sauvegardés, ni envoyés au réseau. Les changements en mémoire n'exposent que l'identifiant public de quête, le statut et l'horodatage. Le moteur n'affirme **pas** avoir confirmé individuellement chaque balise pendant la partie : ces événements ne sont pas fournis dans le journal pour toutes les quêtes.

### Plans d'équipe : partage manuel opérationnel, synchronisation automatique non disponible

Dans `Ctrl+Alt+I → 🎯 Mes quêtes`, les joueurs disposent maintenant de **« Copier mon plan »** et **« Ajouter le plan ami »**. Le code `RPQ1:` contient seulement la sélection publique et les étapes cochées du profil courant (PvE ou PvP). Chaque ami peut être importé sans modifier les choix locaux : ses objectifs apparaissent sur la carte avec des cercles numérotés en tirets. Les plans d'équipe importés sont mémorisés localement, au maximum six coéquipiers; un bouton permet d'en retirer un. Aucune information de compte, nom de personnage, identifiant de session, de raid, coordonnées, mot de passe ou journal n'est incorporée au code.

**Limite observée :** le test réel et synthétique du Worker `wss://raidpilot-team-relay.tyronixstudio.workers.dev/ws` a retourné `QUEST_RELAY_SUPPORT=no` pour un message `questplan`. Le relais en production **ne transporte donc pas les quêtes** au 24 octobre 2026. La transmission automatique et les confirmations de livraison ne peuvent pas être proposées sans mettre à jour le Worker Cloudflare lui-même. Les positions d'escouade restent fonctionnelles comme avant; aucun message de quêtes non reconnu n'est injecté dans les messages de position. Une synchronisation en temps réel devra être testée entre deux PC après évolution contrôlée du serveur.

**À tester en partie réelle :** événement d'acceptation et remise dans un journal EFT réel, identité du mode, cas où les logs sont absents, réception d'un plan de coéquipier, et affichage des balises. Le passage des tests automatisés prouve la logique de lecture et la stabilité de la build, mais pas une progression en raid certifiée.


## 0.14.2.28 — QuestPilot : partage direct facultatif, client prêt et Worker de staging

Cette version prépare le partage direct des plans de quêtes via le relais d'escouade Cloudflare, sans prétendre que le relais en production l'accepte. Le partage manuel `RPQ1` reste disponible et constitue **le mode effectivement opérationnel tant que le Worker n'est pas mis à niveau**.

- **Consentement par connexion** : dans l'icône RaidPilot → Escouade → « Partager mes quêtes avec l'escouade ». Désactivé par défaut, jamais réactivé automatiquement en rejoignant une autre escouade. Confirme quelles données sont visibles.
- **Vérification du support serveur** : le client demande `GET /health` sur le domaine officiel et exige `features.questplan: true`. Sinon, la case revient sur désactivé et suggère « Copier mon plan ». Les clients ont un format de messages `questplan` strict et n'envoient rien à un relais qui n'a pas annoncé cette fonction.
- **Protection de la vie privée** : seuls les IDs publics des quêtes sélectionnées et des étapes cochées sont transmis. Pas de journaux EFT, captures, profil Battlestate, identifiants Discord ou coordonnées dans les paquets de quêtes. Les données de position de l'escouade restent gérées par le module existant.
- **État d'escouade éphémère** : les plans reçus ne sont jamais sauvegardés en tant que progression personnelle, disparaissent si l'ami quitte ou n'émet plus (90 s), sont isolés par mode PvE/PvP, ne peuvent pas modifier tes propres coches et sont identifiés comme « direct ». Les mêmes identifiants de quête conservent couleur/numéros.
- **Transport borné** : une vérification du plan local au plus toutes les 4 s, réémission toutes les 25 s maximum en l'absence de modification, 80 quêtes et 120 étapes maximum par paquet, validation des IDs et protection contre les messages anciens. Les erreurs ne doivent jamais arrêter le suivi des positions.
- **Worker candidat de staging** : `raidpilot-updates/relay/worker-quest.mjs`, configuration de staging et documentation de migration. Ce code doit d'abord être testé puis déployé par une personne disposant d'un accès autorisé au compte Cloudflare. **Aucun Worker en production n'a été remplacé par ce commit.**

Validation prévue : suite JS, tests C# de paquetage, compilation Windows, simulation de deux sockets et de séparation des salles. Les confirmations de quêtes dans les journaux EFT restent locales et ne sont pas diffusées automatiquement. Le partage direct ne pourra être annoncé comme fonctionnel qu'après test de bout en bout sur le serveur de production, avec deux clients réels.
