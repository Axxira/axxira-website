# CORTEX V4 — Visual & Spatial Intelligence (expérimental)

## Architecture réellement implémentée

V4 s'appuie sur **deux moteurs de repères visuels existants et persistants** :
`VisualKeyframeRecognizer/Store` (64×36) et
`SuperKeyframeRecognizer/Store` (haute résolution). Les keyframes ont
été apprises à partir de captures F8 exactes, pas inventées par V4.
Le reconnaisseur existant distingue candidats, confirmations et
retours sur lieu connu (loop closure). V4 ne prétend pas fournir de
SLAM neuronal nouveau, ni une localisation garantie sans F8.

### 1. Fusion visuelle prudente
`CortexVisualIntelligence.cs` reçoit une **proposition déjà confirmée**
du reconnaisseur existant, avant que la correction ne soit appliquée.
Il la confronte à l'incertitude de l'odométrie et calcule un gain
type Kalman scalaire, borné au plus par le gain déjà admis par V2/V4.
Il ne rehausse pas artificiellement la confiance, le déplacement
maximum ou la rotation maximale.

### 2. Récupération après perte de position
Lors d'une grande boucle ou d'un saut supérieur à 6 m, deux
observations temporellement distinctes du **même couple de repères**
basse/haute résolution (correspondances locales) sont exigées dans
une fenêtre de trois secondes. Même lorsqu'elles concordent, la
correction est limitée à 0,75 m et 6° par application. Une
relocalisation partagée entre coéquipiers ne devient pas une preuve
indépendante permettant le saut.

Si le suivi devient incertain et que le joueur est immobile,
`CortexRecoveryPolicy` peut demander un vrai F8 via le mécanisme
existant, avec les vérifications de fenêtre active, raccourci,
état du raid, actions, temps et permissions de l'utilisateur.

### 3. Atlas spatial persistant des erreurs
`CortexSpatialReliabilityAtlas.cs` répertorie des cellules grossières
de 40 m par bande verticale de 8 m. Une cellule n'est apprise
qu'à partir de mesures d'erreur de véritables F8 comparables,
jamais d'un GPS visuel synthétique.

L'atlas conserve au maximum 512 secteurs, expire après 30 jours,
et est stocké localement avec écriture atomique dans le dossier de
localisation du profil. Il ne contient **aucune image**. Il contient
des positions quantifiées de secteur, qui peuvent être sensibles
si les fichiers locaux sont partagés.

Les secteurs risqués peuvent uniquement **réduire** les plafonds
de capture de 12 à 8 ou à 4 secondes, jamais autoriser davantage.
Les deux moteurs de repères persistants précédents ne sont ni
écrasés ni remplacés.

### 4. Diagnostic et comparaison
`CortexV4Report.cs` exporte les intervalles F8 réels, les erreurs
F8 P90, le nombre de corrections visuelles acceptées/rejetées, les
boucles corroborées et le nombre de secteurs d'erreur.
Cet export ne contient pas de coordonnées exactes, captures, chemins
de fichiers, identifiants utilisateur ou descriptions de carte.

## Complément V4.1 — récupération progressive et fiabilité visuelle

La V4.1 complète le mode expérimental V4 avec deux sécurités et
un retour d'expérience mesurable :

- **Double trame réellement indépendante :** une grande relocalisation
  demande désormais deux confirmations visuelles séparées d'au moins
  250 millisecondes. Une répétition de la même trame trop rapide ne crée
  pas une seconde preuve et ne détruit pas la preuve déjà acceptée.
- **Récupération graduelle :** une boucle vérifiée autorise au maximum
  six petites corrections successives de **0,75 m et 6° au plus chacune**,
  dans les quatre secondes, uniquement en retrouvant à chaque fois
  les mêmes repères locaux basse/haute résolution. La perte de cohérence
  interrompt cette séquence et réclame un recalage F8 sûr.
- **Comptabilisation honnête :** une boucle confirmée n'est comptée comme
  validée que lorsque le déplacement a effectivement été appliqué par
  l'estimateur, jamais lors de la seule suggestion visuelle.
- **CORTEX Visual Feedback :** un évaluateur local sans images et sans
  coordonnées mémorise au maximum 16 comparaisons de dérive natives F8
  postérieures à une correction visuelle locale appliquée. Il exige
  quatre intervalles indépendants avant de réduire, si nécessaire, la
  cadence admise de 12 à 8 ou 4 secondes. Les comparaisons de captures
  ambiguës, périmées ou de durées incompatibles sont ignorées. Il ne
  peut **jamais augmenter** les intervalles autorisés par V3.
- Le rapport V4 exporte le nombre de corrections visuelles réellement
  comparables à des F8, leurs fortes dérives et le plafond de prudence.

Ce n'est toujours pas un SLAM neuronal ou une estimation autonome de
position : les banques visuelles V2 existantes, les observations de
déplacement et les F8 exacts demeurent les sources de vérité.
Les gains de précision et de nombre de captures restent à démontrer
avec les rapports de vrais raids comparables. L'activation V4 reste
volontaire, pour éviter de changer silencieusement la navigation des
joueurs qui préfèrent l'ancienne politique.

## Correctif V4.2 — cadence F8 4/6/8/12 s fondée sur des preuves réelles

Le modèle précédent exigeait une correction visuelle locale haute/basse
résolution effectivement appliquée pour dépasser 4 secondes. Or elle est rare
quand le joueur se déplace continuellement, même avec une bonne prédiction.
De plus, l'apprentissage V3 était remis à zéro à chaque raid, tandis que
l'atlas V4 ne mémorisait que les secteurs à risque.

La V4.2 ajoute un second mécanisme expérimental, **distinct du SLAM et de la
reconnaissance de lieux** : une courte mémoire positive et négative des erreurs
observées **sur de vrais F8 natifs comparables**, en mode Precision V2.

- Résumé par carte, lié au profil local via un nom de fichier haché :
  nombres bornés de bonnes et fortes erreurs, sans captures, coordonnées ou
  historiques de déplacement; expiration après sept jours.
- Si l'historique contient six corrections suffisamment fiables, au moins
  deux F8 réellement comparables et fiables du **raid actuel** sont nécessaires
  avant un palier de **6 secondes**.
- Huit secondes exigent quatorze bonnes mesures historiques et quatre
  mesures récentes consécutives du même raid. Les indices de risque V3 et V4
  peuvent restreindre ce palier.
- Douze secondes **n'est pas** une récompense de cet historique : elle continue
  à nécessiter les repères visuels indépendants déjà prévus par V3.
- Dégradation d'incertitude, de cap, état GPS orange/rouge, contradiction
  de mouvement, fort résidu F8, zone à risque V4 ou condition de capture
  dangereuse provoquent un retour conservateur à quatre secondes.
- Le rapport CORTEX V4 indique maintenant les mesures comparables,
  non comparables et les bonnes mesures consécutives. Si ces compteurs ne
  progressent pas, l'espacement ne sera pas autorisé et le rapport permettra
  de cibler le problème de datation/calibration des captures.

V4.2 est intégrée au **mode CORTEX V4 expérimental**, activable dans le menu
Windows. Les délais 6/8 s restent des plafonds empiriques sous conditions,
pas des garanties de précision ni une assertion que des raids antérieurs
étaient déjà appris. La comparaison sur des vrais raids doit mesurer
la fréquence F8/minute et les erreurs F8 médiane/P90 à configuration égale.

## Complément V4.3 — diagnostic des captures F8 (Overlay 0.16.0.1)

Un export JSON V4 peut afficher `WaitingForAnchor` et zéro comparaison
même après des raids précédents : les compteurs `nativeF8`, `visual`,
`feedback` et `learning` décrivent le **superviseur courant**, qui est
réinitialisé lors des changements de session ou de mode. L'atlas `spatial`
peut quant à lui être chargé depuis une ancienne sauvegarde et afficher des
secteurs sans nouvelle observation mesurée. Zéro n'est pas une preuve que
l'estimateur de déplacement ne s'est jamais exécuté.

V4.3 ajoute un journal **distinct de l'état de navigation** :

- `capturePipeline.RawScreenshotNotifications` : avis de nouveaux PNG
  reçus depuis le dossier surveillé pendant un raid. Le nom peut être non
  reconnu par le parseur GPS; il ne s'agit **pas** de F8 validés.
- `capturePipeline.RetainedF8Events` : observations natives de position
  ayant passé la reconnaissance de nom et atteint le gestionnaire de F8.
- `capturePipeline.WithResolvedCaptureTime` et `WithPredictedPose` :
  étapes d'horodatage et de récupération de la pose **avant correction**.
- `capturePipeline.Outcomes` : répartition exhaustive des motifs de
  comparaison non exploitable (`FirstAnchor`, `MissingCaptureTimestamp`,
  `NonMonotonicCaptureTimestamp`, `Interrupted`, `ModeChanged`,
  `MissingPrediction`, `OutOfRangeInterval`, `InvalidGeometry`,
  `InvalidResidual`) ou `Comparable`.
- `capturePipeline.ComparableWithCortexV4` : échantillons exploitables
  enregistrés alors que l'option CORTEX V4 était activée. Les erreurs
  moyenne et P90 de ce bloc ne reposent que sur les vrais F8 comparables;
  absence de résultat = `null`, pas zéro mètre de dérive.
- `capturePipeline.LastObservedUtc` et `LastRawScreenshotUtc` :
  dernières étapes constatées en heure UTC.

Le journal conserve au plus 256 événements et une fenêtre de 14 jours,
en écriture atomique locale, sous un nom de fichier dérivé du hachage du
profil. Il contient des dates UTC, indicateurs de traitement, durées et
éventuelles erreurs mesurées. **Il ne contient aucune capture, image,
coordonnée précise, trajectoire, touche, nom de carte ou identifiant
de joueur.** Il ne transmet rien en réseau. La sauvegarde est signalée
dans le diagnostic si elle échoue. Le schéma du rapport exporté passe à
`schemaVersion: 2` ; les champs existants sont conservés.

### Interprétation lors du prochain essai

1. Jouer avec l'overlay **mis à jour** (les anciens rapports ne peuvent
   être enrichis rétroactivement), activer CORTEX V4 pour l'essai et
   effectuer au moins deux F8 en raid, idéalement après un déplacement
   contrôlé sans interruption du suivi.
2. Exporter le rapport via le menu CORTEX. Si les notifications brutes
   progressent mais `RetainedF8Events` reste à zéro, contrôler le
   format des noms de fichiers EFT ou le chemin du dossier surveillé.
3. Si les F8 reconnus progressent sans `Comparable`, examiner en
   priorité les motifs `Outcomes` : timestamp manquant,
   `MissingPrediction` ou `Interrupted` sont distincts et nécessitent
   des correctifs différents.
4. Si `Comparable` progresse, seulement alors comparer erreur moyenne,
   P90, cap et fréquence F8 entre sessions de même carte, résolution,
   sensibilité et stratégie. Sans captures avec heure certaine ou sans
   pose historique, **ne pas** utiliser le résultat pour étendre la
   cadence 4/6/8/12 s.

**Aucun changement** des estimateurs, du mode interactif, des règles de
sécurité F8, des repères visuels ou de l'intervalle de capture. Ce patch
rend le diagnostic exploitable; son gain de précision in-game reste à
démontrer.

## Activation

Le comportement CORTEX V3 reste activé par défaut et inchangé.
CORTEX V4 est **désactivé par défaut** jusqu'aux essais en raid.

Dans la zone de notification Windows :
- cocher « CORTEX V4 — mémoire spatiale et fusion (test) » ;
- tester plusieurs raids ; un F8 initial reste nécessaire ;
- exporter le rapport CORTEX V4 (.json) ;
- décocher V4 pour comparer immédiatement avec CORTEX V3.

## Critères de validation

1. Suite Windows et compilation de tous les exécutables.
2. Tests de la mémoire persistante, des refus de faux positifs, des
   délais de récupération et des changements de carte/raid.
3. Sur de vrais raids avec profils similaires, comparaison
   CORTEX V3 vs V4 : nombre de F8/min, erreurs médiane/P90, temps de
   récupération d'une perte de position, nombre de boucles acceptées,
   FPS et latence.
4. Rejeter une modification si les faux recalages ou la dérive
   augmentent, même si le nombre de screenshots diminue.

Aucune amélioration de précision, réduction en pourcentage des F8,
ou reconnaissance « jamais vue » n'est affirmée avant les mesures en jeu.
Ce module ne lit ni ne modifie la mémoire du processus Tarkov.
