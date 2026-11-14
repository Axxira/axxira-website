# DungeonMapPilot 0.5.3-alpha — dungeon guides for every illustrated map

FR: Guide pour les 25 cartes classiques de donjons et raids de la bibliothèque, avec 32 variantes selon les ailes. Neuf entrées Forever supplémentaires sans illustration : neuf listes de boss, dont cinq ajoutées à partir de données client récentes (itinéraires non vérifiés). Toutes les entrées ont des étapes ou des listes de boss. Pour les cinq mises à jour récentes, l’ordre du client ne constitue pas un itinéraire vérifié.

EN: Guides for all 25 illustrated Classic dungeon/raid maps, with 32 wing-aware variants. Nine additional WoW Forever text-only entries: nine encounter rosters, including five new client listings (physical routes unverified).

## Usage / Utilisation

- Open with /dmp. Choose a dungeon via Maps, then select the wing if available.
- Read each preparation step and encounter note. Only the player's confirmation marks an objective done.
- Optional steps may be skipped. Undo and a confirmation before restarting remain available.
- Scroll and drag to zoom and pan Blizzard map art. Select floors. Shift-click creates personal pins.
- Text-only entries have no fabricated boss locations or false player coordinates.
- Five previously preview-only entries now show 34 client-listed encounters. They permit manual checklist progress, but the journal order is NOT verified walking order; verify in the game.
- The two original Wailing Caverns and Ragefire Chasm reference-art pins remain approximate.
- Detailed route prose is authored in English and French, with UI warning labels localized elsewhere and English fallback on other languages.

For full per-instance list and sources: GUIDES-STATUS.md.

## Data and compatibility

Target client: WoW Forever interface 16001. SavedVariables remain per character. Route progress for individual wings is independent. Personal marker mode remains opt-in and available on illustrated maps. No automated gameplay actions.

Blizzard map art is referenced from the game client, never redistributed. Map tile file IDs follow OpenMap MIT; license: LICENSE-OpenMap.txt.
This is a locally staged alpha, not publicly released. Automated tests do not certify live traversal or the exact availability of Forever-specific dungeons.


## 0.5.3-alpha — Added November 13 client rosters and level discovery

The five formerly informational-only Forever entries now display 34 boss names recorded in the client around build 1.60.1.70888. These rosters may disagree with community reports and their *index order is not a walking or kill order*. No coordinates, art, loot guarantees or boss mechanics are invented. Every completion remains manual and can be undone. More precise source notes: DATA-SOURCES.md.

A new **At my level / À mon niveau** entry in the Maps dropdown shows dungeons whose published level ranges contain the player's current level; the original full map list remains one click away. An unavailable or protected level falls back safely to all maps. Raids are excluded from this leveling shortcut but remain accessible in the full catalog.

Dalaran's preparation text now cites faction access quests and the optional Tome of Dalaran step; the associated Black Tome reminder appears in the Shadowfang Keep guide. These quest details should be verified on the player's current server.


## Boss Finder / Recherche de boss — 0.5.3-alpha

The **Find boss** button is directly in the map toolbar. Enter at least two characters from a boss or dungeon name and select a result to inspect its guide. Results include up to 24 matches; refine your search if more exist. The guide switches to the matching dungeon/wing, showing the boss description with an explicit **read-only preview** warning. No boss is marked defeated, skipped or completed by a search. Click a step in the guide to resume manual progress.

**FR :** Le bouton **Chercher un boss** apparaît dans la barre des cartes. Tapez au moins deux caractères du nom d’un boss ou d’un donjon. Les résultats conduisent vers la bonne carte et la bonne aile, puis affichent les informations en consultation seule. **La recherche ne valide aucun boss.** Cliquez sur une étape pour reprendre le suivi manuel. La recherche est disponible dans les douze choix de langue d’interface, avec noms de boss conservés tels que documentés dans le client.

Names and limited search indices are local data. No web requests, tracking, inventory changes or gameplay inputs are added. Search does not promise a real path or boss location for uncalibrated maps.


## 0.5.3-alpha — Personal boss notebook and Resume

**Notes / Carnet :** a Notes button is visible directly on the route sidebar. Click it to add a **personal note of up to 200 bytes** for the selected encounter (or the current read-only boss inspection). Notes are saved separately **per character, dungeon wing and boss step**, survive reload, and do not change the guide's completed/skipped bosses. You can save, clear and undo the latest note edit when the value has not changed again. The Notes button displays a star when a note exists, and hovering displays its content. Notes cannot be edited during combat and never include automatic chat, network requests, purchases or game actions. WoW color/link control sequences are rejected.

**Resume / Reprendre :** click the button beside the route selection to locate the **first unfinished step with its prerequisites satisfied**. This changes the guide's current selection only at the player's explicit request; it does **not** complete, skip, kill or automatically move to any boss. Finished guides and announced preview-only routes do not offer this action.

**FR :** le bouton **Notes** dans le panneau Guide permet d'enregistrer une courte consigne personnelle pour chaque boss. Les notes sont locales, par personnage et par aile, indépendantes des boss validés. Le bouton **Reprendre** sélectionne la première étape accessible non terminée, sans valider de combat. Les modifications de notes demandent un clic explicite et sont bloquées pendant le combat.

The 25 illustrated reference maps, 63 floors, 34 total catalog records, 41 guides, route progress history, player markers and 12 interface language selections remain unchanged.
