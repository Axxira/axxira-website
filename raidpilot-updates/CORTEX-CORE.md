# RAIDPILOT CORTEX — V0.15.0.1 (Core 1 + F8 timing guard)

CORTEX est un superviseur de localisation **exécuté en local**. Il complète les capteurs et estimateurs de RaidPilot; il n'est pas un modèle neuronal entraîné ni un moteur SLAM 3D complet.

## Données qui alimentent CORTEX

- Positions estimées par `RealtimePoseEstimator`, horodatages `Stopwatch`, commandes de déplacement et état de mouvement visuel
- Corrections locales **effectivement appliquées** par le moteur de relocalisation, avec accord de deux reconnaisseurs d'images
- Ancres absolues F8 et erreurs mesurées **avant recalage** par `PrecisionDriftAudit`
- Carte courante, génération de raid, identifiant de la fenêtre de jeu, fiabilité et incertitude estimée

Une capture Windows locale silencieuse n'apporte pas de coordonnées EFT absolues. CORTEX n'invente pas de position et ne contourne pas le jeu. Il ne lit pas la mémoire EFT, n'injecte pas de code et n'envoie aucune capture à un serveur.

## Décisions

1. Un premier F8 natif reste indispensable pour initialiser une position absolue.
2. Une correction visuelle seulement proposée ou issue d'un atlas distant ne suffit jamais à espacer F8.
3. Une correction visuelle locale appliquée, de bonne qualité et récente permet au plus 8 s entre F8 (si les autres capteurs sont cohérents).
4. Au moins deux preuves visuelles distinctes sont nécessaires pour la cadence maximale de 12 s.
5. Si le suivi est périmé, les capteurs se contredisent ou le joueur est téléporté/impossible d'après le modèle, CORTEX garde le seuil conservateur de 4 s.
6. Les écarts réels F8 sont conservés sous forme de métriques bornées en mémoire (sans coordonnées). Une forte erreur bloque l'extension de cadence; 35 s et deux F8 de qualité sont nécessaires au rétablissement.
7. Les protections d'interaction (F, R, tir/ADS), fenêtre Tarkov au premier plan et touche de screenshot dédiée demeurent en vigueur.
8. Le joueur peut désactiver CORTEX dans le menu de l'overlay et revenir immédiatement à la politique pré-CORTEX.
9. CORTEX mesure la cadence F8 uniquement depuis le **dernier fichier de screenshot EFT reçu**; une correction visuelle ne remet pas son horloge native à zéro.

Les seuils sont **conservateurs** et sujets à validation avec données de test, pas des garanties de précision métrique.

## Intégration actuelle

- `CortexNavigationEngine.cs`: arbitrage des états, intégrité de session, suivi des preuves et analyse d'erreur
- `OverlayCortex.cs`: activation/désactivation, diagnostic, liaison à l'overlay
- `OverlayForm.cs`: données de mouvement et visuelles réellement appliquées, F8 natifs et décision de cadence
- `MobileExactAnchorPolicy.cs`: extension de 12 s conditionnée à deux preuves indépendantes
- `Tests/CortexNavigationTests.cs`: 17 relectures déterministes, intégrées au harnais de tests Windows

## Vérification terrain demandée — deux joueurs, même raid

1. Mettre à jour RaidPilot 0.15.0.0, garder Beacon 0.3.10 et Launcher 0.2.15.
2. Faire F8 chacun au début du raid, confirmer le raccourci de screenshot dans EFT.
3. Comparer trajet droit, virage serré, escalier, obstacle et retour à un lieu connu avec CORTEX actif et désactivé.
4. Noter le nombre de F8 affichés, la dérive réelle avant F8 et les cas où le cap est faux.
5. Dans « GPS estimé · diagnostic », consulter « Navigation CORTEX »; exporter aussi le rapport F8 V4 si utile.
6. Ne pas affirmer de performance supérieure tant que la comparaison F8 et FPS n'est pas mesurée.

## Phase 2 — CORTEX V2 : mémoire des repères visuels (0.15.1.0)

CORTEX conserve maintenant un **registre borné à 48 lieux** corroborés par les deux reconnaisseurs locaux. Il stocke en RAM les identifiants de keyframes et leur pose de référence (jamais de screenshot); cette mémoire est effacée au recalage F8, en cas de contradiction et à chaque changement de session.

### Indépendance des observations

- Les mêmes identifiants 64×36 + 160×90 retrouvés sur plusieurs trames constituent une **revisite**, pas un nouveau lieu.
- Des identifiants différents au même endroit ne représentent pas non plus deux références géographiques indépendantes.
- La cadence de 12 s n'est possible qu'après deux lieux réellement distincts, séparés horizontalement d'au moins **3 m**, sur un étage compatible et observés dans la fenêtre récente de **5 s**.
- Un repère associé soudainement à des coordonnées ou une orientation incohérentes entraîne une perte de confiance et le repli vers la cadence conservatrice.
- Une correction de position explicitement **appliquée** ne compte pas comme une téléportation physique du joueur; elle reste évaluée par les garde-fous du moteur de relocalisation.
- Les repères de l'atlas collectif et les correspondances non corroborées ne permettent pas d'augmenter l'intervalle F8.

Cette mémoire constitue une **vérification d'identité spatiale**, pas un SLAM 3D ni une localisation métrique absolue. Les anciens algorithmes restent disponibles si CORTEX est désactivé. Les tests automatisés valident la logique; la baisse effective du nombre de notifications et les erreurs GPS doivent être mesurées sur de vraies parties.

## Développement suivant (non livré dans Core 1)

- Intégration d'un suivi de caractéristiques visuelles robuste sur plusieurs frames, mesuré sur des parties EFT réelles
- Mémoire de lieux / fermeture de boucle, séparée par carte, étage, FOV et version de jeu
- Fusion probabiliste des poses/erreurs en espace 2D/3D, tests de dégénérescence visuelle et d'occlusion
- Calibration supervisée des erreurs à partir des diagnostics anonymisés explicitement consentis
- Optimisation dynamique de calcul CPU/GPU (profilage et budgets sur PC moins puissants)
- Évaluation de la conformité aux règles EFT et des comportements de capture
