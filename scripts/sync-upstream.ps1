#Requires -Version 5.1
<#
.SYNOPSIS
    Merge the upstream repository's main branch into this fork and push it to origin.

.DESCRIPTION
    Keeps this fork (l3tal123/OfflineDAoC-BotRealm) in sync with its parent
    (shadowofze/OfflineDAoC-True165). The script fetches upstream/main, lists the
    incoming commits, merges them into the current local main branch and pushes
    the result to origin/main.

    It never force-pushes and never rewrites history. If the merge has conflicts
    it aborts the merge, pushes nothing, and tells you how to resolve them by hand.

    Run it from anywhere; it works on the repository that contains this script.
    The easiest entry point is scripts\sync-fork.cmd (handles the execution
    policy), or:  powershell -NoProfile -ExecutionPolicy Bypass -File scripts\sync-upstream.ps1

.PARAMETER DryRun
    Only fetch and report what would be merged. Nothing is changed or pushed.

.PARAMETER NoPush
    Merge locally, but do not push to origin.

.PARAMETER Quiet
    Only print warnings and the final result (for scheduled tasks).

.EXAMPLE
    .\scripts\sync-fork.cmd -DryRun

.EXAMPLE
    .\scripts\sync-fork.cmd
#>
[CmdletBinding()]
param(
    [switch]$DryRun,
    [switch]$NoPush,
    [switch]$Quiet,
    [string]$UpstreamRemote = 'upstream',
    [string]$UpstreamBranch = 'main',
    [string]$TargetRemote = 'origin',
    [string]$TargetBranch = 'main'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:UpstreamUrl = 'https://github.com/shadowofze/OfflineDAoC-True165.git'
$script:OriginUrl = 'https://github.com/l3tal123/OfflineDAoC-BotRealm.git'

function Write-Step {
    param([string]$Message)
    if (-not $Quiet) { Write-Host "==> $Message" -ForegroundColor Cyan }
}

function Write-Note {
    param([string]$Message)
    if (-not $Quiet -and $Message) { Write-Host "    $Message" }
}

function Stop-WithError {
    param([string]$Message, [int]$Code = 1)
    Write-Host "ERROR: $Message" -ForegroundColor Red
    exit $Code
}

# Runs git and returns { ExitCode, Output (stdout lines), Text (stdout+stderr) }.
# Throws on failure unless -AllowFailure. A child process is used instead of the
# call operator because Windows PowerShell 5.1 turns a native program's stderr
# into terminating errors while $ErrorActionPreference is Stop; this way git's
# stderr is captured as plain text and only shown when it matters.
function ConvertTo-ProcessArgument {
    param([string]$Argument)
    if ($Argument -notmatch '[\s"]') { return $Argument }
    return '"' + ($Argument -replace '"', '\"') + '"'
}

function Invoke-Git {
    param(
        [Parameter(Mandatory = $true)][string[]]$GitArgs,
        [switch]$AllowFailure,
        [switch]$HideOutput
    )
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
    $combined = (@($stdoutText.Trim(), $stderrText.Trim()) | Where-Object { $_ }) -join "`n"
    if ($code -ne 0 -and -not $AllowFailure) {
        throw ("git {0} failed with exit code {1}`n{2}" -f ($GitArgs -join ' '), $code, $combined)
    }
    if (-not $HideOutput -and $combined) { Write-Note $combined }
    return [pscustomobject]@{ ExitCode = $code; Output = $stdoutLines; Text = $combined }
}

# ---------------------------------------------------------------- preconditions
$rootResult = Invoke-Git -GitArgs @('-C', $PSScriptRoot, 'rev-parse', '--show-toplevel') -AllowFailure -HideOutput
if ($rootResult.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($rootResult.Text)) {
    Stop-WithError 'scripts/sync-upstream.ps1 must run from inside the fork''s git repository.'
}
$repoRoot = $rootResult.Text
Set-Location $repoRoot
Write-Step "Repository: $repoRoot"

if (-not (Test-Path (Join-Path $repoRoot '.git'))) {
    Stop-WithError 'No .git directory here. Run scripts\setup-fork.cmd first.'
}

# Remotes ---------------------------------------------------------------------
$upstreamUrl = Invoke-Git -GitArgs @('remote', 'get-url', $UpstreamRemote) -AllowFailure -HideOutput
if ($upstreamUrl.ExitCode -ne 0) {
    Write-Step "Adding missing '$UpstreamRemote' remote -> $script:UpstreamUrl"
    Invoke-Git -GitArgs @('remote', 'add', $UpstreamRemote, $script:UpstreamUrl) | Out-Null
}

$originUrl = Invoke-Git -GitArgs @('remote', 'get-url', $TargetRemote) -AllowFailure -HideOutput
if ($originUrl.ExitCode -ne 0) {
    Write-Step "Adding missing '$TargetRemote' remote -> $script:OriginUrl"
    Invoke-Git -GitArgs @('remote', 'add', $TargetRemote, $script:OriginUrl) | Out-Null
}

# Branch ----------------------------------------------------------------------
$branchResult = Invoke-Git -GitArgs @('symbolic-ref', '--quiet', '--short', 'HEAD') -AllowFailure -HideOutput
$currentBranch = $branchResult.Text
if ($branchResult.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($currentBranch)) {
    Stop-WithError "HEAD is detached. Check out '$TargetBranch' before syncing."
}
if ($currentBranch -ne $TargetBranch) {
    Stop-WithError "On branch '$currentBranch', but this script syncs '$TargetBranch'. Switch to it first: git switch $TargetBranch"
}

# Working tree: block on staged/modified tracked files, tolerate untracked files.
$statusResult = Invoke-Git -GitArgs @('status', '--porcelain', '--untracked-files=normal') -AllowFailure -HideOutput
$statusLines = @($statusResult.Output | Where-Object { $_ } | ForEach-Object { "$_" })
$trackedChanges = @($statusLines | Where-Object { -not $_.StartsWith('??') })
$untracked = @($statusLines | Where-Object { $_.StartsWith('??') })
if ($trackedChanges.Count -gt 0 -and -not $DryRun) {
    Write-Host 'Uncommitted changes:' -ForegroundColor Yellow
    $trackedChanges | ForEach-Object { Write-Host "  $_" }
    Stop-WithError 'Commit or stash your changes first; a sync must not mix with local edits.'
}
if ($untracked.Count -gt 0) {
    Write-Note ("{0} untracked path(s) are present and will be left alone." -f $untracked.Count)
}

foreach ($marker in @('MERGE_HEAD', 'REBASE_HEAD', 'CHERRY_PICK_HEAD')) {
    if (Test-Path (Join-Path $repoRoot ".git/$marker")) {
        Stop-WithError "A $marker operation is still in progress. Finish or abort it before syncing."
    }
}

# ----------------------------------------------------------------------- fetch
Write-Step "Fetching $UpstreamRemote/$UpstreamBranch"
try {
    Invoke-Git -GitArgs @('fetch', $UpstreamRemote, $UpstreamBranch, '--no-tags', '--prune') | Out-Null
} catch {
    Stop-WithError ("Could not fetch from $UpstreamRemote. Check your network/credentials.`n{0}" -f $_.Exception.Message)
}

$upstreamRef = "$UpstreamRemote/$UpstreamBranch"
$head = (Invoke-Git -GitArgs @('rev-parse', 'HEAD') -HideOutput).Text
$upstreamTip = (Invoke-Git -GitArgs @('rev-parse', $upstreamRef) -HideOutput).Text
Write-Step ("Local HEAD {0}   |   upstream {1} {2}" -f $head.Substring(0, 7), $upstreamRef, $upstreamTip.Substring(0, 7))

$ancestorCheck = Invoke-Git -GitArgs @('merge-base', '--is-ancestor', $upstreamTip, $head) -AllowFailure -HideOutput
if ($ancestorCheck.ExitCode -eq 0) {
    Write-Host "Already up to date with $upstreamRef ($($upstreamTip.Substring(0, 7)))." -ForegroundColor Green
    exit 0
}

$logResult = Invoke-Git -GitArgs @('log', '--oneline', '--no-decorate', "$head..$upstreamRef") -HideOutput
$incoming = @($logResult.Output | Where-Object { $_ } | ForEach-Object { "$_" })
Write-Step ("{0} incoming commit(s) from {1}:" -f $incoming.Count, $upstreamRef)
$incoming | ForEach-Object { Write-Note $_ }

if ($DryRun) {
    Write-Host ''
    Write-Host 'Dry run: nothing was merged or pushed.' -ForegroundColor Yellow
    Write-Host 'Changed files:' -ForegroundColor Yellow
    Invoke-Git -GitArgs @('diff', '--stat', "$head...$upstreamRef") | Out-Null
    exit 0
}

# ----------------------------------------------------------------------- merge
Write-Step "Merging $upstreamRef into $TargetBranch"
$merge = Invoke-Git -GitArgs @('merge', '--no-edit', $upstreamRef) -AllowFailure
if ($merge.ExitCode -ne 0) {
    $conflictResult = Invoke-Git -GitArgs @('diff', '--name-only', '--diff-filter=U') -AllowFailure -HideOutput
    $conflicts = @($conflictResult.Output | Where-Object { $_ } | ForEach-Object { "$_" })
    Invoke-Git -GitArgs @('merge', '--abort') -AllowFailure -HideOutput | Out-Null
    Write-Host ''
    Write-Host 'Merge conflict; the merge was aborted and nothing was pushed.' -ForegroundColor Red
    Write-Host 'Files with conflicts:' -ForegroundColor Red
    $conflicts | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    Write-Host ''
    Write-Host 'Resolve it by hand, keeping both the fork''s work and upstream''s:' -ForegroundColor Yellow
    Write-Host "  git merge $upstreamRef"
    Write-Host '  # edit the conflicted files, then: git add <files>'
    Write-Host '  git commit --no-edit'
    Write-Host "  git push $TargetRemote $TargetBranch"
    Write-Host 'See docs/FORK.md (Conflict handling) for the merge policy.'
    exit 2
}

$newHead = (Invoke-Git -GitArgs @('rev-parse', 'HEAD') -HideOutput).Text
Write-Step ("Merged. New HEAD {0}" -f $newHead.Substring(0, 7))

# ------------------------------------------------------------------------ push
if ($NoPush) {
    Write-Host 'Merged locally; push skipped because -NoPush was given.' -ForegroundColor Yellow
    exit 0
}

Write-Step "Pushing $TargetBranch to $TargetRemote"
$push = Invoke-Git -GitArgs @('push', $TargetRemote, "HEAD:$TargetBranch") -AllowFailure
if ($push.ExitCode -ne 0) {
    Write-Host ''
    Write-Host 'The merge is committed locally, but the push failed.' -ForegroundColor Red
    Write-Host "Run 'git push $TargetRemote $TargetBranch' once interactively to sign in (Git Credential Manager stores it),"
    Write-Host 'then push again. Details:'
    Write-Host $push.Text
    exit 3
}

Write-Host "Fork is in sync with $upstreamRef ($($newHead.Substring(0, 7)))." -ForegroundColor Green
exit 0
