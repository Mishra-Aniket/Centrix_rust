#!/usr/bin/env bash
# Builds the web dashboard (React PWA) and copies it into the agent's wwwroot,
# so the C# agent serves the latest dashboard on http://localhost:5200.
set -euo pipefail

AGENT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
WEB_DIR="$(cd "${AGENT_DIR}/../web" && pwd)"
WWWROOT="${AGENT_DIR}/src/LectureAgent/wwwroot"

cd "${WEB_DIR}"
npm run build

# Replace only the dashboard files; legacy monitor.* stays untouched.
rm -rf "${WWWROOT}/assets"
mkdir -p "${WWWROOT}"
cp -R "${WEB_DIR}/dist/." "${WWWROOT}/"

echo "Dashboard published to ${WWWROOT}"
