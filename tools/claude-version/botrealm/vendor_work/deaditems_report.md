# Vendor pass 2: merchant rows pointing at missing items (2026-10-31)

## Numbers
- **Dead ids: 1194.** They come from 2975 MerchantItem rows on 184 lists. 853 of those rows are on lists that spawned merchants use.
- **Matched to a period page: 644.** Matching uses the id as words, with trailing digits dropped. It is limited to the list's realm (the merchants' Realm, or an Alb/Mid/Hib prefix in the list name). Leather/cloth/chain lists also require the matching armour type. Spelling variants are matched word by word, so Starkalaedar matches starklaedar, but alloy never matches fine alloy and ironwood never matches runewood.
- **Built: 207 rows** in add-deaditems165.json, each with Id_nb = the dead id in its exact case. They cover **1576 merchant rows, and 528 of the 853 used-list rows now resolve.**
- **Skipped: 987.**
  - 437 are inventory or crafting items: materials, dyes, catalysts, tinctures, boats and so on. The page gives no object type, model or spell. For 395 of them our world already has the same item under another id.
  - 550 have no period page. For 165 of them our world has the same-name item under another id.
- **Repoint candidates:** 560 skipped dead ids have a same-name item in our world (`existing_same_name` in deaditems_report.json). These cover 268 used-list rows. Repointing those MerchantItem rows (the merchant agent's job) would bring the used-list total to about 796 of 853.

## Prices and values taken from elsewhere
- Price: 167 from the page and 40 from the same item already in our world. That item is usually the `…2` variant; for example, rawhide_starklaedar_jerkin takes its price from rawhide_starklaedar_jerkin2.
- Quality: 203 from the page, 3 from other period pages of the same set, 1 from a sibling.
- Name: our world's spelling when a same-name item exists (e.g. "starklaedar"), otherwise the page name without the Allakhazam tag.
- Models and everything else follow the same rules as pass 1. Per-field sources are under `derived` in the report.

## Spot-check: 10 built rows against their raw pages
- Pass 1: 3646 Abysmal Hauberk, 3437 Accursed Daemon Ring, 4490 Avernal Maligned Spiked Mace, 6950 Tainted Avernal Coif, 3297 Abyssal Gloves.
- Pass 2: bronze_small_hammer, oaken_staff4, cobalt_dirk, heartwood_staff5, rawhide_starklaedar_jerkin.
- All 10 match their page on every field the page gives: slot/type, AF, absorb, DPS, speed, damage type, weight, bonuses (types and values), Default Bonus, class restriction and price/seals.
- Expected differences:
  - heartwood staff uses the page's Base DPS line (7.5) rather than its DPS line (7.4), as designed.
  - Fixed: Darkness Falls rows whose page gives no Quality now use the period quality of their set (89/91%), not the later DB version's 95/97/99. See vendor_items_report.md.

## Pass-1 typo-duplicate check
- I compared each of the 181 pass-1 rows with existing items of the same realm, type, AF/DPS and absorb/speed whose names are at least 80% alike.
- The only hit is Avernal Necklace against Verdant Necklace. Their bonuses differ, so it is a different item.
- **No pass-1 row needs to be dropped.** The two typo duplicates found earlier (Rawhide Svarlaekar Leggings and Sleeves, pages 479/480) were already left out of the 181.
