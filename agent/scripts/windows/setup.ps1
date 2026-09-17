# Centrix first-run setup wizard (Windows PowerShell 5.1 compatible)
# Asks for all settings at install time so no config file editing is needed.
$ErrorActionPreference = "Stop"
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $dir
$settingsPath = Join-Path $dir "appsettings.json"

$json = Get-Content $settingsPath -Raw | ConvertFrom-Json

Write-Host ""
Write-Host "==============================================="
Write-Host "        Centrix First-Time Setup"
Write-Host "   (Just press Enter to accept the [default])"
Write-Host "==============================================="
Write-Host ""

# ---- 1. Recordings folder (type, Browse, or Enter for default) ----
Add-Type -AssemblyName System.Windows.Forms | Out-Null

$defaultFolder = "Documents"
if ($json.FileWatcher.MonitorFolder) { $defaultFolder = $json.FileWatcher.MonitorFolder }
$folder = Read-Host "Folder where class data is saved - videos AND notes [$defaultFolder]  (B = Browse)"
if ($folder -eq "B" -or $folder -eq "b") {
    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
    $dialog.Description = "Select the folder where class recordings (video) and notes (PDF) are saved. All subfolders are included."
    $dialog.ShowNewFolderButton = $true
    $result = $dialog.ShowDialog()
    if ($result -eq [System.Windows.Forms.DialogResult]::OK) {
        $folder = $dialog.SelectedPath
        Write-Host "  Selected: $folder"
    } else {
        $folder = $defaultFolder
        Write-Host "  Browse cancelled - using: $folder"
    }
}
if ([string]::IsNullOrWhiteSpace($folder)) { $folder = $defaultFolder }

if (-not ($folder -eq "Documents") -and -not (Test-Path $folder)) {
    $makeIt = Read-Host "Folder '$folder' does not exist yet. Create it? (y/n)"
    if ($makeIt -eq "y") {
        New-Item -ItemType Directory -Path $folder -Force | Out-Null
        Write-Host "  Created: $folder"
    } else {
        Write-Host "  OK - the agent will wait for the folder to appear."
    }
}

# ---- 2. Center / Room ----
$defaultCenter = "Pune - PCMC Vidyapeeth"
if ($json.Agent.CenterId) { $defaultCenter = $json.Agent.CenterId }
$center = Read-Host "Center name (exactly as in the tracker) [$defaultCenter]"
if ([string]::IsNullOrWhiteSpace($center)) { $center = $defaultCenter }

$defaultRoom = "603"
if ($json.Agent.RoomId) { $defaultRoom = $json.Agent.RoomId }
$room = Read-Host "Room ID [$defaultRoom]"
if ([string]::IsNullOrWhiteSpace($room)) { $room = $defaultRoom }

# ---- 3. Drive folders note ----
Write-Host ""
Write-Host "Drive folders: batch folders (e.g. '27-AJ452NA 2026') already exist on"
Write-Host "Google Drive - the agent uploads into THOSE existing folders only."
Write-Host "It never creates a new folder. If a batch folder is missing, the"
Write-Host "upload fails with a clear error on the dashboard."
Write-Host ""

# ---- 4. API key (keep existing unless asked) ----
$accessPath = Join-Path $dir "DASHBOARD-ACCESS.txt"
$apiKey = $null
if (Test-Path $accessPath) {
    foreach ($line in Get-Content $accessPath) {
        $trimmed = $line.Trim()
        if ($trimmed -match '^[a-f0-9]{32,}$') { $apiKey = $trimmed }
    }
}
if ($apiKey) {
    $keep = Read-Host "An API key already exists. Keep it? (y/n)"
    if ($keep -ne "n") {
        Write-Host "  Keeping the existing key."
    } else {
        $apiKey = $null
    }
}
if (-not $apiKey) {
    $bytes = New-Object byte[] 24
    $rng = New-Object System.Security.Cryptography.RNGCryptoServiceProvider
    $rng.GetBytes($bytes)
    $apiKey = ([System.BitConverter]::ToString($bytes) -replace "-", "").ToLower()
    Write-Host "  New API key generated."
}

# ---- 5. Write everything into appsettings.json ----
$json.FileWatcher.MonitorFolder = $folder
$json.FileWatcher.EnableFileWatcher = $true
$json.Agent.CenterId = $center
$json.Agent.RoomId = $room
$json.Kestrel.Endpoints.Http.Url = "http://0.0.0.0:5200"
$json.Auth.Enabled = $true
$json.Auth.ApiKey = $apiKey

$json | ConvertTo-Json -Depth 10 | Set-Content $settingsPath -Encoding UTF8

# ---- 6. Rewrite the access card ----
$ipText = "<this-pc-ip>"
try {
    $ip = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction Stop |
        Where-Object { $_.IPAddress -notlike "127.*" -and $_.IPAddress -notlike "169.254*" } |
        Select-Object -First 1
    if ($ip) { $ipText = $ip.IPAddress }
} catch {
    # ipconfig fallback for older Windows
    $ipconfig = ipconfig | Select-String "IPv4"
    if ($ipconfig -match "(\d+\.\d+\.\d+\.\d+)") { $ipText = $Matches[1] }
}

$card = @"
DASHBOARD ACCESS
================

Dashboard URL (on this PC):   http://localhost:5200/
Phone / MaxHub (same WiFi):   http://$ipText`:5200/

API key (paste once on the dashboard login screen):
$apiKey

Settings chosen during setup:
  Recordings folder : $folder
  Center            : $center
  Room              : $room
  Drive upload      : into EXISTING batch folders (never creates new ones)

Keep this file private - the key gives full control of the agent.
"@
$card | Set-Content $accessPath -Encoding UTF8

# ---- 7. Marker so run-agent.bat skips setup next time ----
Set-Content (Join-Path $dir ".configured") -Value "configured" -Encoding ASCII

Write-Host ""
Write-Host "==============================================="
Write-Host " [DONE] Setup saved:"
Write-Host "   Data folder (videos + notes) : $folder"
Write-Host "      - Video files (.mp4/.mkv/...) -> uploaded to the batch folder"
Write-Host "      - Notes (.pdf/.pptx)          -> uploaded to the batch folder"
Write-Host "      - All subfolders are included automatically"
Write-Host "   Center / Room     : $center / Room $room"
Write-Host "   Drive upload      : existing batch folders only"
Write-Host "   Dashboard         : http://localhost:5200/"
Write-Host "   Phone / MaxHub    : http://$ipText`:5200/"
Write-Host "   API key           : (saved in DASHBOARD-ACCESS.txt)"
Write-Host "==============================================="
Write-Host ""
Write-Host "NOTE: If Windows asks 'Allow Centrix to communicate on the"
Write-Host "network?' - click ALLOW (needed for phone/MaxHub access)."
Write-Host ""
Write-Host "Now run run-agent.bat and drop a test file into the data folder to verify your first upload."
Read-Host "Press Enter to close"
