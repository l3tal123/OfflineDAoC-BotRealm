# True 1.65 fix pass: monster drops (drops agent, 2026-10-31)

Everything here was tested on a copy of the audit snapshot (`fix/drops/test/world_test.db`), applied in the launcher's order (true165-rules settings + remove rules incl. `where_rules.json`, updates incl. `updates.json`, old period drops deleted and the new `drops165.json` added, then every installed `add-*.json` plus `add-dropitems165.json`). Nothing live was touched.

## Files

| file | what |
|---|---|
| `where_rules.json` | 8 `remove.where` rules for true165-rules.json (archive, not delete) |
| `updates.json` | 29 idempotent UPDATEs (kill-task items: Chance -3 -> %) |
| `drops165.json` | full replacement: 1610 monsters, 11626 item rows |
| `add-dropitems165.json` | 359 ItemTemplate rows built from period item pages (Id_nb `p165_drop_<page key>`) |
| `review_decisions.csv` | the 127 REVIEW items, KEEP / REMOVE with the evidence |
| `unfilled.csv` | 5762 lines: loot a source names that could not be given (and why) + period items no source ties to a monster |
| `work/` | caches and per-row evidence: `drops_report.json` (every drops165 row with its sources), `base_decisions.json`, `killtask_report.json`, `built_items.json` (what each built value came from), `coin_only.json`, `test_result.json` |
| scripts | `prep_live.py`, `common.py`, `sources.py`, `analyze_base.py`, `build_drops.py`, `build_outputs.py`, `test_apply.py`, `write_report.py` (run with `python -I`, in that order) |

## Test result

- every statement ran: yes
- loot rows pointing at a missing item after the run: 0 (before: 42); LootOTD rows with a missing item: 0
- Chance < 0 rows left: 0 (before: 247); dead MobXLootTemplate links left: 0 (before: 216)
- second refresh: loot / item content unchanged: yes (LootTemplate and MobXLootTemplate content, every ItemTemplate row, Mob and MerchantItem counts); rows my rules archived: 0; archive growth on run 2 by reason: {"Realm Exchange moves to the traveler's inns": 3} (pre-existing loop, see 'For the coordinator')
- archived by my rules on run 1 / run 2: LootTemplate 266/0, LootTemplate 131/0, LootTemplate 8/0, LootTemplate 15/0, LootTemplate 66/0, LootTemplate 143/0, LootTemplate 41/0, MobXLootTemplate 216/0
- first run: 6682 old period-drop rows replaced; drops165 (rows, new MobXLootTemplate rows, raised DropCounts) = [11626, 115, 206]; second run [11626, 0, 0]
- loot rows by item package after the run: Public_DB 8051, True165Drop 1883, (none) 1228, atlas_orbs_item 684, Atlas_SELL 525, (none) 189, AtlasXPItem 90, True165Vendor 53, drmed 50, dolpubdb 3
- **spawned monsters with coin only (no item at all): 1876** (before the fix: 2065); 591 of them have a capitalised (named) name. List: `work/coin_only.json` (top of the list below).

## A. Removed (where rules)

Items whose every loot row goes (370 items):
- 232: item_actions REMOVE
- 117: REVIEW item without period evidence (see review_decisions.cs
- 13: no source knows the item (no period page, no Uthgard item or
- 7: Atlas boss trophy (package Public_DB, Object_Type 50)
- 1: Atlas boss trophy (package Public_DB, Object_Type 51)

Loot rows removed by monster+item key (208):
- 140: Atlas-era loot row (item package atlas_orbs_item)
- 65: Atlas timed XP-item drop (Chance -3)
- 3: Atlas-era loot row (item package drmed)
- the Chance < 0 rule is a pattern (`Chance < 0` and not one of the 29 kept kill-task rows), so any Atlas timed XP-item row that comes back later is caught too.

Dangling loot rows (item template does not exist): 42 removed (Apocalypse x13, Xanxicar x13, Weno'iak x4 ...); where a period page or Uthgard names the item for that monster, drops165 gives the existing or built item instead.
Dead MobXLootTemplate links: 216 removed (exact MobName|LootTemplateName keys, and only while the link still has no LootTemplate rows).

Kept (not freeshard): 157 Darkness Falls seal (left as is), 280 non-Atlas row, item known, 353 Atlas-package row backed (L1), 55 Atlas-package row backed (L2), 256 Atlas-package row backed (L3). "L1" = period mob page / period item page / Uthgard names this monster; "L2" = live Allakhazam mob page with a pre-Nov-2003 comment; "L3" = period item page found in the monster's zone and drops off monsters, no dropper named (weakest; kept, not proof).

Items no source knows (removed, judgement: also checked for spelling variants with difflib >= 0.93):
2_Green_Dragon_Crystal, Arcanite_Twinkling_Tiara, Band_of_Eternity, Cleaver_of_Eternal_Retribution, Darkened_Vine_Jewel, Emerald Encrusted Ring, Ethereal_Drakescale_Band, Ring of the Syver, Sanguine_Girdle, Sapphire Encrusted Necklace, Shadowed_Blackthorn_Bracer, Shimmering_Finliath_Firebrand, Star_Sapphire_Encrusted_Ring

## B. Kill-task items: Chance -3 -> normal % drop (updates.json)

| monster | item | chance | rate from |
|---|---|---|---|
| drakoran leech | Drakoran Leech Hand | 25 | estimated 25 % (no Uthgard record of the item) |
| Alp Luachra | Alp Luachra Head | 6 | Uthgard measured on alp luachra: 39 in 684 kills |
| Granite Giant | Ancient Granite Stone | 1 | Uthgard measured on granite giant: 23 in 4486 kills |
| Cyclops | Bloody Cyclops Eye | 9 | Uthgard measured on cyclops: 28 in 306 kills |
| Forest Stalker | Blood Red Berry Wine | 5 | Uthgard measured on forest stalker: 58 in 1157 kills |
| Aged Boreal Cockatrice | Cockatrice Feather | 25 | estimated 25 % (no Uthgard record of the item) |
| Ellyll Villager | Bottle of Ellyll Wine | 2 | Uthgard measured on ellyll villager: 4 in 204 kills |
| bucca | Bucca Silver Mirror | 1 | Uthgard measured on bucca: 5 in 453 kills |
| bwgan hunter | Bwgan Hunter Eye | 1 | Uthgard measured on bwgan hunter: 1 in 130 kills |
| Caer Caddug Bodyguard | Caer Caddug Bodyguard Bracer | 25 | estimated 25 % (no Uthgard record of the item) |
| Cliff Crawler | Cliff Crawler Leg | 25 | estimated 25 % (no Uthgard record of the item) |
| cornish hen | Cornish Hen Tail | 25 | estimated 25 % (no Uthgard record of the item) |
| Dark Fire | Crystallized Darkfire | 25 | estimated 25 % (no Uthgard record of the item) |
| Danaoin Fisherman | Danaoin Fishing Fly | 1 | Uthgard measured on danaoin fisherman: 1 in 2273 kills |
| Danaoin Farmer | Danaoin Harvest List | 25 | estimated 25 % (no Uthgard record of the item) |
| Danaoin Clerk | Danaoin Poison | 1 | Uthgard measured on danaoin clerk: 2 in 1561 kills |
| Corpse Mold | Decayed Water Orb | 25 | estimated 25 % (no Uthgard record of the item) |
| Freybug | Decorative Arrows | 5 | Uthgard measured on freybug: 6 in 111 kills |
| Deep Goblin | Deep Goblin Hair | 25 | estimated 25 % (no Uthgard record of the item) |
| Undead Monk | Dried Monk Skull | 1 | Uthgard measured on undead monk: 9 in 1921 kills |
| Dunter | Dunter Head | 4 | Uthgard measured on dunter: 52 in 1355 kills |
| Grave Goblin Shaman | Enchanted Metal Studs | 3 | Uthgard measured on grave goblin shaman: 4 in 147 kills |
| Fiery Fiend | Ever Burning Ember | 4 | Uthgard measured on fiery fiend: 6 in 154 kills |
| Faerie Frog | Faerie Frog Eye | 6 | Uthgard measured on faerie frog: 40 in 678 kills |
| faint grim | Faint Grim Scrap | 25 | estimated 25 % (no Uthgard record of the item) |
| Amadan Touched | Forgotten Silver Jasper Locket | 10 | Uthgard measured on amadan touched: 92 in 886 kills |
| Melancholic Fairy | Melancholic Fairy Wing | 1 | Uthgard measured on melancholic fairy: 1 in 73 kills |
| Decaying Marshman | Rusted Pendant | 25 | estimated 25 % (no Uthgard record of the item) |
| Tylwyth Teg Rover | Unused Arrowhead | 25 | estimated 25 % (no Uthgard record of the item) |

Judgement: "period kill-task item" = the item has a realm-matching period item page or is on the monster's period Known Loot (26 rows), plus 3 kept REVIEW items on a Chance -3 row (Cockatrice Feather, Crystallized Darkfire, Unused Arrowhead: spelling variants of period / Uthgard pages). In 1.65 kill-task items dropped only while the player had the task; there is no task flag in the loot tables, so they become plain % drops as the brief asks. The Uthgard-backed Chance -3 items that are not period (e.g. Vial of Shimmering Essence) lose the timed row and come back only through drops165 when Uthgard measured them on that monster.

## C. The 127 REVIEW items

10 KEEP, 117 REMOVE (all in `review_decisions.csv`). Kept:
- Boreal_Cockatrice_Feather (Cockatrice Feather): period-zam item 5753 'Cockatrice Feathers' (capture 20030810; spelling variant)
- Crystallized_Darkfire (Crystallized Darkfire): period-zam item 5321 'Crystalized Dark Fire' (capture 20031013; spelling variant)
- Dragons_Blood_Boots (Dragon's Blood Boots): period-zam mob 3223 'Gjalpinulva' (capture 20030909) Known Loot 'Dragons Blood Boots'
- Dragonscale (Dragonscale Buckler): live-zam item 11284 'Dragonscale Buckler' comment dated 20030911
- Hauberk_of_Valhalla (Hauberk of Valhalla): period-zam mob 3223 'Gjalpinulva' (capture 20030909) Known Loot 'Hauberk of Valhalla'; live-zam item 10217 'Hauberk of Valhalla' comment dated 20030818
- Ring_of_Arcane_Gestures (Ring of Arcane Gestures): period-zam mob 3195 'Golestandt' (capture 20030803) Known Loot 'Ring of Arcane Gestures'
- Soulbound_Necklace (Soulbound Necklace): period-zam mob 3223 'Gjalpinulva' (capture 20030909) Known Loot 'Soulbound Necklace'
- Staff_of_Ageless_Power (Staff of Ageless Power): period-zam mob 3223 'Gjalpinulva' (capture 20030909) Known Loot 'Staff of Ageless Power'
- Unused_Arrowhead (Unused Arrowhead): Uthgard item page 7417 'Unused Arrowheads' (spelling variant)
- Villskygge_Bracer (Villskygge Bracer): period-zam mob 4687 'Nosdoden' (capture 20030914) Known Loot 'Villskygge Bracer'

The live Allakhazam archive holds item pages with their comments (the separate item_comments file has only 2 pages); a comment is "early" when its posted date is before 2003-11-01. REMOVE = no early comment, no period page, no Uthgard item page or drop, also under spelling variants.

## D. New drops165.json

1610 monsters (1086 regular, 426 named, 98 raid/epic), 11626 rows.
Rows by first source: Uthgard measured (this monster) 8553, period mob Known Loot 2262, live Allakhazam mob page (pre-Nov-2003 comment) + period item page 590, period item page names the monster 214, seal kept from pass 1 7.
Rates not measured on that monster: Uthgard rate of the item on another monster 1160, regular monster magical item 3 % 1028, named monster magical item 15 % 437, raid/epic boss magical item 25 % 399, plain loot 20 % 42 (each row says which in `work/drops_report.json`).

Fixes against pass 1 (2026-10-30):
- pass 1 had 455 rows whose item belongs to a realm whose players never meet that monster (they could never drop, e.g. Albion dyes on Midgard monsters); new file: 0. Uthgard records are used only when their zone is in a realm (classic part) the monster lives in; 32 such records were skipped (e.g. Uthgard Albion zones for a name that only lives in Midgard). Names in several realms (27, e.g. impling, manes demon, tree spirit) get each realm's Uthgard drops with that realm's items; realm-0 items (gems, dyes) from one realm also reach the other realm's spawns of the same name (one shared table per name).
- kept: min kills 20, ROG section and coin bags ignored, nearest-level template, DropCount only raised.
- Darkness Falls currency seals: the 7 Daemon seal rows pass 1 gave DF monsters (demoniac familiar, 3 lieutenants, soultorn grove protector / isen herra, tormented necyomancer) are carried over unchanged; no other seals added.
- Removed-spawn names (the 2026-08-29 conversion removed them; the spawns agent may restore some): their drops are in the file too (130 monsters), so a restored spawn has loot. Script-spawned bosses without Mob rows (Apocalypse and the horsemen, corrupt primals, Tuscaran elders ...): 9 monsters.
- Estimates (only when no Uthgard number exists for the item anywhere): named magical 15 %, raid/epic 25 %, regular magical 3 %, plain loot 20 %; drop_count named 2, raid 4, regular 1 (2 when the rolled chances add up to 50 %+). Raid/epic = dragons, Legion, DF princes/high lords/Adremal/Nahemah, Govannon, epic-dungeon (Caer Sidi, Tuscaran, Galladoria, Summoner's Hall) named, any named of level 62+. Named = the named-mob audit's list (CapnBry case) or a capitalised script name.
- Built items: 359 rows from period item pages with `build_vendoritems165.build` (stats from the page, look from the nearest sibling; price from the page, else the sibling's sell value), Name without Allakhazam's tag, PackageID True165Drop. Gems a one-word name has no sibling for (Sphene, Bloodstone ...) take the look of an existing Realm-0 gem and the price from Uthgard's sell value x 2 (item_sell_ratio 50). 24 pages could not be built (no sibling, no DPS/AF on the page, quest items): see `work/build_fail.json` and `unfilled.csv`.
- Not covered: of the 1864 period items that drop off monsters but dropped from nothing, 1270 now drop from a monster a source names; 594 stay unassigned (listed in unfilled.csv, kind 'period item, no monster named').
- Audit drop_missing findings: 806 monsters with HIGH/MODERATE drop_missing findings: all period Known Loot now drops 581; monster not spawned / unknown 115; part of the period Known Loot now drops 49; no period Known Loot to compare (finding rests on item pages / Uthgard) 37; none of the period Known Loot drops 14; no period Known Loot and still no items 10

Darkness Falls seals (not changed, evidence for the separate decision): apprentice necyomancer has Daemon Emerald Seal 50 % x2 on the server, but two period comments on its page (period-zam mob 2821) say "they dont drop seals" (2002-09-25) and "i did not get any seals from them" (2002-07-31); Uthgard measured Emerald Seal 29.9 % of 174 kills. Molochian tempter has Daemon Sapphire Seal 40 % x3; period comment 2003-06-16 (period-zam mob 2832): "every time they dropped a seal it was emerald. So the lower ones drop emerald seals", 2002-08-01: sapphire from the purple-con ones; Uthgard: Sapphire 41 % of 1,994 kills, no emerald. (audit dungeons-116, dungeons-225)

## Overrides of the audit grades

- 29 items the audit graded REMOVE/REVIEW are on a realm-matching period mob page's Known Loot (a period source the item grader did not use), so they stay and drop where that page says: 2_Tuscarian_Discordant_Hauberk, 3_Black_Velvet-lined_Armguards, 3_Blue_Suede_Boots, 3_Destroyer_of_Dreams, 3_Jerkin_of_the_Ghostly_Soul, Anurigundas_Belt, Anurigundas_Choker, Aroons_Ancient_Staff, Bladed_Crystal_Rapier, Bloody_Moonstone-studded_Axe, Cloak_of_Elemental_Buffering, Dragons_Blood_Boots, Easmarachs_Stinger, Fiery_Geomancer's_Robe, Gloom_Warder's_Spear, Hauberk_of_Valhalla, Lightless_Staff_of_Magical_Wrath, Primal_Great_Sword_of_Fire, Primal_Ring_of_Air, Primal_Ring_of_Earth, Protector_of_Weary_Souls, Ring_of_Arcane_Gestures, Ring_of_Elements, Snow_Diamond, Soulbound_Necklace, Staff_of_Ageless_Power, Tuscarian_Bloodforged_Hauberk, Villskygge_Bracer, Writhing_Blackthorn_Sleeves.
- 3 such period-loot items stay removed: their template carries post-1.65 bonus types and they are outside the stats agent's scope (fix/stats/items.json), so nothing would fix them: Andskygge_Bracer (post-1.65 bonus types [156]); Dragonstorm (post-1.65 bonus types [164, 194, 201]); Vest_of_the_Pack (post-1.65 bonus types [209]). If the stats agent takes them in, drop them from where rule 0/1 and rerun build_drops.py.
- 6 items the audit left on REMOVE with the evidence 'Uthgard-only' are kept (owner ruling: Uthgard is not freeshard; Uthgard measured them on the Darkness Falls bosses): Adremel`s Flawless Daemon Fire Spear, Kobil`s Flawless Fiery Gauntlets, Kobil`s Flawless Fiery Seal, Legions_Flawless_Soul_Crusher, Legions_Flawless_Soul_Splitter, Shimmering_Adremels_Flawless_Avernal_Impaler.
- A REVIEW item with an early live comment but post-1.65 bonus types outside the stats scope is REMOVE (Dragonfury).

## For the coordinator

- Pre-existing loop (not my files): every refresh archives 3 Mob rows (Brynhild, Eilwen, Adalyn) with the mob_names rule 'Realm Exchange moves to the traveler's inns' and add-inns165.json inserts them again; the only change a second refresh makes.
- Option not applied (the brief counts live pages only with a pre-Nov-2003 comment): 48 coin-only monsters would get 75 rows if a live Allakhazam mob page WITHOUT an early comment counted when a period item page puts the same item in that zone as a monster drop.
- drops165 only points at templates, so its rows get the stats agent's fixes; the built items come from period pages and carry only 1.65 bonus types (map_bonuses maps nothing later).

## Judgement calls

- Where rules use exact lower-case keys (item ids, monster|item pairs, mob|template pairs) so they keep matching after a rebuild from the normal database and cannot hit the new drops165 / add rows (the builder never emits a removed item or pair). The Chance < 0 rule is a pattern on purpose.
- Atlas-package rows (atlas_orbs_item / drmed) are removed only when no source ties the item to that monster; the 256 L3 rows (period page found in the zone, no dropper named) are kept - removing them would empty many SI epic and classic named tables.
- Public_DB / unpackaged base rows of items a source knows stay as they are (their chances are the OpenDAoC values, e.g. 99 % rows on SI epic bosses); only freeshard rows were in scope.
- Period Known Loot matching: name (+plural/singular), realm, habitat zone must be one of the spawn zones when the page names a known zone; Darkness Falls / Summoner's Hall / battleground pages match on name only (their page realm is arbitrary). A tag like "(Alb)" on a Known Loot line picks that realm's item; several templates of one name: the one whose type/AF/DPS/bonuses match the period page, then one already used in loot, then the nearest level.
- Live Allakhazam mob pages count only with a comment dated before 1 Nov 2003, and only for items that have a period item page saying they drop off monsters (the live loot lists include ToA "(nld)" items).
- Item spelling variants (difflib >= 0.96 with the same last word, or >= 0.93 with matching type/AF/DPS) count as the same item (e.g. Luminescent Exergum / Exeregum Stone, Legend Slayer's / Slayer Quiver).
- Gjalpinulva gets both respec stones at 100 % like the other two dragons (her period page lists both; the rate is the Uthgard rate measured on Golestandt/Cuuldurach, the same rows they have).
- Classic quest items (a cq_ template of the name) and kill-task pages are not built as general loot.
- Old pass-1 DropCount raises stay (the launcher only raises DropCount); where a pass-1 table was wrong the extra cap only allows more of the (now correct) rolled items.
- Uthgard drops whose item has no template and no period page (small/large chest, some copper coins, cat's eye apatite, food...) are not invented; they are listed (unfilled.csv, kind Uthgard drop).

## Coin-only spawned monsters (first 60 by spawn count)

| monster | spawns | level | zones |
|---|---|---|---|
| giant cicada | 238 | 44.3 | Delling Crater, Faraheim |
| ancient zombie | 216 | 57.7 | Caldey, Dales of Devwy, Gwyddneau |
| mygga | 181 | 5.6 | Aegir's Landing, Munin Sound |
| vanvettig djur | 171 | 44.3 | Faraheim, Munin Sound |
| morvalt larling | 157 | 53.0 | Trollheim |
| skadedjur | 157 | 42.0 | Modernagrav |
| crater cicada | 144 | 32.7 | Delling Crater |
| faraheim doe | 142 | 50.1 | Faraheim |
| morvalt skugga | 137 | 55.8 | Trollheim |
| white wolf | 130 | 29.9 | Faraheim, Jamtland Mountains, Modernagrav, Odin's Gate |
| drakoran guardsman | 124 | 53.2 | Avalon City, Dales of Devwy |
| morvalt krigsman | 116 | 19.4 | Aegir's Landing |
| orc finguard | 113 | 53.0 | Aldland |
| redcap bestigar | 112 | 54.5 | Modernagrav |
| giant spider | 110 | 1.6 | Black Mtns. North, Black Mtns. South, Campacorentin Forest |
| oaken treant | 110 | 48.7 | Allta Fearann |
| rock crab | 110 | 9.4 | East Svealand, Gotar, West Svealand |
| sea dauber | 109 | 31.4 | Munin Sound |
| animus giant | 108 | 61.8 | Iarnwood |
| faraheim fawn | 108 | 44.3 | Faraheim |
| phantom magi | 108 | 46.2 | Caldey, Dales of Devwy, Gwyddneau |
| undead apple harvester | 108 | 31.5 | Avalon Isle |
| washed-up skeleton | 108 | 40.3 | Munin Sound |
| white light | 108 | 53.5 | Aldland, Caldey, Dales of Devwy |
| tangle weed | 105 | 1.3 | Domnann |
| morvalt framkalla | 104 | 49.4 | Gripklosa Mountains, Modernagrav |
| shale golem | 103 | 23.6 | Munin Sound |
| urchin hooligan | 103 | 12.1 | Isle of Glass |
| wild redcap | 102 | 52.8 | Iarnwood |
| water beetle | 101 | 6.9 | Connacht, Lough Derg, Shannon Estuary, Silvermine Mts. |
| biting insect | 99 | 11.3 | Aegir's Landing, Munin Sound |
| arboreal spirit | 97 | 38.2 | Allta Fearann |
| chiseler | 95 | 22.4 | Faraheim, Uppland, West Svealand, Yggdra Forest |
| fisher grip | 95 | 38.7 | Munin Sound |
| frukta warg | 93 | 39.2 | Varulvhamn |
| griffon frostwing | 92 | 49.2 | Delling Crater |
| mammoth calf | 92 | 10.4 | Aegir's Landing, Delling Crater, Gripklosa Mountains |
| botonid disperser | 90 | 51.2 | Cothrom Gorge |
| snake | 89 | 2.3 | Black Mtns. South, Camelot Hills, Campacorentin Forest |
| fengundar | 88 | 45.2 | Iarnwood |
| pulverizer | 88 | 59.5 | Iarnwood |
| villainous youth | 87 | 4.3 | Connacht, Lough Derg, Shannon Estuary, Silvermine Mts. |
| etvarg dominator | 86 | 51.7 | Iarnwood |
| ingester | 86 | 33.4 | Munin Sound |
| rowdy | 86 | 6.3 | Connacht, Lough Derg, Shannon Estuary, Silvermine Mts. |
| cicada-killer | 85 | 54.4 | Faraheim |
| woolly mammoth | 85 | 34.6 | Delling Crater, Gripklosa Mountains |
| cniogcrag infester | 84 | 18.3 | Domnann |
| botonid rencanraider | 83 | 48.2 | World's End |
| faraheim stag | 83 | 51.2 | Faraheim |
| drowning victim | 82 | 25.1 | Munin Sound |
| eirebug | 81 | 6.1 | Connacht, Lough Derg, Silvermine Mts. |
| erfaren soldat | 81 | 52.6 | Trollheim |
| sjoalf worshipper | 81 | 38.9 | Munin Sound |
| tidal grip | 81 | 30.9 | Munin Sound |
| water beetle collector | 81 | 5.2 | Connacht, Lough Derg, Shannon Estuary, Silvermine Mts. |
| cutthroat vines | 80 | 39.1 | Vigilant Rock |
| pitch skeleton | 79 | 51.6 | Caldey, Dales of Devwy, Gwyddneau |
| sea hound | 79 | 33.2 | Aldland, Avalon Isle |
| hungry shriller | 78 | 12.4 | Caillte Garran, Domnann |

(1876 in all; most are monsters no source gives loot for: Uthgard never measured them in that realm and the period page lists none.)

## Unfilled (summary)

- Uthgard drop: 5046
- period item, no monster named: 594
- period loot of the monster: 80
- old loot row with no item template (removed): 42
- most frequent Uthgard items without a template or period page: small chest (584), large chest (552), cat's eye apatite (107), green sapphire (82), cat's eye tourmaline (49), obsidian (48), zircon (47), some copper coins (43), heliodor (41), pink sapphire (38), red spinel (35), yellow diamond (33), spinel (27), pearl (21), dried pork (21)
