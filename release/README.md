# Release tools

How a BotRealm release is built and published. Players don't need any of this; they use the downloader from the
release page (see [docs/PLAY.md](../docs/PLAY.md)).

A release is the **server and the launcher** (the browser dashboard) with everything the two need to run, and it does
**not** include the game client. The player supplies the client in `new-client\` beside `runtime\` (or points
`runtime\new-client.txt` at it); the launcher finds it there and ENTER REALM starts it. A release install is therefore
`runtime\`, `tools\dotnet\`, `START BOTREALM.cmd` and the player docs; any `new-client\` folder in it was put there by
the player.

| File | What it does |
|---|---|
| `make_release_true165.py` | Packs a release — the server, the launcher and everything they need, **not** the game client — into verified parts under GitHub's 2 GB limit, adds the player docs, and records the baseline. It refuses to pack if it finds a login, a log, a backup, a test character, a developer switch, a world that isn't clean, or a `new-client\` folder. |
| `make_patch_true165.py` | Run after `make_release_true165.py`: packs only the files that changed since any released version (one `--base` per earlier release, using that release's `baseline-<version>.sha256`) and adds a `Patch` section to the version's `download-manifest.json`. The patch is cumulative, so any released version updates in one go. It never carries the player's own data (the world database, the login, logs) or the game client: `new-client\` belongs to the player, who supplies it, and no update touches it. World content changes ship as `runtime/botrealm` JSON, which the launcher applies at START SERVER. |
| `Get-BotRealm.ps1` | The downloader players run. It checks the PC (64-bit Windows 10/11, AVX2, memory, free space, a short folder path), then asks: **1 = Update my game** or **2 = New install**. A new install downloads the parts from the release (resuming if stopped), checks each part's SHA-256, unpacks it, deletes it, and offers a desktop shortcut; it installs into a new folder and never overwrites one. An update hashes every file the patch replaces first and stops if one was changed on that PC (it can then make a fresh install and copy the save over), backs up what it replaces in `patch-backups\`, undoes a failure, and finishes an interrupted update on the next run. It also finds and updates a folder from a release before the BotRealm rename. It never downloads, replaces or removes the game client. |
| `test_patch_true165.py` | End-to-end test of the update on a small fake server folder: a changed file stops it, a failure is undone, a good run gives exactly the new version with the save and the player-supplied `new-client\` untouched, and a half-applied update finishes. |
| `GET-BOTREALM.cmd` | What players double-click. It runs the downloader with Windows PowerShell, and says so if it was started from inside the zip. |
| `make_downloader_true165.py` | Makes the small `Download-BotRealm-v<version>.zip` for the release page. |
| `RELEASE-NOTES-<version>.md` | The release page's text. |

## Making a release

1. **Game package:** `development-source/tools/build_release_true165.py` builds `runtime\`, `tools\dotnet\` and
   `START BOTREALM.cmd` from a working install, with the clean world from `database/make_clean_db.py`. It does not
   include the game client, and nothing else in the release does either.
2. **Client:** not part of this release, and not built here. The game client is its own project, distributed
   separately; players put its folder at `new-client\` beside `runtime\` (or point `runtime\new-client.txt` at it).
   The downloader never fetches, replaces or removes it.
3. **Pack:**
   ~~~
   python -I release/make_release_true165.py --game <game package> --repo . --out <assets> --version <version>
   python -I release/make_patch_true165.py --base <earlier-version>=<baseline-<earlier-version>.sha256> [--base ...] --game <game package> --repo . --out <assets> --version <version>
   python -I release/make_downloader_true165.py --assets <assets> --version <version>
   ~~~
   The release assets are `BotRealm-v<version>-game[-partNN].zip`, `download-manifest.json` and `SHA256SUMS.txt`
   (there are no client parts). The downloader reads the part names from the manifest, so a release that reuses
   unchanged parts from an earlier release still downloads correctly.
4. **Test** a full install from the parts on disk, as a player would:
   `GET-BOTREALM.cmd -LocalAssets <assets> -Mode install`. Then update a copy of a real install of the
   previous version with `-Mode update -Update <copy> -LocalAssets <assets>`. Compare both with
   `<assets>/baseline-<version>.sha256`, and run the update test:
   `python -I release/test_patch_true165.py --work <empty scratch folder> --world <clean opendaoc-165.sqlite3.db> --repo .`.
5. **Publish:** create the release `v<version>` with `RELEASE-NOTES-<version>.md` as its text, and upload everything
   in `<assets>/v<version>/`. Check every uploaded file's SHA-256 against `SHA256SUMS.txt`. Then run the downloader
   against the live release.
6. **Keep the baseline:** keep `<assets>/baseline-<version>.sha256` with the release assets, outside the repository.
   The next release's `--base` comes from it. Do not commit it.

The Python tool filenames keep their `true165` suffix on purpose: they are internal names, like the content file
names (`true165-rules.json`), the database (`opendaoc-165.sqlite3.db`) and the `state-165` folder.
