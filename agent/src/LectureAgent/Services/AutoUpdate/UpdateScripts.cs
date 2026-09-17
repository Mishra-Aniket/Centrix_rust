namespace LectureAgent.Services.AutoUpdate;

/// <summary>
/// Self-contained helper scripts the agent writes to its update workspace and spawns
/// detached JUST BEFORE exiting. They wait for the agent process to die, swap the
/// install directory, restart the agent and roll everything back if the new build
/// never comes healthy. Kept as strings so a broken installation can always be
/// repaired from inside the shipped binary.
/// </summary>
public static class UpdateScripts
{
    public const string WindowsScriptName = "apply-update.ps1";
    public const string UnixScriptName = "apply-update.sh";

    public const string WindowsScript = """
# Centrix auto-update helper. Runs detached; the agent that wrote this file exits
# immediately after spawning it. On any failure the previous install is restored.
param(
    [Parameter(Mandatory=$true)][int]$ParentPid,
    [Parameter(Mandatory=$true)][string]$InstallDir,
    [Parameter(Mandatory=$true)][string]$StagingDir,
    [Parameter(Mandatory=$true)][string]$BackupDir,
    [Parameter(Mandatory=$true)][string]$LogFile,
    [string]$HealthUrl = '',
    [string]$ServiceName = ''
)

function Write-Log([string]$message) {
    "$(Get-Date -Format o) $message" | Out-File -FilePath $LogFile -Append -Encoding utf8
}

Write-Log "Update helper started. Waiting for agent pid $ParentPid to exit."
$deadline = (Get-Date).AddMinutes(3)
while ((Get-Process -Id $ParentPid -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 2
}

Write-Log "Backing up $InstallDir to $BackupDir"
if (Test-Path $BackupDir) { Remove-Item -Recurse -Force $BackupDir }
New-Item -ItemType Directory -Force -Path $BackupDir | Out-Null
Copy-Item -Recurse -Force -Path (Join-Path $InstallDir '*') -Destination $BackupDir
if (-not (Test-Path (Join-Path $BackupDir 'Centrix.dll')) -and -not (Test-Path (Join-Path $BackupDir 'LectureAgent.dll'))) {
    Write-Log 'Backup failed (Centrix agent binary missing); aborting update without touching the install.'
    exit 1
}

Write-Log 'Applying staged update'
Copy-Item -Recurse -Force -Path (Join-Path $StagingDir '*') -Destination $InstallDir
Write-Log 'Update applied; restarting agent.'

function Restart-Agent {
    $restarted = $false
    if ($ServiceName) {
        Start-Process -FilePath 'sc.exe' -ArgumentList "start $ServiceName" -WindowStyle Hidden -Wait
        $restarted = ($LASTEXITCODE -eq 0)
    }
    if (-not $restarted) {
        $exe = Join-Path $InstallDir 'Centrix.exe'
        if (-not (Test-Path $exe)) { $exe = Join-Path $InstallDir 'LectureAgent.exe' }
        if (Test-Path $exe) { Start-Process -FilePath $exe -WorkingDirectory $InstallDir -WindowStyle Hidden }
    }
}

Restart-Agent

if (-not $HealthUrl) { Write-Log 'No health URL configured; update considered complete.'; exit 0 }

$healthy = $false
for ($attempt = 0; $attempt -lt 30; $attempt++) {
    Start-Sleep -Seconds 5
    try {
        Invoke-WebRequest -UseBasicParsing -TimeoutSec 5 -Uri $HealthUrl | Out-Null
        $healthy = $true
        break
    } catch { Write-Log "Health check attempt $($attempt + 1) failed: $($_.Exception.Message)" }
}

if ($healthy) {
    Write-Log 'New version is healthy. Update complete.'
    exit 0
}

Write-Log 'New version never became healthy; ROLLING BACK.'
if ($ServiceName) { Start-Process -FilePath 'sc.exe' -ArgumentList "stop $ServiceName" -WindowStyle Hidden -Wait; Start-Sleep -Seconds 5 }
Get-Process | Where-Object { $_.Path -eq (Join-Path $InstallDir 'Centrix.exe') -or $_.Path -eq (Join-Path $InstallDir 'LectureAgent.exe') } | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

Get-ChildItem -Path $InstallDir -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item -Recurse -Force -Path (Join-Path $BackupDir '*') -Destination $InstallDir
Restart-Agent
Write-Log 'Rollback restored and agent restarted.'
exit 2
""";

    public const string UnixScript = """
#!/bin/sh
# Centrix auto-update helper (macOS/Linux). Runs detached; the agent exits right
# after spawning it. Restores the previous install if the new build never gets healthy.
PARENT_PID="$1"
INSTALL_DIR="$2"
STAGING_DIR="$3"
BACKUP_DIR="$4"
HEALTH_URL="$5"
LOG_FILE="${LA_LOG_FILE:-$BACKUP_DIR/update.log}"
RESTART_CMD="${LA_RESTART_CMD:-cd \"$INSTALL_DIR\" && nohup ./Centrix >> \"$INSTALL_DIR/update-restart.log\" 2>&1 & echo $!}"

log() { echo "$(date -u +%Y-%m-%dT%H:%M:%SZ) $1" >> "$LOG_FILE" 2>/dev/null; }

log "Update helper started; waiting for agent pid $PARENT_PID to exit."
i=0
while [ "$i" -lt 90 ]; do
    kill -0 "$PARENT_PID" 2>/dev/null || break
    sleep 2
    i=$((i + 1))
done

log "Backing up $INSTALL_DIR to $BACKUP_DIR"
rm -rf "$BACKUP_DIR"
mkdir -p "$BACKUP_DIR"
cp -R "$INSTALL_DIR/." "$BACKUP_DIR/" || { log 'Backup failed; aborting.'; exit 1; }
[ -f "$BACKUP_DIR/Centrix.dll" ] || [ -f "$BACKUP_DIR/LectureAgent.dll" ] || { log 'Backup incomplete (Centrix agent binary missing); aborting.'; exit 1; }

log 'Applying staged update'
cp -R "$STAGING_DIR/." "$INSTALL_DIR/" || { log 'Copy failed; aborting.'; exit 1; }
log 'Update applied; restarting agent.'

restart_agent() { sh -c "$RESTART_CMD" >> "$LOG_FILE" 2>&1; }

restart_agent

if [ -z "$HEALTH_URL" ]; then log 'No health URL configured; update considered complete.'; exit 0; fi

i=0
while [ "$i" -lt 30 ]; do
    sleep 5
    if curl -sk -m 5 "$HEALTH_URL" >/dev/null 2>&1; then
        log 'New version is healthy. Update complete.'
        exit 0
    fi
    log "Health check attempt $((i + 1)) failed."
    i=$((i + 1))
done

log 'New version never became healthy; ROLLING BACK.'
pkill -f "$INSTALL_DIR/Centrix" 2>/dev/null || pkill -f "$INSTALL_DIR/LectureAgent" 2>/dev/null
sleep 3
rm -rf "$INSTALL_DIR"
mkdir -p "$INSTALL_DIR"
cp -R "$BACKUP_DIR/." "$INSTALL_DIR/"
restart_agent
log 'Rollback restored and agent restarted.'
exit 2
""";
}
