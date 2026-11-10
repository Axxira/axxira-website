# ForeversPilot FastXp 1.10.1 · Français / English

Assistant de leveling pour **WoW Forever**, interface 16001. **Ding est obligatoire**
et doit rester activé. ChangeLayer est facultatif. Le joueur se déplace et combat
lui-même : aucune quête abandonnée, aucun objet acheté, vendu ou équipé automatiquement.

## Commencer

1. Installe Ding et ForeverPilot_Leveling, puis redémarre complètement WoW.
2. L'accueil apparaît au premier chargement hors combat. Dans **Options → Général**,
   choisis Français, English ou Auto, puis **Appliquer la langue** hors combat.
3. Vérifie ta classe et ta spécialisation dans Profil. Un profil manuel est prioritaire;
   l'aperçu d'un autre personnage ne modifie pas ton personnage réel.
4. Suis la flèche et l'action principale. **Passer** écarte la quête pour ce personnage
   sans l'abandonner. Sa reprise se fait explicitement dans Options → Outils.

**Options** regroupe langue, profil, pause, taille, position, objets, HDV, layers et aide.
Les noms de quêtes, PNJ et objets restent ceux du jeu. Les demandes de layer sont en anglais.

## Deux éditions

- **Launcher RaidPilot : toutes les fonctions sont gratuites**, Fast XP et conseils
  de classe compris. Aucun abonnement, essai expirant ou contrôle en ligne requis.
- **Distribution publique : Fast XP et conseils de classe/spécialisation nécessitent
  un abonnement de 9,99 CAD/mois** et le compagnon Windows. Options → Abonnement donne
  les instructions et le lien du portail. Renouvellement mensuel; annulation des prochains
  paiements depuis le portail ou PayPal. La période déjà payée reste active.
- Le portail indique si les abonnements sont ouverts. Une confirmation PayPal seule
  n'active rien : le service doit confirmer le paiement. Conserve ta clé personnelle,
  importe-la dans le compagnon, ferme WoW pour la première installation et active ton accès.
  Ouvre régulièrement le compagnon pour actualiser l'accès (validité hors ligne limitée
  à sept jours et à la période payée). Reconnecte-toi au jeu pour charger l'actualisation.
- Sans accès public actif, Ding retrouve son guidage; prix HDV, outils et Options restent
  accessibles. Le ZIP Lua ne se connecte jamais directement à PayPal.

Portail officiel : https://foreverpilot-subscriptions.tyronixstudio.workers.dev

## Ce qui est pris en compte

Journal réellement confirmé, offres du PNJ, prérequis, niveau, classe, quêtes terminées,
objectifs voisins, trajets connus, apprentissage et exclusions par personnage.
Les sorties suivent trois phases : **préparation → objectifs de zone → remises groupées**.
Avant de partir, le guide regroupe les quêtes faisables du village (jusqu'à 700 yards
du point de départ), dans la limite du journal, et vérifie chaque prise dans le journal.
Les quêtes solo adaptées du catalogue de la zone peuvent compléter le parcours;
une proposition de la base n'est jamais considérée comme une offre confirmée du PNJ.
Tous les objectifs éligibles déjà pris dans cette zone rejoignent la sortie, sans
limite artificielle de quatre ou six quêtes. Un journal plein ne force plus un retour
si du travail reste en zone. Un PNJ déjà à côté permet une remise sans interrompre
la sortie. Les nouvelles prises au village attendent le retour; une prise rencontrée
sur place peut être intégrée. Les quêtes indisponibles, de groupe, trop basses ou
mises de côté ne bloquent pas la boucle. Les besoins de repos/entretien restent prioritaires.
Les quêtes de classe proches, déjà prises ou réellement offertes sont prioritaires;
une prise lointaine non confirmée reste candidate sans imposer un détour immédiat. Un maître de vol proche
est proposé pour découverte/vérification; sa carte doit être ouverte pour confirmation.

Les récompenses bleues observées dans la fenêtre de quête ou l'entrée sélectionnée du
journal peuvent favoriser légèrement une quête si leurs statistiques conviennent à
ton profil et ton équipement. Compare les effets avant de choisir. Les armes et effets
complexes ne reçoivent pas de bonus supposé; les récompenses inconnues restent inconnues.
Ce n'est pas un catalogue complet des quêtes donnant du bleu ni une promesse d'XP/h.

Les conseils d'objets suivent classe ET spécialisation : **Upgrade OUI/NON**, gains et
pertes, ou **À VÉRIFIER** si les données manquent. Ce sont des estimations, pas une simulation
de DPS. Les effets spéciaux et règles du serveur peuvent demander un contrôle manuel.

Le bouton de scan HDV nécessite un commissaire-priseur ouvert. Les prix montrent les
pièces or/argent/cuivre, l'âge du relevé et son caractère partiel. Aucun prix live inventé.
Les lumières des buts de Montréal (10 secondes, uniquement dans WoW) nécessitent le
launcher Windows; elles ne font pas partie du ZIP Lua autonome.

## Limites et diagnostic

La simulation 1–60 vérifie des transitions logiques sur les données disponibles.
Elle ne garantit ni toutes les quêtes du serveur, ni un trajet sans obstacles, ni zéro
grind, ni un parcours réel 1–60 validé. Quand le journal ou le PNJ contredit la route,
le jeu prime. Les progrès et quêtes écartées sont isolés par personnage.

Commandes : `/fpl options`, `/fpl help`, `/fpl premium`, `/fpl debug`, `/fpl journal`,
`/fpl resync`, `/fpl skip`, `/fpl reprendre`. Ne publie jamais tes sauvegardes ou ta clé.

## English quick start

Install and enable **Ding** and **ForeverPilot_Leveling**, then fully restart WoW.
Open **Options → General** to set language, class/spec profile, pause, size and position.
Apply language outside combat. Follow the arrow; **Skip** sets a quest aside for this
character without abandoning it. Restore it explicitly from Tools.

The **RaidPilot launcher edition is entirely free**, including Fast XP and class advice,
with no subscription or online check. The **public edition** offers Fast XP and class/spec
advice through a **CAD 9.99/month** subscription. Open Options → Subscription, save your
personal portal key, confirm PayPal and import the key into the Windows companion.
Payment must be verified by the service. Close WoW for the initial access installation.
Refresh with the companion regularly; offline access lasts at most seven days and never
past the paid period. Log back into WoW to load refreshed access. Cancel future renewals
in the portal or PayPal; already paid time remains available. Ding guidance resumes when
public access expires. AH prices and tools remain available.

AH values are dated observations, possibly partial, never guaranteed live prices.
Gear advice follows class/spec and shows YES/NO or CHECK when uncertain. Class quests
and nearby flight masters are prioritized when available and feasible. Live game state
takes priority over route data. Neither the simulator nor UI checks establish a complete
real-world 1–60 playthrough. Montreal goal lights require the Windows launcher.

Outings follow **collect → zone objectives → grouped turn-ins**. Collect eligible
village quests within 700 yards of the pickup anchor, up to actual journal capacity.
Every pickup needs live journal confirmation. Suitable solo zone-catalog quests
can join as DATABASE suggestions; an NPC's real offers always take precedence.
All eligible held objectives in the zone participate, with no four/six-quest cap.
A full journal does not force a return while zone work remains. Hand in at an NPC
already beside you, then continue. New village pickups wait until the return;
pickups encountered on the spot can join. Unavailable, group, obsolete and skipped
quests do not lock the outing. Rest and maintenance needs may still interrupt it.
Nearby, held and live-offered class quests keep priority; distant unconfirmed pickups
remain candidates without forcing a detour. Observed rare rewards may add a small,
capped preference based on actual class/spec and equipped stats. Compare effects
manually; weapons and unknown effects receive no guessed bonus. This is not a complete
rare-reward catalog or a measured XP/hour claim. Updating from 1.9.1 only needs a reload
outside combat; no new files are loaded.

## Source, licences et crédits

26 modules Lua séparés : état, graphe, planification, validation, récupération, parcours,
navigation, apprentissage, objets, marché, interface et options. Les routes/données ouvertes
proviennent de Ding; aucune donnée Zygor ou RestedXP n'est copiée.

Source : https://github.com/Axxira/axxira-website/tree/raidpilot-updates/raidpilot-updates/source/foreverpilot

Code distribué sous **GPLv3**, voir `LICENSE_GPLv3.txt`. Conserver les crédits et licences
des dépendances. L'abonnement ne retire pas les droits GPL sur le code reçu. Le client Lua
est ouvert et modifiable : le contrôle local n'est pas un mécanisme inviolable. Le service
vérifie les paiements et le compagnon vérifie les signatures avant l'activation normale.
L'ancien addon `ForeverPilot` est un bridge indépendant, jamais remplacé par ce produit.

## 1.10.1

FR : les neuf classes sont accessibles depuis Profil. Choisir une autre classe ouvre un aperçu isolé; les conseils du personnage réel suivent sa classe et sa spécialisation. Les anciens profils incompatibles sont ignorés. Options reste accessible quand le guidage est masqué, désactivé ou sans accès Premium, et ne dépend plus du module objets. Nom affiché : ForeversPilot FastXp; dossier et réglages conservés. Conseils statistiques estimés, effets complexes à vérifier.

EN: all nine classes are accessible in Profile. Choosing another class opens an isolated preview; actual equipment advice follows the player class and specialization. Incompatible legacy profiles are ignored. Options remains available with hidden, disabled or locked guidance, independently of the equipment module. Display name: ForeversPilot FastXp; existing folder and settings preserved. Stat-based estimates; complex effects still require review.
