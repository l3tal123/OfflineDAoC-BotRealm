# Release tools

How a True 1.65 release is built and published. Players don't need any of this; they use the downloader from the
release page (see [docs/PLAY.md](../docs/PLAY.md)).

| File | What it does |
|---|---|
| `make_release_true165.py` | Packs a release into verified parts under GitHub's 2 GB limit, adds the player docs, and records the baseline. It refuses to pack if it finds a login, a log, a backup, a test character, a developer switch or a world that isn't clean. |
| `make_patch_true165.py` | Run after `make_release_true165.py`: packs only the files that changed since any released version (one `--base` per release, from `release-baselines/`) and adds a `Patch` section to the version's `download-manifest.json`. The patch is cumulative, so any released version updates in one go. It never carries the player's own data (the world database, the login, logs, the client's `save\`); world content changes ship as `runtime/true165` JSON, which the launcher applies at START SERVER. |
| `Get-OfflineDAoC-True165.ps1` | The downloader players run. It checks the PC (64-bit Windows 10/11, AVX2, Vulkan, memory, free space, a short folder path), then asks: **1 = Update my game** or **2 = New install**. A new install downloads the parts from the release (resuming if stopped), checks each part's SHA-256, unpacks it, deletes it, and offers a desktop shortcut; it installs into a new folder and never overwrites one. An update hashes every file the patch replaces first and stops if one was changed on that PC (it can then make a fresh install and copy the save over), backs up what it replaces in `patch-backups\`, undoes a failure, and finishes an interrupted update on the next run. |
| `test_patch_true165.py` | End-to-end test of the update on a small fake game: a changed file stops it, a failure is undone, a good run gives exactly the new version with the save untouched, and a half-applied update finishes. |
| `GET-OFFLINE-DAOC-TRUE165.cmd` | What players double-click. It runs the downloader with Windows PowerShell, and says so if it was started from inside the zip. |
| `make_downloader_true165.py` | Makes the small `Download-OfflineDAoC-True165-v<version>.zip` for the release page. |
| `RELEASE-NOTES-<version>.md` | The release page's text. |

## Making a release

1. **Game package:** `development-source/tools/build_release_true165.py` builds `runtime\`, `tools\dotnet\` and
   `START OFFLINE DAOC TRUE 1.65.cmd` from a working install, with the clean world from `database/make_clean_db.py`.
2. **Client package:** `source/new-client/tools/stage_true165_release.py` stages `new-client\` (the game, its
   converted content and the engine; no `save\`, `dev_mode.txt` or tests).
3. **Pack:**
   ```
   python -I release/make_release_true165.py --game <game package> --client <folder holding new-client> --repo . --out <assets> --version <version>
   python -I release/make_patch_true165.py --base 0.1=release-baselines/0.1/package.sha256 [--base 0.2=...] --game <game package> --client <folder holding new-client> --repo . --out <assets> --version <version>
   python -I release/make_downloader_true165.py --assets <assets> --version <version>
   ```
4. **Test** a full install from the parts on disk, as a player would:
   `GET-OFFLINE-DAOC-TRUE165.cmd -LocalAssets <assets> -Mode install`. Then update a copy of a real install of the
   previous version with `-Mode update -Update <copy> -LocalAssets <assets>`. Compare both with
   `<assets>/baseline-<version>.sha256`.
5. **Publish:** create the release `v<version>` with `RELEASE-NOTES-<version>.md` as its text, and upload everything
   in `<assets>/v<version>/`. Check every uploaded file's SHA-256 against `SHA256SUMS.txt`. Then run the downloader
   against the live release.
6. **Baseline:** commit `<assets>/baseline-<version>.sha256` as `release-baselines/<version>/package.sha256`.
