# True 1.65 period vendor items: report (2026-10-31)

## Numbers
- 1508 period item pages list merchants (period_item_pages.jsonl as of this run, 9.5k+ pages; the crawl was still adding pages).
- 1207 are in our world under the same name. 95 more are in our world under another spelling: 48 have an Allakhazam tag in the title like "(Hib)", "(Mid)" or "AF 70", and 47 are spelling variants with the same realm, type, slot and AF/DPS (Starkalaedar→starklaedar, Dangean→daingean, Costaic→constaic, Haurberk, Suppresion, BronzeTwo-Handed, Leggings→pants and so on). 1 is a housing item (Extended Hoyy→holly Shrubbery).
- **181 are genuinely missing, and all 181 are built** in add-vendoritems165.json. They are Id_nb `p165_item_<key>`, PackageID True165Vendor. 177 are Darkness Falls seal items (the period versions; our world only has the later "Exceptional/Superior/Exquisite" versions) and 4 are Mithril throwing weapons (Midgard).
- Another 24 missing pages were skipped:
  - 7 Albion housing paintings and arches: the page has no object type or model.
  - 6 rulings: page 379 is named just "Gloves". Pages 570 and 572 (Gossamer Padded) are labelled Hibernia but are the Midgard set. Page 2033 "Cabalist Staff of Mind" is really the Staff of Matter. Pages 479 and 480 are typos of Svarlaedar items we already have.
  - 5 duplicate pages of an item that was built (typos such as "Avernail Coif" and "Shimmerimg"). Each has `same_as` in the report.
  - 3 have no sibling to take a model from: Mithril Lochaber, Avernal Maligned Hammer, Infernal Malison Broadsword (Hib).
  - 2 pages list no bonuses (Diabolic Boots, Diabolical Forged Leggings). They are incomplete, because the later versions do have bonuses.
  - 1 armour page has no Armor Factor (Silken Soultorn Boots (Alb)).

## Prices
- From the page: 163 (seal counts or coin).
- From a sibling: 18. Each is the same item's later DF version, using its seal count. Where both exist, the page price matched the sibling's price on 125 of 157.
- None: 0 of the built items.
- Currency: 71 Emerald, 64 Sapphire and 42 Diamond Seals, plus 4 coin. DOL seal merchants use Price as the seal count, so these must go on the matching seal merchant. `price_currency` in the report says which.

## Siblings (model, plus values the page doesn't give)
- 179 of 181 come from the same item's later version, e.g.:
  - Avernal Boots → Exceptional_Avernal_Boots
  - Accursed Demon Mace → Superior_Accursed_Demon_Mace
  - Fiendish Gloves → Exceptional_Fiendish_Gloves
  - Mithril Throwing Daggers → Exceptional_Maligned_Throwing_Daggers
- Derived values are listed per item under `derived` in vendor_items_report.json:
  - Weight: 10 items, where the page has none or says 0.00 lbs.
  - Shield DPS/speed: every shield.
  - Level: shields and instruments.
  - Proc and charge spells: only when the sibling's spell does exactly what the page says. 7 sibling spells were checked and all matched.
  - Quality (fixed 2026-10-31, owner: use period values):
    - 138 from the page and 10 from another period page of the same set.
    - 29 Darkness Falls rows had taken 95/97/99 from the later Exceptional/Superior/Exquisite version. They now use the period quality of their own set from other period DF pages: 14 rows at 89% and 8 Infernal (Diamond-tier) rows at 91%.
    - The Abyssal set (7 rows) has no period quality on any page. It uses the most common period DF quality, 89% (157 pages).
    - The other 4 non-page values are the Mithril throwing weapons, which are not DF items. Two have 95 (from Exceptional_Maligned_Throwing_Daggers) and two have 85 (from mithril_balanced_*).
    - Only Quality changed: same 181 ids, every other column identical. The old file is kept as _add-vendoritems165.before-quality.json.

## Encodings (checked against 7150 page/DB pairs)
- Level is AF/2 (cloth: AF) or (DPS−1.2)/0.3. This held for 464/488 vendor armour items and 269/304 vendor weapons. Jewellery uses Magic Level.
- DPS_AF and SPD_ABS: AF/absorb, or DPS×10 / speed×10. Weight is in tenths of a pound. Bonus = Default Bonus %.
- Object_Type, Item_Type and Hand use the majority conventions of existing items: shields 42/11/2, instruments 45/12/1 with DPS_AF = eInstrumentType, thrown weapons 16/13/0, left-hand-usable 11/2.
- AllowedClasses is set only from "Requires Class:" (class IDs joined by ";"). Otherwise it is "0".

## Unfinished / uncertain
- **"Bow +N pts" is not built** on 4 Hibernian short bows and Accursed Maligned Avenger (a large weapon carrying a "Bow" bonus looks like a page error). Hibernia's short-bow skill mapping is unverified.
- Infernal Malison Defender's charge (DD 97 heat) is not built: no matching spell exists (the sibling's is 95).
- Two DF staves list the same line twice ("Enchantment +1 lvls / +31 lvls"). I read this as skill +1 and focus 31.
- The "Focus: Light Magic − 38 lvls" note lines on DF staves are used as focus bonuses.
- Hibernian magic names were mapped as Way of the Sun = Light, Way of the Moon = Mana, Void Mastery = Void, and Druid Nature Spec = Nature (79). Only 1–7 DB examples back each of these.
- I did not finish the planned check of 10 built rows against their raw pages. Only 2 were spot-checked: Avernal Boots and Rawhide Svarlaekar (that page was later ruled a typo).

## Period data that looked wrong
- Typo titles on Allakhazam: Haurberk, Suppresion, Shimmerimg, Avernail, Hoyy, BronzeTwo-Handed.
- Pages labelled with the wrong realm (Gossamer Padded marked Hibernia).
- "Nurture 0 pts" (dropped).
- Silken Soultorn Boots (Alb) is priced at 30g on a DF seal item.
- Existing world items differ from the period pages: our vendor Cabalist/Spiritmaster focus staves carry no focus bonuses, though the period pages list them. Dye prices differ (Dark Purple Leather Dye is 16g50s in our world, 30g on the page).
- MerchantItem lists reference templates that don't exist. For example, Morgen's list MidLeatherStarklaedar has 25 of 30 slots empty.
