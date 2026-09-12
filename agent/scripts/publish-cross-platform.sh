#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
OUTPUT_DIR="${ROOT_DIR}/artifacts"
RIDS=("win-x64" "osx-arm64" "osx-x64" "linux-x64")

rm -rf "${OUTPUT_DIR}"
mkdir -p "${OUTPUT_DIR}"

# One dashboard API key for this publish batch; written into every appsettings.json
# so phone/tablet dashboards can authenticate without editing config by hand.
DASHBOARD_KEY="$(openssl rand -hex 24)"

for rid in "${RIDS[@]}"; do
  dotnet publish "${ROOT_DIR}/src/LectureAgent/LectureAgent.csproj" \
    --configuration Release \
    --runtime "${rid}" \
    --self-contained true \
    --output "${OUTPUT_DIR}/${rid}"

  node -e '
    const fs = require("fs");
    const [settingsPath, key] = process.argv.slice(1);
    const json = JSON.parse(fs.readFileSync(settingsPath, "utf8"));
    json.Kestrel = json.Kestrel || {};
    json.Kestrel.Endpoints = json.Kestrel.Endpoints || {};
    json.Kestrel.Endpoints.Http = { Url: "http://0.0.0.0:5200" };
    json.Kestrel.Endpoints.Https = { Url: "https://0.0.0.0:5201" };
    json.Auth = { Enabled: true, ApiKey: key };
    fs.writeFileSync(settingsPath, JSON.stringify(json, null, 2) + "\n");
  ' "${OUTPUT_DIR}/${rid}/appsettings.json" "${DASHBOARD_KEY}"

  cat > "${OUTPUT_DIR}/${rid}/DASHBOARD-ACCESS.txt" <<EOF
Dashboard access
================

Dashboard URL (on the PC):   http://localhost:5200/
Phone / tablet (same WiFi):  http://<this-pc-ip>:5200/

API key (enter once in the dashboard login screen):
${DASHBOARD_KEY}

The key is also stored in appsettings.json (Auth:ApiKey).
Keep this file private — it grants full control of the agent.
EOF
done

echo "Published artifacts to ${OUTPUT_DIR} (dashboard key: ${DASHBOARD_KEY})"
