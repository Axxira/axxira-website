# DungeonMapPilot 0.5.3 — Route Guide Coverage and Limits

## The 25 illustrated Classic maps

These are reference maps using Blizzard client file IDs. Newly authored boss notes are **text only**, except the two older approximate-art marked walkthroughs. No corridor-by-corridor accuracy or player-position tracking is claimed.

| Map ID | Dungeon or raid | Wing guides | Encounter steps |
|---:|---|---:|---:|
| 33 | Shadowfang Keep | 1 | 8 |
| 34 | The Stockade | 1 | 5 |
| 36 | Deadmines | 1 | 8 |
| 43 | Wailing Caverns | 1 | 9 |
| 47 | Razorfen Kraul | 1 | 6 |
| 48 | Blackfathom Deeps | 1 | 7 |
| 70 | Uldaman | 1 | 8 |
| 90 | Gnomeregan | 1 | 5 |
| 109 | Sunken Temple | 1 | 8 |
| 129 | Razorfen Downs | 1 | 6 |
| 189 | Scarlet Monastery | 4 | 8 |
| 209 | Zul'Farrak | 1 | 8 |
| 229 | Blackrock Spire | 2 | 14 |
| 230 | Blackrock Depths | 1 | 21 |
| 249 | Onyxia | 1 | 1 |
| 289 | Scholomance | 1 | 14 |
| 309 | Zul'Gurub | 1 | 10 |
| 329 | Stratholme | 2 | 17 |
| 349 | Maraudon | 1 | 8 |
| 389 | Ragefire Chasm | 1 | 4 |
| 409 | Molten Core | 1 | 10 |
| 429 | Dire Maul | 3 | 19 |
| 469 | Blackwing Lair | 1 | 8 |
| 509 | AQ20 | 1 | 6 |
| 531 | AQ40 | 1 | 9 |

**25 illustrated maps / 63 reference floors / 32 distinct Classic route variants.** The original Wailing Caverns and Ragefire Chasm route identifiers, prerequisite logic and approximately anchored pins are preserved. All other route steps have no invented x/y coordinates.

## Nine additional WoW Forever text-only entries

Instance IDs are candidate IDs from public community/client references and must match the live instance name before automatic use. Their artwork, current playable availability and physical routes are not assumed.

| Candidate ID | Dungeon | Scope | Named bosses |
|---:|---|---|---:|
| 3065 | The Hall of Thanes | Documented encounter roster; live route not verified | 4 |
| 2999 | Ruins of Lordaeron | Documented encounter roster; live route not verified | 7 |
| 2998 | Excavation Site: Wetlands | Documented encounter roster; live route not verified | 4 |
| 2959 | City of Dalaran | Documented encounter roster; live route not verified | 9 |
| 2970 | The Drowned City | Boss list recorded in client 1.60.1.70888; navigation unverified | 6 |
| 3039 | Krol'dok Stronghold | Boss list recorded in client 1.60.1.70888; navigation unverified | 5 |
| 2994 | Alcaz Prison | Boss list recorded in client 1.60.1.70888; navigation unverified | 5 |
| 2974 | Blackmaw Hold | Boss list recorded in client 1.60.1.70888; navigation unverified | 7 |
| 3001 | Shaper's Terrace | Boss list recorded in client 1.60.1.70888; navigation unverified | 11 |

All nine new Forever dungeons now have client-documented boss rosters: 24 names from earlier beta data and **34 additional names** from build 1.60.1.70888, a total of **58 boss entries**. The five updated rosters are *not verified walking or kill routes*. Manual checklist confirmation and undo are available, but players must verify actual encounter availability and boss order in-game. No map art or coordinates have been fabricated.

## Player controls and honesty

- Open with /dmp, then use Maps. Select a wing for Scarlet Monastery, Blackrock Spire, Stratholme or Dire Maul.
- Preparation, named encounters, optional detours, manual step completion, optional Skip, Undo, and confirmed Restart. Progress is separate by character and wing.
- Scroll zoom, drag pan, floor selection and editable personal markers remain on illustrated reference maps. Static maps do not claim a reliable player GPS.
- Native map artwork, when available, shows the text guide without projecting reference-only map pins to unsuitable coordinates.
- For guide-only entries, an explicit missing-art notice explains that no illustration has been supplied.
- Full individual encounter notes are authored in English and French. Common warnings and catalog labels are in 11 supported locale selections (plus auto); other language selections fall back to English for detailed boss text.
- No automatic player movement, boss defeat marking, currency use, inventory actions or game purchases.

## Sources and rights

- Classic map file IDs adapted from OpenMap under its MIT license. See LICENSE-OpenMap.txt. Blizzard textures are loaded from the player's client and not redistributed.
- Classic boss rosters and tactics cross-checked with https://undying.app/dungeons , https://www.wowhead.com/classic/guides/dungeons and https://wowcalc.net/en/classic/guides/dungeons .
- WoW Forever dungeon roster and preliminary client IDs: https://wowforeverinfo.com/guides/dungeons/ and https://github.com/xIGBClutchIx/ForeverPlusPlus/blob/main/docs/forever-api.md .
- Five additional boss rosters require live validation of encounter order, access and mechanics. Dungeon routes are suggested preparation orders, not verified walkable paths.

## Testing status

All references and steps have simulated Lua client coverage for route data integrity, per-wing progress, undo, personal markers, localization fallback, roster-only manual completion and zero false map markers. Real in-game boss, quest and entrance verification remains necessary; this is a local alpha, not a public stable release.


## Update November 13, 2026: client encounter rosters

Data refreshed from build 1.60.1.70888 and cross-checked with 1.60.1.70938 community reports. The updated five Forever dungeons contribute **34 named encounters**, not new route pins. Community data disagrees on some names (e.g. Drowned City Deathless Guardians vs Deathless Marrow) and the Krol'dok party-size field. The guide flags such uncertainty instead of choosing an unverified claim.

Current file sources: DATA-SOURCES.md. The new Maps menu offers **À mon niveau / At my level** without suppressing any of the 34 entries.


## In-addon boss search (0.5.3)

Find boss / Chercher un boss searches known encounter names and both English and French dungeon names. The result panel is a read-only inspection of the step. It intentionally does **not** modify the saved route cursor, completed encounters, skip markers or undo history; the player can select a step manually to resume their chosen guide. Result count is capped for UI responsiveness and input is bounded.


## Personal notes and unfinished-step navigation, v0.5.3

Per-character local notes are keyed to a stable route ID and a boss step ID. Reading a boss's note does not advance or mark completion. Notes are limited to 200 bytes and 300 notes per character; the last 12 edits can be undone if not overwritten. Rich-text color and item-link sequences are blocked.

Resume selects the first uncompleted/unskipped step with valid prerequisites after an explicit button click. The existing per-wing progress, optional skips and manual undo retain their own state.
