# True 1.65 Mode recipes vs the 1.64 client (tdl.irf)

Source: `development-source/research/period-data/client-1.64/ifd/tdl.irf` (Aug 2003, 8,610 recipes + section headers). Server: `runtime/data/opendaoc-165.sqlite3.db` CraftedItem (8,479), read-only. Output: `recipes165-unmatched.json` (`remove`, `keep_but_level_differs`, `renames_kept`, `missing_on_server`).

## tdl.irf columns

0 recipe id · 1 craft skill id (same ids as CraftingSkillType) · 2 index · 3 realm (1 Alb, 2 Mid, 3 Hib) · 4 0 · 5 icon/model · 6 group · 7 base product name · 8 material tier code · 9 item level · 10 craft level · then ingredients. Rows with id 0 are craft headers; rows with level 9999 are section headers (e.g. "cloth dyes", "leather dyes", "enamels", "poisons", "trophy potions"); dye/enamel rows give only the colour, the product is "<colour> cloth dye" / "<colour> leather dye" / "<colour> enamel".

## Tier code → product word (derived by joining craft + craft level + base-name suffix; ≥3 hits)

| code | word(s) |
|---|---|
| ADAM | adamantium, sapphire |
| ALLO | alloy, dolomite |
| AR10 | barbed footed flight, blunt footed flight, footed flight, keen footed flight |
| AR11 | barbed footed flight, blunt footed flight, footed flight, keen footed flight |
| AR12 | barbed footed flight, blunt footed flight, footed flight, keen footed flight |
| ARO1 | rough clout |
| ARO2 | rough |
| ARO3 | clout |
| ARO4 | rough flight |
| ARO5 | standard |
| ARO6 | footed clout |
| ARO7 | flight |
| ARO8 | footed |
| ARO9 | footed flight |
| ASTE | asterite, diamond |
| BRON | bronze, copper |
| CL10 | silksteel |
| CLO1 | woolen, woolen hooded |
| CLO2 | linen |
| CLO3 | brocade |
| CLO4 | silk |
| CLO5 | gossamer |
| CLO6 | sylvan |
| CLO7 | seamist |
| CLO8 | nightshade |
| CLO9 | wyvernskin |
| FALL | cobalt, fine alloy |
| GEM0 | raw |
| GEM1 | uncut |
| GEM2 | rough |
| GEM3 | flawed |
| GEM4 | imperfect |
| GEM5 | polished |
| GEM6 | faceted |
| GEM7 | precious |
| GEM8 | flawless |
| GEM9 | perfect |
| GENE | light |
| IRON | ferrite, iron |
| LE10 | tempered |
| LEA1 | rawhide |
| LEA2 | tanned |
| LEA3 | cured |
| LEA4 | hard |
| LEA5 | rigid |
| LEA6 | embossed |
| LEA7 | imbued |
| LEA8 | runed |
| LEA9 | eldritch |
| ME10 | arcanite, arcanium |
| MET9 | netherite, netherium |
| MITH | carbide, mithril |
| NA10 | crystalized, netherite improved os |
| NAT1 | leaf |
| NAT2 | bone |
| NAT3 | vine |
| NAT4 | shell |
| NAT5 | fossil |
| NAT6 | amber, cobalt improved os |
| NAT7 | carbide improved os, coral |
| NAT8 | chitin, sapphire improved os |
| NAT9 | diamond improved os, petrified |
| OR10 | arcanite, arcanium |
| ORE1 | bronze, copper |
| ORE2 | ferrite, iron |
| ORE3 | quartz, steel |
| ORE4 | alloy, dolomite |
| ORE5 | cobalt, fine alloy |
| ORE6 | carbide, mithril |
| ORE7 | adamantium, sapphire |
| ORE8 | asterite, diamond |
| ORE9 | netherite, netherium |
| STEE | quartz, steel |
| WO10 | duskwood, duskwood exceptional, duskwood exceptional large, duskwood f, duskwood reinforced, dyrwood heavy |
| WOO1 | rowan, rowan f |
| WOO2 | elm, elm f |
| WOO3 | oaken, oaken f |
| WOO4 | ironwood, ironwood f |
| WOO5 | heartwood, heartwood exceptional, heartwood exceptional large, heartwood f, heartwood reinforced |
| WOO6 | heartwood heavy, runewood, runewood exceptional, runewood exceptional large, runewood f, runewood reinforced |
| WOO7 | runewood heavy, stonewood, stonewood exceptional, stonewood exceptional large, stonewood f, stonewood reinforced |
| WOO8 | ebonwood, ebonwood exceptional, ebonwood exceptional large, ebonwood f, ebonwood reinforced, stonewood heavy |
| WOO9 | dyrwood, dyrwood exceptional, dyrwood exceptional large, dyrwood f, dyrwood reinforced, ebonwood heavy |

Metals BRON…ME10 = bronze, iron, steel, alloy, fine alloy, mithril, adamantium, asterite, netherium (MET9), arcanium (ME10); ores ORE1…OR10 = copper, ferrite, quartz, dolomite, cobalt, carbide, sapphire, diamond, netherite, arcanite; woods WOO1…WO10 = rowan, elm, oaken, ironwood, heartwood, runewood, stonewood, ebonwood, dyrwood, duskwood; leathers LEA1…LE10 = rawhide…tempered; cloth CLO1…CL10 = woolen…silksteel; Hib armour NAT1…NA10 = leaf…crystalized; gems GEM0…GEM9 = raw…perfect; GENE = untiered.

## Per craft

| craft | server recipes | matched 1.64 | not in 1.64 (remove) | name matches, level differs >25 (kept) |
|---|---|---|---|---|
| Alchemy | 1038 | 846 | 192 | 318 |
| Armorcraft | 1470 | 1470 | 0 | 0 |
| Metalworking | 200 | 120 | 80 | 0 |
| Spellcraft | 1456 | 1420 | 36 | 0 |
| Tailoring | 1288 | 1288 | 0 | 0 |
| Weaponcraft | 1886 | 1886 | 0 | 0 |
| Woodworking | 336 | 306 | 30 | 0 |
| Clothworking | 150 | 120 | 30 | 0 |
| Fletching | 565 | 553 | 12 | 0 |
| Leatherworking | 90 | 90 | 0 | 0 |

Total: 8479 server recipes, **380 to remove**. Siegecraft: the server has none.

## Groups to remove (not in the 1.64 list)

- **dye/enamel colour not in the 1.64 colour list (later dyes)** — 180 recipes. e.g. dark gold cloth dye, dark gold enamel, dark gold leather dye, dark violet cloth dye, dark violet enamel, dark violet leather dye, deep violet cloth dye, deep violet enamel …
- **metalworking item/material combination not in 1.64** — 80 recipes. e.g. adamantium gem-studded jewelry box, alloy gem-studded jewelry box, amber embossed jewelry box, amber ornate lamp, amber roughhewn hinge, amber small bracket, amber small lamp, arcanite gem-studded jewelry box …
- **spellcraft gem type not in 1.64 (Trials of Atlantis era gems)** — 36 recipes. e.g. faceted brilliant rune, faceted brilliant sigil, faceted brilliant spell stone, flawed brilliant rune, flawed brilliant sigil, flawed brilliant spell stone, flawless brilliant rune, flawless brilliant sigil …
- **carved souvenir box not in 1.64 (only curio box)** — 30 recipes. e.g. duskwood carved souvenir box, dyrwood carved souvenir box, ebonwood carved souvenir box, elm carved souvenir box, heartwood carved souvenir box, ironwood carved souvenir box, oaken carved souvenir box, rowan carved souvenir box …
- **ruffled scarf not in 1.64 (only scarf)** — 30 recipes. e.g. brocade ruffled scarf, gossamer ruffled scarf, linen ruffled scarf, nightshade ruffled scarf, seamist ruffled scarf, silk ruffled scarf, silksteel ruffled scarf, sylvan ruffled scarf …
- **harp not in the 1.64 fletching list** — 12 recipes. e.g. duskwood harp, dyrwood harp, ebonwood harp, heartwood harp, runewood harp, stonewood harp
- **combined buff/regen potion not in 1.64** — 12 recipes. e.g. barrel of combined buffs, barrel of combined regens, draught of combined buffs, draught of combined regens

## Kept as renames (same 1.64 recipe, different name)

- server "lamellar gauntlets" = client "lamellar gloves"
- server "osnadurtha coif" = client "osnadurtha helm"
- server "strong elixir of speed" = client "strong eixir of speed"
- server "superior quilted steeple hat (Albion)" = client "superior pointed steeple hat"
- server "superior padded/woven steeple hat (Midgard/Hibernia)" = client "superior steeple hat"
- server "eroding (tinctures)" = client "erroding (client spelling)"

**Not Atlas:** the 1.64 client already has the weapon/armour tinctures (volatile/stable/reactive …) and the elixirs of speed, so they stay. The plan line "drop the Atlas tinctures" only fits the 12 combined buff/regen barrels and draughts here.

## Kept but levels differ

318 alchemy recipes (mostly dyes/enamels at a flat server level, e.g. 1069 for leather dyes) have a 1.64 product of the same name at a different craft level. Product is period; only the level may be off. Listed under `keep_but_level_differs`.

## 1.64 recipes missing on the server

- **Metalworking** (80): tempered metal studs, tempered scales, tempered strapping, tempered wire
- **Leatherworking** (30): saddlebag
- **Weaponcraft** (35): exceptional stilletto, fortified stilletto, long sword, pivot, ram beak, spring arms, stilletto, winding crank
- **Armorcraft** (12): bucket, counterweight, gimbal, ram cladding
- **Tailoring** (27): arm cushion, ballista trigger, superior pointed steeple hat, superior steeple hat, swing harness
- **Fletching** (3): torsion cables
- **Siegecraft** (42): ball of ice, ballista, ballista bolt, bullet, catapult, catapult stone, grapeshot, onager, palintone, ram, scorpion, siege ram, small ram, trebuchet
- **Spellcraft** (246): air sigil, ashen rune, ashen sigil, crushed diamond seal dust, dust rune, dust sigil, earth sigil, earth spell stone, etched aquamarine eye, fire sigil, fire spell stone, heat rune, heat sigil, ice rune, ice sigil, ice spell stone, mineral encrusted spell stone, oozing spell stone …
- **Alchemy** (51): extraordinary behemoth potion, greater avian potion, greater carnivorous potion, greater herpetology potion, greater mammalogy potion, lesser avian potion, lesser carnivorous potion, lesser herpetology potion, lesser mammalogy potion, major avian potion, major carnivorous potion, major herpetology potion, major mammalogy potion, minor avian potion, minor carnivorous potion, minor herpetology potion, minor mammalogy potion

Siege: 14 per realm (small ram, scorpion, onager, ram, ballista, catapult, siege ram, palintone, trebuchet, catapult stone, ballista bolt, bullet, grapeshot, ball of ice) plus their parts in other crafts (weaponcraft: ram beak, spring arms, winding crank, pivot; armourcraft: gimbal, ram cladding, bucket, counterweight; tailoring: ballista trigger, arm cushion, swing harness; fletching: torsion cables). Also absent: the trophy potions (minor…greater mammalogy/herpetology/avian/carnivorous, extraordinary behemoth), saddlebags, tempered metal studs/wire/scales/strapping, Hib stilletto (high tiers). The spellcraft "missing" list (246) is mostly client gem names (e.g. "air sigil") that the server names differently while still matching 1,420 recipes; check before adding anything there.

## Confidence

High for the 380 removals: each has no 1.64 product of that name in its craft, after tier words, section suffixes, hyphen normalisation and the renames above. Medium for the "missing" lists (client naming varies).
