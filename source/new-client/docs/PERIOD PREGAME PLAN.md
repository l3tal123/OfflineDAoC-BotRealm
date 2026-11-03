# Period 1.64 / 1.65 pregame - design note

**Status: built** (`godot/game/pregame_164.gd`, art from `pipeline/build_pregame_164.py`): Choose Your Realm, character
select, the one-page creation and the summary. The login panel is still the later client's. BotRealm only.

Goal: the realm selection, character select and creation screens as the period 1.64 / 1.65 client showed them, in place
of the later client's 3D screens.

## Sources

- The real 1.64 client (`DAoC 1.64.zip`, retail CD 2003-08; `DAOC_164_ZIP` in `pipeline/paths.py`): `charman\*.tga`,
  `data\login.mpk`, `data\title.tga` / `camback.tga`, `charman\summary.mpk` (summary.txt), `charman\names.dat`, and its
  `DAoC Manual.pdf` (Character: Select & Create, pages 8-18).
- The control positions, button sheet states and help texts, read from the 1.64 `game.dll` / `login.dll` (the layout
  extract is kept in the BotRealm work folder, `TRUE165_WORK/work/164/pregame-layout/1.64 PREGAME LAYOUT.txt`).

## What the period pregame was

All 2D, 800 x 600 painted screens with a Celtic font. Buttons are pictures with four states side by side or stacked:
grey (normal), gold (hover), white, red (pressed).

1. **Login** - `login.bmp`: the DAoC logo with Account / Password / Quick Entry ("select an existing character to load
   the game directly, or No Thank You"), over the stone chamber (`smcamback`) with the animated flames.
2. **Server select** - `server_selection.bmp`: "Choose a Server and Hit Play", the server list and "News / Patch
   Messages".
3. **Choose Your Realm** - `chooserealm.tga`: the Celtic triple knot on a night sky, red dragon in the middle, the tree
   (Hibernia), hammer (Midgard) and chalice (Albion) in its three loops (`chooserealm_but`, 4 states each, the selected
   one ringed in its realm colour). Its help text is painted at the bottom.
4. **Character select (Main)** - `Main.tga`: standing stones on a cliff under a full moon. PLAY and OPTIONS in the
   middle, QUIT bottom left, Delete Character bottom right (CREDITS exists too). A button per character slot ("Create
   New Character" on an empty one); the selected character stands on the stones. 8 characters per realm.
5. **Creation** - `chargen.tga`, one page: Enter Name + Generate; Select Race (5 per realm); Select Class (the base
   classes / Hibernia's "Way of ...", unavailable ones dark); Select Sex; Hair Color - / +; Select Face - / +; Height
   Short / Average / Tall; Points Left and the 8 attributes with - / + (class stats in yellow); a full-body figure in
   the middle with a rotating close-up of its face above; a help box at the lower right that describes whatever the
   mouse is over. Cancel / Proceed.
6. **Summary** - `chargen_Summ.tga`: the race + class description (summary.txt, e.g. "[BRIT-FIGH] Briton Fighter ...
   At fifth level, you must choose ...") with Cancel / Back / Proceed; Proceed saves and returns to the select screen.
7. **Delete** - type YES to confirm; **Options** - mouse / keyboard / resolution / performance / interface / sound.

## Decisions

- BotRealm only, base classes only.
- Looks: the period choices only - face (the classic look), hair colour, height. The later client's Catacombs customize
  page is not used.
- Attributes are spent at creation (Points Left + 8 stats). The client mirrors the server's rules exactly
  (CharacterStatValidator: 30 points over the race's STARTING_STATS_DICT, costs 1 / 2 / 3 above +10 / +15). A new
  character starts with the class's three stats raised, and every point can be moved.
- 8 slots per realm; the server refuses new characters in a realm's slots 9-10 and keeps existing ones.
- No server select screen (one local server) and no credits pages.
- Widescreen: the 800 x 600 screens are upscaled x4 and the painted sky is extended sideways - no black bars.
- Options in the pregame: the in-game options window with only its video / audio / controls tabs and a CLOSE button.

## Layout and protocol facts

- Height travels in bits 11-12 of the create packet's CreationModel (model = (size << 11) | modelId; 1 Short,
  2 Average, 3 Tall; 0 is refused). The Catacombs customize fields are sent as defaults.
- The create packet's slot byte is per account: realm r uses slots (r-1)*10 .. +9.
- The launcher always starts the client with `--world-mode=1.65`.
- Button sheets: state order normal / over / pressed / disabled. Character slots: x 9, w 345, h 60, y 30 + 60i.
- Figure viewports - select: x 464..800, y 70..600; creation: body x 216..584, y 69..600, head x 488..584, y 24..150.
