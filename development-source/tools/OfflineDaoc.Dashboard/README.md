# Offline DAoC BotRealm — web dashboard (preview)

A separate, removable web front-end for the live world: the **Active Population** roster (50 per page, searchable,
sortable) and the **live World Map** (bot dots drawn over the new client's painted zone maps). It is a preview: it
replaces nothing yet, and the WinForms launcher is untouched.

## Why it is its own folder

Everything is in this folder, so removing the feature is one delete:

- **In the repository:** delete `development-source/tools/OfflineDaoc.Dashboard/`.
- **In a playable folder:** delete `runtime/dashboard/`.

Nothing else references it: no launcher code, no server code, no client code, no build step in the solution.

## Running it

Deployed, it sits in `<playable>\runtime\dashboard\`:

```
START DASHBOARD.cmd
```

That opens `http://127.0.0.1:10310/` in the default browser. Other options:

```
OfflineDaoc.Dashboard.exe --port 10310 --root <playable>\runtime --maps <playable>\new-client\godot\content\maps --no-open
```

`--root` and `--maps` are detected automatically when the exe sits in `<playable>\runtime\dashboard`.

Building it from source:

```
dotnet build development-source/tools/OfflineDaoc.Dashboard/OfflineDaoc.Dashboard.csproj -c Release
```

The build output (with `wwwroot/` and the SQLite runtime files) is what gets copied into `runtime/dashboard/`.

## What it is, technically

- **Read-only.** It opens `runtime/data/opendaoc-165.sqlite3.db` read-only for the roster and the zone geometry, and
  reads the live snapshot from the server through the **existing** state-165 file broker (`bot-world.request` →
  `state-165/bot-world.json`). It writes nothing except that one request file, exactly like the launcher does. There
  is no POST endpoint: no game state can be changed from the web UI.
- **Loopback only.** A plain `TcpListener` on `127.0.0.1`, no URL reservations and no admin rights, no ASP.NET
  runtime, nothing on the LAN. The `Host` header is checked so a web page cannot reach it (DNS rebinding guard).
- **No build step / no npm.** Vue 3's browser build is vendored in `wwwroot/vendor/vue.global.prod.js` (MIT, see
  `VUE-LICENSE.txt`); the UI is plain `index.html` + `styles.css` + `app.js`.
- **Endpoints:** `GET /api/overview`, `GET /api/population`, `GET /api/world`, `GET /api/health`, and
  `GET /zone-map/<zoneId>` for the painted zone maps.

## Files

| Path | What it is |
|---|---|
| `Program.cs` | Entry point: options, banner, lifecycle. |
| `DashboardOptions.cs` | Command line, install/database/server/maps detection. |
| `DashboardHost.cs` | The loopback HTTP host: static files, JSON endpoints, zone images, Host check. |
| `DashboardData.cs` | Builds the JSON: roster + live overlay, counts, zone geometry, region suggestion. |
| `WorldReader.cs` | Read-only SQLite queries: roster and zones/regions. |
| `LiveBots.cs` | The state-165 file-broker client for the live snapshot. |
| `wwwroot/` | The UI. `app.js` holds both screens; `vendor/` holds Vue. |

## Known limits (preview)

- Read-only: no start/stop, no options, no Bot Inspector detail view, no Realm Exchange yet. Those stay in the
  launcher until this front-end is proven.
- Hired companions are not listed (they live in their own table; the launcher shows them).
- The map draws the zone rectangles the database describes, with painted maps where the client content has them
  (`z<zoneId>.png`); the launcher's zoom-to-bot and follow behaviours are not ported.
- The server's bot snapshot is refreshed at most every couple of seconds; when the server is stopped the saved
  roster is shown and the map has no dots.
