#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
SOURCE_FOLDER="${1:-}"
DRIVE_FOLDER="${2:-LectureRecordings}"
CREDENTIALS="${ROOT_DIR}/src/LectureAgent/config/google_credentials.json"

# No folder argument? Ask once (Enter = Documents alias, user's Documents folder)
if [[ -z "$SOURCE_FOLDER" ]]; then
  printf 'Folder jahan recordings aati hain [Documents]: '
  read -r SOURCE_FOLDER
  SOURCE_FOLDER="${SOURCE_FOLDER:-Documents}"
fi

if [[ ! -d "$SOURCE_FOLDER" ]]; then
  printf 'Source folder not found: %s\n' "$SOURCE_FOLDER" >&2
  exit 1
fi

if [[ ! -f "$CREDENTIALS" ]]; then
  printf 'Google credentials missing: %s\n' "$CREDENTIALS" >&2
  exit 1
fi

# Dashboard reachable from other devices on the same WiFi (phone, tablet, laptop).
export Kestrel__Endpoints__Http__Url="${Kestrel__Endpoints__Http__Url:-http://0.0.0.0:5200}"
export Kestrel__Endpoints__Https__Url="${Kestrel__Endpoints__Https__Url:-https://0.0.0.0:5201}"

# API key auth: the dashboard asks for this key on first open and stores it in the browser.
if [[ -z "${AGENT_API_KEY:-}" ]]; then
  AGENT_API_KEY="$(openssl rand -hex 24)"
fi
export AGENT_API_KEY
export Auth__Enabled=true
export Auth__ApiKey="$AGENT_API_KEY"

export PATH="/usr/local/share/dotnet:$PATH"
export DOTNET_ROOT="/usr/local/share/dotnet"
export FileWatcher__MonitorFolder="$SOURCE_FOLDER"
export FileWatcher__EnableFileWatcher=true
export GoogleDrive__Enabled=true
export GoogleDrive__RootFolderPath="$DRIVE_FOLDER"

LAN_IPS="$(ipconfig getifaddr en0 2>/dev/null || true)$(ipconfig getifaddr en1 2>/dev/null || true)"
if ! printf '%s' "$LAN_IPS" | grep -q '[0-9]'; then
  LAN_IPS="$(hostname -I 2>/dev/null || true)"
fi

cd "$ROOT_DIR"
printf 'Monitoring: %s\n' "$SOURCE_FOLDER"
printf 'Drive folder: %s\n' "$DRIVE_FOLDER"
printf '\nOpen on this PC:  http://localhost:5200/\n'
if printf '%s' "$LAN_IPS" | grep -q '[0-9]'; then
  printf '\nOpen on phone/tablet (same WiFi):\n'
  for ip in $LAN_IPS; do
    printf '  http://%s:5200/\n' "$ip"
  done
else
  printf '\nPhone (same WiFi): use this PC local IP -> http://<pc-ip>:5200/\n'
fi
printf '\nDashboard API key (enter once in the app):\n  %s\n' "$AGENT_API_KEY"
printf '\nPress Ctrl+C to stop.\n\n'

dotnet run --project src/LectureAgent/LectureAgent.csproj
