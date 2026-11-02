# Offline DAoC True 1.65 downloader: installs the game into a NEW folder, or updates an installed game in place.
#
# GET-OFFLINE-DAOC-TRUE165.cmd runs this. It checks the PC, then either
#  - New install: downloads the complete game in verified parts (resuming if stopped) into a new folder, or
#  - Update (from v0.2 on): updates an installed game in the folder it is in, with a small patch. Every file it would
#    replace is checked against the released versions first; nothing is touched if any was changed. Everything it
#    replaces is backed up in patch-backups\, and a failed update is undone. Saves are never touched.
param(
    # New install: where the game goes (a new folder). Default: OfflineDAoC-True165 next to this downloader's folder,
    # or <drive>:\Games\OfflineDAoC-True165 when that path would be too long.
    [string]$Destination = '',
    # Update: the installed game folder ('pick' opens a folder picker). Default: ask.
    [string]$Update = '',
    [string]$ReleaseVersion = '0.2',
    # Testing only: read the release files from <folder>\v<version>\ instead of GitHub.
    [string]$LocalAssets = '',
    # Testing only: answer the questions with yes (and 'install' or 'update' instead of asking which).
    [switch]$Yes,
    [ValidateSet('ask','install','update')]
    [string]$Mode = 'ask',
    [switch]$NoShortcut,
    # Fork installs: update in place even where files were changed on this PC (the fork's own launcher, server and
    # content builds), after backing them up. The fork's builds have to be put back after the update; without the
    # switch the update stops and touches nothing.
    [switch]$AllowLocalChanges
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$Repo = 'shadowofze/OfflineDAoC-True165'
$Product = 'Offline DAoC True 1.65'
$StartFile = 'START OFFLINE DAOC TRUE 1.65.cmd'
# The new client keeps its graphics (shader) cache in new-client\save, up to 200 characters deeper, and Windows refuses
# paths over 260. So the game folder's own path should stay within 42 characters, or every start recompiles the shaders.
$MaxGamePath = 42
$cache = Join-Path $PSScriptRoot (Join-Path '.downloads' "v$ReleaseVersion")

function Say([string]$text = '') { Write-Host $text }
function Ask([string]$question) {
    if ($Yes) { return $true }
    $answer = Read-Host "$question (Y/N)"
    return ($answer -match '^\s*[Yy]')
}
function Stop-Install([string]$why) {
    Say ''
    Write-Host $why -ForegroundColor Yellow
    exit 1
}
function Get-Sha([string]$path) { return (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }

# ---------------------------------------------------------------- the PC
Say "$Product v$ReleaseVersion - downloader"
Say ''
Say 'Checking your PC...'
if (![Environment]::Is64BitOperatingSystem) { Stop-Install 'This game needs 64-bit Windows 10 or 11.' }
if ([Environment]::OSVersion.Version.Major -lt 10) { Stop-Install 'This game needs Windows 10 or 11.' }
Add-Type -Namespace OfflineDaoc -Name Cpu -MemberDefinition '[DllImport("kernel32.dll")] public static extern bool IsProcessorFeaturePresent(uint feature);'
if (![OfflineDaoc.Cpu]::IsProcessorFeaturePresent(40)) {   # 40 = PF_AVX2_INSTRUCTIONS_AVAILABLE
    Stop-Install 'Your CPU does not support AVX2, which the bots'' navigation needs (most CPUs from 2014 onward have it). The game cannot run on this PC.'
}
if (!(Test-Path -LiteralPath (Join-Path $env:SystemRoot 'System32\vulkan-1.dll'))) {
    Say ''
    Say 'Vulkan was not found on this PC. The game is made for Vulkan; without it, it tries DirectX 12 instead.'
    Say 'Installing the latest graphics driver from NVIDIA, AMD or Intel usually adds Vulkan.'
    if (!(Ask 'Continue anyway?')) { Stop-Install 'Nothing was downloaded.' }
}
$memory = (Get-CimInstance Win32_ComputerSystem -ErrorAction SilentlyContinue).TotalPhysicalMemory
if ($memory -and $memory -lt 15GB) {
    Say ("This PC has {0:N0} GB of memory; 16 GB is recommended. The game may run slowly with many bots." -f ($memory / 1GB))
}

# ---------------------------------------------------------------- downloads
function Get-Asset([string]$name, [string]$outFile, [int64]$bytes = -1) {
    if ($LocalAssets) {
        Copy-Item -LiteralPath (Join-Path (Join-Path $LocalAssets "v$ReleaseVersion") $name) -Destination $outFile -Force
        return
    }
    $url = "https://github.com/$Repo/releases/download/v$ReleaseVersion/$name"
    # Only a shorter, unfinished file is continued; anything else starts over.
    $existing = Get-Item -LiteralPath $outFile -ErrorAction SilentlyContinue
    if ($existing -and ($bytes -lt 0 -or $existing.Length -ge $bytes)) { Remove-Item -LiteralPath $outFile -Force }
    $curl = Join-Path $env:SystemRoot 'System32\curl.exe'
    if (Test-Path -LiteralPath $curl) {
        & $curl --location --fail --retry 10 --retry-delay 5 --continue-at - --progress-bar --output $outFile $url
        if ($LASTEXITCODE -eq 0) { return }
        Say "The download of $name stopped (code $LASTEXITCODE); trying another way..."
        Remove-Item -LiteralPath $outFile -Force -ErrorAction SilentlyContinue
    }
    Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $outFile
}

# Download (cached, resumable) and verify one part; returns its local path.
function Get-Part($part, [int]$index, [int]$count) {
    if ($part.Name -notmatch '^[A-Za-z0-9._-]+\.zip$' -or $part.SHA256 -notmatch '^[A-Fa-f0-9]{64}$') { Stop-Install 'The download list has an invalid entry.' }
    $file = Join-Path $cache $part.Name
    $ok = (Test-Path -LiteralPath $file) -and (Get-Item -LiteralPath $file).Length -eq $part.Bytes
    if ($ok) { $ok = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -eq $part.SHA256 }
    if (!$ok) {
        Say ''
        Say ("Downloading part {0} of {1} ({2:N2} GB)..." -f $index, $count, ($part.Bytes / 1GB))
        try { Get-Asset $part.Name $file ([int64]$part.Bytes) } catch {
            Stop-Install "The download stopped: $($_.Exception.Message). Run this file again to continue; finished parts are kept."
        }
        Say 'Checking it...'
        if ((Get-Item -LiteralPath $file).Length -ne $part.Bytes -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $part.SHA256) {
            Remove-Item -LiteralPath $file -Force -ErrorAction SilentlyContinue
            Stop-Install 'That part did not download correctly. Run this file again to retry it; finished parts are kept.'
        }
    }
    return $file
}

# Unpack a verified part's <prefix>/<rel> entries into $target (every entry is checked before anything is written).
function Expand-Part([string]$file, [string]$prefix, [string]$target) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $targetPrefix = $target.TrimEnd('\') + '\'
    $zip = [IO.Compression.ZipFile]::OpenRead($file)
    try {
        foreach ($entry in $zip.Entries) {
            $name = $entry.FullName.Replace('\','/')
            if (!$name.StartsWith($prefix,[StringComparison]::Ordinal) -or $name.Contains(':') -or $name.Contains('/../')) { Stop-Install 'A downloaded part has an unexpected layout.' }
            $rel = $name.Substring($prefix.Length)
            if ($rel -and ![IO.Path]::GetFullPath((Join-Path $target $rel)).StartsWith($targetPrefix,[StringComparison]::OrdinalIgnoreCase)) { Stop-Install 'A downloaded part has an unsafe entry.' }
        }
        foreach ($entry in $zip.Entries) {
            $rel = $entry.FullName.Replace('\','/').Substring($prefix.Length)
            if (!$rel -or $rel.EndsWith('/')) { continue }
            $path = Join-Path $target $rel
            [IO.Directory]::CreateDirectory((Split-Path -Parent $path)) | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $path, $true)
        }
    } finally { $zip.Dispose() }
}

New-Item -ItemType Directory -Path $cache -Force | Out-Null
$manifestFile = Join-Path $cache 'download-manifest.json'
Say ''
Say 'Getting the list of files...'
try { Get-Asset 'download-manifest.json' $manifestFile } catch {
    Stop-Install "The download list could not be fetched. Check your internet connection and run this again. ($($_.Exception.Message))"
}
$manifest = Get-Content -LiteralPath $manifestFile -Raw | ConvertFrom-Json
if ($manifest.Version -ne $ReleaseVersion -or !$manifest.RootFolder -or !$manifest.Parts) { Stop-Install 'The download list is not what this downloader expects. Get the newest downloader from the release page.' }
$patch = $manifest.Patch

# ---------------------------------------------------------------- new install
function Get-DefaultFolder {
    $folder = Join-Path (Split-Path -Parent $PSScriptRoot) 'OfflineDAoC-True165'
    if ([IO.Path]::GetFullPath($folder).Length -gt $MaxGamePath) {
        $folder = Join-Path ([IO.Path]::GetPathRoot($PSScriptRoot)) 'Games\OfflineDAoC-True165'
    }
    return [IO.Path]::GetFullPath($folder)
}

function Test-Folder([string]$game) {
    if ($game -match 'OneDrive') {
        Say ''
        Say "The game folder would be inside OneDrive: $game"
        Say 'OneDrive can move or lock the game''s files while it runs. A folder such as C:\Games is safer.'
        if (!(Ask 'Install here anyway?')) { Stop-Install 'Nothing was downloaded. Move this downloader folder to a short path such as C:\Games\OfflineDAoC and run it again.' }
    }
    if ($game.Length -gt $MaxGamePath) {
        Say ''
        Say "The game folder's path is long ($($game.Length) characters): $game"
        Say "The game works best within $MaxGamePath characters, such as C:\Games\OfflineDAoC-True165. With a longer path it"
        Say 'still runs, but loads slowly (it cannot keep its graphics cache), and some files may not install.'
        if (!(Ask 'Install here anyway?')) { Stop-Install 'Nothing was downloaded. Run this again without choosing a folder, or choose a shorter one.' }
    }
    if ($game -match '[^\x20-\x7E]') {
        Say ''
        Say "The game folder's path has letters outside plain English ($game). Some tools the game uses may not handle that."
        if (!(Ask 'Install here anyway?')) { Stop-Install 'Nothing was downloaded. Move this downloader folder to a path such as C:\Games\OfflineDAoC and run it again.' }
    }
}

function Install-Game([string]$game) {
    $done = Join-Path $game '.install'
    if ((Test-Path -LiteralPath $game) -and !(Test-Path -LiteralPath $done)) {
        if (Test-Path -LiteralPath (Join-Path $game $StartFile)) {
            Stop-Install "$Product is already installed in $game. To update it, run this file again and choose 1 (Update my game)."
        }
        Stop-Install "The folder $game already exists. Move or rename it, or run this downloader from another folder. An existing folder is never overwritten."
    }
    Test-Folder $game
    $parts = @($manifest.Parts)
    $totalDownload = [int64](($parts | Measure-Object -Property Bytes -Sum).Sum)
    $installed = [int64]$manifest.InstalledBytes
    $drive = New-Object IO.DriveInfo ([IO.Path]::GetPathRoot($game))
    $need = $installed + [int64](($parts | Measure-Object -Property Bytes -Maximum).Maximum) + 1GB
    if (!(Test-Path -LiteralPath $done) -and $drive.AvailableFreeSpace -lt $need) {
        Stop-Install ("The game needs about {0:N0} GB of free space on {1} while it installs ({2:N0} GB free). Free up space, or move this downloader folder to another drive, and run it again." -f ($need / 1GB), $drive.Name, ($drive.AvailableFreeSpace / 1GB))
    }
    Say ''
    Say "This installs $Product v$ReleaseVersion into this new folder:"
    Say "  $game"
    Say ("Download: {0:N1} GB in {1} parts. Installed size: {2:N1} GB." -f ($totalDownload / 1GB), $parts.Count, ($installed / 1GB))
    Say 'If it stops (or you close this window), run the same file again: it continues where it stopped.'
    if (!(Test-Path -LiteralPath $done) -and !(Ask 'Start the download?')) { Stop-Install 'Nothing was downloaded.' }
    [IO.Directory]::CreateDirectory($done) | Out-Null
    $index = 0
    foreach ($part in $parts) {
        $index++
        $marker = Join-Path $done ($part.Name + '.done')
        if (Test-Path -LiteralPath $marker) { continue }
        $file = Get-Part $part $index $parts.Count
        Say ("Unpacking part {0} of {1}..." -f $index, $parts.Count)
        Expand-Part $file ($manifest.RootFolder + '/') $game
        Set-Content -LiteralPath $marker -Value $part.SHA256 -Encoding ASCII
        Remove-Item -LiteralPath $file -Force
    }
    foreach ($rel in @($manifest.Required)) {
        if (!(Test-Path -LiteralPath (Join-Path $game $rel) -PathType Leaf)) { Stop-Install "The install is missing $rel. Run this file again." }
    }
    Remove-Item -LiteralPath $done -Recurse -Force
}

# ---------------------------------------------------------------- update in place
function Test-NeverTouch([string]$rel) {
    $low = $rel.ToLowerInvariant()
    foreach ($p in @($patch.NeverTouch)) {
        if ($low -eq $p.TrimEnd('/') -or $low.StartsWith($p)) { return $true }
    }
    return $false
}

function Find-Installed {
    $found = @()
    foreach ($folder in @((Join-Path (Split-Path -Parent $PSScriptRoot) 'OfflineDAoC-True165'),
                          (Join-Path ([IO.Path]::GetPathRoot($PSScriptRoot)) 'Games\OfflineDAoC-True165'))) {
        $full = [IO.Path]::GetFullPath($folder)
        if ((Test-Path -LiteralPath (Join-Path $full $StartFile)) -and ($found -notcontains $full)) { $found += $full }
    }
    return $found
}

function Get-InstalledVersion([string]$game) {
    foreach ($rel in @('VERSION.txt', 'new-client\VERSION.txt')) {
        $file = Join-Path $game $rel
        if (Test-Path -LiteralPath $file) {
            $text = (Get-Content -LiteralPath $file -TotalCount 1)
            if ($text -match 'v(\d+\.\d+)') { return $Matches[1] }
        }
    }
    return '?'
}

# What has to happen to each patch file in this folder: 'skip' (already new), 'replace' (a released version),
# 'add' (missing), or 'changed' (a version no release had: changed on this PC).
function Get-Plan([string]$game) {
    $plan = @()
    foreach ($f in @($patch.Files)) {
        if (Test-NeverTouch $f.Path) { Stop-Install 'The update list has an invalid entry.' }
        $path = Join-Path $game $f.Path
        if (!(Test-Path -LiteralPath $path -PathType Leaf)) { $plan += [pscustomobject]@{ File = $f; Action = 'add' }; continue }
        $have = Get-Sha $path
        if ($have -eq $f.Sha256) { $action = 'skip' }
        elseif (@($f.From) -contains $have) { $action = 'replace' }
        else { $action = 'changed' }
        $plan += [pscustomobject]@{ File = $f; Action = $action }
    }
    return $plan
}

function Stop-Running([string]$game) {
    $prefix = $game.TrimEnd('\') + '\'
    $running = @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
        try { $_.Path -and $_.Path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) } catch { $false } })
    if ($running.Count -gt 0) {
        Stop-Install ("The game is running from that folder ({0}). Quit the game, click STOP SERVER in the launcher, close the launcher, and run this again." -f (($running | ForEach-Object { $_.ProcessName } | Sort-Object -Unique) -join ', '))
    }
}

# Copy a player's progress from an old folder into a fresh install (the old folder is only read).
function Copy-Progress([string]$old, [string]$new) {
    foreach ($rel in @('runtime\data\opendaoc-165.sqlite3.db', 'runtime\data\opendaoc-165.sqlite3.db-wal',
                       'runtime\data\opendaoc-165.sqlite3.db-shm', 'runtime\account.txt')) {
        $src = Join-Path $old $rel
        $dst = Join-Path $new $rel
        if (Test-Path -LiteralPath $dst) { Remove-Item -LiteralPath $dst -Force }
        if (Test-Path -LiteralPath $src) { Copy-Item -LiteralPath $src -Destination $dst -Force }
    }
    foreach ($rel in @('runtime\server\state-165', 'new-client\save')) {
        $src = Join-Path $old $rel
        if (Test-Path -LiteralPath $src) {
            & robocopy.exe $src (Join-Path $new $rel) /E /COPY:DAT /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
            if ($LASTEXITCODE -ge 8) { Stop-Install "Copying $rel from your old folder failed. Your old folder was not changed." }
            $global:LASTEXITCODE = 0
        }
    }
}

function Update-Game([string]$game) {
    if (!(Test-Path -LiteralPath (Join-Path $game $StartFile)) -or !(Test-Path -LiteralPath (Join-Path $game 'runtime\OfflineDAoC.exe'))) {
        Stop-Install "No $Product game was found in $game. Choose the folder that has $StartFile in it."
    }
    Stop-Running $game
    $state = Join-Path $game '.patch'
    $from = Get-InstalledVersion $game
    Say ''
    Say "Checking your game in $game (v$from)..."
    $plan = Get-Plan $game
    $changed = @($plan | Where-Object { $_.Action -eq 'changed' })
    $work = @($plan | Where-Object { $_.Action -eq 'replace' -or $_.Action -eq 'add' })
    $removes = @()
    foreach ($r in @($patch.Remove)) {
        $path = Join-Path $game $r.Path
        if ((Test-Path -LiteralPath $path -PathType Leaf) -and (@($r.From) -contains (Get-Sha $path))) { $removes += $r }
    }
    if ($changed.Count -gt 0 -and $AllowLocalChanges) {
        # Fork installs (fork addition): the fork replaces the launcher and server builds, so those files never match a
        # released version. Treat them like ordinary files to replace - they are backed up and can be put back - and
        # carry on with the update.
        Say ''
        Say "$($changed.Count) file(s) this update replaces were changed on this PC (the fork's own builds):"
        $changed | ForEach-Object { Say "  $($_.File.Path)" }
        Say 'Replacing them after backing them up, because -AllowLocalChanges was given.'
        Say 'The fork builds have to be put back afterwards; without that, the game runs the released versions.'
        foreach ($item in $changed) { $item.Action = 'replace' }
        $changed = @()
        $work = @($plan | Where-Object { $_.Action -eq 'replace' -or $_.Action -eq 'add' })
    }
    if ($changed.Count -gt 0) {
        $list = Join-Path $game 'UPDATE-CHANGED-FILES.txt'
        $changed | ForEach-Object { $_.File.Path } | Set-Content -LiteralPath $list -Encoding UTF8
        Say ''
        Say "$($changed.Count) file(s) this update would replace were changed on this PC since they were installed, so the"
        Say "update can't safely be applied here. Nothing was changed. The list is in $list."
        Say ''
        Say 'You can put the original files back (for example from a fresh download) and run this again, or make a fresh'
        Say "v$ReleaseVersion install in a new folder and bring your save over (your account, characters, companions, bots"
        Say 'and settings). Your old folder is only read, never changed.'
        if (!(Ask 'Make a fresh install and bring your save over?')) { Stop-Install 'Nothing was changed.' }
        $new = [IO.Path]::GetFullPath("$game-v$ReleaseVersion")
        Install-Game $new
        Say 'Bringing your save over...'
        Copy-Progress $game $new
        Say ''
        Write-Host "A fresh $Product v$ReleaseVersion with your save is in $new" -ForegroundColor Green
        Say 'Play from that folder from now on. Your old folder was not changed; delete it when you are happy.'
        return $new
    }
    if ($work.Count -eq 0 -and $removes.Count -eq 0) {
        if (Test-Path -LiteralPath $state) { Remove-Item -LiteralPath $state -Recurse -Force }
        Say ''
        Write-Host "Your game is already up to date (v$ReleaseVersion)." -ForegroundColor Green
        return $game
    }
    $replaceBytes = [int64](($work | Where-Object { $_.Action -eq 'replace' } | ForEach-Object { (Get-Item -LiteralPath (Join-Path $game $_.File.Path)).Length } | Measure-Object -Sum).Sum)
    $need = [int64]$patch.Bytes + [int64]$patch.UnpackedBytes + $replaceBytes + 1GB
    $drive = New-Object IO.DriveInfo ([IO.Path]::GetPathRoot($game))
    if ($drive.AvailableFreeSpace -lt $need) {
        Stop-Install ("The update needs about {0:N1} GB of free space on {1} ({2:N1} GB free). Free up space and run this again; nothing was changed." -f ($need / 1GB), $drive.Name, ($drive.AvailableFreeSpace / 1GB))
    }
    Say ''
    Say ("Update v{0} -> v{1}: {2} file(s) to update or add, {3} to remove. Download: {4:N0} MB." -f $from, $ReleaseVersion, $work.Count, $removes.Count, ($patch.Bytes / 1MB))
    Say 'Everything it replaces is backed up first, and your saves are never touched.'
    if (!(Ask 'Update now?')) { Stop-Install 'Nothing was changed.' }

    # download and unpack the patch into a staging folder inside the game folder (same drive: fast, safe moves)
    $staging = Join-Path $state 'staging'
    [IO.Directory]::CreateDirectory($staging) | Out-Null
    $index = 0
    $parts = @($patch.Parts)
    foreach ($part in $parts) {
        $index++
        $marker = Join-Path $state ($part.Name + '.done')
        if (Test-Path -LiteralPath $marker) { continue }
        $file = Get-Part $part $index $parts.Count
        Expand-Part $file ($patch.RootFolder + '/') $staging
        Set-Content -LiteralPath $marker -Value $part.SHA256 -Encoding ASCII
        Remove-Item -LiteralPath $file -Force
    }
    foreach ($item in $work) {
        $staged = Join-Path $staging $item.File.Path
        if (!(Test-Path -LiteralPath $staged) -or (Get-Sha $staged) -ne $item.File.Sha256) {
            Remove-Item -LiteralPath $state -Recurse -Force -ErrorAction SilentlyContinue
            Stop-Install 'The downloaded update does not match its list. Run this again to download it fresh; nothing was changed.'
        }
    }

    # back up, then replace; a failure puts every file back
    $backupFile = Join-Path $state 'backup.txt'
    if (Test-Path -LiteralPath $backupFile) { $backup = (Get-Content -LiteralPath $backupFile -TotalCount 1) }
    else {
        $backup = Join-Path $game ("patch-backups\v{0}-to-v{1}-{2}" -f $from, $ReleaseVersion, (Get-Date -Format 'yyyyMMdd-HHmmss'))
        Set-Content -LiteralPath $backupFile -Value $backup -Encoding UTF8
    }
    [IO.Directory]::CreateDirectory($backup) | Out-Null
    Set-Content -LiteralPath (Join-Path $backup 'BACKUP.txt') -Encoding UTF8 -Value @(
        "Files the $Product v$ReleaseVersion update replaced or removed, as they were before it (v$from).",
        'To undo the update: close the game, copy everything in this folder (except BACKUP.txt) back into the game',
        'folder, replacing files, and delete ADDED-FILES.txt''s files. Delete this folder when you no longer need it.')
    $added = Join-Path $backup 'ADDED-FILES.txt'
    $applied = New-Object System.Collections.Generic.List[object]
    $failAfter = [int]($env:OFFLINEDAOC_TEST_FAIL_AFTER)   # testing only: fail after N files to prove the undo
    try {
        foreach ($item in $work) {
            $rel = $item.File.Path
            $target = Join-Path $game $rel
            $saved = Join-Path $backup $rel
            if ($item.Action -eq 'replace') {
                if (!(Test-Path -LiteralPath $saved)) {
                    [IO.Directory]::CreateDirectory((Split-Path -Parent $saved)) | Out-Null
                    [IO.File]::Copy($target, $saved, $false)
                }
            } else { Add-Content -LiteralPath $added -Value $rel -Encoding UTF8 }
            $applied.Add($item)
            if ($failAfter -gt 0 -and $applied.Count -ge $failAfter) { throw 'test failure (OFFLINEDAOC_TEST_FAIL_AFTER)' }
            [IO.Directory]::CreateDirectory((Split-Path -Parent $target)) | Out-Null
            [IO.File]::Copy((Join-Path $staging $rel), $target, $true)
        }
        foreach ($r in $removes) {
            $target = Join-Path $game $r.Path
            $saved = Join-Path $backup $r.Path
            if (!(Test-Path -LiteralPath $saved)) {
                [IO.Directory]::CreateDirectory((Split-Path -Parent $saved)) | Out-Null
                [IO.File]::Copy($target, $saved, $false)
            }
            Remove-Item -LiteralPath $target -Force
        }
    } catch {
        $why = $_.Exception.Message
        Say ''
        Say "The update failed ($why). Putting your files back..."
        foreach ($item in $applied) {
            $rel = $item.File.Path
            $target = Join-Path $game $rel
            $saved = Join-Path $backup $rel
            if ($item.Action -eq 'replace' -and (Test-Path -LiteralPath $saved)) { [IO.File]::Copy($saved, $target, $true) }
            elseif ($item.Action -eq 'add' -and (Test-Path -LiteralPath $target)) { Remove-Item -LiteralPath $target -Force }
        }
        foreach ($r in $removes) {
            $target = Join-Path $game $r.Path
            $saved = Join-Path $backup $r.Path
            if (!(Test-Path -LiteralPath $target) -and (Test-Path -LiteralPath $saved)) { [IO.File]::Copy($saved, $target, $false) }
        }
        Remove-Item -LiteralPath $state -Recurse -Force -ErrorAction SilentlyContinue
        # everything is back, so this attempt's backup is no longer needed
        Remove-Item -LiteralPath $backup -Recurse -Force -ErrorAction SilentlyContinue
        $backups = Join-Path $game 'patch-backups'
        if ((Test-Path -LiteralPath $backups) -and !(Get-ChildItem -LiteralPath $backups -Force)) { Remove-Item -LiteralPath $backups -Force }
        Stop-Install "Your game is back as it was (v$from). Run this again to retry. If it fails again, report it with this message."
    }
    Remove-Item -LiteralPath $state -Recurse -Force
    Remove-Item -LiteralPath (Join-Path $game 'UPDATE-CHANGED-FILES.txt') -Force -ErrorAction SilentlyContinue
    Say ''
    Write-Host "Your game in $game is updated to $Product v$ReleaseVersion." -ForegroundColor Green
    Say "The replaced files are backed up in $backup (BACKUP.txt there says how to undo the update)."
    return $game
}

# ---------------------------------------------------------------- which one
$choice = $Mode
if ($Update) { $choice = 'update' }
if ($choice -eq 'ask') {
    if (!$patch) { $choice = 'install' }
    else {
        $found = @(Find-Installed)
        Say ''
        Say "1 = Update my game (v$(($patch.From) -join ' / v') to v$ReleaseVersion, in the folder it is in; a smaller download)"
        Say '2 = New install (the complete game, into a new folder)'
        if ($found.Count -gt 0) { Say "Found an installed game: $($found -join ', ')" }
        do { $answer = Read-Host 'Press 1 or 2, then Enter' } until ($answer -match '^\s*[12]\s*$')
        $choice = if ($answer.Trim() -eq '1') { 'update' } else { 'install' }
        if ($choice -eq 'update' -and !$Update) { $Update = if ($found.Count -eq 1) { $found[0] } else { 'pick' } }
    }
}
if ($choice -eq 'update') {
    if (!$patch) { Stop-Install "v$ReleaseVersion has no update for an installed game. Choose a new install instead." }
    if (!$Update -or $Update -eq 'pick') {
        $found = @(Find-Installed)
        if ($Update -ne 'pick' -and $found.Count -eq 1) { $Update = $found[0] }
        else {
            Add-Type -AssemblyName System.Windows.Forms
            $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
            $dialog.Description = "Choose your $Product folder (the one with $StartFile in it)."
            $dialog.ShowNewFolderButton = $false
            if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { Stop-Install 'No folder was chosen. Nothing was changed.' }
            $Update = $dialog.SelectedPath
        }
    } elseif ($found -and $found.Count -eq 1 -and !$Yes) {
        if (!(Ask "Update the game in $Update?")) { Stop-Install 'Nothing was changed.' }
    }
    $game = Update-Game ([IO.Path]::GetFullPath($Update))
} else {
    $game = if ($Destination) { [IO.Path]::GetFullPath($Destination) } else { Get-DefaultFolder }
    Install-Game $game
    Say ''
    Write-Host "$Product v$ReleaseVersion is installed in $game" -ForegroundColor Green
    if (!$NoShortcut -and (Ask 'Put a shortcut to the game on your desktop?')) {
        $shell = New-Object -ComObject WScript.Shell
        $link = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'Offline DAoC True 1.65.lnk'))
        $link.TargetPath = Join-Path $game $StartFile
        $link.WorkingDirectory = $game
        $link.Save()
        Say 'A shortcut "Offline DAoC True 1.65" is on your desktop.'
    }
}
Remove-Item -LiteralPath (Split-Path -Parent $cache) -Recurse -Force -ErrorAction SilentlyContinue

Say ''
Say 'To play:'
Say "  1. Double-click $StartFile in $game (or the desktop shortcut)."
Say '  2. Click START SERVER and wait until the launcher shows RUNNING. The first start after an update can take a few minutes.'
Say '  3. Click ENTER REALM. Your account is created the first time.'
Say ''
Say 'READ ME FIRST.txt in the game folder has more, and you can delete this downloader folder now.'
