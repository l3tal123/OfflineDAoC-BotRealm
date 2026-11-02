# Offline DAoC BotRealm — launcher and live dashboard

This folder is the launcher. It replaces the WinForms launcher
(`development-source/tools/OfflineDaoc.Launcher`, kept in the repository for parity checks only) with a small
.NET host plus a web UI that opens in your browser.

## Running it

Double-click `runtime\OfflineDAoC.exe` in a playable folder (or use a shortcut). A console window appears and the
browser opens `http://127.0.0.1:10310/`:

- **START SERVER** — runs the True 1.65 pre-flight (mode check, content refresh with a database backup) and starts
  `CoreServer.exe`, the same command the old launcher used.
- **ENTER REALM** — starts the new Godot client logged in as the local account, as soon as port 10300 is ready.
- **STOP SERVER** — asks the server to shut down and waits for it (the world saves first).
- **Server tab** — live state, pid, memory, uptime, game-client detection and the server console log.
- **Active Population / World Map** — the live roster and the live map that the previous dashboard had.

Keep the console window open while you play: it is the launcher, and closing it stops the server. A second
double-click does not start a second launcher; it brings the existing dashboard back up.

Options: `OfflineDAoC.exe --port 10310 --root <playable>\runtime --no-open`. `--root` is detected automatically
when the exe sits in `<playable>\runtime`.

## What it is, technically

- **The host** (this program, ~1,500 lines of C#): process control for the server and the game client, the True 1.65
  mode marker and content refresh (the launcher's own `TrueMode` code is recompiled in, not copied), the rolling
  server console (`RollingServerLog`), the local login file, SQLite reads and writes, and the state-165 file broker
  that the server publishes its live snapshots to.
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
| `DashboardData.cs` | Roster + live overlay, counts, zone geometry, painted maps, region suggestion. |
| `WorldReader.cs` | Read-only SQLite queries: bot roster and zones/regions. |
| `LiveBots.cs` | The state-165 file-broker client for the live snapshot. |
| `DashboardOptions.cs` | Command line and install/database/server/maps detection. |
| `wwwroot/` | The UI: `index.html`, `styles.css`, `app.js`, vendored Vue. |
| `../OfflineDaoc.Launcher/` | The old WinForms launcher, recompiled in part and kept for parity checks. |

## Parity with the old launcher

Ported: start server (mode check + content refresh + log), stop server, server state/readiness, ENTER REALM,
single instance, the live roster and the live world map.

Ported in phase 2 (all of them keep the server-stopped gate, enforced by the host):

- Options: XP rate, Make Me a GM, RvR/PvE announcement switches.
- Bots: generate 1/10/100 at level 1 or 50, delete one bot, delete every bot, teleport to a bot (GM + server running).
- Bot Goals Setting: the three level bands, 100% validation per row, save/undo/defaults.

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

Still to come (end of the migration):

- Retire the WinForms project from the repository once parity is confirmed, and move the docs to the new launcher.
- Keep and relic reset is deliberately absent: the current launcher no longer offers it to players either.

## Removing the launcher

Revert to the WinForms launcher by restoring the files backed up in
`<playable>\deployment-backups\web-launcher-<timestamp>\` (the old `OfflineDAoC.*` files and the old
`runtime\dashboard` folder), or delete this folder and rebuild the old project.
