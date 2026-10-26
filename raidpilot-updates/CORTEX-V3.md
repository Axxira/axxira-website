# CORTEX V3 — apprentissage local de dérive (expérimental)

## Objectif
Réduire les F8 intrusifs lorsque la position est effectivement fiable, sans
sacrifier l'ancrage exact natif ni promettre de précision absolue.

Cette phase est un **modèle statistique en ligne**, pas un réseau neuronal,
un système SLAM complet ou une garantie de sûreté. Aucun accès mémoire Tarkov.

## Données
- Uniquement des résidus **F8 natifs comparables** issus de PrecisionDriftAudit
  (erreur de position et de cap, temps écoulé, incertitude annoncée).
- 24 mesures au plus en RAM ; aucune image, coordonnée, trajet, touche ou
  identité personnelle enregistrée dans ce modèle.
- Un nouvel environnement de jeu, une nouvelle partie ou une désactivation
  de CORTEX remet le modèle à zéro.
- Les mesures impossibles ou trop anciennes sont ignorées par l'apprentissage.

## Politique de décision
1. Avant six mesures comparables : règles d'origine à 4 / 8 / 12 secondes.
2. Après six mesures : évaluer le quantile empirique P90 des résidus normalisés
   par la racine du temps, puis estimer le risque à 8 et 12 secondes.
3. Une forte erreur, une erreur de cap ou une augmentation soudaine limite
   l'intervalle à 4 ou 8 secondes.
4. L'apprentissage ne peut jamais fabriquer un F8 exact, créer un repère
   visuel ou surpasser les garde-fous de mouvement et d'action.
5. Après un repère local réellement appliqué et des F8 stables, une fenêtre
   de grâce de 5 à 9 secondes peut maintenir **au plus 8 secondes** si
   la santé de localisation reste verte et l'incertitude très faible.
6. 12 secondes requiert toujours des repères visuels distincts, récents et
   cohérents. En l'absence de preuve, revenir à 4 secondes.

**Important** : le P90 calculé est un indicateur empirique sur peu de mesures,
pas un intervalle de confiance ou une garantie statistique. Les résidus
successifs sont corrélés ; un changement de carte, d'environnement ou de
comportement peut invalider les données précédentes.

## Mesures dans le diagnostic
- Erreurs F8 réelles, nombre d'échantillons, risque empirique à 8 et 12 s.
- Intervalle plafond choisi (4, 8 ou 12 s) et raison des restrictions.
- Cadence réellement observée entre deux F8 natifs : moyenne et répartition
  des intervalles (<6 s, 6–10 s, >=10 s).
- Les diagnostics ne contiennent ni coordonnées ni images.

## Validation nécessaire avant affirmation de gain réel
1. CI Windows : tests CORTEX, localisation, QuestPilot et build Overlay/Beacon.
2. Répéter des raids complets sur au moins deux machines et diverses cartes.
3. Comparer A/B CORTEX activé/désactivé avec le même profil de déplacement.
4. Vérifier simultanément les F8/minute et médiane/P90 d'erreur de position
   observée aux ancrages natifs. Refuser un gain de cadence si la dérive
   augmente de façon inacceptable.
5. Tester perte de fenêtre active, visée, recharge, collision, escalier,
   étage, FPS faible, résolution et changement de carte/raid.

Le code ne garantit ni une amélioration sur toutes les configurations ni une
diminution automatique des captures lorsqu'aucun repère fiable n'est détecté.
