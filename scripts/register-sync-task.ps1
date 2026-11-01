#Requires -Version 5.1
<#
.SYNOPSIS
    Register (or remove) a Windows Scheduled Task that syncs this fork with
    upstream by running scripts/sync-upstream.ps1 daily.

.DESCRIPTION
    GitHub disables scheduled workflows in forks until you enable Actions, so a
    local scheduled task is the reliable way to keep the fork in sync while you
    work on this PC. The task runs as your Windows user, only while you are
    logged on, and uses the git credentials Git Credential Manager already has
    (sign in once with an interactive `git push`, see docs/FORK.md).

.PARAMETER DailyAt
    Local time, "HH:mm" (24-hour). Default 09:00.

.PARAMETER TaskName
    Scheduled task name. Default OfflineDAoC-ForkSync.

.PARAMETER Remove
    Delete the scheduled task instead of creating it.

.PARAMETER RunNow
    Start the task immediately after registering it.

.EXAMPLE
    pwsh ./scripts/register-sync-task.ps1

.EXAMPLE
    pwsh ./scripts/register-sync-task.ps1 -DailyAt 18:30 -RunNow

.EXAMPLE
    pwsh ./scripts/register-sync-task.ps1 -Remove
#>
[CmdletBinding()]
param(
    [string]$DailyAt = '09:00',
    [string]$TaskName = 'OfflineDAoC-ForkSync',
    [switch]$Remove,
    [switch]$RunNow
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$syncScript = Join-Path $PSScriptRoot 'sync-upstream.ps1'

if ($Remove) {
    $existing = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    if (-not $existing) {
        Write-Host "Scheduled task '$TaskName' does not exist; nothing to remove." -ForegroundColor Yellow
        exit 0
    }
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
    Write-Host "Removed scheduled task '$TaskName'." -ForegroundColor Green
    exit 0
}

if (-not (Test-Path $syncScript)) {
    Write-Host "ERROR: $syncScript is missing." -ForegroundColor Red
    exit 1
}

if ($DailyAt -notmatch '^([01]\d|2[0-3]):[0-5]\d$') {
    Write-Host "ERROR: -DailyAt must be HH:mm (24-hour), for example 09:00." -ForegroundColor Red
    exit 1
}

# Prefer PowerShell 7 (pwsh), fall back to Windows PowerShell.
$pwshExe = Join-Path $PSHOME 'pwsh.exe'
if (-not (Test-Path $pwshExe)) { $pwshExe = Join-Path $PSHOME 'powershell.exe' }

$arguments = '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "{0}" -Quiet' -f $syncScript
$action = New-ScheduledTaskAction -Execute $pwshExe -Argument $arguments -WorkingDirectory $repoRoot
$trigger = New-ScheduledTaskTrigger -Daily -At $DailyAt
$settings = New-ScheduledTaskSettingsSet `
    -StartWhenAvailable `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit (New-TimeSpan -Minutes 15)
$description = "Merge shadowofze/OfflineDAoC-True165 main into the OfflineDAoC-BotRealm fork and push (docs/FORK.md)."

try {
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings `
        -Description $description -Force | Out-Null
    Write-Host "Registered scheduled task '$TaskName' at $DailyAt daily." -ForegroundColor Green
    Write-Host "  runs: $pwshExe $arguments"
    Write-Host "  logs: Task Scheduler > Task Scheduler Library > $TaskName (History)"
    Write-Host "Remove it with: pwsh ./scripts/register-sync-task.ps1 -Remove"
} catch {
    Write-Host "ERROR: could not register the scheduled task: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "You can still sync by hand with pwsh ./scripts/sync-upstream.ps1." -ForegroundColor Yellow
    exit 1
}

if ($RunNow) {
    Start-ScheduledTask -TaskName $TaskName
    Write-Host "Started '$TaskName' now; check Task Scheduler History for the result." -ForegroundColor Green
}
