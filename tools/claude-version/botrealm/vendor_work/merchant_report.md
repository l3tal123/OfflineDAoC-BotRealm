# Merchant matching + stock proposal (True 1.65), 2026-10-31

Input: 9,696 period Allakhazam item pages (crawl re-extracted during this pass). World: world165_snapshot.db (read-only). Nothing in any database was changed.

## Numbers
- Pages with a "purchased from Merchants" block: **2,859**. 2,378 name a merchant. 290 give only a location ("Found in": housing towns, Darkness Falls, Isle of Glass). 191 give nothing (the block is followed straight by "Send a picture").
- Distinct period merchants (name + realm): **274**
  - **exact** name match: 240. Kendris (Gwyddneau weapon merchant) is one of these but has **no merchant list**.
  - **spelling** match: 11. Nine are confirmed by stock overlap: Billy Faulklier, Natana Alcael, Alastar MacDonnell, Jiskarr de'Mordan, Ella, Falla Hemingr, Ozur, Torrad Gunderson, Uli Johannsson. Two are **unconfirmed** and were not used: "Dyemaster Marna" → "Marna", whose craft list holds none of her enamels, and "Ylaine Barret" → "Ylaine Barrett".
  - **ambiguous**: 0. Takker has 3 NPCs on 2 lists. I picked the Vale of Mularn one because its zone is the one in the period Found-in.
  - **none**: 23 (broken down below).
  - Unambiguous merchant → list matches used for additions: **248**
- Stock additions (merchant_stock_additions.json): **795**. high 671, medium 59, low 65.
  - action `add_row`: 600. action `fix_dead_row`: 195.
  - 64 of them use a planned item id `p165_item_<page_key>`, because no template exists for that realm.
  - 35 `add_row` items have **no free slot**.
  - They touch 78 lists. 43 of those lists are shared with other NPCs.
- Already on the list: 3,795 (page, merchant) pairs.
- On the list under another name, with the page's numbers equal (merchant_present_other_name.json): **166**. These were not added.

## The 23 merchants not matched
- **Truly missing (no NPC of the name anywhere, including the removed-mob tables): 11.**
  - Albion: Eileen Morton (instruments, Camelot Hills).
  - Hibernia:
    - Caryan (Doddering Weewere Hunter's weapons, Tir na Nog)
    - Madarl and Nealcail (cloth dyes and cloaks, Tir na Nog)
    - Melanaha (wreaths and hats, Lough Derg)
    - Rhodyn (Hib instruments, Lough Derg / Tir na Nog)
    - Somyr (second-hand Cruanach armour, Tir na Nog)
    - Tempeste (Hib bows, Lough Derg / Tir na Nog)
  - Midgard:
    - Borjal (shields, Vale of Mularn / Myrkwood / Gripklosa)
    - Royd (totems, Jordheim)
    - Vinandr (circlets and hats, Mularn / Gripklosa)
  - Near names exist (Rhosyn, Melna, Rory). I did not use them because none is close enough.
- **NPC exists but is not a merchant (no list): 6.**
  - Brach Leof (92 pages of alchemy/spellcraft goods), Gremain Watford (43) and Shayly Parke (14): Camelot City, empty Guild.
  - Chelseigh Stillman (58): the world NPC is spelled "Chelseigh Stilman", Camelot, empty Guild.
  - Caley (5): Tir na Nog.
  - Eluned (17, Hib robes): an Armor Merchant in Lough Derg with no list.
  - These are the biggest gaps. Each needs a list plus a GameMerchant class, which is a decision for the owner or coordinator.
- **Realm conflict: 4.** Dala/Alb, Darbo/Alb, Debo/Hib and Eluned/Alb. The page's realm disagrees with the only same-name merchant, so this is most likely an Allakhazam listing error. Skipped.
- **Not merchants: 2.** Howling Knifeman and Howling Maiden are monsters that Allakhazam listed in the merchant block of 2 pages.

## Town-name entries
The 13 "town" names (Caerwent, Dalton, Old Sarum, Rilan, Dunshire, Kilcullen, Meath, Torrylin, Arothi, Carlingford, Eirikstaad, Wyndham, Darkness Falls) are **not merchants**.
- They are the page's *Found in:* list, which sits right after an empty merchant block. The old extractor's `merchants` field swallowed it.
- 120 pages list all 12 housing towns. These are housing items: signposts, targets, fountains and furniture.
- 25 (Alb), 16 (Hib) and 13 (Mid) pages list one realm's 4 towns. These are realm paintings, maps and outhouses.
- In our server these items are sold by the **code-loaded housing lists** (HouseTemplateMgr.cs: `housing_indoor_shop`, `housing_outdoor_shop`, the `housing_*_npc` lists and others). No Mob uses these lists.
- The realm Decorator NPC lists (Albion/Midgard/Hibernia Decorator) are **all dead rows** (27 of 27 each).
- Details are in merchant_location_entries.json. I made no additions for these because no merchant is named.
- 88 pages say only "Darkness Falls" (seal items, no merchant named). Also not added.

## Slot rule (checked in data and code)
- `MerchantTradeItems.MAX_PAGES_IN_TRADEWINDOWS = 5` and `MAX_ITEM_IN_TRADEWINDOWS = 30`. So a list holds pages **0-4**, slots **0-29**, at most 150 items.
- No (list, page, slot) is used twice. Slots are filled from 0 up and can have gaps.
- Two lists use slots above 29, which the client never shows: `Mythirians` (up to 37) and `105879b1-…` (up to 31).
- ItemListID, ItemTemplateID and Id_nb are all `COLLATE NOCASE`. Every comparison must ignore case. My first pass did not, and it under-counted overlaps.
- Each `add_row` record carries `suggested_page`/`suggested_slot`: the first free (page, slot), handed out per list with high confidence first. A later step can use these directly.

## How the two special cases were handled
- **fix_dead_row (195):** the list already has a row for the item, but that row's ItemTemplateID has no ItemTemplate (field `dead_row_template_id`, e.g. `rawhide_starklaedar_jerkin`). The proposal is to point that row at the existing template `item_template_id` (often the realm "…2" variant, e.g. `rawhide_starklaedar_jerkin2`), or to create the missing id. Do not add a second row. These records have no suggested slot.
- **no free slot (35):** `MidAlchemy` (Holsvi Hallgrim; 119 rows + 52 proposed) and `MidWeaponMerchant` (Signy; 104 rows + 60 proposed). They carry "LIST FULL" in `why` and `suggested_slot: null`.
  - Signy's list holds **none** of her 60 period items: moon claws instead of Midgard swords and axes.
  - The better fix is a separate list for Signy rather than appending. That is the owner's call.

## Dead-template finding (merchant_dead_rows.json)
- **2,975 MerchantItem rows in 184 lists** point at 1,194 distinct ItemTemplate ids that do not exist, even comparing without case. None of them were removed by True 1.65 (none are in offline_true165_removed). They come from the old OpenDAoC data.
- **853 of these rows sit in 69 lists that NPCs actually use.** Those items are silently missing from the shops.
- The ids name the items (`<material>_<item>`). Most common kinds:
  - weapons: spears 218, hammers 179, bows 137, daggers 107, throwing axes, hammers and knives 93 each
  - leather armour: jerkin, leggings, sleeves and gloves ~130 each
  - shields 118, catalysts 78, "lining" 62, hull paints 34
- Worst lists in use:
  - `MidLeatherMerchant` (Aage, Lyyli): 75 of 90 dead, all mjuklaedar.
  - `77793002` (Seva): 40 of 123 dead.
  - `HibFullWeaponMerchant` (Cassaldra): 38 of 145 dead.
  - The three realm **Decorator** lists: 27 of 27 dead (housing paintings, arches).
  - `MidLeatherSvarlaedar`, `MidLeatherStarklaedar` and `cb8f70b0…` (Dritsa): 25 of 30 dead.
  - About 15 Hibernian weapon lists: 20 dead each, all `cobalt_*` (dagger, dirk, rapier, spears).
  - Midgard throwing and hammer lists: 14-20 dead each (`*_weighted_throwing_*`, `*_battle/pick/small_hammer`).
  - The full table is in the file.

## Suspicious lists
- **Darkness Falls seal merchants:** all 17 matched lists hold **0** of their period items. They sell "Superior/Exquisite/Exceptional X" where the period pages say "X".
  - 127 of these prefixed items have exactly the period item's DPS, speed and seal price (same item, later name). They are in merchant_present_other_name.json.
  - Others differ slightly, e.g. period Accursed Demon Hammer DPS 13.1 vs Superior 14.1, and Corrupted Soultorn Cap AF 35 vs 34. Those were proposed as planned `p165_item_*` items.
  - Prefixed items whose page gives no numbers were added as **low** confidence.
- **Name-only differences confirmed by stats** (Allakhazam vs our DB): "Starkalaedar" vs "starklaedar", "Haurberk" vs "hauberk", "Cymric Jerkin" vs "cymric leather jerkin". These were not added; a rename may be wanted.
  - The Siluric and Cymric leather pages show AF 2 points off our templates, so those stayed **low**.
- **Lists that hold none of the merchant's period items:** Signy (`MidWeaponMerchant`), Hakan (`MidStuddedMerchant`, 90 rows), Warren Gibson (`AlbLeatherSiluric`) and Fiora/Morgen (`MidLeatherStarklaedar`, mostly dead rows). The list may be the wrong one for that NPC.

## Files (work folder)
- merchant_matching.json
- merchant_stock_additions.json
- merchant_present_other_name.json
- merchant_location_entries.json
- merchant_dead_rows.json
- merchant_stats.json
- Scripts:
  - merchant_parse.py
  - merchant_match.py
  - merchant_report_data.py

To refresh after the crawl finishes, run `python -I extract_pages.py`, then `merchant_parse.py`, then `merchant_match.py`, then `merchant_report_data.py`.

## Pass 2 (2026-10-31)
**A. Refreshed pages (9,874):** 2,889 merchant blocks (2,408 name a merchant); merchant counts unchanged (274: 240 exact, 11 spelling, 23 none). merchant_stock_additions.json now **805** (high 671, medium 68, low 66), same format and ids, plus a field `new_in_pass2`. **Only 10 are new** (9 `fix_dead_row` medium: Oaken block/heater/round shields on `MidShields` (Aric) and `7c48f0da…` (Ime); 1 low `add_row`: Embossed Large Blocking Shield on MidShields). No pass-1 record dropped. Pass-1 copies kept: merchant_stock_additions_pass1.json, merchant_matching_pass1.json. Note: the snapshot DB is still the pre-integration one, so pass-1 rows show as "missing" again; use `new_in_pass2` to pick only the new ones.

**B. DF seal merchants (dfseal_swaps.json):** 27 seal NPCs, one list each, 749 rows.
- **170 swaps** (old later-named row -> period item): 166 high, 1 high but flagged `pass1_added_row` (that period item was already added as a new row in pass 1: skip the swap or drop the pass-1 row), 1 medium (Tulo: two existing counterparts), **2 low = currency mismatch** (Wovo, Emerald merchant: Corrupted Avernal Gloves priced in Sapphire on its page; Horto, Diamond merchant: Infernal Malison Rapier priced in Emerald). 167 go to built `p165_item_*`, 3 to existing plain-named templates (Tulo: Accursed Avernal Great Sword / Bladeblocker / Protector). `currency_ok` is true for 167.
- **579 rows with no period counterpart** (`no_period_counterpart`): **92** have a period page with the plain name but no item was built (almost all are "Found in: Darkness Falls" pages with no merchant named, which the build skipped; e.g. Tainted Abysmal Gloves page 6949) -> buildable next; **487** have no period page with the plain name at all -> possibly post-1.65 items or not crawled. Nothing proposed for removal.
- No dead rows and no plain-named rows on the seal lists: every seal row is a prefixed (Superior/Exquisite/Exceptional) item.
- **The 66 low additions:** 21 are covered by a swap (do the swap, not the add), 15 are on DF lists with no swap (stay low), 30 are not DF (Siluric/Cymric name-or-AF doubts, St John's Wort / Treant Wood near names, etc.).

**C. New lists (new_lists.json):** 8 lists, each only that NPC's period items, page 0..4 / slot 0..29, none over 150.
| new_list_id | NPC (Mob) now | rows (high/med/low) | change |
|---|---|---|---|
| p165list_Signy | GameMerchant, Weapon Merchant, list MidWeaponMerchant | 60 (60/0/0) | repoint Signy; old list stays for Niklaas |
| p165list_Holsvi_Hallgrim | GameMerchant, Merchant, list MidAlchemy | 134 (132/2/0) | repoint Holsvi; old list stays for Acchiles Treldolph, Urho |
| p165list_Brach_Leof | GameNPC, no guild, Camelot | 92 (92/0/0) | make merchant |
| p165list_Chelseigh_Stillman | "Chelseigh Stilman", GameNPC, Camelot | 58 (44/14/0) | make merchant (world spelling Stilman; period Stillman) |
| p165list_Gremain_Watford | GameNPC, Camelot | 43 (41/2/0) | make merchant |
| p165list_Shayly_Parke | GameNPC, Camelot | 14 (14/0/0) | make merchant |
| p165list_Caley | GameNPC, Tir na Nog | 5 (4/0/1) | make merchant; Rowan Wooden Boards has no Hibernian template (planned p165_item_5895, NOT built) |
| p165list_Eluned | GameNPC, Guild "Armor Merchant", Lough Derg | 17 (5/12/0) | make merchant |
All items are coin-priced, so target ClassType is **DOL.GS.GameMerchant** for all eight. Guild titles for the six non-merchants are not in the pages (left to the owner). Mob_IDs, positions and current ClassType/Guild/list are in each record's `npc` field.

Scripts: merchant_pass2.py (runs merchant_match.py, then writes dfseal_swaps.json and new_lists.json).

## Missing merchants (deep dive, 2026-10-31) -> missing_merchants.json
**11 missing merchants:** 6 placed at **CapnBry coordinates** (Eileen Morton, Madarl, Nealcail, Vinandr, Borjal, Royd), 1 at an approximate **period loc** (Somyr), 2 at **derived** spots (Caryan next to Somyr; Melanaha in Mag Mell), 2 **unplaceable** (Rhodyn, Tempeste: town is Lough Derg or Tir na Nog, no source says which). Stock lists for all 11: 245 rows (high 119, medium 118, low 8; the 8 low have no ItemTemplate yet).
- **Coordinates:** CapnBry gives zone id + zone-local x,y,z; region X = Zones.OffsetX*8192 + x (same for Y). Checked on Holsvi Hallgrim and Borra: converts to exactly their spots in our world. Z is CapnBry's own. Heading/Model/Size are copied from the nearest same-kind merchant (marked `derived`); no source gives model, race or gender.
- **Where they stood (period/near-period sources):**
  - Eileen Morton, Albion Instrument Merchant, Camelot Hills (Cotswold) 559495,510743 z2496, lvl 21; next to Ydenia Philpott (a separate period NPC, so a new spawn).
  - Madarl / Nealcail, Hibernia cloth dye + robe merchants, Tir na Nog 23059,29579 / 24226,28034 z7198, lvl 35, beside Kinnat/Aulif (woven armour). Allakhazam live: "Mob No Longer Exists" (removed later).
  - Vinandr, Midgard Armor Merchant (hats/circlets), Mularn 805812,725073 z4719, lvl 33, beside Asta.
  - Borjal, Midgard Shield Merchant, Bjarken (Gripklosa) 288092,303244 z4160, lvl 50, beside Hammond/Arnie.
  - Royd, Midgard bounty-item merchant (totems), Jordheim 31632,33741 z8029, lvl 50. Class: GameBountyMerchant if the totems are bounty-point priced (the pages give no price).
  - Melanaha, Hibernia hat merchant, Mag Mell (Lough Derg), lvl 35 (live). No coordinates anywhere: placed 80 units from Anice (derived).
  - Somyr / Caryan: **not merchants**. They are the /level (1.62) free-gear NPCs. Warcry July 2003 (period): "Somyr and Caryan near the mage trainers" in Tir na Nog. Somyr's period page gives "Loc 15,22", i.e. about 23192,30192 in region 201 (+-500, right by Ena the Mentalist Trainer). They need a gift-NPC class, not GameMerchant: owner decision.
  - Rhodyn (15 instrument pages, with Edsoner LD + Fingal TnN) and Tempeste (20 bow pages, with Kenzia/Briana TnN + Mannix LD): appear only on item pages. Not in CapnBry, Allakhazam mob pages, Uthgard or Warcry. The co-sellers' spots are listed as options.
- **New stock source found:** the period Allakhazam **mob pages** (period_mobs.jsonl.gz, captures 2003) carry a full "Goods for Sale" list. They are fuller than the item pages: Borjal 65 vs 46, Nealcail 13 vs 3, Royd 12 vs 6. I used them for these merchants (rows marked "from the period mob page"); items with no template are low, `template_id: null`. A later pass could do the same for **every** merchant.
- **The 6 extra names:**
  - **Dyemaster Marna = our "Dyemaster Mara"** (CapnBry spot 76 units from Mara, same enamel stock: all 17 period items already on EnamelDye3). Proposal: rename. The earlier first-name match to "Marna" (craft components) was wrong.
  - **Ylaine Barret = our Ylaine Barrett** (Isle of Glass, CapnBry). Confirmed spelling; period Allakhazam calls her a quest NPC, the 1 item page is weak.
  - Dala, Darbo, Debo, Eluned realm conflicts are Allakhazam listing errors: each NPC is at its own realm's spot in CapnBry. Dala's arrival in Jordheim is in Warcry's 1.60 notes.

## Merchant pages pass (2026-10-31) -> merchant_goods_additions.json
Source: period Allakhazam merchant pages ("Goods for Sale", wayback-allakhazam/period_mobs.jsonl.gz; no other period NPC captures in the archive carry goods). Compared against the lists **after integration**: world165_post.db is a scratch copy of the snapshot, plus all runtime\true165 add files (181 + 222 items, 502 + 422 + 191 stock rows, 7 spawns) and all 1,065 true165-rules updates (all applied without error).
- **Merchant pages parsed: 6,152 NPC pages; 241 merchants with goods; 237 matched** to a world list.
  - Not matched: Cragen (2 same-name NPCs with different lists), Aghaistin (Hibernia bounty-item merchant in Tir na Nog, not in our world), Kendris (NPC has no list), and one page with a broken title (a Midgard DF Sapphire armour merchant; image name says "Marbo").
- **Goods items: 6,756.** Already stocked: 6,109. Stocked under another name with equal stats: 149.
- **Additions: 335** (high 186, medium 16, low 133) on 56 lists. Free page/slot is computed against the post-integration lists.
  - **2 lists would overflow** (35 items without a slot): `966eda39…` (Saffa/Gilbert/Alchemist Lendand, 20) and `9ab94f25…` (Darcy, 15).
  - Biggest: Cedric 59 high (Hibernian crafting components), Saffa 34, Darcy 22, Signy 13, Im 11.
- **Unresolvable: 159.** 44 have an item page but no template ("buildable, not built", e.g. Mithril Full Helm (AF 50/54) for Torr Upton, Liquid * Renewal for Cedric, Svartalf Crafted jewellery for Daracha, Plain Robe for Eluned). 115 have no item page (138 of the 159 are DF seal goods, e.g. Eternal Strife set on Iago). Details in merchant_goods_unresolved.json.
- **IMPORTANT, Darkness Falls:** this pass contradicts the pass-2 swaps.
  - Seal-merchant pages captured **4 Sep 2003** (Ebo, Yojo, Darbo, Wovo, Beelo, Horto) list only plain names.
  - Pages captured **11 Oct to 1 Nov 2003** (Debo, Pico, Godo, Dano, Neno, Kodo, Kulo, Hulo, Lulo, Lucifo, Orto, Thago, Tulo, Iono, Iago, Marbo) list the **Superior/Exceptional/Exquisite** versions, mostly next to the plain ones. Lucifo, Orto, Thago and Marbo list only prefixed items.
  - The period item pages agree: e.g. "Exceptional Avernal Plate Arms" (page 12246, Last Updated 9 Oct 2003, captured 1 Nov 2003) is sold by Debo, priced in Sapphire Seals.
  - So by the end of the period (Nov 2003) the prefixed items were on sale. Whether the plain ones stayed beside them, or are just stale Allakhazam entries, is not settled by these sources.
  - **The 167 integrated DF swaps (prefixed -> plain) may remove period items.** Owner decision: keep both, or revert the swaps.
  - The 76 prefixed additions in this file (73 low) re-add the prefixed items next to the swapped-in plain ones; they are held at low on purpose.
- **Lists whose stock differs clearly from the period goods** (>50% of rows not in the merchant's goods, 10+ rows): 58 lists, 21 of them used by that merchant alone. No removals proposed; full list in merchant_goods_unresolved.json -> different_stock. Many are partial goods pages, so the "not in goods" rows are not proof of non-period stock. Most notable unshared ones:
  - Kaitlyn Montgomery `AlbTwoHandedChains` (108 rows, 61 not in her 47 goods)
  - Seva `77793002` (123/98, 25 goods)
  - Renny `bb05e054…` (108/78)
  - Daracha `6326d1cf…` (84/84, 4 goods)
  - Wony `96c7a1bf…` (84/74)
  - Ewen `77793005` (86/80)
  - Geor Nadren `AlbCymricLeather` (30/24: the "cymric leather" vs period "Cymric" naming question again)
  - Holt Finan, Dala, Uli, Falla, Kedric, Fingal, Baran, Cafell, Lara Weathers, Nia Loaman, Ebo, Beelo, Ottar, Sissy
- Low-confidence reasons: Siluric/Cymric naming (period "Hard Siluric Boots" vs our "hard siluric leather boots", AF 2 off), "Two-handed" vs "Two-hand" spellings, the DF prefixed items, and page stats that differ from the template.

Scripts: merchant_goods_pass.py. Outputs: merchant_goods_pages.json (parsed pages), merchant_goods_additions.json, merchant_goods_unresolved.json (unresolved, per-merchant matches, different_stock, overflow), merchant_goods_stats.json.
