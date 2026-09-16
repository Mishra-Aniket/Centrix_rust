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
mkdir -p "$HERE/stage/agent" "$HERE/stage/app" "$HERE/output"

dotnet publish "$ROOT_DIR/agent/src/LectureAgent/LectureAgent.csproj" \
  --configuration Release -r win-x64 --self-contained true \
  --output "$HERE/stage/agent" --nologo -v q

dotnet publish "$ROOT_DIR/agent/src/LectureAgent.Desktop/LectureAgentApp.csproj" \
  --configuration Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  --output "$HERE/stage/app" --nologo -v q

mkdir -p "$HERE/stage/app/Assets"
cp "$ROOT_DIR/agent/src/LectureAgent.Desktop/Assets/app.ico" "$HERE/stage/app/Assets/"
cp "$ROOT_DIR/agent/src/LectureAgent.Desktop/Assets/app.png" "$HERE/stage/app/Assets/"
cp "$ROOT_DIR/agent/src/LectureAgent.Desktop/Assets/app.ico" "$HERE/stage/app/"
cp "$ROOT_DIR/agent/src/LectureAgent.Desktop/Assets/app.png" "$HERE/stage/app/"

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

rm -f "$HERE/output/Centrix-Setup.pdb" "$HERE/output/Centrix-Setup.xml" "$HERE/output/LectureAgent-Setup.pdb" "$HERE/output/LectureAgent-Setup.xml"
VERSION="1.0.0"
if [ -f "$HERE/lecture-agent.iss" ]; then
  ISS_VER="$(sed -n 's/.*#define MyAppVersion "\([^"]*\)".*/\1/p' "$HERE/lecture-agent.iss" | head -1)"
  if [ -n "$ISS_VER" ]; then
    VERSION="$ISS_VER"
  fi
fi

if [ -f "$HERE/output/Centrix-Setup.exe" ]; then
  cp "$HERE/output/Centrix-Setup.exe" "$HERE/output/Centrix-Setup-$VERSION.exe"
  cp "$HERE/output/Centrix-Setup.exe" "$HERE/output/LectureAgent-Setup-$VERSION.exe"
  echo "==> Done: $HERE/output/Centrix-Setup-$VERSION.exe"
elif [ -f "$HERE/output/LectureAgent-Setup.exe" ]; then
  cp "$HERE/output/LectureAgent-Setup.exe" "$HERE/output/Centrix-Setup-$VERSION.exe"
  cp "$HERE/output/LectureAgent-Setup.exe" "$HERE/output/LectureAgent-Setup-$VERSION.exe"
  echo "==> Done: $HERE/output/Centrix-Setup-$VERSION.exe"
fi
