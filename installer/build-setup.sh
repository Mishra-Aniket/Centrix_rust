#!/usr/bin/env bash
# Builds installer/output/LectureAgent-Setup-<version>.exe — ONE exe, no Windows
# needed to build. On a center PC it extracts the agent + desktop app into
# C:\ProgramData\LectureAgentApp and starts the desktop app's one-time setup
# wizard (config, service via UAC, firewall, Google sign-in).
#
#   ./installer/build-setup.sh
#
# Re-running this refreshes the embedded payload from installer/stage, so always
# run it after code changes (it calls build-installer.sh to restage first).
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT_DIR="$(cd "$HERE/.." && pwd)"

command -v dotnet >/dev/null || {
  export PATH="/usr/local/share/dotnet:$PATH"
  export DOTNET_ROOT="/usr/local/share/dotnet"
}

echo "==> Restaging the win-x64 payload (agent + desktop app)"
"$HERE/build-installer.sh"

echo "==> Zipping the payload"
cd "$HERE/stage"
rm -f ../payload.zip
zip -rq ../payload.zip agent app
mv ../payload.zip "$ROOT_DIR/agent/src/LectureAgent.Setup/payload.zip"

echo "==> Publishing the single-file Setup exe"
cd "$ROOT_DIR/agent"
dotnet publish src/LectureAgent.Setup/LectureAgent.Setup.csproj \
  --configuration Release -f net8.0-windows -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  --output "$HERE/output" --nologo -v q

rm -f "$HERE/output/LectureAgent-Setup.pdb" "$HERE/output/LectureAgent-Setup.xml"
VERSION="$(sed -n 's/.*#define MyAppVersion "\([^"]*\)".*/\1/p' "$HERE/lecture-agent.iss" | head -1)"
mv "$HERE/output/LectureAgent-Setup.exe" "$HERE/output/LectureAgent-Setup-$VERSION.exe"

echo "==> Done: $HERE/output/LectureAgent-Setup-$VERSION.exe"
