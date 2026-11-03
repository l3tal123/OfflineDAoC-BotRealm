# BotRealm traveler's-inn hire window: server protocol

Agreed between the server and the client, 2026-10-30 (server build mode-165 2786ef0). One shared protocol for the original client and the
new client. Built in the new client: `godot/game/companions/` (`companion_net.gd`, the `srv_*_window.gd` windows).

## Server -> client

DebugMode packet 0x21, fixed 128-byte body: [0]=0 [1]=0x48 ('H') [2]=1 (version) [3]=op. Multi-byte values are little-endian.

| op | meaning | fields |
|---|---|---|
| 1 | open | [4] mode (0 innkeeper, 1 healer), [5] used, [6] open, [7] next slot level |
| 2 | close | - |
| 3 | text | [4] slot, [5..126] ASCII, NUL-terminated |
| 4 | numbers, kind 0 (hire page) | [5] race, [6] gender (0 m, 1 f), [7] class, [8] face, [9] face count, [10] hair, [11] hair count, [12] hair colour, [13] colour count, [14..15] model, [16..19] cost in copper, [20] hire enabled |
| 4 | numbers, kind 1 (roster row) | [5] row, [6] state (0 at inn, 1 with you, 2 dead), [7] level, [8] class, [9] race, [10] gender, [11] buttons (1 summon, 2 dismiss, 4 release, 8 revive), [12..13] model, [14..15] object id, [16..17] companion id |
| 5 | slots | [4] used, [5] open, [6] total (7), [7] next level |
| 6 | preview | [4..5] preview NPC object id |

Text slots (op 3): 0 title, 1 race, 2 class, 3 gender, 4 face, 5 hair, 6 name, 7 cost, 8 problem, 9 slots line,
10-12 blurb; roster row r: 16+3r name, 17+3r "Class level", 18+3r where.

## Client -> server (slash commands)

`/companionui race- | race+ | class- | class+ | gender | face- | face+ | hair- | hair+ | look | name | hire | tab N |
name <text> | summon <row> | dismiss <row> | release <row> | revive <row> | close`. RELEASE uses the server's confirm
dialog. GM `/companionui test` opens the window with sample data.

## Rules (owner)

Companion slots open at levels 5, 10, 15, 20, 30, 40, 45 (7 companions, a full group at 45); first hire free, later ones
about 2 h of coin income at the slot level. No tactics bar, no stance tab (only /aggressive and /defensive, group-wide),
no companion bag (equipment only, via the trade window). Revive: healer mode, free. The preview NPC is optional for the
new client: it draws its own classic figure from the numbers in op 4.

## 2026-10-30: one LOOK picker (owner: "merge the buttons into one option")

Classic figures have no separate face and hair: each look is a fixed face + hair pair (a monsters.csv row drawn with the
race/sex's figure). The FACE and HAIR rows are replaced by one LOOK row:
- `/companionui look-` / `look+` step through the race/sex's classic looks; `look` (NEW LOOK) picks a random one.
- The looks: every monsters.csv row whose col 2 (monnifs id) is the race/sex's figure, one per distinct face (col 18) +
  hair (col 4) + head geom (col 20), in id order (Briton male: 20). The model sent is that row's id.
- Text slot 4 = "Look n of N"; slot 5 unused (empty). Numbers [8] look index, [9] look count; [10] / [11] 0.
- face+/face-/hair+/hair- may stay as aliases of look+/look-.
New client: SrvHireWindow shows RACE / CLASS / GENDER / LOOK (+ NEW LOOK); figure.json "looks" holds every such row.
