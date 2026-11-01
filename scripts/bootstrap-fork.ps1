#Requires -Version 5.1
<#
.SYNOPSIS
    Set up (or repair) this checkout as a git clone of the fork, with the upstream
    parent repository configured for syncing.

.DESCRIPTION
    Use this after unpacking a GitHub "Code > Download ZIP" export (which has no
    .git directory), after moving the folder, or on a fresh clone where you want
    the remotes and sync settings checked.

    What it does, idempotently:
      1. Initialises a git repository on branch main when none exists, and
         attaches the working tree to origin/main *without modifying any file*
         (the extracted files are kept as they are).
      2. Adds or repairs the remotes:
           origin   = https://github.com/l3tal123/OfflineDAoC-BotRealm.git  (the fork)
           upstream = https://github.com/shadowofze/OfflineDAoC-True165.git (the parent)
      3. Applies the repo-local git settings this fork relies on
         (long paths, OpenSSL TLS backend, fetch pruning, rerere for repeated
         upstream conflicts, upstream-tracking main, no rebase on pull).
      4. Fetches origin/main and upstream/main.
      5. Prints the branch state and the next steps.

    It does not merge, commit, push, build, start a server or touch game data.

.PARAMETER Force
    Re-point the remotes even if they already exist with a different URL.

.EXAMPLE
    .\scripts\setup-fork.cmd

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts\bootstrap-fork.ps1 -Force
#>
[CmdletBinding()]
param(
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$originUrl = 'https://github.com/l3tal123/OfflineDAoC-BotRealm.git'
$upstreamUrl = 'https://github.com/shadowofze/OfflineDAoC-True165.git'

function Write-Step { param([string]$Message) Write-Host "==> $Message" -ForegroundColor Cyan }
function Write-Note { param([string]$Message) if ($Message) { Write-Host "    $Message" } }
function Fail { param([string]$Message, [int]$Code = 1) Write-Host "ERROR: $Message" -ForegroundColor Red; exit $Code }

# Runs git and returns { ExitCode, Output (stdout lines), Text (stdout+stderr) }.
# A child process is used instead of the call operator because Windows PowerShell
# 5.1 turns a native program's stderr into terminating errors while
# $ErrorActionPreference is Stop; this captures stderr as plain text instead.
function ConvertTo-ProcessArgument {
    param([string]$Argument)
    if ($Argument -notmatch '[\s"]') { return $Argument }
    return '"' + ($Argument -replace '"', '\"') + '"'
}

function Invoke-Git {
    param([string[]]$GitArgs, [switch]$AllowFailure, [switch]$HideOutput)
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = 'git'
    $psi.Arguments = (@($GitArgs | ForEach-Object { ConvertTo-ProcessArgument $_ }) -join ' ')
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.StandardOutputEncoding = New-Object System.Text.UTF8Encoding($false)
    $psi.StandardErrorEncoding = New-Object System.Text.UTF8Encoding($false)
    $process = [System.Diagnostics.Process]::Start($psi)
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    $stdoutText = $stdoutTask.Result
    $stderrText = $stderrTask.Result
    $code = $process.ExitCode
    $process.Dispose()

    $stdoutLines = @($stdoutText -split "`r?`n" | Where-Object { $_ -ne '' })
    $text = (@($stdoutText.Trim(), $stderrText.Trim()) | Where-Object { $_ }) -join "`n"
    if ($code -ne 0 -and -not $AllowFailure) {
        throw ("git {0} failed:`n{1}" -f ($GitArgs -join ' '), $text)
    }
    if (-not $HideOutput -and $text) { Write-Note $text }
    return [pscustomobject]@{ ExitCode = $code; Output = $stdoutLines; Text = $text }
}

Set-Location $repoRoot
Write-Step "Repository folder: $repoRoot"

# ---------------------------------------------------------------- 1. git repo
$hasGit = Test-Path (Join-Path $repoRoot '.git')
if (-not $hasGit) {
    Write-Step 'No .git found: initialising a repository on branch main (files are not modified)'
    Invoke-Git -GitArgs @('init', '-b', 'main') | Out-Null
}

# Local config this fork depends on. http.sslBackend=openssl is the fallback for
# the Schannel "SEC_E_NO_CREDENTIALS" failure seen on some Windows setups;
# OpenSSL is bundled with Git for Windows and works for GitHub either way.
$configPairs = @(
    @('core.longpaths', 'true'),
    @('http.sslBackend', 'openssl'),
    @('fetch.prune', 'true'),
    @('rerere.enabled', 'true'),
    @('pull.rebase', 'false'),
    @('push.autoSetupRemote', 'true'),
    @('remote.pushDefault', 'origin')
)
foreach ($pair in $configPairs) {
    Invoke-Git -GitArgs @('config', '--local', $pair[0], $pair[1]) -HideOutput | Out-Null
}

# The server build copies CoreServer/config/serverconfig.xml from the source tree, and that
# file is deliberately git-ignored (it is a local/server config). A fresh clone or ZIP export
# therefore cannot build until it exists; create it from the committed example.
$configDir = Join-Path $repoRoot 'development-source/server/CoreServer/config'
$configFile = Join-Path $configDir 'serverconfig.xml'
$configExample = Join-Path $configDir 'serverconfig.example.xml'
if ((Test-Path $configExample) -and -not (Test-Path $configFile)) {
    Write-Step 'Creating CoreServer/config/serverconfig.xml from serverconfig.example.xml (git-ignored, needed by the build)'
    Copy-Item -LiteralPath $configExample -Destination $configFile
}

# ------------------------------------------------------------------- 2. remotes
function Ensure-Remote {
    param([string]$Name, [string]$Url)
    $existing = Invoke-Git -GitArgs @('remote', 'get-url', $Name) -AllowFailure -HideOutput
    if ($existing.ExitCode -ne 0) {
        Write-Step "Adding remote $Name -> $Url"
        Invoke-Git -GitArgs @('remote', 'add', $Name, $Url) -HideOutput | Out-Null
        return
    }
    if ($Force) {
        Write-Step "Re-pointing remote $Name -> $Url"
        Invoke-Git -GitArgs @('remote', 'set-url', $Name, $Url) -HideOutput | Out-Null
        return
    }
    Write-Note "remote $Name exists: $($existing.Text)"
}

Ensure-Remote -Name 'origin' -Url $originUrl
Ensure-Remote -Name 'upstream' -Url $upstreamUrl

# ------------------------------------------------------- 3. fetch both remotes
Write-Step 'Fetching origin/main and upstream/main'
$fetchOrigin = Invoke-Git -GitArgs @('fetch', '--no-tags', 'origin', 'main') -AllowFailure
if ($fetchOrigin.ExitCode -ne 0) {
    Write-Host 'Could not fetch from origin. Check network/credentials; continuing with local state.' -ForegroundColor Yellow
    Write-Host $fetchOrigin.Text
}
$fetchUpstream = Invoke-Git -GitArgs @('fetch', '--no-tags', 'upstream', 'main') -AllowFailure
if ($fetchUpstream.ExitCode -ne 0) {
    Write-Host 'Could not fetch from upstream. Check network; continuing with local state.' -ForegroundColor Yellow
    Write-Host $fetchUpstream.Text
}

# ------------------------------------------- 4. attach the working tree to main
$headCheck = Invoke-Git -GitArgs @('rev-parse', '--verify', '--quiet', 'HEAD') -AllowFailure -HideOutput
$originMainCheck = Invoke-Git -GitArgs @('rev-parse', '--verify', '--quiet', 'refs/remotes/origin/main') -AllowFailure -HideOutput

if ($headCheck.ExitCode -ne 0) {
    if ($originMainCheck.ExitCode -ne 0) { Fail 'No HEAD and no origin/main: cannot attach the working tree.' }
    Write-Step 'Attaching the extracted files to origin/main (index only; no file is overwritten)'
    Invoke-Git -GitArgs @('update-ref', 'refs/heads/main', 'refs/remotes/origin/main') -HideOutput | Out-Null
    Invoke-Git -GitArgs @('symbolic-ref', 'HEAD', 'refs/heads/main') -HideOutput | Out-Null
    # --mixed resets the index to HEAD and leaves the working tree untouched.
    Invoke-Git -GitArgs @('reset', '--mixed') -HideOutput | Out-Null
}

$branchResult = Invoke-Git -GitArgs @('symbolic-ref', '--quiet', '--short', 'HEAD') -AllowFailure -HideOutput
$currentBranch = $branchResult.Text
if ($branchResult.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($currentBranch)) {
    Fail 'HEAD is detached. Check out the main branch, then run this script again.'
}
if ($currentBranch -eq 'main' -and $originMainCheck.ExitCode -eq 0) {
    Invoke-Git -GitArgs @('branch', '--set-upstream-to=origin/main', 'main') -AllowFailure -HideOutput | Out-Null
}

# ------------------------------------------------------------------ 5. summary
Write-Host ''
Write-Host 'Fork checkout ready.' -ForegroundColor Green
Write-Host ("  branch       : {0}" -f $currentBranch)
$head = Invoke-Git -GitArgs @('rev-parse', '--short', 'HEAD') -AllowFailure -HideOutput
if ($head.ExitCode -eq 0) { Write-Host ("  HEAD         : {0}" -f $head.Text) }
$statusResult = Invoke-Git -GitArgs @('status', '--porcelain', '--untracked-files=normal') -AllowFailure -HideOutput
$status = @($statusResult.Output | Where-Object { $_ } | ForEach-Object { "$_" })
$changed = @($status | Where-Object { -not $_.StartsWith('??') }).Count
$untracked = @($status | Where-Object { $_.StartsWith('??') }).Count
Write-Host ("  worktree     : {0} change(s), {1} untracked path(s)" -f $changed, $untracked)
Write-Host ''
Write-Host 'Next steps:'
Write-Host '  .\scripts\sync-fork.cmd -DryRun             # see what upstream has'
Write-Host '  python -I database\unpack_db.py             # write the clean world DB (ignored by git)'
Write-Host '  docs\FORK.md and docs\DEV-ENVIRONMENT.md    # setup, workflow, toolchain'
