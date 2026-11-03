# BotRealm: classic figures

Research report, 2026-10-30.
The csvs it names are in the client's gamedata.mpk.

This spec describes how the original client builds, dresses and animates the classic race figures. It is worked out mostly from the 1.64 client's `game.dll`, whose code is simpler; the SI-era `game.dll` still has the same suffix tables. Points marked **[inferred]** or **[verify]** were not traced through the code all the way.

**Paths used below.** `CLIENT` = the DAoC client's app folder (`DAOC_CLIENT`, pipeline/paths.py). The working copies referred to below were extracted into a temporary folder (any scratch folder will do):
- `gd\` holds every SI-era gamedata csv.
- `gd164\` holds the 1.64 csvs.
- `z164\game.dll` and `z164\fig\HCeltM.NIF` are extracted from `DAoC 1.64.zip`.

## 1. The classic figure set

**Files.** One NIF per race and gender, named in `monnifs.csv` col 2. Each NIF has the skeleton and every part variant in it.

| Race | Male (monnifs id / NIF / anim set) | Female |
|---|---|---|
| Briton | 206 `bcommonm` / 2 | 209 `BCommonF` / 6 |
| Highlander | 210 `BHilandM` / 2 | 211 / 6 |
| Saracen | 213 `BSaracenM` / 2 | 214 / 6 |
| Avalonian | 217 `BAvalonM` / 2 | 218 `bavalonf` / 6 |
| Troll | 237 `NTrollM` / 24 | 238 / 25 |
| Norse | 239 `NVikingM` / 28 | 240 / 29 |
| Kobold | 241 `NKoboldM` / 26 | 242 / 27 |
| Dwarf | 243 `NDwarfM` / 40 | 244 / 191 |
| Celt | 250 `HCeltM` / 48 | 251 / 49 |
| Elf | 252 `HElfM` / 48 | 253 / 49 |
| Firbolg | 254 `HFirbolgM` / 35 | 255 / 36 |
| Lurikeen | 256 `HLurikeenM` / 33 | 257 / 34 |
| Sylvan | 346 `HSylvanM_2` / 76 | 347 / 77 |
| Inconnu | 348 `BInconnuM_2` / 78 | 349 / 79 |
| Valkyn | 351 `NValkynM_2` / 96 | 352 / 97 |

- The files without `_2` (`BInconnuM`, `HSylvanM`, `NValkynM`) aren't used by `monnifs.csv`.
- Half Ogre, Frostalf and Shar also have classic NIFs (monnifs 449/450, 464/465, 490/491), so NPCs of those races can be classic too.
  Built 2026-11-02 as `hal_m/f` (`BHalfOgreM/F`, sets 164/165, rows 1008–1019 / 1020–1031), `fro_m/f` (`NSvartalfM/F`, sets 174/29, rows 1051–1062 / 1063–1074) and `sha_m/f` (`HSharM/F`, sets 197/198, rows 1075–1086 / 1087–1098): the prefixes `build_monsters.RACE_FIGURES` gives their NPCs.
- The 1.64 copies are NetImmerse 4.2.1, the SI-era copies Gamebryo 10.1. The geometry is the same: all 49 HCeltM shapes have equal vertex counts. Use the SI copies, which the pipeline already reads.

**Skeleton.**
- Every classic figure has the same 63 `Bip01` bones; Dwarves add `Bip01 Neck1`. All have `Bip01` at z = 37.0 and Scene Root scale 1.
- Fig3 has 109 bones. Classic has none of the twist, robe, eyelid, ponytail, beard or fingers 2–4 bones.
- Classic has fingers 0–1 only, plus Held, Shield, Back, Belt and Helmet on both sides, and 18 cloak bones `Bip01 Ext01..Ext18` (no `C/L/R Cloak` bones).
- There is also a `targetchest` node.

**Parts.** Separate skinned shapes, the same in every race NIF:
- Body: `Body1..5` and `Body1b..5b` (the "b" versions have no neck bone)
- `Arms1..6`, `Gloves1..3`, `LBody1..4` (Highlander/Saracen kilt is LBody2), `Legs1..4`, `Boots1..5`
- Head: `HeadA1..5` and `HeadB1..5`. Each head mesh includes its hair.
- `Cloak1..3`, which use the Ext bones and carry 3 UV sets
- `Lod1a..d`, whole-body low-detail meshes

Everything is visible by default in the file; the client hides what isn't worn.

**Looks.** The look is set entirely by the `monsters.csv` row, i.e. the model id the server sends. There is no skin tone, eye colour, tattoo, slider or size option in classic.
- Col 4 "Head" = the hair texture. It is the head mesh's base texture on UV set 0.
- Col 18 "Face" = the face texture, drawn as a decal on UV set 1. The 1.64 `HeadA1` has `base_texture uvset 0 hair_blonde.tga` and `decal_0 uvset 1 S_Mhed1.tga`.
- Col 20 "Head Geom" 1/2 picks `HeadA` or `HeadB`.
- Player row blocks per race (male / female): Briton 32–34 / 35–38, Avalonian 61–64 / 65–68, Highlander 39–42 / 43–46, Saracen 48–51 / 52–55, Norse 503–506 / 507–510, Troll 137–144 / 145–152, Dwarf 185–192 / 193–200, Kobold 169–176 / 177–184, Celt 302–309 / 310–317, Firbolg 286–293 / 294–301, Elf 334–341 / 342–349, Lurikeen 318–325 / 326–333, Inconnu 716–723 / 724–731, Valkyn 773–780 / 781–788, Sylvan 700–707 / 708–715. Example: Celt male 302 = `c_mhed1`, `hair_black`, geometry A.
- Converting a Catacombs look to classic:
  - `fig3tofigmain.csv` maps (race−1, gender, Cata face 0–6, Cata hair style 0–7) to a classic model id. Example: Briton male face 0, style 3 → 14. It also points at extra rows such as 471–474 and 535–538.
  - `fig3tofighaircolor.csv` maps (race, Cata hair colour 0–15) to an old colour 1–8, which indexes `hair.csv` (hair set = `monsters.csv` col 19) **[inferred]**.
  - `figtofig3main.csv` goes the other way.

## 2. Armour on a classic figure

**How it works.** Armour is texture sets on the race's own meshes. The worn item picks which mesh variant shows. Texture and mesh both come from `objects.csv`, which is the same in 1.64 and SI.

- Texture columns:
  - 4 body (male)
  - 5 body (female)
  - 6 arms
  - 7 gloves
  - 8 lbody
  - 9 legs
  - 10 boots
  - 11 cloak
  - 12 helm

  Each is a `pskins.csv` id. Col 4 of `pskins.csv` is the archive number (`items\pskins\pskinNNN.mpk`).
- Mesh columns:
  - 13 Head#
  - 14 Body#
  - 15 Lbody#
  - 16 Cloak#
  - 32 Arms#
  - 33 Gloves#
  - 34 Legs#
  - 35 Boots#
  - 37–42 female overrides (9 items; see open questions)
- The item's extension is not used for classic meshes. The SI function at `0x41b5c1` (robe → mesh 7, else extension) only feeds the Catacombs variant.

**Starting part suffixes** (1.64 `0x418e71`). All default to "1", then race flags from `monsters.csv` change them:
- Kilt (col 16) → lbody "2"
- Sarac. (col 17) → body/arms/lbody "2"
- "Norse" (col 24) → body/arms "2". This flag is 1 for Norse, Celt, Firbolg, Lurikeen, Inconnu and Valkyn.
- Head = A1 or B1 by Head Geom.

**Per worn item** (1.64 `0x41917c`):
- **Body#:** 1 → body 1 / arms 1 (Saracen: 2 / 2); 2 → 2/2; 3 → 3/3; 4 → body 2 / arms 4; 5 → body 5 / arms 6.
- **"b" body:** a "b" is added when a cloak is worn and `monsters.csv` col 28 "cloak body" = 1 (every player row). A cloak with no torso item gives body "1b", or "2b" for Sarac./Norse races.
- **Arms# / Gloves# / Legs# / Boots#:** when non-zero, they override that slot's mesh number.
- **Lbody#:** 1 or 2 → lbody 1/2 (unless the race has a kilt), legs 1, boots 1. 3 (robe) → lbody 3, legs hidden, boots 2.
- **Cloak#:** 1 → Cloak1. 2 (hood) → Cloak2, head becomes `Head{A|B}5`, and the helm is skipped. 3 → Cloak3.
- **Head#:** head becomes `Head{A|B}<Head#>`.

**Textures** (1.64 `0x417176`). An item's skin goes on every variant of its slot: `Body1..5b`, `Arms1..6`, `Gloves1..3`, `LBody1..4`, `Legs1`, `Boots1..2`, `Cloak1..3`. Slots without an item keep the race defaults from `monsters.csv` cols 3–10. Kilted races keep their own plaid unless wearing a robe.

**Worked examples** (Briton male, model 32):

| Item | Classic | Catacombs (current `armor.json`) |
|---|---|---|
| Leather jerkin (server model 31, `objects.csv` 31 "Leather Tunic") | `Body2` (`Body2b` with a cloak) with `b_lea2_body1m`; `Arms2` with the default `b_cth1_arm1`; `LBody1` with `b_lea2_lbody1`; `Legs1`/`Boots1` default | `{body: [1, leabody01_02_m]}` |
| Chain hauberk (41; skins 337 / 338 / 341 = `b_chn1_body1m` / `b_chn1_body1f` / `b_chn1_lbody1` in `pskin001`; Body# 2, Lbody# 1) | `Body2(b)` with chain, `Arms2`, `LBody1` with chain | `{body: [5, chnbody05_02_m]}` |
| Robe 1 (58; Body# 3, Lbody# 3) | `Body3(b)`, `Arms3` with `b_rob1_arm3`, `LBody3`, no legs, `Boots2` | body mesh 7 |
| Hooded Cloak (96; skin 362 `cloak`, Cloak# 2, Head# 5) | `Cloak2` + `HeadA5` | — |

**Helms.**
- The mesh is a static NIF in `items\` named by `items.csv`. `objects.csv` col 2 is the base NIF. The `monsters.csv` "Alt Helm" value (col 26) picks an alternate column: 1 → col 24, 2 → col 28, 3 → col 29.
  - Alt Helm values: Saracen, Troll, Firbolg and Lurikeen = 1; Valkyn and Sylvan = 2; Kobold = 3; everyone else 0.
  - Example: 335 Norse Leather Helm 1 = `NHelm1` / `N_Trlhelm1` / `N_ValHelm1` / `N_Trlhelm1`. 1289 Wolf hat = `nhd_hat05` / `nt_` / `nv_` / `nk_hat05`.
- The texture is col 12 (93 Full Helm → `b_plt1_helm2`, `pskin007`).
- Under a helm, the head becomes `Head{A|B}<Head#>`. Example: 93 has Head# 3, so `HeadA3`.
- Scale is `monsters.csv` col 25 (Briton male 102, Troll 125, Kobold 155). It attaches to `Bip01 Helmet`.

**Weapons and shields** use the same item NIFs on the same bone names: `Bip01 R/L Held`, `R/L Shield`, `R/L Back`, `R/L Belt`. 1.64 `0x417ad6` scales a held object by `monsters.csv` col 46 "Shield Scale" when the attach node contains "Shield", otherwise by col 23 "Obj Scale".

## 3. `figure_version_config.xml` and `character_version`

- `pregame\figure_version_config.xml` is only the UI for the "Figure Version Config Menu": Male/Female columns, Classic/Catacombs/Done buttons.
- The setting lives in `user.dat`, read at SI `0x441d8f`:
  - `[main] CatacombsFigures` (default 1)
  - `[character_version]` keys `"<race−1>-<gender>"` for races 0–20 × genders 0–1, where 1 = Catacombs and 0 = Classic, stored at `0xf8dbd0`. Your `eden\user.dat` has all keys = 1.
  - `"500-0"` (stored at `0xf8dbcc`) covers all NPCs and monsters.
- The choice is made in `0x434338(monster id, classic)`. It returns the `monsters.csv` "Cata Nif" (col 67) unless the classic flag is set, otherwise "NIF" (col 2). Players get the flag from `!character_version[race−1][gender]` (`0x5aecf9`), NPCs from `500-0 == 0`.
- So the 1.64-faithful 1.65 Mode is classic for player-race NPCs too.

**Catacombs-only items.**
- There is no Catacombs-only body armour: all 1,683 `objects.csv` rows with Cata skins also have classic skins.
- 7 classic textures are missing: pskins 5000–5006 `b_chn0_*` ("Rediscovered Chain", e.g. object 2938).
- 8 helm rows exist only for Catacombs: event masks and pumpkins 4809–4820, plus 2732/2794 whose NIF is the generic "loot". The head-swap full helms (Dragonsworn/Dragonslayer, NIF 0, Head# up to 11) have no classic mesh.
- The code has no explicit fallback. Rule used: no classic helm NIF → draw no helm; Head# above 5 → keep `Head{A|B}1`; missing texture → keep the race default skin.
- Dye: `tints.csv` classic RGB (cols 2–4) equals the Cata RGB for all 144 rows, so `dye_tints.json` can be reused.

## 4. Animations

- Classic figures use their own clip sets, set by `monnifs.csv` col 3 (table in section 1). Example: set 48 = `W_hm`, `R_hm`, `I_hm`, `Sp_H_St1` / `Sp_Mid1` / `SP_H_Default_C`, `a_h_1s_med`. Fig3 sets are 232 + fig3 index, all `Cat_*`.
- Classic clips key only the 63 classic bones, including the Ext cloak bones (`R_hm` 60 tracks, `I_hm` 61). `Cat_R_HM` matches only 43 classic bones and no cloak bones, so don't reuse Cata clips.
- Coverage is complete: `anims.csv` has 174 columns (Cata 180; the extras are Em Dragon, Alt Fly/Swim, Summon A–C) and `canims.csv` 140 = 140. Only `EM_MEDITATE`, a few `classic_BritonMale_*` emotes and `P_H_1FW_*` are missing on disk.
- Cast clips use the same columns: Cast Start/Mid/End, 1H Circ, 2H Wax, Upraised, Bk-Hand, Stamp, Crossed, Z, instruments.
- Spell effect attach points in `spell_fx.json` are rootnode, `targetchest`, `Bip01 R/L Held`, Head, Pelvis, Spine2, `Bip01 Ext12`, R Finger0 and R Hand. All exist on the classic figures. `Ext12` exists only there, not on fig3.
- Strides come from `monnifs.csv` cols 4–7 (e.g. `hceltm` 135/75/75/75). Size is `monsters.csv` col 12 (Troll male 130, Lurikeen 65, Dwarf male 75, Firbolg male 135, Kobold 70).

## 5. What the pipeline already has, and the gaps

**Already there:**
- `pipeline\nif_character.py` `main()` and `build()`: a general classic NIF → skinned glTF exporter with animnode clips, trimming, borrowed-length retargeting, `file_bind`, and TEXCOORD_1 for the next UV set. `build_monsters.py` uses it for monsters.
- `godot\content\characters\celt_male\`: a one-off HCeltM test. It has 7 parts (`HeadA1@1` = `c_mhed1` face only, with no hair base; `Body1`, `LBody1`, `Arms1`, `Legs1`, `Gloves1`, `Boots1` in `c_cth0_*`) and 9 movement clips from set 48. Only `zone_viewer.gd --model=celt_male` loads it.
- `TextureIndex` already covers `figures\skins`, `Mskins` and `items\pskins`.
- `monsters.json` player rows carry the classic figure name and scale (e.g. 302 → `hceltm`, race `cel_m`).

**Gaps:**
- No classic race figures with all variants.
- `armor.json` and `helms.json` are Catacombs-only: fig3 variants, Cata textures, Cata helm NIFs.
- No classic helm meshes converted.
- No combat, cast or emote clips on classic figures.
- No face-decal head material.
- `build_monsters.RACE_FIGURES` sends every player-race NPC to fig3.

## 6. Plan (1.65 Mode only; `Server.world_mode == "1.65"`)

1. **`pipeline\build_classic_races.py`** → `godot\content\characters\classic\<prefix>\figure.gltf + figure.json`, keyed by the same fig3 prefix (`cel_m` → `HCeltM`).
   - Export all 49 part nodes under their NIF names. The head gets both UV sets; cloaks get all 3.
   - Clips: the classic anim set through `build_races`' CLIPS, emote, COMBAT, style and cast tables, so clip names match fig3 and every caller works unchanged.
   - `figure.json` holds: monnifs id, anim set, strides, scale (col 12), sound set (col 15), flags (kilt, sarac, norse, cloak_body, fem, alt_helm col 26, helm/obj/shield scale cols 25/23/46), default skins (cols 3–10), the race's model rows `{model: {face, hair, geom}}`, the hair set, and the `fig3tofigmain` / `fig3tofighaircolor` maps.
   - Textures go to `characters\classic_textures\{original,x4plus,x4plus_bc}`. Both sets ship; the flag picks.
2. **`pipeline\build_classic_armor.py`** → `characters\classic_armor.json` `{model: {skins: {body_m, body_f, arms, gloves, lbody, legs, boots, cloak, helm}, mesh: {head, body, lbody, cloak, arms, gloves, legs, boots, fem}, helm_nif: [col 2, 24, 28, 29]}}`. Reuse `build_armor.wanted_models()`.
   **Helms:** add `build_helms.py --classic` to convert the `items\` helm NIFs with their col-12 texture into `content\helms_classic\`.
3. **`game\classic_figure.gd`**: `class_name ClassicFigure extends Fig3Figure`.
   - Override `load_figure`, `apply`, `set_armor`/`_apply_armor`, `_apply_cloak`, `_apply_kilt`, `set_helm`, `_refresh_hair`, `_apply_full_helm` and `option_count`.
   - Keep the public fields: `hood_up`, `cloak_hidden`, `helm_hidden`, `cloak_emblem`, `anim`, `meta`, `play()`.
   - Implement section 2 exactly: reset suffixes, then process items in slot order. The torso is processed with the cloak flag when a cloak is worn, and a cloak without a torso gives the 1b/2b body. Show only `Body<s>`, `Arms<s>`, …, `Head{A|B}<n>`, `Cloak<n>`; put each slot texture on all of that slot's variants.
   - Head shader: hair texture on UV0 × face decal on UV1 by alpha. Dye stays multiply.
   - Treat `hood_up` like Cloak# 2 **[inferred]**.
4. **Factory and call sites.** Add `static func Fig3Figure.make() -> Fig3Figure` that returns a `ClassicFigure` in 1.65 Mode. Use it at:
   - `player.gd:234`
   - `npcs\npc.gd:125`
   - `world_host\bot_view.gd:59`
   - `pregame.gd:757`
   - `ui\inventory_sheet.gd:452`
   - `companions\innkeeper_window.gd:269`

   In 1.65 Mode, `item_armor`/`armor_parts` should return the classic per-item record so `npc._dress` and the player dress code (`player.gd:558`, `npc.gd:384`) can stay as they are.
5. **Looks.**
   - Pass the server's model id: `apply({"model": id})` for NPCs, bots and other players, and `c.model` for your own character.
   - In 1.65 Mode the create screen should offer the race's model rows and send that id in `CreateCharacter` "model" (`server.gd:517`).
   - Old Catacombs-look characters map through `fig3tofigmain` / `fig3tofighaircolor`.
   - Player-race NPCs use their row's own NPC skins (merchant, guard) as defaults, as the 500-0 rule implies.
6. **Weapons and spells** need no code change: same bone names. Add the Obj/Shield Scale factor on the attachment **[verify the inherited-scale semantics]**.

**Open questions:**
- The female columns 37–42 (9 items): the 1.64 code writes the fields at offsets `0xe4/0xe8/0xec/0xf0/0xf4` to lbody/legs/arms/gloves/boots, which doesn't match the csv header order.
- Whether the SI client's `/hood` on a Cloak#-1 cloak switches to Cloak2 + head 5.
- Which cloak UV set carries the emblem.
- The skip rule for the slot-7 item in the 1.64 slot loop at `0x419016`.
- The Lod1a–d meshes could serve as a far LOD; that's optional.