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

Not ported yet — these still exist only in the WinForms launcher, and are the next steps:

- Options: XP rate, Make Me a GM, announcements, "open folder" helpers.
- Bots: generate, delete/queue delete, delete all, teleport to bot, bulk generation.
- Bot Goals Setting (percentages, save/undo/defaults, server-stopped gate).
- Keep and relic reset.
- Realm Exchange, Server Stats, Bot Inspector detail, Active Groups, Active RvR, Battlegrounds, Realm Events,
  Realm Records screens (the server-side data for all of them is already published or readable).

## Removing the launcher

Revert to the WinForms launcher by restoring the files backed up in
`<playable>\deployment-backups\web-launcher-<timestamp>\` (the old `OfflineDAoC.*` files and the old
`runtime\dashboard` folder), or delete this folder and rebuild the old project.
