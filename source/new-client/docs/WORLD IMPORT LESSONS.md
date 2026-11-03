# MMOEngine: world import lessons and state (living document)

This is the single place for everything learned while bringing DAoC's world into MMOEngine (Godot 4). Read it before touching the pipeline or doing a zone or full-world import, and keep it updated. Started 2026-10-09 during the Lough Derg milestone.

**Current state and the ordered next steps** are in memory `mmoengine-state-and-next` (resume point).
- Open issues: a distant tree near Mag Mell shows different-coloured leaves clipping (investigate with one render when the owner isn't playing). Crawler zamConfirm(18467) is pending.
- Next: sky polish and snow zones, ambient effects/sounds and NIF-embedded lights, drop tables/quest rewards from the crawl, NPCs/monsters, full-world import.

## Owner rules for this project

- **Every zone must reach Lough Derg's quality** (owner, 2026-10-09): the same pipeline, checks and treatment for all zones, with no manual one-offs.
  - What that covers: client models with every texture layer; the decoded blend ground; grass clutter; water; the client sky, lights, fog, day/night and weather; correct orientation; collision; validation; x4plus upscaling.
- **The ground seam on a patch edge at Mag Mell is client-authentic:** the pebble weight jumps about 0.3 → 0.05 in the client's own blend maps, so leave it. Rule: if the real client shows it, leave it alone.
- **Sky and day/night** (built 2026-10-09):
  - `pipeline/build_sky.py` exports all 25 sky_*.dat files to `content/sky/*.json` plus their textures, and maps each zone to its sky (zones.dat skydome=, else the realm's sky).
  - Clock (server DayNightCycleTimer data): a game day is 24 game hours, day increment 24, night 18:00-06:00 runs ×1.25. That's 30 real minutes of day and 24 of night, a 54-minute cycle.
  - `game/sky_system.gd` + `game/sky.gdshader`: colour sets for dawn/day/dusk/night × clear/stormy, canopy rings, two cloud layers (+ the stormy *2 versions), sun disk/glow/flare, moon + moonback, stars, storm cycle, rain.
  - Gap-fills: the phase hours (night ≤4.5, dawn 6, day 7.5-16.5, dusk 18, night ≥19.5) and the light scale factors.
  - Muspelheim's red haze comes straight from sky_muspelheim.dat.
  - **Rain bug (2026-10-09):** storms showed no rain because the emitter was moved to the player's feet every frame, so drops spawned at ground level and fell into the terrain. Keep the emitter at the [rain] ceiling (400 units = 10 m) above the player, with the visibility AABB spanning ceiling → floor below it.
    - Drop values are the client's: 1,000 drops, length 20 units, velocity 500, radii 40-800 units, colours top (160,160,160,0) → bottom (160,160,160,64).
    - Not yet: blocking rain indoors and under roofs, splashes (zones/sky/rainsplash.tga), snow zones, rain/thunder sounds.
  - **Night sky: client vs mine.**
    - From the client: night canopy/cloud colours, whitestar32.tga, star_scale 2.5, max_stars 700, moon.tga + moonback.tga and their colours.
    - Mine: ring blending, cloud projection, star placement (hashed grid).
    - The star grid is sized so about max_stars stars show over the visible half of the sky. The first version showed ~12x too many.
    - **This applies to every sky** (owner, 2026-10-09). Each of the 25 sky_*.dat files sets its own max_stars, star_scale and star_texture (some skies have none, e.g. underground/uforest). sky_system.gd passes the zone's own values, so never hard-code a star count or texture.
    - Check each sky against the owner's view of the client when its zone is imported: star density, moon size, cloud layers.
- **Lighting sources in the original client** (learned 2026-10-09):
  1. **Sun / moon light, ambient and fog** come from the zone's sky_*.dat `[lights_and_fog_clear|stormy]`:
     - dawn/day/dusk/night `*_ambient_light` (RGB) and `*_ambient_light_amount` (0-1);
     - `*_dynamic_light` + `_amount` (the sun; at night the moon, e.g. hibernia night 0.1 white);
     - `*_distance_fog` RGB;
     - stormy versions replace them by storm level.
  2. **Zone point lights:** `dat<id>.mpk/lights.csv`, one row per light: `x, y, z(absolute height), type`.
     - The type indexes `zones/textures/LIGHTDEF.CSV`: `id, name, r, g, b, 10 (unknown, constant), radius in units, enabled`.
     - Base colours: 0 default white, 1 blue, 2 red, 3 orange, 4 yellow, 5 light blue, 6 purple (190,77,255), 7 green, 8 yellow-green, 9 fire-light (255,100,0). Radius 256 units.
     - 10-19 are the same colours "BIG" (radius 512); 20-29 "GIANT" (radius 1024); 30-38 deep red / pale green / pale blue in normal/BIG/GIANT.
     - Lough Derg has 30 lights: 14 purple (Hibernian town glows, e.g. the hut beside the Mag Mell Elf Hall), 9 fire-light, 5 orange, 1 yellow, 1 BIG light-blue.
  3. **Glow texture slots on models** (NiTexturingProperty glow): crystals, lamps, windows. Added as emission in nif_materials.gd.
  4. **Material emissive colour** (NiMaterialProperty) is added too.
  5. **Vertex colours** carry baked lighting/shading on many buildings (NiVertexColorProperty).
  6. **NiAmbientLight / NiDirectionalLight / NiPointLight blocks inside some NIFs** (about 400 across the client, mostly effects and housing) are skipped for now. They're candidates for per-model lights (spell glows, braziers).
- **Engine (gap-fills):** OmniLight3D per lights.csv row, range = client radius × 1.5, no shadows, distance fade at 120 m. Energy 2.2 × (0.15 + 0.85 × night_level), where night_level comes from the sky phase (night = 1, dawn/dusk = 0.5, storms add 0.3).
- **Not yet:** NIF-embedded lights, snow zones, ambient effects/sounds.
- **Shader lesson (2026-10-09):** sampling mipmapped textures inside per-layer branches or loops with implicit derivatives makes the GPU pick the wrong mip level. The ground blend's fine pebble textures sparkled as grain that crawled with movement; it was most visible at night, and the owner first thought it was FSR (FSR is off). The fix: take `dFdx/dFdy(UV)` once before the loop and use `textureGrad`. Apply the same rule to any future multi-layer shader.
- **Grid/fabric pattern lesson (2026-10-09):** a fine regular dot grid over the whole screen, worst at night, came from the **soft shadow filter at quality 3**. Its dither pattern shows with no temporal AA. Quality 5 removes it (confirmed by the owner with the F11 live toggle). SSAO was not the cause and stays on (now full resolution, quality 3). Live image-check keys stay in the viewer: F10 SSAO, F11 shadow filter 0-5.
- **Testing lesson:** never run several Godot test renders while the owner is playing. Three back-to-back renders crashed their graphics driver (RTX 2060, 6 GB). Ask first, and run one GPU job at a time.

- **Everything is new code** in Godot 4 / MMOEngine. Only the old game's ASSETS and DATA are used: NIF, DDS/TGA/BMP, MPK/NPK, zone CSV/PCX/DAT, gamedata CSVs, and DB rows read as data. Never copy or port old client, server, launcher or bot code. Write format readers fresh.
- **Don't make new assets unless absolutely necessary** (owner, 2026-10-09). Move the game's own assets over, upscaled and placed properly; list gaps to fill later. Engine code that re-renders client assets the way the client did is fine (e.g. water from watery + app/water/bump3.tga du/dv + a sky cloud layer).
- **Texture upscaling: Real-ESRGAN `realesrgan-x4plus`** (NOT the anime model) for the whole game.
  - 4x for faces and close-up gear.
  - 2x for the bulk: run at 4x, then Lanczos down to 2x.
  - Alpha is preserved by the tool.
- **Goal:** import the full game world as accurately as possible WITHOUT manual checking of everything. Build automatic checks for every class of mistake found (see Validation).
- **Art direction:** "old but new". Keep the original look; no hyper-real assets; water must not out-resolve the world.
- **FPS cap option** (owner, 2026-10-09): the temporary Esc menu offers monitor refresh (vsync) / 30 / 60 / 75 / 90 / 120 / 144 / 165 / 240 / unlimited, saved in user://settings.cfg. It's a frame cap only; **never lower graphics fidelity for it.** `--bench` still measures uncapped.
- **Player movement has a toggle: Modern MMO (default) vs Classic DAoC**, in the temporary Esc menu (game/game_menu.gd, saved to user://settings.cfg). It's player only, and **bot movement is not decided yet** (owner, 2026-10-09).
  - Classic: A/D always turn; Q/E side-step at 75 units/s and backward at 75 units/s with the client's St_hm/WB clips (the figure's monnifs.csv strides).
  - **All clips are stride-synced:** speed / (stride / clip length), with strides in monnifs.csv columns Run/Walk/Back/Strafe (Celt 135/75/75/75). The run plays at 1.41×, which fixes the sliding feet.
- **Modern movement is WoW-style** (owner, 2026-10-09; a deliberate change from DAoC, with no slow side-steps):
  - W forward; S backpedal at 65%;
  - A/D turn (they strafe while right mouse is held); Q/E strafe;
  - right mouse = steer (character follows the camera); left mouse = orbit the camera only; both buttons = run forward;
  - strafing at full run speed, diagonals normalised;
  - Space jump keeps momentum; R / Num Lock autorun.
  - Details are in memory `mmoengine-wow-movement`.
- **Strafing uses the run cycle with the body turned about 72° toward the side** (diagonals 45°), not the client's St_hm_L/R side-step clips. Those are DAoC's slow shuffle; the 2-second right one made the legs look locked during RMB+D strafing (owner, 2026-10-09). Direction is checked by `godot/tests/strafe_probe.gd` (`--probe=strafe_probe`, which injects input and logs clip, position and movement).
- **Animation playback rates come from gamedata animnifs.csv:** speed = fps column / authored fps (15). Idle plays at 4 fps (30 frames = 7.5 s), swim at 6, land at 20. The first build played idle at 15, which the owner called "fast speed".
- **Every zone has its own atmosphere** (owner, 2026-10-09): not extreme in most zones, but there's always a distinct vibe. Weather, day/night and proper lighting are expected to bring it out. Carry each zone's mood through: sector.dat fog/sky colours, sky dome (zones.dat `skydome=`), ambient sounds (zones/zoneNNN/sounds.dat), lights.csv point lights, ambient effects (zones/textures/amb_*.nif: birds, butterflies, fireflies, falling leaves, fish).
- **Shadows:** soft, low-resolution shadow maps, no ray tracing (owner, 2026-10-09: the first crisp leaf-detail shadows were too high-res). Directional shadow atlas 2048, **soft shadow filter quality 5 (ultra)**, **4 splits with blend_splits on**, blur 2.5, sun angular size 1.2°, 140 m. Lesson: with 2 splits and no blending, the hand-off between shadow zones showed as a dark line on the ground that followed the player (owner clip 2026-10-09).
- **Water: bring the client's water over; don't replace it** (owner, 2026-10-09). Rejected along the way: a screen-space treeline reflection ("too perfect"), invented depth clarity, refraction and sun glints.
  - Client pieces: the zone water texture (sector.dat river `texture=watery.tga`, zones/Nifs/watery.dds 64x64), `app/water/bump3.tga` (256x256 du/dv environment-bump map, RG centred on 128), a sky layer as the environment, and alpha blending.
  - Also in `app/water/`: water_wake.dds, footsplash.NIF, Displacement1.bmp, Droplet1.bmp (for swimming/wake effects later).
  - `water.gdshader` uses only these. Exact env texture, blend amounts and alpha are still to confirm against the client (owner clips).
- **The owner's reference is their current DAoC client** (OpenDAoC client, post-Catacombs data). When it differs from strict 1.65, match what they see unless told otherwise. Example: the ground blend described below.

## Real-ESRGAN folder-mode pairing bug (found 2026-10-09)

- Running realesrgan-ncnn-vulkan on a folder once paired outputs with the WRONG file names: 78 of the 80 classic UI pieces came out as other pictures (rightbot.png showed the Quest Journal art). Sizes looked right because we resize to orig × factor afterwards.
- `build_zone.upscale_dir` now checks every output against its own original (alpha-aware mean error > 30 → redo that file on its own).
- `pipeline/check_upscales.py` checks every original/x4plus pair under godot/content; run it after any upscale. Checked 2026-10-09: characters / scenes / sky / zone200 are fine; a few detail and noise textures read 14–27 from smoothing, not swaps.
- Tiny or dithered pieces (under 24 px, bar fills, list icons, gem strips) come out wrong even on their own run → `build_ui_classic.upscale_each` falls back to a plain 4× Lanczos for those.

## Paths and tools

- **Project:** `DAOC HIATUS/OFFLINE DAOC MMOENGINE GODOT`.
- **Client (read-only source):** the DAoC client's `app` folder (`DAOC_CLIENT`, pipeline/paths.py).
- **Pipeline (`pipeline/`):**
  - `daoc_mpk.py`: MPAK reader, written fresh.
  - `netimmerse.py` + `spec/nif.xml`: NIF reader that interprets the NifTools spec.
  - `nif_static.py`: NIF to glTF with the material table.
  - `texture_index.py`: find a texture by name anywhere in the client.
  - `build_zone.py <id>`: builds the zone content.
  - `validate_zone.py <id>`: automatic checks.
  - `corpus_test_nif.py`: parse every NIF in the client.
  - `audit_texture_slots.py`: texture slot and UV-set usage across the client.
  - `data/zone_spots.json`: teleport spots.
- **Godot project (`godot/`):** main scene `game/zone_viewer.tscn`.
  - `game/zone_loader.gd`: terrain, water, fixtures.
  - `game/nif_materials.gd`: layered shader for all NIF texture slots.
  - `game/terrain.gdshader`, `game/water.gdshader`.
  - `game/player.gd`: third person, run speed 191 units/s = 4.775 m/s.
  - Content is loaded at runtime from `godot/content/` (`.gdignore`, no editor import).
  - After adding `class_name` scripts, run `tools/godot/Godot_v4.7.2-stable_win64_console.exe --headless --path godot --import` once, or the classes aren't found.
- **Viewer test modes** (console exe, `--path godot --screen 1 -- ...`):
  - `--tour=<abs prefix> [--spots=a,b]`: screenshots at teleport spots.
  - `--bench`: FPS with vsync off.
  - `--shots="x,y,face,dist;..." --out=<abs prefix>`: free-camera shots at DAoC zone coordinates.
- **Launcher for the owner:** `PLAY LOUGH DERG TEST.cmd`.
- **Real-ESRGAN:** `tools/realesrgan/realesrgan-ncnn-vulkan.exe` (RTX 2060, Vulkan). Godot 4.7.2 is in `tools/godot/`.
- **Game DB (read-only queries are fine):** `CLAUDE VERSION/runtime/data/opendaoc.sqlite3.db`, opened with `file:...?mode=ro`. Tables:
  - `Area` (town centres);
  - `BindPoint`;
  - `Zones` (offsets);
  - `Mob` (X,Y,Z; good ground-truth heights).

## Coordinates (verified)

- **DAoC zone-local units:** 0..65536, X east, Y south, Z up. 1 unit = 0.025 m (a humanoid is ~72 units, about 1.8 m).
- **Zone origin in region coordinates:** `region_offset_x*8192`, `region_offset_y*8192` (zones.dat, or the DB `Zones` table). Lough Derg is offset 39,59, so region (346463,490663) is zone-local (26975,7335).
- **World position to Godot:** (X, Y, Z) becomes (x=X, y=Z, z=Y) times 0.025.
- **NIF model vertex to Godot:** (x, y, z) becomes (x, z, **-y**) times 0.025. This is a proper rotation, so triangle winding is unchanged.
- **Fixture yaw in Godot:** `-A` degrees about +Y, where A is the fixtures.csv angle.
- **Lesson: the first conversion used (x,z,y), which mirrors every model.**
  - Symptoms: signpost text read backwards (the Tir na mBeo rock), and buildings faced backwards (the Parthanan Farm stable).
  - How it was verified:
    - Lough Derg docks: the base sits on the shore and the far end over deep water, but only with (x,z,-y) and yaw -A.
    - The stable matches the owner's screenshot: its open front faces west, with haystacks to the SW.
  - Any future axis change must re-run the dock check (planned in validate_zone).
- **Terrain height:** `terrain.pcx * scalefactor + offset.pcx * offsetfactor` (sector.dat [terrain]); 256x256 samples, 256 units apart, row = Y.
  - Verified against 1,434 Mob Z values in Lough Derg: median diff 0, 96% within 30 units.
  - Rendered with a Catmull-Rom 2x grid (513x513) that keeps every original sample exactly.
- **Buildings can be raised by design.** Mag Mell NPCs stand 145 units up, on raised floors. A "floating" look needs checking against NPC heights before it gets "fixed".

## Zone files (`zones/zoneNNN/`)

- **`dat<id>.mpk`:**
  - sector.dat (fog, sky, terrain factors, rivers/water, jump points);
  - terrain.pcx, offset.pcx, water.pcx (water body id per cell; 255 = dry);
  - grassmap.pcx, densemap.pcx;
  - shademap/shadow.pcx;
  - nifs.csv (model list) and fixtures.csv (placements).
- **Fixtures:** use **dat<id>.mpk**. `csv<id>.mpk` is a later patch copy that adds freeshard extras (e.g. "SI Portal Visual 200", which the owner wants removed).
- **fixtures.csv columns:**
  - ID, NIF#, name, X, Y, Z, A, Scale%, Collide, Radius, Animate, Ground, Flip, Cave, UniqueId;
  - Ground=1 means Z comes from the terrain; Ground=0 means Z is absolute;
  - Collide is non-zero = solid (values like 1/256/400/6700).
- **Models:** `zones/Nifs/<name>.npk` (one nif plus sometimes textures); 3.x-era pieces are in zones/Dnifs. Textures are loose in zones/Nifs or elsewhere; use texture_index.
- **zones.dat** (in zones/zones.mpk) has a UTF-8 BOM and banner lines; regex-parse a section instead of using an INI parser.
- **Water:**
  - sector.dat `[riverNN]`: height, texture (watery.tga), flow, type;
  - NN is the id used in water.pcx;
  - the water texture is `zones/Nifs/watery.dds`.
- **Ground colour, three generations:**
  - `tex<id>.mpk`: 8x8 tiles of 256 px BMP, the 1.65 painted ground (Aug 2002). No roads; smooth, low contrast.
  - `lod<id>.mpk`: 8x8 tiles of 256 px DXT, a later baked ground with roads and shorelines.
  - `ter<id>.mpk`: **Catacombs splat**. textures.csv lists per patch (patch x, patch y) the blended textures (h_grass, h_soil, h_tinyPebbles, rock4, clover … and a whole-zone colormap such as `LoughDerg`, tiles=1, tileable=0). There are patchXXYY-LL.dds weight maps (128x128 **DXT3**; XX = patch x, YY = patch y).
  - **The owner's client renders this splat.** Dirt, pebbles and scrub around towns: Mag Mell looked "too perfectly green" without it.
  - **DECODED (2026-10-09, `pipeline/terrain_splat.py`):**
    - Each weight file holds 3 weights in RGB; its **alpha duplicates the next file's red** (multipass blending), so skip it.
    - The weights then follow textures.csv order. Weight 0 is the base texture (always 0: it fills underneath); each later texture is laid over the result by its weight (sequential lerp), and the zone colormap (tileable=0, e.g. `loughderg`) comes last, spanning the whole zone.
    - Verified: compositing reproduces the client's lod bake with median correlation 0.96 (worst 0.87), against 0.16 for the naive channel order.
    - **`tiles` is repeats across the WHOLE ZONE** (256 = every 6.4 m), not per patch. Per patch made pebbles millimetre-sized (flat grey). Scale is still to be confirmed by the owner.
  - Engine: `terrain.gdshader` blend mode, with one chunk = one patch; base + 8 layers + 2 RGBA weight maps per patch (`splat/patch_XX_YY_w0/1.png`). It fades to the lod bake beyond ~260 m. The F1 ground set "blend" is the default.
  - Tile order: tile `XX-YY` = column XX (east), row YY (south).
- **Lesson: never upscale or mip per tile.** Stitch the 8x8 tiles into one image, upscale once, and draw the terrain with one texture and zone-wide UVs. Per-tile processing left visible seams.

## Ground clutter (grass tufts, flowers, pebbles)

DAoC scatters billboard clutter that is **not** in fixtures. This is the "little grass sticking up".
- **Definitions:** `zones/textures/grass_<realm>[exp].csv` plus a sprite sheet `grass_<realm>[exp].dds`.
  - Realm: alb (zones 0-99), mid (100-199), hib (200-299); `exp` for SI zones.
  - Also grass_toa and grass_frontiers.
- **CSV columns:**
  - ID, name, **Group**, **Chance** (weight within group), **Shape** (0 crossed upright cards; 2 flat on ground, e.g. rocks and leaves; 4 lily; 1/3 variants);
  - Height, ΔHeight, Length, ΔLength, Width, ΔWidth (DAoC units), ΔScale%;
  - atlas rect x1,x2,y1,y2 (pixels), optional tint R,G,B.
- **`grassmap.pcx`:** value/10 = group (0,10,…,100). **`densemap.pcx`:** 0-255 density per 256-unit cell.
- **Built** (`godot/game/clutter.gd`): 32 m chunks within 72 m of the player, with a fixed seed per cell. Upright crossed cards (shape 0/1/3) or flat quads (shape 2/4), atlas rects from the csv, tint, wind sway, fading out at 48-70 m.
  - Density is `densemap/255 × MAX_PER_CELL (70)` per 6.4 m cell, matched to the owner's screenshots by eye.
  - Only group 5 grows under water. F4 toggles it.
- Benchmark with blend + grass: 152-186 FPS at 1080p, vsync off.

## Models and materials

- **NIF versions:** 10.1 (characters, newer), 4.2.2.0 / 4.2.1.0 / 4.1.0.12 (most world objects, items, effects), and 45 old 3.x/2.3 files (not read yet).
  - **pyffi fails on all DAoC 4.x files.** netimmerse.py reads all 5,329 of them with zero bytes left over; `corpus_test_nif.py` re-checks.
- **Pick the most detailed child of NiLODNode** (smallest near extent).
- **Skip:** particles, lights, cameras, NiTextureEffect (reported).
- **Collision:**
  - Untextured shapes with no UVs (typically under nodes named `collidee`, `*coll*`, `collisionswitch`) are the model's collision volumes. They're exported as hidden `collision_<n>` nodes and used for physics.
  - Models without them that the zone marks Collide get trimesh collision.
  - Trees and other non-Collide fixtures with a fixtures.csv **Radius** get the client's stand-in box (corrected 2026-10-11, see "Tree boxes" below); without a Radius they are walk-through.
- **Texture slots used across the client:** base, dark (light maps / overlays), detail (bark, grain, usually UV set 1, tiled), gloss, glow, bump, decal0. They use **UV sets 0-5**.
  - Export every slot with its own UV set: up to 4 distinct sets go to TEXCOORD_0..3, read as UV, UV2, CUSTOM0.xy and CUSTOM0.zw.
  - The material spec lives in glTF `extras.nif_materials`; materials are named `nifmat:<n>`.
  - **Never drop a layer silently**: anything not drawable becomes a warning that the build prints.
  - **Lesson:** the first converter used only base/UV0, so tree bark (detail slot on UV1) was missing and trunks looked smooth.
- **Layer maths** (`nif_materials.gd`): base × tint × mix(1, dark.rgb, dark.a) × mix(1, detail.rgb, detail.a), then decal over by alpha, × vertex colour, + emissive×base + glow.rgb×glow.a.
  - **Detail maps are modulate-2x**, so they're stored pre-doubled as `<name>__d2`.
  - **Lesson:** DAoC puts transparent overlays (climbing vines `hclimbvine`, lichen) in the DARK slot. A plain multiply turned the bindstone and Mag Mell buildings black, so every layer must be weighted by its own alpha.
- **Decals usually clamp** (TexDesc clamp_mode != 3), so outside 0..1 the decal is skipped.
- **Material colour:** keep the diffuse tint (normalised to max 1) but not its darkness; emissive is used.
- **Vertex colours** are used unless NiVertexColorProperty vertex_mode = 0 (source ignore).
- **Trees are authored with trunks below the origin.** Don't flag them as "buried".
- **Characters:**
  - HCeltM.NIF is 10.1, with skins assigned at runtime from figures/skins/*.mpk (c_cth0_* clothes, c_mhed1 head);
  - the head's face mapping is in **UV set 1**;
  - skin alpha is a hair/eyebrow tint mask (opaque material);
  - the bind pose is a T-pose (arms lowered via skin weights for previews);
  - fig3*.csv tables are for the Catacombs models, not the classic ones.
- **Animations:** `app/anims/*.kfa` (4,061 files, e.g. 2HDragon_run.kfa). Not decoded yet; next task for the run cycle.

## Spots and town data

- Town centres come from DB `Area`, bind stones from `BindPoint` (convert by subtracting the zone origin).
- **Lesson:** the Mag Mell Area centre lands INSIDE the Elf Hall's raised platform, which gave "floating building" views from underneath. Spots must be placed on open ground in front of landmarks; validate them against model footprints (to add to validate_zone).
- Spots live in `pipeline/data/zone_spots.json` (compass `face`: 0 north, 90 east); build_zone puts them in zone.json.

## Validation (automatic, for the full-world import)

- **`corpus_test_nif.py`:** every NIF parses with zero bytes left over.
- **`audit_texture_slots.py`:** slot and UV-set coverage across the whole client.
- **`build_zone.py`:** prints WARNING for every texture layer the engine can't draw, and lists missing textures.
- **`validate_zone.py <id>`:**
  - floating/buried objects by base footprint versus terrain (tune: measure from the origin and ignore below-origin trunks);
  - objects standing in water;
  - docks reaching water (orientation check);
  - near-black multiply layers.
  - Writes validation.json.
- **Still to add:**
  - spot placement inside geometry;
  - per-model isolated renders with a near-black/magenta pixel check;
  - a contact sheet per zone;
  - a ground-blend comparison against the lod bake;
  - NPC-height ground truth per zone (Mob Z vs terrain);
  - tile-seam measurement.

## Performance (Lough Derg, RTX 2060, 1080p, native, shadows + SSAO + MSAA 4x)

- **204 FPS average with vsync off** (165-279) after batching fixtures per 102 m map cell, with visibility ranges of 30x the object's size (160-900 m).
- On an RTX 20 card, G-SYNC Compatible needs DisplayPort; over HDMI it is not available.

## Environment lessons (Windows tooling)

- A failed `Path.write_text` (cp1252 can't encode a character) leaves the file EMPTY. Always pass `encoding='utf-8'`.

## Sky, weather and atmosphere (client data, next to import)

- **`zones/sky/sky.mpk`:** 25 sky definitions, `sky_*.dat` (albion, hibernia, midgard, the frontiers, muspelheim, volcanus, stygia, oceanus, aerus, uforest, default …), INI-style:
  - sun: texture sundisk/sunflare/sunglow with scales, and colours for dusk, noon, storm and noon+storm; ecliptic_angle;
  - moon + moonback;
  - stars: texture, max_stars, scale;
  - two cloud layer pairs (cloud_top/bot_texture, tile, speed ×0.001, plus …2 variants) that vary by weather condition: clear, partly cloudy, cloudy, overcast, thunderclouds.
  - Textures are in zones/sky: cloudsA-G, overcast, puffs, streaks, wisps, tatters, dense, rainsplash, snowflake, phases, moon, sundisk, sunflare, sunglow, whitestar32, u_forest_sky.
- **Zone assignment:** `skydome=` in the zones.dat section (e.g. zone104 Muspelheim → sky_muspelheim.dat). Zones without it (Lough Derg) use the realm/default sky; check which one the client picks.
- **Per-zone mood:** also sector.dat [fog] (colour, amount, clip) and [sky] colour; [forest] fog for forest areas; lights.csv point lights; sounds.dat; ambient effects (zones/textures/amb_*.nif).
- **Plan:** a sky-dome renderer driven by these files, plus day/night (sun/moon movement, dusk/noon colour blending) and weather states. All of it comes from client data; nothing invented.

## Characters and animation (working, 2026-10-09)

- `pipeline/nif_character.py` exports the skeleton (all Bip01 nodes), skin (top-4 weights) and clips to glTF: `godot/content/characters/celt_male/celt_animated.gltf`.
- **Clips:** gamedata `anims.csv` gives a row per figure (48 = Celt/Elf Male) with columns Walk, Back, Run, Turn L/R, Jump, Land, Fly, Idle, Death, Ladder, Slide L/R, Idle1-3, Cast…, Sit…, Crawl (swim)…; `animnifs.csv` maps anim id → kfa (R_hm.kfa run, W_hm walk, I_hm idle, WB_Hm back, Sw_h swim, ju_h_a jump).
- **kfa layout:** NiNode #0 has chains of NiStringExtraData (bone index into figures/animnode.dat) and NiKeyframeController (data = NiKeyframeData); the k-th string goes with the k-th controller.
- **Conventions:** quaternion keys are standard column-vector rotations, equal to the TRANSPOSE of the file's 3x3 matrices.
- **Lesson:** inverse bind matrices for glTF are `inv(row_vector_world)` written row-major. Transposing them gave rubber-stretched arms.
- **player.gd** plays idle / run / walk (Shift) / walk_back / swim from the movement state; `--anim=<clip>` forces one clip for test shots.
- **Clip length:** the client plays animnifs `frames` at the authored fps (column 5, 15). Catacombs kfa keys run past that (Cat_JU_H_A has 4 s of keys, the client plays 6 frames = 0.4 s), so `clip_files` returns the length and `trim()` cuts the tracks to it.

### Catacombs (fig3) models: the player and ALL NPCs use these (owner 2026-10-09, every race)
- **Build:** `python pipeline/nif_character.py cata` → `godot/content/characters/celt_male_cata/celt_animated.gltf` + `.json`. The viewer loads it by default (`--model=celt_male` = classic).
- **Skeleton:** `figures/fig3/sfigNNN.mpk` `<race>_skeleton.nif` (cel_m_skeleton in sfig001, 129 nodes incl. twist bones, EyeLids, Ponytail1/2).
- **Parts:** gamedata `fig3map.csv` row = (fig3 figure id, part slot) → up to 25 variant ids → `fig3parts.csv` (filename, archive # → `figures/fig3/figNNN.mpk`).
  - Slots: 0 head, 1 body, 2 lbody, 3 legs, 4 boots, 5 arms, 6 gloves, 7 cloak, 8 hair, 9 full helm, …
  - Celt male = figure 33. It uses the shared Briton/Celt meshes `*_BC_m` and the Briton head mesh `bri_m_head01`; Celt hair meshes are `cel_m_Hair01..`.
  - Body, boot and glove variants 1-8 are picked by the worn item (items.csv "Cata Morph" columns; still to decode).
- **Part NIFs** (Gamebryo 10.1): one skinned NiTriShape each.
  - The **skin bones are named in a NiStringsExtraData list**, in skin-bone order; the bone nodes are not in the file.
  - No textures inside: the client assigns them.
  - The skeleton rest pose **is** the bind pose. Checked: every bone's skin transform rebuilds the same vertex = vertex × the shape's own transform. So the classic method applies: vertices in the rest world, IBM = inv(rest world).
  - The head and hair carry NiGeomMorpherController morphs (`FM1=3…` face morph map, `HM1=5…` hair-under-helm map). Not used yet.
- **Textures** are in the character skin archives (`figures/Mskins/mskin*.mpk`, `items/pskins/pskin*.mpk`, `figures/skins/skin*.mpk`). TextureIndex now includes them, ranked after every other location.
  - Clothing: `pskins.csv` "Cata …" sets. The last column is the mskins id. Celt starter clothes = "Cata Celtic Cloth 1", pskins 4281 body cthbody01_01_m, 4282 arms cthArms01_01, 4283 gloves cthgloves01_03, 4284 legs cthLegs01_02, 4285 boots cthboot01_03.
  - Face: `fig3facemap.csv` (Celt male face 1 = skins 3675 Cel_m_Head01.dds).
  - Hair: `fig3haircolormap.csv`, per style 16 colours, each (texture skins id, tint fig3tints id, morph). Colour 5 = cel_m_hair01_blonde + Hair Tint 3 (210,64,14) red, my pick to match the classic red hair.
  - Skin: `fig3skincolormap.csv` → fig3tints (Celt skin 1 = no tint). Eyes: `fig3eyecolormap.csv` (eyecolor_generic_m_color01.dds, not applied yet).
  - Classic ↔ Catacombs character maps: `figtofig3main.csv`, `fig3tofigmain.csv`, `fig3tofighaircolor.csv`.
- **Animations:** anims.csv **265** = Catacombs Celt male → `Cat_*_HM.kfa` (Cat_I_HM idle 120 frames = 8 s, Cat_R_HM, Cat_W_HM, Cat_WB_HM, Cat_SW_H swim 10 fps, Cat_JU_H_A/B, Cat_St_HM_L/R).
  - Format: Gamebryo 10.1 with the NiStringExtraData in `extra_data_list` instead of the 4.x chain. `read_clip` handles both.
  - Strides: monnifs `cel_m` run 160 / walk 75 / back 75 / strafe 75 (classic hceltm: run 135).
- **Sound set:** the json has `sound_figure: hceltm` (Celt sound set 8).
- **Eye colour:** `fig3eyecolormap.csv` → skins id (eyecolor_generic_m_color01..08). The texture is white everywhere except a coloured iris at the exact UV of the face texture's iris, so the client multiplies it over the face. `bake_eyes()` bakes face × eye into `<face>__<eye>.png`, which is then upscaled.
- **Armour on Catacombs figures** (`cata_outfit()`; `python nif_character.py cata --outfit=<objects.csv ids> --name=<variant>`):
  - objects.csv item → its Skins columns (4 body, 6 arms, 7 gloves, 9 legs, 10 boots) → classic pskins rows (e.g. 337 "Chain")
  - → the pskins set named **"Cata <same name>"** → per-slot Catacombs textures
  - → **the texture name carries the mesh variant**: leaBody01 = body mesh 1, chnBody05 = body mesh 5 → fig3map part variant.
  - Verified by render: Hard Leather (36-40) and Chain (41-45) sets; chain uses Body05_BC_m with pauldrons and belt.
- Lesson: "Cata Celtic Cloth 1" is an ARMOUR item, not the default clothes (the owner caught it).

### All 1.65 races with every creation option (`pipeline/build_races.py`, 2026-10-09)

`python pipeline/build_races.py [cel_m tro_f ...] [--no-upscale]` → `godot/content/characters/races/<prefix>/figure.gltf + figure.json`, shared textures in `characters/fig3_textures/{original,x4plus}` (766 textures, x4plus 4x), index `races/races.json`. Engine side: `game/fig3_figure.gd` (class Fig3Figure: `load_figure("cel_m")`, `apply({face, eyes, skin, hair, hair_color, tattoo, size, sliders[0..8], mood})`). Test: `tests/figure_lineup.tscn -- --figures=a,b | --vary=<key> [--heads]`.

- **Races** (fig3 index m/f): Albion Briton 1/2, Avalonian 9/10, Highlander 13/14 (monsters.csv calls them "High Male"), Saracen 3/4, Inconnu 23/24; Midgard Norse 15/16, Troll 31/32, Dwarf 21/22, Kobold 19/20, Valkyn 27/28; Hibernia Celt 33/34, Firbolg 29/30, Elf 11/12, Lurikeen 17/18, Sylvan 35/36. (Not 1.65: Frostalf 5/6, Shar 7/8, Half Ogre 25/26, Minotaurs 61-63.)
- **monnifs.csv "Cata <Race> <Gender>"** (ids 582-617): name prefix (cel_m), anim set (anims.csv, = 232 + fig3 index), strides, fig3 index (col 13), **skeleton archive** (col 15: sfig001 or sfig002, NOT always sfig001; the two copies differ only in attachment bones).
- **monsters.csv player rows** (Cata Nif col 67 = that monnifs id; use the most common values): CatSkin 68 body, 70 head, 71 arms, 72 gloves, 73 legs, 74 boots = **the race's default clothes** (e.g. Celt male arms = 3232 cthArms01_ss_Bri, bare arms, not the shirt-sleeve 3671); **78/79/80 Cata Tint Arms/hands/Legs** = which parts take the skin tint (read at game.dll 0x417938); **81 Cata Scale** (%: Lurikeen 63, Dwarf m 80, Celt m 101 / f 96, Troll m 138, Firbolg m 134); col 15 sound set.
- **Size:** Short ×0.95, Average ×1.0, Tall ×1.05 of the Cata Scale (game.dll 0x450527).
- **Faces** fig3facemap (7, head textures); **eyes** fig3eyecolormap (8, multiplied over the face, same UVs); **skin tones** fig3skincolormap → fig3tints rgb (8, tone 1 = white).
- **Hair styles** = fig3map slot 8 meshes, labels from fig3descriptions. When there is one label more than meshes, the "Bald" label is the style without a mesh and the other styles take the meshes in order (cel_m: Hair 1-5, Bald, Hair 7 = bri_m_hair08, Hair 8 = bri_m_hair09; cel_m_Hair04 is a beard only). **Hair colours** fig3haircolormap (style × 16: texture skins id + tint id); every colour's texture is multiplied by its tint (white/bluebase textures included).
- **Tattoos** fig3decalmap (only Celt 5, Troll 15, plus "No Tattoo"). Celt tattoos are paint on mid-grey with no alpha, Troll decals are alpha-masked: one formula fits both, face × lerp(1, 2·decal, decal alpha). (Mine: the client's composite code is deferred, 0x425da8 only queues the parameters.)
- **Face sliders:** the head NIF's "FM1=3 FM2=4 …" means target FMn has **feature code** v: (1,2) Nose, (3,4) Eyes, (5,6) Lips, (7,8) Jaw, (9,10) Ears…, 21-24 Mood. The code pairs in ascending order match the race's fig3descriptions Morphs labels (Briton 1-8 = Nose Eyes Lips Jaw; Troll 1-4,7-10 = Nose Eyes Jaw Ears). Slider value 0..8, 4 neutral (figtofig3main): below 4 blends the odd (low) target, above 4 the even one. Blend shapes are named FMn; figure.json `sliders` gives low/high per label. **The old note that "FM9-12 point past the targets" was wrong** (it read the map backwards).
- **Heads without the FM map:** fig3parts names fig014.mpk for bri_f / sar_f / val_f heads; those copies have the morph targets but no map (and fig014's Sar_f_Head01 has 10 hidden "Box" shapes). fig015.mpk has the labelled copies → `labelled_head()` picks the copy containing "FM1=". Hidden shapes (flags bit 0) are skipped.
- **Skin tint rules** (skins.csv material columns BaseMap → mskins id, **Tint Chan**): head = whole-texture multiply (face alpha is a gloss map, not a mask); body parts flagged by monsters 78-80 take the tint only through **Tint Chan 'a'** = the texture's alpha (bare hands/arms 255, cloth lower). **The right texture copy is the one in the mskins archive the BaseMap row names** (figures/Mskins/mskinNNN.mpk): e.g. cthGloves01_SS_Cel in mskin039 has the alpha mask, the copy in mskin013 does not. Tint Chan 'b' (73 hair textures) is not decoded; treated as whole multiply.
- **Skin binding:** some parts list more skin bones than their NiStringsExtraData names (hig_f_Hair02) → fall back to the skin instance's bone node name; before, those vertices hung at the root as floating specks.
- **Animation retargeting:** apply **rotation keys to every bone but translation keys only to the root (Bip01)**. All 30 race skeletons share the same base proportions (Bip01 at 37.7 units, same pelvis/thigh); race size is the Cata Scale. Some Catacombs clips were keyed on other proportions: Cat_troll_idle puts EyeLids 2.8 units off (the lid triangles stretched into grey slabs over the troll's eyes), the neck 3.3, the hands 4.4, the cloak controls up to 34. With their translations applied, the bones stretch.
- **Highlander kilt:** monsters.csv col 69 (CatSkin Lbody) = skins 6058 `cat_b_cth1_kilt01` on fig3map slot 2 (LBody), Highlanders only. The figure part is `lbody`.
- **Helper boxes:** hig_f_Hair02 carries ten visible 24-vertex `Box` shapes (3ds Max helpers) → skip shapes named Box/BoxNN. A part with several shapes gets nodes `<part>`, `<part>__1`, `<part>__2` …; Fig3Figure groups them by the name before `__`.
- **Open:** Mood slider (which of codes 21-24 it drives); hair under helms (HM codes); helmets (fig3map slot 9); armour/dye on the race figures; NPC figures.

## Sound (working for zones 200 / 209 / 222, 2026-10-09): how to import the rest of the game

**Owner rule:** client sounds only. Never replace or substitute a sound without the owner's OK. A reference the client itself can't play stays silent and goes on the gap list. Ambient noise (the zone's background bed loop) counts as part of this ("there is ambient noise that you need to account for too").

**Commands**
- `python pipeline/build_sounds.py <zone ids...> [--creatures=<sound set ids>]`
  - Writes `godot/content/zones/zoneNNN/audio.json` per zone.
  - Writes `godot/content/sounds/sounds.json`, merged across runs, so zones can be built one at a time.
  - Copies only the files actually used into `godot/content/sounds/files/` (lower-case names).
- Full game: run it with every zone id, plus `--creatures=` with every Sound Set used by monsters.csv. Celt male = 8, female = 11; the `model_sound_sets` map in sounds.json lists figure → set for all figures.
- Tests run headless with the dummy audio driver, so they never touch the GPU:
  - `--probe=audio_probe` logs which regions/sounds play across day, night, storm and dawn.
  - `--probe=step_probe` logs footsteps per movement key.
  - `--mute` skips zone audio.

**Client sound tables** (`app/sounds/`, Mythic's own sheets; CSV, latin-1, `;` rows are comments):
- `cc_sounds.csv`: `,ID#,name,file,rate,vol,pan,offset,r1,r2,…,polygroup`.
  - `*` means "use the `#,0,s_default` row": rate 1, vol 1, pan C, offset 0, r1 0, r2 1000.
  - Values can be ranges (`.8-1.0`, `1.04 - 1.10`, so strip spaces); pan looks like `.9L-.9R` or `.5R` or `C`.
  - r1/r2 are distances in units (waterfalls 1000/2000). Offset is in ms.
  - **Rows retired to ID 0 are reachable only by NAME.** The creature, door, spell and weapon sheets refer by name, so keep both an id table and a name table.
  - A `!,POLYGROUPS` block gives `ambient_sfx` a limit of 6 with cutoff 1; `TutorialVO` is 1 / cutoff 2.
- `cc_groups.csv`: `,g_name,"s_X01, *02, *03",100,usage`. Play one member at random. `*NN` replaces the first member's last len(NN) characters. `s_null` = play nothing.
- `cc_queues.csv`: `,q_name,"member, member",interval_ms,repeat`. Members play in order, spaced by the interval. In the interval column, -1 means end-to-end and 0 means timestamps; members may include `s_silence(n)`.
- `cc_loops.csv`: `l_name,start,body,stop,dur(ms | xN | -1 infinite),int`. Used by siege, objects and so on.
- `cc_mp3s.csv`: `ID,name,file`. `zones.dat entry_music=` uses these ids: 5 = hibentry.mp3, 6 = dungeon.mp3, 21 = calltoarms.
- `cc_music.csv`: region entry-music names (not used yet).
- `cc_creatures.csv`: `setID,name,attack,pain light/med/heavy,die,evade,splat,move on dirt,move in water,swim surface,swim underwater,surface,fly,idle,unarmed set,misc01-10,…`.
  - gamedata `monsters.csv` column 15 "Sound Set" links a monster/model to its row (via monnifs NIF #).
  - The column notes in the sheet's own `;` rows explain each trigger (light pain = under 10% HP, etc.); read them when adding combat.
- Not imported yet (later): `cc_doors.csv` (door open/close per door type), `cc_weapons.csv` (swing/hit per weapon), `cc_spells.csv`, `cc_interface.csv` (UI clicks; do this in the UI phase), `CC_events.csv` (level up, bind, …).
  - gamedata `animsound.csv` has only 7 horse/rider rows.
  - `objects.csv` and `spells.csv` have Sound columns.
- **Format lesson:** 99 of the first 169 WAVs are **IMA-ADPCM (format 17)**, which Godot can't load at runtime. build_sounds decodes them losslessly to 16-bit PCM; this is a format change only and matches Python's audioop exactly. The rest are PCM 11/22 kHz and are copied as-is.

**Zone audio: `zones/zoneNNN/sounds.dat`** (one per zone, a Brad Derrick template per terrain type; the header comments document it)
- `[SoundRegionNN]` header fields:
  - Name;
  - fade_on_mp3 (1 = duck under mp3 music);
  - fade_time (s);
  - random_sound_spacing (min s between random sounds in that region);
  - zone_wide_sounds (1 = ignore shapes);
  - sound_type (1 ambient sfx, 2 ambient music, 3 sound effect).
- `SoundNN=Type, ID, Period, Chance, Volume, Start, End, Weather`:
  - Type 1 = random one-shot: every Period seconds roll Chance %.
  - Type 2 = loop: Chance is rolled each time it starts.
  - Volume runs 0-100; music uses 111.
  - Start/End are game-time HHMM. They may wrap past midnight; 0/0 means always.
  - Weather: 0 any, 1 clear only, 2 rain only.
- `ShapeNN=0,L,T,R,B` (rect) or `1,X,Y,R` (circle), in zone units.
- **Layout in practice:**
  - Region 00 is the global ambience bed loop (prairie ambloop2 at volume 18; city = ambloop5, mine = ambloop6).
  - Wind regions are split day/night and include "picks up" windows.
  - The rain region has rain loops + gusts + thunder, all with weather 2.
  - Birds, animals and insects are split day/night, mostly weather 1.
  - Shape regions (lakes) come next.
  - Music is **generative**: each region is one instrument part with 2-4 variant stems ("cantus firmus" pad, strings, guitar…), random period 7-19 s, chance 75, volume 111, weather 1, and day/night windows. Day = "acoustic 01" 05:01-19:30; night = "light 06" 19:31-05:00; Spraggon = "dark 04".
- **Dead references are part of the real client:**
  - Lough Derg's lake loop 176 has no row; `lakeloop.wav` exists as s_Lake_Loop 1161.
  - Tir na Nog has rain loop 509, horse hooves/whinny 526-533 (rows exist by name with ID 0) and distant music 578/579.
  - They stay silent, are listed in audio.json `dead_refs`, and are on the owner's gap list (they may OK mapping them).
  - **Owner-approved mappings** live in `OWNER_MAPPED` in build_sounds.py (by row name; exported rows carry `owner_mapped_from`). Add entries only with the owner's OK.
    - 2026-10-09: 176 → s_Lake_Loop (lakeloop.wav) at volume ×0.04 (owner: the full row volume was "omega loud"; OWNER_MAPPED entries may carry a volume scale; in-game volume options come later); 526-529 → s_Horse_Hooves(d)01-04; 530-533 → s_Horse_Vocal(d)_01-04 (whinny01-04).
    - Lake check: `--probe=lake_probe` (fades in inside the circles, silent outside).
    - Still silent: Tir na Nog 509 (rain loop 2), 578/579 (distant music).

**Engine (`godot/game/zone_audio.gd`, `character_sounds.gd`)**
- ZoneAudio follows the rules above. It uses SkySystem's clock, storm > 0.125 counts as raining (same as the rain particles), and shapes are checked against the player.
- Buses: Master → Ambient / Music, plus 9 panned sub-buses each, for the cc pan ranges. The Esc menu has volume sliders saved in `user://settings.cfg [audio]`.
- **My interpretations (where the client data is silent; owner may judge by ear):**
  - Volumes are linear amplitude: region volume/100 × cc vol.
  - A random sound doesn't retrigger while it is still playing.
  - A music region plays one part at a time.
  - Polygroup cutoff 1 steals the oldest voice with a 0.25 s fade; other cutoffs skip the new sound. Without stealing, wind gusts starved the birds.
  - Loops start at a random offset.
  - Entry music plays on zone load.
- **Footsteps:** the kfa files have **no step markers**, and the sheet only says "each step". CharacterSounds samples each movement clip's foot bones once, by forward kinematics over the clip's own tracks, and plays the step when playback passes the moment a foot comes down. Hands are used for the swim stroke; contacts within 0.15 s merge into one.
  - Results: run about 2.8 steps/s, walk 1.8 steps/s, and backpedal/strafe follow their clips.
  - Surface: dirt = move_dirt; wading (water > 0.1 m above the feet) = move_water, i.e. wet step + slosh 250 ms later; swimming = `g_Swimstroke_Small`, which the cc_groups note says is hard-coded.
  - Distance: a linear fade over r1..r2 from the camera (mine).
  - Use the same component for NPCs and monsters later, keyed by their Sound Set.
- **Not done yet:**
  - long-fall splat (threshold unknown);
  - surfacing sound;
  - underwater bed s_Bed_Underwater 1383 (the client triggers it in code);
  - doors (needs the door fixtures and cc_doors);
  - combat, spell and UI sounds;
  - region entry music on crossing region borders (cc_music);
  - positional object sounds (fountains, fires, waterfalls: s_Fountain_Loop 28, s_Fire_Loop_*, s_Waterfall_*). Find out how the client attaches them before inventing placement.

## Server prop objects become world objects (owner, 2026-10-09; game-wide)

- **Why they were missing:** the bandit camp tent, campfire and crates (and the banners, forges, fences and building supplies) are not in the client's zone files. The old server spawned them from its DB `WorldObject` table (GameStaticItem).
- **Resolve a model:** `WorldObject.Model` → gamedata `objects.csv` row → column 2 NIF # → `items.csv` row → name → `app/items/<name>.nif`.
  - Example: tent 3401 → 1775 Fest_refugeetent; campfire 2656 → 1064 wardnode; crates 3411 → 1787 Fest_dtrash01.
- `build_zone.py` reads the old DB **read-only** (`world_objects()`) and adds them as fixtures with `source: world_object` and `no_collision` (walk-through, like static items).
  - Position: X/Y minus region_offset × 8192; absolute Z.
  - Yaw: -heading × 360/4096. **Unverified:** check the tent against the client.
  - Lough Derg has 8.
  - One server campfire (32415,6151) sits 2.3 m under the terrain in the data.
- **Untargetable prop NPCs** (Mob rows with the CANTTARGET flag 8) go the same way when their model is a prop. In Lough Derg they are all creatures or controllers (ambient rats, pixies, flies, ghastly siabra, the farm controller), so none qualify there. Ambient creatures belong to the NPC phase.
- `--no-upscale` builds keep the existing x4plus sets. NifMaterials falls back to `textures/original` for textures not upscaled yet; run the x4plus pass later as a GPU job.

## Effects: fires, smoke, glows (particles) and object effects (figured out 2026-10-09)

**Where effects come from:**
- **Zone fixtures:** their own NIFs contain the particle systems, so the fixture model carries its fire.
- **Server prop objects:** some models are EMPTY. The campfire's `items/wardnode.nif` is just two nodes; the visible fire is attached as an effect:
  - `objects.csv` columns 56/57/58 "Alb/Mid/Hib Realm Effect" (campfire = 74)
  - → `moneffectsets.csv` row (up to 6 effect ids; 74 "Campire" → 71)
  - → `moneffects.csv` row: NIF num, **attach node** (`wardnode`), max visible range in units (2500), rate, track angle, autoscale, rotate axis/amount
  - → `moneffectnifs.csv` row: filename (`campfire_sfx`)
  - → `app/effects/<filename>.nif`
  - (`speffects.csv` is the spell-effect table, a different id space.)
- **The effect NIF** (campfire_sfx) holds static meshes (burning logs `smwoodbeam`, scorch `wdscorch`) plus **NetImmerse 4.x particle systems**. Each is a NiAutoNormalParticles / NiRotatingParticles shape with a **NiParticleSystemController**:
  - `emitter` (a node; its transform is the emission origin and axes);
  - `emit_rate` per s; `lifetime` + `lifetime_random` (s);
  - `speed` + `speed_random` (units/s);
  - `vertical_direction` (declination from the emitter's +Z) + `vertical_angle` (its variation = spread); `horizontal_direction`/`horizontal_angle` (planar angle and variation);
  - `size` (units); `start_random` (emitter box: random start offset per axis);
  - `num_particles` (max); `emit_start_time`/`emit_stop_time`; `start_time`/`stop_time` (the loop cycle);
  - the `particle_extra` modifier chain:
    - **NiGravity**: type 0 planar (direction) or 1 spherical (towards `position`), plus `force`;
    - **NiParticleColorModifier**: NiColorData RGBA keys over **normalized life 0..1**;
    - **NiParticleGrowFade**: grow s from 0 to full size, fade s at the end;
    - **NiParticleBomb**: decay, duration, deltav, start, position, direction.
  - Texture: the inherited NiTexturingProperty.
  - Blend: NiAlphaProperty flags. Bit 0 enables blending; bits 1-4 are the source factor, bits 5-8 the destination factor. A destination of ONE (0) or DESTALPHA (8) is **additive** (flames, glows: flags 269, 4109); INVSRCALPHA (7) is normal **mix** (smoke: 4845).
- Campfire values: flame rate 30, life 0.4 s, speed 52.5 units/s, size 10 units, box 16×16, colour dark red → orange → transparent, grow 0.03 s, fade 0.23 s, spherical gravity force 18 towards a point 89 units up. Smoke: rate 30, life 1.5 s, size 25, colour transparent → grey 50% → transparent, grow 1.5 s. Embers: hotglow, additive, with a particle bomb.

- **Particle size is a RADIUS:** NiParticlesData `particle_radius` equals the controller's `size`, so the billboard is 2 × size wide. Drawing it 1 × size made the flames look half-dead.
- **Animated material alpha:** effect meshes can start invisible. The campfire logs' NiMaterialProperty alpha is 0, and a NiAlphaController fades it 0 → 1 over the first second (clamped). `nif_static.controlled_alpha()` uses the settled value: the last key for clamp (flags bits 1-2 = 2), else the largest key. Without it the logs vanished.
- Verified by render 2026-10-09: the bandit camp campfire shows logs, flames, embers and smoke.

**Pipeline:**
- `nif_static.particle_systems()` exports every visible particle system into the glTF `extras.nif_particles`. Values are in metres and Godot axes (emitter basis + origin, emitter box, size, speed, gravity), plus the colour keys, grow/fade, bomb, texture and blend. Particle textures join the model's texture list (exported and upscaled like the rest).
- **Engine:** `zone_loader.gd` builds one GPUParticles3D per system per instance:
  - ParticleProcessMaterial: direction from the declination, spread = variation, velocity = speed ± random, box emission, gravity, colour ramp, scale curve from grow/fade;
  - billboard quad, unshaded, vertex colour, additive or mix;
  - visibility range from the fixture view range, or the moneffects max visible range.
- **Server props with effects:** `build_zone.world_objects()` follows the Realm Effect chain for the zone's realm and places the effect NIF at the object's attach node.

## The torch (client values, 2026-10-09)

- **Owner:** the old client had a torch light around the player; it must be **on by default** and toggled with a key. Gamebots get it too, later.
- **In game.dll:** `0x43e5a4` (torch on) calls `0x47d4f4(intensity 200, falloff 6, mode 4, the player's figure)`; `0x43e5e9` (off) passes 0s. Two global NiPointLights (`0x20892d4`, `0x20892d8`, built in `0x47c882`) hang on node `0x20892dc`, which sits **128 units (3.2 m) above the figure**:
  - light 1: white diffuse, dimmer 200 × 0.01 = **2.0**; it lights the main scene groups (0x2456cdc, 0x2456cf4, 0x2457ab0);
  - light 2: white, dimmer 200 × 0.0025 = **0.5**; it lights only scene group 0x2459dbc (unidentified; not drawn yet);
  - attenuation for both: constant 0, linear 6 × 0.001 = **0.006**/unit, quadratic **1/384²**. Brightness 2/(0.006 d + d²/147456): full (saturated) to about 6.8 m, about 0.57 at 10 m, 0.33 at 15 m, 0.22 at 20 m.
  - `torchmode` is saved in user.dat (`0x4431d6` load, `0x44367e` save); "Torch" is key-binding string 0x8f. The default key wasn't found (it's in compiled code); the owner remembered T or Shift+T.
- **Engine:** player.gd `torch` is an OmniLight3D at 3.2 m, white, range 30 m, attenuation 1.0, no shadows, energy 3.0 (mine, relative to the zone lights).
  - It joins `ZoneLoader.add_dynamic_light`, so it scales with night like the zone lights: the client's fixed-function lighting saturates in daylight, so the torch only shows at night.
  - On by default; **T (or Shift+T) toggles**; saved in user://settings.cfg [player] torch. Checked by render at 01:00.
- **NPCs:** the old server's GameNPC TORCH flag (64) is set on 56 NPC rows game-wide and **none in Lough Derg**. The client has only the one torch light pair (the player's), so an NPC torch must be shown another way (held torch model or effect). Check when NPCs are imported.

## Camera (client values, 2026-10-09)

- **Zoom:** game.dll `0x4ac407` (the setter behind the user.dat "cameradistance" setting) clamps distance to **50..500 units = 1.25..12.5 m**. 1500 units is allowed only while the player is linked to another entity (entity +0x256 with a riding/vehicle check, `0x43f60c`/`0x43f63b`: siege engine, mount or boat).
  - Owner: "lock mine, leave yours unlocked". player.gd uses CAM_MIN/CAM_MAX; test runs (--probe/--shots/--tour/--bench/--free-camera) keep 40 m.
- **Pitch:** `0x4ac471` clamps the camera field +0x24 to −50..50 degrees. Matched for the owner (CAM_PITCH ±50°, owner asked 2026-10-09); test runs keep −77°..34°.

## Player collision, step-up and floors: how the original client does it (game.dll RE, 2026-10-09)

**Owner rule:** "anything that a player can climb up without jumping in the original client should be viable here", copied from the client's own logic so the full-world import needs no per-spot testing. First case: the Mag Mell castle platform ledge (16606,4358), 0.26 m = 10.4 units. It is part of the "Outer Wall" model (houtwall), whose collision mesh includes the platform edge, so my old controller (walls first) blocked it.

**How it was found:**
- Tools and recipe: the server tree's `CLIENT REVERSE ENGINEERING NOTES.md` (capstone, pefile, fixed image base 0x400000).
- Scripts are in `pipeline/client_re/`: re_xref.py (string xrefs, function dump with float constants), re_refs.py (refs to an address), re_callers.py (call sites), re_full.py (full .text disassembly, ~1.3 M lines, 8 s).
- Path: the string `gspFigureCollisionGroup` → its global → the functions using it → the callback they install → the movement function.

**The client's per-frame player update `0x4365b2`**
- Player struct at `[0x104a7fc]`: +0x00 x, +0x4c y, +0x58 z, +0x3c height above ground, +0x50 vertical speed.
1. **Move** (0x438bb9 etc.).
2. **Ground pick** `0x467692(x, y, z, z_before, &dist)`:
   - It dispatches on the zone collision mode `[0x1495824]`. The outdoor/default branch (0x467749) casts a ray **straight down** (scene coordinates have z negated).
   - The ray starts **max(64, z_before − z) units above the feet** (constant 64.0 at 0x9676e0).
   - Targets: the zone's scene node for each 4096-unit quarter in a 3×3 block around the player (`0x57fd8b` → table 0x2457418 = the **Collide** fixtures only; non-Collide fixtures go to table 0x2457738, chosen in 0x478387 → 0x57fbe9 kind 1/2, which the outdoor pick never walks - corrected 2026-10-11), plus the dynamic-object list (0x43511b iteration, 0x1faf780).
   - It returns hit distance − lift, i.e. how far the floor is below the feet (negative = above).
   - **If the floor is above the feet, z is raised onto it. This is the client's step-up: anything up to 64 units (1.6 m) above the feet that the pick sees (terrain, Collide fixtures, dynamic objects - never trees or bushes), as long as walls don't stop the move first.**
3. **Wall test** `0x464ce4(x, y, floorZ + entity[+0x44])`:
   - It moves the player's collider (the figure's scene object `entity+0xcf6`, kept in `[0xc9322c]`) and runs the Gamebryo collision groups: figure `[0xc93238]`, camera `[0xc93234]`, a third `[0xc93230]`.
   - **`entity+0x44` = a random hover height** between two per-figure-type values (table 0xb77c54/0xb77c58 indexed by entity+0x19c × 0x1d0; that matches monsters.csv "min float / max float"). It is set at 0x423bba and is **0 for players and normal figures** (`fldz` at 0x41a0e5 and 0x433c8e). So any lift of the player's wall collider comes from the figure's bounding volume itself.
   - Contact callback `0x464c4d` (installed at `[collisionData+0x38]`):
     - **zeroes each contact normal's vertical component** (walls are purely vertical);
     - skips contacts facing away (dot ≥ 0);
     - keeps up to 100 contacts: normals 0x1faf1d0, objects 0xc93098, count `[0x104c244]`.
   - Slide `0x464912`: removes the move along each normal × 1.02, handling two-wall corners (dot < −1e-5).
   - Contact object names have meaning: `jump###` (7 chars) = zone jump; `climb` → 1, `sclimb` → 2 (keep climb points; "You start climbing the wall."); `boundary` (0x464b59, with the 0.99 ratio check).
4. **Second ground pick** at the final position:
   - dist > 128 units → long-fall flag (entity+0xcde);
   - > 16 → falling flags (entity+0x1c0/+0x40);
   - **> 1 → airborne** (stored as height above ground; gravity takes over);
   - **≤ 1 → stand on the floor** (z −= dist), with landing and fall-damage handling (2000 = a fall-height threshold).
   - **There is no step-down snap beyond 1 unit.**
- **Slopes:** there is no slope limit, since any terrain the pick finds within 64 units is walkable. Mountains and zone edges are blocked by **`bound.csv` invisible walls** (still to import).

**Collision data on objects:**
- `0x467bb4(group, obj)` calls `0x70a180(obj)` before adding an object to a group. That function gives every NiTriBasedGeom (RTTI 0x9a1e90) a NiCollisionData (`0x6f73d0`, 0x4c bytes, **default mode 2**) and recurses into NiNode children (RTTI 0x9a16f8). `0x6f7990` copies world vertices for triangle tests.
- Mode setter `0x48a24b(arg)`: 0 → 0 USE_OBB, **1 → 4 USE_NIBOUND (bound sphere)**, 2 → 2 USE_ABV, **3 → 1 USE_TRI (triangles)**, 4 → 3 NOTEST.
- `0x45f865(obj)`: all children get mode 4 (bound sphere). If the model has a node named **`collidee`**, the object and the collidee subtree switch to **triangles** and the ancestors up the chain too.
- `0x45f755(obj)`: bound sphere for the whole object; it detaches `collidee` and `rubble_pile` children (siege rubble).
- `0x483c54`: name-tagged nodes (`_NN` / `-NN` tokens) get mode 4.
- Other collision-related node names in game.dll: `alwayscollide`, `porchcollision`, `collisionswitch%05d`, `!collidee_damaged`/`!collidee_undamaged`, `!collidee_PlugTier%d`, and the camera/zonejump collision groups. (There is no `tree_proxy_collider` string in this game.dll - an earlier note was wrong.)
- **Tree boxes (2026-10-11, owner: bots walked up Myrkwood pine trunks and Avalon willow leaves):** fixtures.csv columns load into a 0x80-byte record (0x494462: Collide +0x2c, **Radius +0x30**, Scale +0x24). For a fixture with Collide = 0 and Radius > 0, 0x49084e spawns an invisible entity within 1024 units of the player: size word +0x1976 = 2 × Radius × Scale / 100 (integer), base z = file z − 512 raised to the ground (0x43f4db), height +0xce6 = 1024. The player's per-axis step tests (0x435d7a x / 0x43609b y, via 0x43a3ca / 0x43a3f9) stop where |dx| and |dy| < size + 24 and the feet are between base − 96 and base + 1020: an upright square on the world axes. ZoneLoader.proxy_faces builds it (sides only, capsule radius taken off); tests/proxy_box_probe.gd checks it.

**MMOEngine implementation (`player.gd` `_client_move` / `_floor_below`, `zone_loader.gd` layers):**
- Physics layer 1 = walls: fixture collision geometry; only the player's capsule tests it.
- Physics layer 2 = floors: terrain plus the visible faces of the Collide fixtures and world objects (ZoneLoader.picked); the pick ray and the camera arm use it.
- Physics layer 4 = scenery: the other fixtures' faces (trees, bushes); only the camera arm meets them.
- **Walking down (fixed 2026-10-09, owner video):** treating "more than 1 unit above the floor" as airborne made the player flicker into the jump/fall clip on every downhill frame ("random flailing"). The client only counts more than **16 units** as falling (`0x4369e0` sets the falling flags). So if the player was grounded and isn't jumping, a drop of up to 16 units keeps them on the ground (`FALL_HEIGHT`). Checked with `--probe=downhill_probe`: 10 m downhill, 0 airborne frames.
- Order per frame: integrate vertical; pick at the new spot from max(1.6 m, drop) above the feet and lift if the floor is higher; `move_and_slide` against walls only (MOTION_MODE_FLOATING, y restored, because normals are flattened); second pick: ≤ 0.025 m → stand, else airborne.
- **Collider bottom, pinned by client data:**
  - Lough Derg's Outer Wall (houtwall) carries its own client collision mesh, `COLLIDEE/collider ext/collider ext08`, and it covers the Mag Mell platform's 0.26 m edge. The real client still walks up it.
  - So the client's collider bottom is **above 0.26 m**: the floor pick at the player's centre can't lift before a feet-level collider would touch the edge. A 5 cm lift was tested and blocked.
  - Capsule: from 0.45 m above the feet up to 1.8 m, radius 0.25 m (was 0.3 m / 0.35 m). The floor pick also runs at the collider's leading edge: a step of 0.45 m or less lifts you as you reach it, so steep, narrow stairs (0.40 m risers on 0.25 m treads) don't catch the next riser first. tests/stairs_walk_probe.gd climbs both Lethantis stairs. Raised on 2026-10-09 because the Lethantis Association tower's stairs in Campacorentin Forest have 0.37 m / 0.40 m risers (16 units, tests/steps_probe.gd) that the client climbs on foot. Revise if a client case shows a higher step being climbed or a ledge under 0.45 m blocking.
- The `COLLIDEE` subtree also holds **textured** meshes (houtwall `collider int again:*`). My "untextured shape = collision" heuristic misses those, which is one more reason to switch nif_static to the client's `collidee` node rule.
- **Still to do:**
  - read entity+0x44 and the figure collider's bound (statically, owner's choice: option 1);
  - make nif_static mark wall geometry by the client's **`collidee` node rule** instead of my "untextured shape" heuristic;
  - check whether the fixtures.csv Collide column adds visible faces as walls (I currently do that when a model has no collision geometry);
  - import `bound.csv`;
  - the zone collision modes 2/3/4 (cities/dungeons: 0x4698fd, 0x46a4b8, 0x46a8e8 and the pick variants 0x469886, 0x46a3e1) for Tir na Nog and Spraggon Den.

## Character select / creation ("pregame"), in progress 2026-10-09

- **Client assets (app/pregame):**
  - `charScreen{Alb,Hib,Mid}.npk`: the realm backdrops as real 3D scenes (Hibernia: elf towers, birches, mossy boulders, green portal, banner, sky dome, cloud particles; Albion: stone tower and hills; Midgard: snowy mountain, gate, skulls, snow particles);
  - `settings.dat` [fog] per realm;
  - `pregame.mpk`: the screen frames character_selection / creation / customize / location (1024×768, transparent where the scene shows), realm_selection, buttons_wide, breadcrumbs (step medallions), misc_pieces_new (arrows, +/−, toggles, gender, locks), dialog_box, slider, small_button, eye / hair / skin palettes, realm banners, starting-town pictures;
  - `pregame002/003.mpk`: later palette and customize art;
  - `realmdesc.mpk`: realm texts.
  - Not used: splash.mpk (OfflineDAoC 0.35 art), *.max sources, trial.mpk.
  - **Private-server edit:** character_creation.tga has "Class / Race Combination: https://planner.atlasfreeshard.com" painted in; build_pregame covers it with the same stone strip from character_selection.
- **Build:** `python pipeline/build_pregame.py` → `godot/content/pregame/{alb,hib,mid}/scene.gltf`, `ui/{full (2x), small (4x), exact (palettes, not upscaled)}`, `pregame.json`.
- **Scene materials** (also in zones from now on):
  - two-texture blend: shape NiIntegerExtraData Texture1Index / Texture2Index / DetailIndex / DarkIndex name the NiTexturingProperty shader-texture slots. Result = mix(Texture2, Texture1, vertex alpha) × vertex rgb (baked light) × dark (light map) × detail×2. Direction checked on Midgard keep walls (90% wall stone at alpha 1).
  - NiBillboardNode cards: vertices relative to the node, node translation in glTF, shader billboard (mode bits 5-6 of the flags: 1 = rotate about up).
  - **Additive** alpha (destination ONE / DESTALPHA) → the 'add' material (glows, flares, plasma); before this they drew as solid discs.
  - Nodes with NaN transforms are skipped.
- **Placement (verified 2026-10-09):** every realm scene has a flat 4-vertex `collision_0` floor quad at Godot y = −4.98 (= 198.6 units), centred on x ≈ −3.5, z ≈ 0. **The character stands at Godot (−3.5, −4.98, 0)**, the same in all three realms. My first mapping (3.5, +4.96, 0) had both signs wrong and put the cameras 10 m in the air, so the scene looked miniature.
  - Raw notes: game.dll 0x5af0ce writes (140, 0, 198.6) to the character node; a camera orbit block at 0x5af444 is scaled by a runtime size s: 1.194s, −0.028s, 1.889s, 0.361s, ±40.
  - **Camera heading:** from the spot, offset (sin 135°, cos 135°) × d = (+0.707d, −0.707d) in x/z, looking back at the character. That gives the owner's framing in all three realms: realm banner left of the character; Hibernia portal far left and elf tower right; Albion keep tower right; Midgard gate (stone arch) left and mountain behind. (tests/pregame_shot `--ring=3.2,1.5`, view 3.)
- **Screen blend (alpha flags dst = 3, INVSRCCOLOR; src SRCALPHA):** used by Midgard's snow-drift clouds (`snow_drift01`, a black-backed cloud with solid alpha) and Albion/Midgard's snow-puff billboard. Before this it drew as 'mix'/'mask' → **rows of grey boxes** (owner caught it). Now:
  - nif_static `alpha_mode` / `particle_blend` return 'screen';
  - NifMaterials and NifParticles draw it as c·a + behind·(1 − c·a) with a mix blend (alpha = brightest channel). The colour-ramp alpha fades each cloud in and out; read literally, the client blend would cut black holes at alpha 0, so I treat that as mine.
- **Mesh particles (NiParticleMeshes + NiParticleMeshModifier):**
  - Each particle draws the modifier's NiBillboardNode plane **scaled by the particle radius** (plane ±8.3 units × radius 33.8 → 14 m clouds), not a radius-sized sprite. The spec carries 'quad' [w, h] in metres.
  - NiParticleMeshesData.unknown_link_2 holds per-particle clone nodes that are NOT in the scene tree. Never export them as static geometry.
  - NiParticleRotation → spec 'rotation' (rad/s) → angular velocity.
  - Particle cull boxes are sized from speed × lifetime + emitter box + quad (Midgard drifts travel about 300 m).
- **Owner's reference screenshots of the real screens:** layout and decisions are in memory `mmoengine-charselect-in-progress`.
  - 1.65 races and classes only; no Adjust Attributes (autofill); customize zooms to the face; 5 face sliders per race (Albion: Lips, others: Ears, plus Nose/Eyes/Jaw/Mood); Face, Eye Color, Skin Tone, Hair Style, Hair Color, Size, locks, Random/Default.
- **Tools:** tests/pregame_shot.tscn (`--realm --cam --top`); NifScene / NifParticles classes. **My test windows go on the Acer (`--screen 1`).**

### Two-texture blend direction (fitted to the owner's screenshots, 2026-10-09)

- Shapes with NiIntegerExtraData Texture1Index / Texture2Index blend two shader textures by vertex alpha; the shader does mix(tex2, tex1, alpha).
- **Texture1 at alpha 1 is right for:** Midgard gate / rocks / ground and the Albion terrain. The owner's Albion create shot has the grey rock (alb_rock01 = Texture2) under the character, where alpha ≈ 0.28.
- **Wrong for the Hibernia terrain:** around the character alpha ≈ 0.07 and the client shows grass (h_grass_craggy = Texture1), not the gravel forest_floor01. It is the only blend shape with a third shader texture (the DetailIndex light map). `nif_static.material_spec` swaps tex1/tex2 when the shape has that detail texture and base == Texture2.
- I tried "alpha weights the base texture" first. It fixed Hibernia but put grass under the Albion character, so the rule is still not understood from client code.
- **When it applies later:** any NIF using Texture1Index/Texture2Index. So far that is only the pregame scenes. Check new scenes against client screenshots. Lough Derg zone terrain uses the zone splat system; the owner says it's correct.

### NiBillboardNode cards: +Z faces the camera (fixed 2026-10-09)

- Flat billboard cards (all the grass_alb tufts: 684 in Hibernia, 192 in Albion; snow puffs; glow discs) lie in the NIF's XY plane. NetImmerse turns the node so the card's own +Z faces the camera and +Y is up.
- Our 'face' billboard used the axis-converted vertices, so the cards were edge-on and invisible (the owner: "missing the little grass shoots").
- Fix in the NifMaterials shader: VERTEX = (x, −z, y) before the camera-facing matrix.
- Lough Derg models have no billboard cards, so zones are unaffected. Any later zone with NiBillboardNode cards gets this automatically.

### Character-select lighting vs the client (in progress 2026-10-09)

- The NIF scenes carry no lights or cameras. The client wraps the scene in a "BackgroundRoot" node translated by (140, 0, 198.6) (game.dll 0x5aeeba–0x5af0ec) and adds no lights there.
- Per-shape NiFogProperty blocks (616 in Hibernia) all have flags 0 = fog OFF.
- Materials are authored for fixed-function lighting (NiVertexColorProperty vertex_mode 2 = ambient + diffuse from vertex colours, lighting_mode 1 = E_A_D; material ambient/diffuse ≈ 0.59–0.7), so the client darkens the textures. With ambient 1.0 white, a linear tonemap and the settings.dat fog colour, ours came out about 40 % brighter and bluer than the owner's screenshots:
  - Hibernia backdrop (left): client (65, 80, 60) vs ours (96, 125, 108);
  - ground: client (118, 124, 69) warm yellow-green vs ours (100, 106, 92).
- **Causes found and fixed (pregame scenes only; NifScene sets NifMaterials.use_material_light):**
  1. The client lights these scenes with ambient light: colour = texture × min(emissive + material ambient × light, 1), or the vertex colours for vertex-coloured shapes. Our converter drops the material darkness (right for zones), so trees and cards were too bright. Now spec 'mat_light' (material ambient) scales the lit part.
  2. Emissive surfaces (Hibernia sky panorama emissive 1.0, grass cards 0.6) were ambient + emission = up to 2×. Now the lit part is capped at 1 − emissive (shader uniform light_scale; emission keeps full colour).
  3. Shader-index detail light maps (Hibernia hib_terrainshadowsmap) are now a true modulate 2× (×2^2.2 = 4.594 in linear), not a pre-doubled png clipped at 1.0, so the ground is brighter than the backdrop as in the client.
  - Per-realm ambient in pregame.gd REALM_LIGHT: Hibernia (1.0, 0.94, 0.8) energy 1.0.
  - Result (Hibernia select vs client): ground (122, 122, 88) vs (118, 124, 69); backdrop (85, 108, 92) vs (65, 80, 60), still a little bright and blue.
- Fix approach (continuing): tune pregame ambient colour / energy and fog per realm against the owner's client screenshots (Hibernia select, Albion create; the owner will send more). Midgard was judged "looks good" by the owner.
- Hibernia's terrain has a "detail" shader texture `hib_terrainshadowsmap` (DetailIndex 2), a light map that we pre-double (__d2) and clip at white. The client's modulate-2x brightens white areas to 2.0; clipping loses that. That is probably why the client's ground is brighter and yellower than the rest of the scene.

### Pregame camera from game.dll (2026-10-09)

- Init 0x599a0e: yaw 1.5π, pitch 0.03125π; distance limits 40 / 200 units.
- 0x5af444 (static, angles in units of π = 4·atan(1)): +215°, −5°, limits 340° / 65°.
- Select setup 0x59ed4e: distance 200, target (0, 0, 44).
- Update 0x59ea59: R = RotX(−π/2)·RotY(yaw)·RotZ(pitch) (Gamebryo transpose-style matrices: RotX = [[1,0,0],[0,c,s],[0,−s,c]] …), camera = target + (−40, 40, 0) + R·(−dist, 0, side). The camera looks along local +X. Customize zooms to distance 40, with the target lerped to the midpoint of Bip01 Head and Bip01 Helmet.
- Converted to Godot (scene minus (140, 0, 198.6), then (x, z, −y)·0.025) and fitted to the owner's shots: yaw +14°, vfov 40°, CAM_POS (−0.727, −3.81, −4.28) looking at (−4.5, −3.865, −1.0), character at (−4.09, −4.98, −0.679). Select = that view; create = the same camera aimed at the character (matches the owner's Albion create shot).

### Pregame screens as built (2026-10-09, `game/pregame.gd`)

- **Flow:** login → realm → select → create → customize → FINISH → select → PLAY (no location screen: 1.65 had none; the later client's character_location.tga is unused; the start town comes from `_start_town()`, Mag Mell only for now; the old server StartupLocation table varies it by class) (CharacterStore.selected → zone_viewer → `player.set_figure`). Characters: `user://characters/<account>.json` (CharacterStore), 10 per realm.
- **Client data used:**
  - charman/summary.txt (race + base class pairs, descriptions, N/A = greyed);
  - startlocs.txt (town + text; the later client's, kept in pregame.json but unused);
  - names.dat (RANDOM name);
  - realmdesc.txt.
  - build_pregame.charman_texts() writes them into pregame.json.
- **Frames:** each 1024×768 frame is drawn in three slices (left panel / stretched middle / right panel), so wide screens stretch only the middle. Widgets are placed in frame units with an L/C/R anchor. Realm select is letterboxed.
- **Art:**
  - breadcrumbs.tga medallions (normal/hover pairs);
  - misc_pieces_new.tga (short stone bars = button states normal/hover/pressed/disabled at y 105 + 22n; long black bar = text field; knobs 24 px at (185|215, 7); small arrows at (3|26, 200/216/232); gold arrows at (110|173, 177); +/- at (106..235, 138); locks at (226, 209) open and (244, 210) closed);
  - realm emblem buttons are opaque, with two states side by side.
- **Camera:** spot (−3.5, −4.98, 0), bearing 135°, distance 3.2 / height 1.5 / aim 1.0 × figure scale. The yaw is offset so the character sits in the open scene area. Customize aims at the Bip01 Head bone.
- **Swatches:** computed from each option's texture (eye iris, face × skin tint, hair × tint). The client palette images include later races, with an unknown row mapping.
- **Testing:** `--pregame-shot=<folder>` (throwaway account) and `tests/figure_probe.gd`. **Check every edited script headless with `--check-only` before any windowed run.** A parse error shows a grey window forever (the owner saw it twice). Ignore the autoload names in check mode.
- **Open:** Mood slider; level-5 trainer class choice; non-Hibernian start zones; medallion mapping to confirm with the owner.

## Login, loading screen and title music (2026-10-09)

- **Build:** `python pipeline/build_ui.py` → `godot/content/ui/{original,x4plus}`, `fonts/`, `loading_tips.json`; title music is exported into content/sounds.
- **Login** (`game/login_screen.gd`, the main scene). The owner's choices:
  - background: the client's stone chamber (data/login.mpk `smcamback.bmp`) with its 16-frame flame strip (`flames.bmp`, 60×210 frames on black, drawn additive, phases offset) along the bottom;
  - logo: the three-realm emblem from the OfflineDAoC 0.35 launcher (its `Assets/offline-daoc-realm-emblem.png`, from the retired WinForms launcher that is no longer in the repository);
  - fonts: the client's own Minion Pro / Myriad Pro (app/ui/fonts);
  - **all text UPPER CASE and bold** (owner: mixed case "looks corny").
  - The client's launcher was the reference, stripped of server lists, patch notes, tech support and credits: LOG IN, CREATE ACCOUNT, EXIT, SAVE ACCOUNT NAME / SAVE PASSWORD.
- **Accounts** (`game/accounts.gd`): local only. user://accounts.cfg holds a salted, 4096-round SHA-256 hash per account. user://login.cfg (saved name/password) is written with ConfigFile.save_encrypted_pass using a key tied to OS.get_unique_id().
- **Test runs skip the login and loading screens:** --probe, --shots, --tour, --bench, --skip-login. Debug captures: --login-shot=<png>, --loading-shot=<png>.
- **Loading screen** (`game/loading_screen.gd`, shown by zone_viewer while the zone builds):
  - the original DAoC box art (owner-provided, `assets/ui_source/daoc_box_art.jpg`) as ONE full-screen background (cover), with a dark gradient at the bottom. Owner: showing it twice "looks like crap";
  - "ENTERING OFFLINE DAOC" (owner's wording, not the zone name);
  - the client's bronze splash bar (data/splashloadbar1 empty / splashloadbar2 full), with its solid black backing keyed to transparency in build_ui (owner: no black border);
  - a client tip from `data/loading/tips.dat`, changing every 8 s.
  - The tips file is the 2019 list: 98 kept, 42 post-1.65 tips (Catacombs, Labyrinth, champion/master levels, NF keeps, siege towers and tents, ROG, Bainshee, URLs) left out by `build_ui.POST_165`, listed in loading_tips.json "dropped".
  - The client also ships per-region loading art (`data/loading/hib1.mpk`, `tnn.mpk`, …), unused for now.
- **Title music:** cc_mp3s.csv 1 `mp3_Title` = `title.mp3` (hard-coded in the client for login/character select). The `PregameMusic` autoload loops it through login and loading (and character select later), then fades it out as the world appears, where the zone entry music takes over.

## What is client vs what is mine (audit 2026-10-09, owner approved)

- **From the client** (converted and upscaled):
  - all placed models and their texture layers;
  - heightmap; painted, baked and blended ground plus weights;
  - grass sheet and table;
  - water outlines and heights, watery, bump3, cloudsA;
  - the Celt model, skins and 6 animations.
- **Mine (gap-fills):**
  - Godot gradient sky tinted by the zone colours (to be replaced by the client sky domes);
  - lighting, shadow, fog and SSAO values;
  - the water texture mix;
  - grass density and wind sway;
  - terrain in-between smoothing points;
  - the Celt part/skin choice (client assignment rules not decoded yet);
  - the layer-maths reading;
  - teleport spot placement;
  - placeholder HUD.
- **Tools only:** NifTools nif.xml spec, Real-ESRGAN, Godot.
- **The owner approved these gap-fills** ("fit the game's art style well and filled in gaps well"). Keep the approach, still prefer client assets first, and keep reporting new stand-ins in this split.

## Open work (2026-10-09)

1. DONE: ground blend decoded and drawn (confirm the pebble scale with the owner).
2. DONE: ground clutter (tune colour/brightness against the client; tufts read a little bright).
3. DONE: Mag Mell spot moved to the plaza (25900,7700 facing 75). Still to do: auto-check spots against footprints.
4. DONE: Celt animations (idle/walk/run/back/swim/jump). Next: other races/figures, jump/land states, weapon stances.
5. Read 3.x NIFs; draw NiTextureEffect environment maps and bump maps.
6. Validator tuning plus the remaining checks above; then the full-world import.
7. Atmosphere from client data: sky domes (sky.mpk), day/night, weather states, lights.csv point lights, ambient effects and sounds.
8. Viewer: F6 fly mode added (Space up, Ctrl down, Shift fast, no collision).
9. Sound: zone ambience, music, weather and footsteps are working (see the Sound section). Next: doors, positional object sounds, combat/spell/UI sheets, and the owner's call on the dead references.

## Vertex animation: NiGeomMorpherController (2026-10-09, pregame banners)
The character-select banners, grass shoots and mushrooms move by NiGeomMorpherController + NiMorphData:
morph 0 = the base mesh, morphs 1..N = frames (relative displacements when relative_targets = 1), each with weight
keys - the banner is a 64-frame flipbook (one frame per 0.1 s, weight 1 on its frame, crossfaded) over 6.3 s.
Controller flags bits 1-2 = cycle (0 loop, 1 reverse / ping-pong, 2 clamp = play once), bit 3 = active; also
frequency, phase, start / stop time. Some grass and Midgard's two small pennants are flagged clamp (play once).
pipeline/nif_static.py morph_animation() exports the frames as glTF morph targets (displacements through the
shape's matrix, DAoC->Godot axes, x0.025) on a node named morph_<n>, with the sampled weight table + timing in
extras.nif_morphs; game/nif_scene.gd drives the blend shapes each frame. The scenes also have NiKeyframeController
(node motion) and NiTextureTransformController (UV scroll) - not carried over yet.
Check: godot res://tests/morph_probe.tscn (headless) prints the frame weights over time.

## Character animation trap: bones a clip "holds at rest" keep the previous clip's pose (2026-10-09, owner bug)
Symptom: the player stands visibly tilted after logging in at Mag Mell; moving "fixes" it.
Cause (measured, tests/tilt_probe.tscn): the client's Cat_I_HM.kfa idle keys Bip01 Pelvis with ONE key equal to the
bind pose (and the pauldrons likewise). nif_character.build_cata writes that channel into the glTF, but Godot's
glTF import drops one-key tracks equal to the rest pose, so the engine's idle never sets those bones. The player
spawns 0.3 m up, plays "jump" for a moment, and the jump's pelvis (3 deg off) and pauldron angles stay through idle.
Running leaves the pelvis near rest, so it looks right again. Every race figure is upright; the model is fine.
RULE for every animated character (players, NPCs, monsters, bots): a bone the current clip doesn't key must go
back to its rest pose, never keep the last clip's pose. Fix options, smallest first:
  1. at runtime give each AnimationPlayer a "RESET" animation with every bone at rest (the mixer then fills bones
     the playing clip lacks from RESET) - one place: Fig3Figure.load_figure + Player.set_model, and the NPC loader;
  2. or in the pipeline write single-key tracks as two keys / always write a full-bone pose per clip, and verify the
     imported Animation still has the track.
Check after any character pipeline change: tests/tilt_probe.tscn (spawn vs after-run bone diff must be empty).
FIXED 2026-10-09 (owner OK'd): option 1. Fig3Figure.add_rest_reset(anim) builds the RESET animation (every bone's
rest rotation / position / scale) and sets the mixer deterministic; called in Fig3Figure.load_figure and
Player.set_model. Every future NPC / monster / bot loader must call Fig3Figure.add_rest_reset on its AnimationPlayer.
tests/tilt_probe.tscn: no bone differs between spawn and after a run (cel_m, tro_m, val_m, the fallback model).

## Real-ESRGAN invents letters in small painted text (2026-10-09, owner)
x4plus garbled the small words painted into the UI bitmaps ("Strength" -> "Slrength", "Class" -> "Olass"). Fix:
pipeline/fix_ui_text.py finds the words in the ORIGINAL bitmap and, inside those areas only, uses the client's own
pixels enlarged plainly (4x Lanczos + light sharpen); the stone keeps the x4plus upscale. Raw AI output is kept in
content/ui/classic/x4plus_raw (re-runnable). Only bitmaps with real painted words are touched (TEXT_PIECES: button
sheets, char/skill/magic/combat-style sheets, paper dolls, trainer - both skins); the detector also hits icons and
stone highlights, so never run it on everything. Re-run after any build_ui_classic.py upscale.
Large or blocky lettering (quickbar numbers, lag-meter letters, paper-doll slot names, headings) upscales fine.

## City zones (Tir na Nog, 2026-10-09)
- zones.dat type=1 zones have no terrain: dat<id>.mpk holds city.csv (chunk list), adjacency.csv (chunk visibility),
  lights.csv, zonejump.csv, sector.dat; the chunks are zones/zone<id>/nifs/<name>NN.npk (+ .nhd height headers, unused)
  and <name>NN_pMM.npk collision patches ('collisionswitch' > '__patch_collidee' > 'collider ...').
- The chunks share one city frame. sector.dat [rotate] rotate = quarter turns; the frame origin is NOT in the client
  files we read - measured by fitting the old DB NPC positions onto the floors (Tir na Nog: 180 degrees at
  23808, 23808, 8000). Do the same for Camelot / Jordheim (pipeline/build_zone.py CITY_ORIGIN).
- zonejump.csv target = the old DB ZonePoint Id (arrival X/Y/Z region units + heading).

## Spell effects: how the client really plays them (2026-10-10, Gleam Ray vs the owner's clip)
- speffects.csv has a TWO-ROW header: col 6 Track Angle (turn to face the target), col 7 Auto Scale (scale to the character it sits on; 293 of 307 effects), col 8 Spin Axis. Auto Scale = the character's size against a standard figure: a race figure's scale, a monster's drawn height / 1.85 m. NOT spawn size / 50: measured on a badger cub, that drew the hit ~3x too big.
- An effect NIF is a TIMELINE from its start: every particle system has its own emit window (emit_start..emit_stop within its cycle); hidden nodes / systems are switched on by NiVisControllers partway in (the ray streak, the explosion's cores); NiKeyframeControllers stretch them, NiAlphaControllers fade them. Pre-warming / burning continuously (fine for zone fires) makes spell effects huge and constant. nif_static.convert(..., node_anims=True) exports these for effects only; zone objects stay as before.
- Hidden effect nodes have flags bit 0 set; skipping them all (fine for zones) loses the ray. Keep a hidden node only when a NiVisController shows it.
- Godot: a GPUParticles3D node's scale reaches particle speed and the emission box, not the sprite size (tests/particle_scale_main.gd). Size the quads to the world scale yourself (NifParticles.fit_to_scale).
- Node.duplicate() does not copy plain script variables: anything a NifScene sets up (morphs, animations) must be rebuilt in _ready from metadata.
- The target effect waits for the cast effect's release (when its hidden streak shows); the server's damage does not.
- Shields: objects.csv col 22 Apply Texture = a pskins.csv texture (items/pskins/pskinNNN.mpk) on UV SET 2 of the shield; the guild emblem layers (cloakpattern01 base, symbol_NNN) sit on sets 0 / 1.

## Effect NIFs: colour animations, environment maps, clamped layers (2026-10-11, owner's Sluaghbinder comparison)
- **NiMaterialColorController** (NetImmerse 4.x: target colour in flags bits 4-5 - 0 ambient, 1 diffuse, 2 specular,
  3 emissive): many effects keep their colour ONLY there (black material, colour keys fade in) - 119 of 385 effect
  models. nif_static.color_animations -> spec "color_anims" (30 keys/s), played by NifScene's timeline (emissive ->
  "emissive", diffuse -> "tint" over a texture, normalised by the animation's peak); scenery gets the settled colour.
- **NiTextureEffect** in a node's effect list (texture type 2 = environment map, coordinate generation 2 = sphere
  map) applies to every shape below that node: spec slot "env", added to EMISSION in NifMaterials via the view
  reflection's sphere-map coordinate. Shields (eshield, holy, blood shield), potions, slime blobs, crystals.
- **Clamped layers**: the samplers repeat, so a clamped layer must stay half a texel (at the drawn mip) inside the
  picture - clamping to 0..1 let the filter blend in the opposite edge (earth crack plates took the hot rim's row).
- Effect sprites with alpha blend on + alpha test at threshold 0 are soft blends (not 0.05 cut-outs).
- Re-run: pipeline/reconvert_effects.py (DAOC_CLIENT = the client folder the effects are converted from),
  then pack_scenes.gd --force res://content/monsters/effects.

## NiPathController (2026-10-13)
- 164 effect NIFs move nodes along paths: NiPathController = NiPosData (the path: keyed points with Hermite
  tangents) + NiFloatData (how far along, 0..1, over time). Ignored, those parts sat at their first point - the
  Shaman's orange poison mist (pestilence: three emitters spiralling up from 1 m below the origin) stayed under the
  ground and showed nothing.
- The path's key times are the fraction along it (0..1), with Max's exporter adding a last copy at the controller's
  stop time: sample the path at the float data's value directly (nif_static.path_position).
- Converted into the same per-frame node / emitter motion as NiKeyframeController (node_animation frames,
  emitter_anim) - the engine side needed nothing new.

## Monster casting clips (2026-10-13)
- anims.csv gives every monster anim set a clip per casting-style column (1H Circ, 2H Wax, Upraised, Bk-Hand, Stamp,
  Crossed, Z, plus Cast Start/Mid/End): RD_Cast_A/B/C, undead_sp_st/mid/end, Mushroom_spellA/B/C, the banshee's SPA/B/C;
  some sets point at idle or an attack (wolves, rock elementals, simulacrums). build_monsters.py builds each distinct
  kfa once ('cast_k_<kfa>', '_mid' loops) and writes monsters.json 'cast_clips' (style clip -> built clip); Npc /
  Player (morph forms) resolve the spell's style clips through it. Before this, pets only showed their hand glows.

## Weapon grip frames (2026-10-13)
- Weapon NIFs carry HELD / BELT / BACK / GROUND frame nodes. A few NIFs (bonedancerax_1h, epic_wpn_hunter_bow) turn
  their mesh under a rotated 'item' node that the held frame turns back; weapons.json 'held' (nif_static.held_frame)
  undoes it. The other frames are not applied: shields and most weapons already match the owner's checked look with
  the plain grip, and the client's exact use of BELT / BACK is not known.

## Effects + figures, 2026-10-13 (Necromancer audit)

- **Billboards with animated parts** (Arawn's eyes, scare faces, lifetap skulls, blood shield flares - 47 effects): a
  NiBillboardNode's children were baked at rest for the shader billboard, so their keys never played. Now the outermost
  billboard becomes a pivot node NifScene turns to the camera every frame (`bbpivot_*`), the parts below it play their
  keys inside it (`nif_static.chain_animation`: every animated node in the chain on its own controller - nested animated
  nodes were also only playing the innermost one). Face mode: the camera sets the whole frame (the node's own rotation
  counts for nothing), +Z to the camera, +Y up. Up mode: turns about world up, the node's own rotation KEPT, its NIF +Y
  to the camera (the scare face billboards swap y/z; dropped, the face looked up and away).
- Shader billboards now keep the node's scale (`length(MODEL_MATRIX[0])`): cards on a small target were full size.
- **Emissive-only lighting** (NiVertexColorProperty lighting_mode 0) on an effect piece = unshaded (`spec.unlit`):
  black + env map stays dark red (blood shield), not grey sheen.
- **Upscale failures**: Real-ESRGAN wrecks tiny (<16 px side) and flat-fill textures (8x8 black came back grey with
  alpha 0-111). `build_zone.upscale_dir` plain-resizes those and any upscale whose colour / alpha doesn't match its
  source; `pipeline/repair_upscales.py` fixed the 43 already converted.
- **Figures**: monsters.csv cols 32/33 = min / max float height (world units, the client's random hover entity+0x44;
  ghosts / mylings 50-62, pixies 50-62, the shade 40-45, sea creatures, Drakoran -9) -> monsters.json `float_units`;
  col 39 Body Alpha 2 = cut-out (skeleton ribs, harpy feathers), 1 = blended (treants, djinn); a NIF NiAlphaProperty
  blend (ghost) or a skin with see-through texels and no setting (the shade - the server only sends a model change)
  = blended -> `alpha`. `Npc.figure_look` applies both (NPC views and your own morph). `build_monsters.py
  --table-fields` refreshes them without a rebuild.
- Your own morph loaded the packed `.scn`, whose textures pack_scenes strips: it now loads the glTF like NPC views.
- **Every spells.csv row is converted** (`build_spell_fx.py`; `--used-only` for the old behaviour). The table had
  only the client effects of DB / NPC / emote spells, but boss scripts build spells in code (the dragons' breaths
  5700-5703, Fornfrusonen, Govannon, Legion ...) or play an effect by number - those showed nothing (owner: the
  Malmohus dragon's glare). `pipeline/audit_boss_fx.py` checks every named-mob script area's effects + sound sets;
  then `build_sounds.py --spells` and pack_scenes. Weapon effects (item glows): `build_weapon_effects.py` (the 1.64
  client's spweapons rows 1-94).

## Trees, detail maps and upscale colours (2026-10-14, owner: Malmohus pines with "giant black lines")
- **nifs.csv "Ambient Only"** (column 3, under the "Ambient" group header) is 1 on every tree and evergreen. The client lights
  those models with ambient + the whole sun / moon light: no angle falloff, no shadows (Midgard noon: ambient 0.42/0.49/0.60
  + sun 0.5 = a Small Evergreen at its texture's own colour in the owner's screenshot). Angle shading turned the tree cards'
  shared centre-line normals and the trunks' far sides black. build_zone writes `ambient_only` into zone.json models;
  zone_loader passes it to NifMaterials (shader variant: light() without N.L and with no directional shadow).
  pipeline/apply_ambient_only.py backfills built zones (417 models in 101 zones, all trees).
- **Detail maps are a true modulate 2x.** Pre-doubled pngs (`__d2`) clipped at white could only darken; pinedetail is 80%
  above half brightness, so the client's bark is up to 2x lighter than the base. Detail slots now use the raw map
  (`<name>__x2`) with spec `detail_x2` (the shader doubles it). pipeline/detail_x2.py migrated built content (578 maps,
  921 models). Weapons, monster figures and helms draw their glTF's own materials and still use the `__d2` pngs - keep them.
- **Real-ESRGAN darkens dark pictures** (~19% under brightness 20, 1 in 11 upscales over 10% darker) and blackens what's
  under a cut-out's transparent parts. pipeline/tone_restore.py: restored = upscale + up(original - down(upscale)), cut-outs
  bled (pipeline/alpha_bleed.py) before and after. Built into build_zone.upscale_dir; whole game redone (13,484 upscales:
  1,069 over 10% darker -> 21).
