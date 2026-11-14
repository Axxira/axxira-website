# Research notes and provenance — DungeonMapPilot 0.5.1-alpha

Data checked: 13 November 2026 (EST). This file records reference scopes, not a certification that an in-game encounter or physical route has been walked.

## Official geography and level brackets
- https://worldofwarcraft.blizzard.com/en-us/news/24301455/explore-the-new-dungeons-of-world-of-warcraft-forever
- Nine new five-player dungeons and published level bands. These are recommendations, not hard entrance requirements.

## Client-recorded boss lists (not verified boss kill order)
- https://forevermastery.com/en/guides/wow-forever-dungeons — updated Nov 12, lists bosses of all nine, client DungeonEncounter build 1.60.1.70888 for the final five.
- https://metammo.com/wow-forever/dungeons/the-drowned-city — build 1.60.1.70938, lists six encounter names.
- https://metammo.com/wow-forever/dungeons/kroldok-stronghold — build 1.60.1.70888, lists five encounter names.
- https://forevermastery.com/en/dungeons/shapers-terrace — 11 encounter names, English/French source labels.
- https://wowforevertalents.com/dungeons/krol-dok-stronghold/ — client reports ten players, while Blizzard's introductory article says five.
- https://warcraft.wiki.gg/wiki/Drowned_City — community roster includes Deathless Marrow; the client list in this addon's source says Deathless Guardians.
- https://endgametools.com/en/wow-forever/dungeons/shapers-terrace — community DungeonJournal run order and boss count differ from the 11 client encounter indices.

The addon intentionally uses client encounter index **for display only**. It makes **no assertions** about the floor or corridor of a boss, its exact appearance order, whether its loot drops, or the required party size where sources disagree. User manually chooses which entry to read and confirms progress.

## Access and optional encounter context
- https://forevermastery.com/en/guides/wow-forever-dungeons : Dalaran Alliance/Horde access quests, Shadowfang Black Tome, optional Lyn the Ignored.
- https://www.wowhead.com/classic/fr/quest=477/frontières : Ambermill's French place name is Moulin-de-l'Ambre, distinct from Bois-du-Bûcher (Pyrewood Village).

## Other retained sources
- OpenMap MIT file ID tables: LICENSE-OpenMap.txt. No Blender, font or Blizzard art files are bundled.
- Previous guide descriptions and floor catalog in GUIDES-STATUS.md.

Validation limitations: bosses, dungeon entry availability, NPC spell mechanics, movement paths, world-map GPS and high-density UI layouts still require live gameplay tests. No game server access, purchases or automatic progress is included.
