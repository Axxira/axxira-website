# RaidPilot Team Relay v2 — candidature de synchronisation QuestPilot

**Statut : CODE PRÊT POUR TEST LOCAL ET STAGING; PAS DÉPLOYÉ EN PRODUCTION PAR CETTE MISE À JOUR.**

Le Worker en production à `raidpilot-team-relay.tyronixstudio.workers.dev` supporte déjà le protocole `pose` mais **ne relaie pas encore `questplan`** (dernier test synthétique 2026-10-24). NE PAS annoncer le partage automatique fonctionnel avant test de transmission **entre deux véritables WebSockets**. Un simple `/health` ne prouve pas à lui seul que l'ensemble est disponible.

## Fonctionnement du candidat

- Compatible avec `GET /health` et `GET /ws?team=<cle-derivee>&name=<nom>`, `welcome`, `peer_joined`, `peer_left`, ainsi qu'avec les poses XYZ/yaw existantes.
- Ajoute `questplan` uniquement quand le joueur a coché **Escouade → Partager mes quêtes**. Format sortant : `{"type":"questplan","mode":"pve","at":<epoch_ms>,"quests":["<24hex>"],"done":["<24hex>:<24hex>"]}`. Un Worker ne relaie que les identifiants publics et retourne le nom choisi de l'utilisateur et un ID éphémère propre à sa session. Ni coordonnées dans un paquet `questplan`, ni journaux, captures ou identifiants EFT.
- Les plans ne sont **jamais stockés par le Worker**. Le code d'escouade original n'est pas envoyé : la salle utilise la même dérivation SHA256 que RaidPilot. Les membres de la salle peuvent voir les plans des joueurs qui ont opté pour le partage; le code reste une invitation privée, pas une authentification d'identité.
- Maximum 12 connexions par salle, trames bornées à 32 Kio, 80 quêtes/120 étapes par plan, intervalle >=2 s pour `questplan`, contrôle des horodatages, rejet des modes/tâches non valides et espace de noms par Durable Object. Le client préfère le mode manuel tant que `features.questplan: true` n'est pas disponible.

## Avant le déploiement

1. **Sauvegarder le script et la configuration du Worker actuel** dans le compte Cloudflare de son propriétaire. Identifier sa vraie topologie Durable Objects, l'usage des migrations et son éventuel trafic actif. Le code candidat **n'est pas un patch garanti** sur l'infrastructure inconnue.
2. Dans un environnement de développement autorisé, exécuter `node ../source/overlay/Tests/test-quest-relay-worker.cjs` pour valider les règles du protocole.
3. Depuis `raidpilot-updates/relay`, avec une authentification Cloudflare explicitement autorisée, tester dans une instance de **staging séparée** : `npx wrangler deploy --config wrangler-staging.toml`. Ceci ne doit PAS remplacer le Worker déjà utilisé par les joueurs.
4. Exécuter le test `smoke.mjs` adapté au domaine de staging, puis un test bidirectionnel `questplan` entre deux clients et un test de non-fuite entre salles, avant toute migration.
5. Pour déployer la migration sur le Worker de production, adapter sa configuration réelle; prévoir une stratégie de retour arrière et de conservation du protocole `pose`. Ne pas écraser directement un Worker existant dont le code ou les migrations sont inconnus.
6. Vérifier `node raidpilot-updates/relay/probe-quest.mjs` (protocole réel); il doit renvoyer `QUEST_RELAY_SUPPORT=yes`. Seulement ensuite annoncer le partage direct comme opérationnel.

Les tests locaux, la compilation Windows et le stockage de ce candidat sur GitHub **ne remplacent pas** le déploiement Cloudflare ni l'essai de deux PC réels.

Références : https://developers.cloudflare.com/durable-objects/best-practices/websockets/ et https://developers.cloudflare.com/durable-objects/examples/websocket-hibernation-server/
