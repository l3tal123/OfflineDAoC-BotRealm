TRUE 1.65 AUDIT FIX PASS (2026-10-31) - how the installed content was made

The owner read the spawn/drop audit (development-source\research\period-data\TRUE 1.65 SPAWN AND DROP AUDIT 2026-10-31\)
and said to fix it. Rules: true 1.65 only (cutoff 1 Nov 2003, no Trials of Atlantis stats anywhere), zero
Atlas/Phoenix-era (freeshard) drops, Uthgard counts as a valid source, live Allakhazam only with a pre-Nov-2003
comment, fill loot gaps from period data and invent only rates, marked.

What is installed in runtime\true165\ (and this tools\claude-version\true165\ copy):
- true165-rules.json
  - settings.disabled_regions 27;165;233-242;244 (duplicate Summoner's Hall 233, post-1.65 Passage of Conflict 244)
  - remove.mob_classes: SpectralProvisionerSpawner no longer removed (real period Caer Sidi boss)
  - remove.where: 8 freeshard / dead loot rules (drops\where_rules.json) + the Nisse's Lair haunt rule (spawns\)
  - updates: 7,237 item stat fixes (stats\updates_final.json), 158 level / faction / haunt updates (spawns\updates.json),
    29 kill-task drops made normal % drops (drops\updates.json), 1 night/day spawn flag fix (coord\updates.json)
- drops165.json: drops pass 2 (drops\): Uthgard measured drops matched by realm + period Known Loot + period item pages
- add-dropitems165.json: 359 items built from period item pages
- add-restoredspawns165.json: 16,838 period spawns the 1.65 conversion removed (PackageID true165-restored)
- add-npctemplates165.json (6 templates for shared-template level fixes), add-spectralprovisioner165.json
- coin165.json (build_coin165.py): per-monster coin chance, read by the server (mode165\Coin165.cs)

Folders here: stats\, spawns\, drops\ (each agent's scripts, reports, csv lists; scripts were written for the
working folder outside the repository, so their paths point there) and coord\ (dryrun.py = the launcher's Refresh in Python, and the
ghost flag update). The old build_drops165.py made the first-pass drops165.json; don't re-run it, it would undo pass 2.
Dry run: two refreshes on a copy of the 2026-10-31 world; the second changed nothing; quick_check ok.
Backup of the content before this: development-source\server\deployment-backups\true165-content-before-audit-fixes-*.
