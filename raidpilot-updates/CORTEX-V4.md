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
