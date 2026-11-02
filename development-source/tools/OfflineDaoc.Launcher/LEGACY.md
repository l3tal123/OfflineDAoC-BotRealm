# Legacy: the WinForms launcher (do not deploy)

**This launcher is retired.** The launcher players run is the web launcher in
[`../OfflineDaoc.Dashboard`](../OfflineDaoc.Dashboard): a .NET host (`OfflineDAoC.exe`) that owns the server and the
game client and serves the dashboard in the browser.

This project is kept in the repository for two reasons only:

1. **Parity reference.** Everything it does was ported to the web launcher screen by screen; if something behaves
   differently, this is the source of truth to compare against.
2. **Upstream merges.** `development-source/tools/OfflineDaoc.Launcher` is upstream's code. Keeping it means upstream
   changes to it merge cleanly instead of colliding with a deletion, and it still compiles.

It is **not** part of the deploy path and is not shipped in a playable folder. Its UI-free files are compiled into
the web launcher by relative path (`TrueMode.cs`, `RollingServerLog.cs`, `NewClient.cs`, `XpRateSetting.cs`,
`ServerPropertyWrite.cs`, `BotCharacterGenerator.cs`), so the two cannot drift silently.

Do not add new features here. New launcher work goes in `development-source/tools/OfflineDaoc.Dashboard`.
