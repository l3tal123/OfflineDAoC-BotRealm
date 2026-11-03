# BotRealm — launcher and live dashboard

This folder is the launcher: a small .NET host plus a web UI that opens in your browser. It replaced the old WinForms
launcher, which is no longer in the repository.

## Running it

Double-click `runtime\BotRealm.exe` in a playable folder (or use a shortcut). A console window appears and the
browser opens `http://127.0.0.1:10310/`:

- **START SERVER** — runs the pre-flight (database check, content refresh from `runtime/botrealm` with a database backup) and starts
  `CoreServer.exe`, the same command the old launcher used.
- **ENTER REALM** — starts the new Godot client logged in as the local account, as soon as port 10300 is ready.
- **STOP SERVER** — asks the server to shut down and waits for it (the world saves first).
- **Server tab** — live state, pid, memory, uptime, game-client detection and the server console log.
- **Active Population / World Map** — the live roster, and the world map: a paintable, zoomable realm overview or any
  zone's own map, with every online bot as a dot (see *The world map* below).

Keep the console window open while you play: it is the launcher, and closing it stops the server. A second
double-click does not start a second launcher; it brings the existing dashboard back up.

Options: `BotRealm.exe --port 10310 --root <playable>\runtime --no-open`. `--root` is detected automatically
when the exe sits in `<playable>\runtime`.

## What it is, technically

- **The host** (this program, ~1,500 lines of C#): process control for the server and the game client, the world-mode
  marker and content refresh (the launcher's own `TrueMode` code, in `Launcher/`), the rolling server console
  (`RollingServerLog`), the local login file, SQLite reads and writes, and the state-165 file broker that the server
  publishes its live snapshots to.
- **The web UI** (`dashboard\wwwroot`): plain HTML/CSS/JS with Vue 3's browser build vendored, so there is no build
  step, no npm at run time and nothing to install. It is served by a plain `TcpListener` on `127.0.0.1` — no
  admin rights, no URL reservations, no ASP.NET runtime, no open firewall port.
- **Read endpoints** are same-origin only (a web page cannot read another origin's responses) and the `Host` header
  is checked against DNS rebinding. **Launcher actions** (start/stop server, enter realm) additionally require the
  per-run token that `/api/status` hands only to the page the launcher itself served.

## Files

| Path | What it is |
|---|---|
| `Program.cs` | Entry point: single instance, browser, lifecycle, clean shutdown. |
| `LauncherHost.cs` | Server and client process control, content refresh, console buffer, status. |
| `DashboardHost.cs` | The loopback HTTP host: static files, JSON data endpoints, launcher actions. |
| `DashboardData.cs` | Roster + live overlay, counts, bot zone placement, and the world-map payload (`/api/world`). |
| `MapCatalog.cs` | The client's painted map catalog (`godot/content/maps/maps.json`): painted zones and their squares on each realm map. |
| `WorldReader.cs` | Read-only SQLite queries: bot roster and zones/regions. |
| `LiveBots.cs` | The state-165 file-broker client for the live snapshot. |
| `DashboardOptions.cs` | Command line and install/database/server/maps detection. |
| `wwwroot/` | The UI: `index.html`, `styles.css`, `app.js`, vendored Vue. |
| `Launcher/` | The UI-free launcher logic that came from the old WinForms project: content refresh, rolling console log, client launcher, XP rate, server-property writes, bot generator. |
| `../../server/GameServer/bots/autonomous/` | Two server files compiled in by path (`BotGoalSettings.cs`, `RealmEventRecordStore.cs`), so they cannot drift from the server. |

## Parity with the old launcher

Ported: start server (mode check + content refresh + log), stop server, server state/readiness, ENTER REALM,
single instance, the live roster and the live world map.

Ported in phase 2 (all of them keep the server-stopped gate, enforced by the host):

- Options: XP rate, Make Me a GM, RvR/PvE announcement switches.
- Bots: generate 1/10/100 level-1 bots (they earn their levels in the world), delete one bot, delete every bot, teleport to a bot (GM + server running).
- Bot Goals Setting: the three level bands, 100% validation per row, save/undo/defaults. The parts a band cannot use
  (RvR for levels 1-19, battlegrounds for level 50) are locked at 0% and left out of the row total.

Ported in phase 3 (read-only screens, all fed by the server's published snapshots or read-only reads):

- Groups, RvR, Battlegrounds, Realm Events, Realm Records, Realm Exchange, Server Stats and the Bot Inspector.
  The snapshots are fetched through the same state-165 file broker the launcher uses; the exchange is a read-only
  query on the world database; the records come from the separate event ledger.
- Active RvR is the desktop launcher's screen, not a second realm-events page: it lists the bots whose current
  objective is RvR (name / meet-up clock, realm, class, level, zone, formation, phase, task left, current objective)
  with realm and text filters and sortable headings. Keeps, relics and sieges stay in Realm Events.
- Groups is the desktop launcher's card list, rebuilt as a responsive grid: one card per active party with its
  leader, realm, size, level range, shared goal, route status, the group's task or meet-up clock, and the member
  roster with roles and per-member meet-up clocks. The cards have no inner scrollbars, so nothing is clipped.
  The clocks (meet-up, task, staging) are the launcher's own display clock: milliseconds from the roster, ticked
  locally once a second, with no extra database or server reads.

The old WinForms project is gone from the repository; the launcher-only logic it carried lives in `Launcher/` and the
docs describe this launcher. Keep and relic reset is deliberately absent: the launcher no longer offers it to
players either.

## The world map

The map is built from the new client's own painted maps, which every install already carries under
`new-client/godot/content/maps` (`maps.json` lists the zones and where the client's realm maps place their squares):

- **Realm overviews** (Albion, Avalon, Midgard, Aegir, Hibernia, Hy Brasil) draw the client's painted realm map and
  put every online bot where it stands inside its zone's square. A second layer, *Zone maps*, draws the zone
  pictures themselves composed at the same squares, so the terrain is readable too.
- **Frontier overviews** (one per realm) cover the old frontiers, which the client's realm maps do not place: their
  squares come from the world database.
- **Zone maps** show one zone's painted map at full size, for every zone the client has a picture of: open world,
  capitals and dungeons (a capital's picture covers only its playable square; bots outside it are not shown there).
- The **filter** lists the realm and frontier overviews, and then only the zones that currently have bots in them,
  with counts. Any zone on an overview can also be opened by clicking its square.
- Hovering a bot shows its name, level, class, realm, zone, task and the "click to select" hint; hovering a zone
  square shows its bot count and opens its map on click. Clicking a bot switches to Active Population with that bot
  selected. `SHOW ON MAP` in the roster does the reverse: it opens the overview (or the capital/dungeon map) that
  places the bot, zooms to it and follows it until the map is dragged or zoomed.
- Wheel zooms around the cursor, dragging pans, **Reset view** fits the map again. The map polls only the view it
  shows, so it stays small even with three thousand bots in the world.

The painted maps are optional: without them the map falls back to zone outlines from the world database (the same
six realm overviews, no zone pictures).

## Removing the launcher

Restore the files backed up in `<playable>\deployment-backups\web-launcher-<timestamp>\` (the old `BotRealm.*`
files and the old `runtime\dashboard` folder) and delete `runtime\dashboard` to go back to whatever launcher that
backup held. The old WinForms project is not in the repository any more, so there is nothing else to rebuild.
