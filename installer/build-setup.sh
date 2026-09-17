#!/usr/bin/env bash
# Builds installer/output/Centrix-Setup-<version>.exe — ONE exe, no Windows
# needed to build. On a center PC it extracts the agent + desktop app into
# C:\ProgramData\Centrix and starts the desktop app's one-time setup
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

echo "==> [1/6] Building modern web frontend"
cd "$ROOT_DIR/web"
npm run build
mkdir -p "$ROOT_DIR/agent/src/LectureAgent/wwwroot"
cp -R "$ROOT_DIR/web/dist/"* "$ROOT_DIR/agent/src/LectureAgent/wwwroot/"

echo "==> [2/6] Restaging win-x64 payload (agent + desktop app)"
# Staging is disposable build output. Recreate it so an installer never retains
# files from a previous build (including old web assets or credentials).
rm -rf "$HERE/stage/agent" "$HERE/stage/app"
mkdir -p "$HERE/stage/agent" "$HERE/stage/app" "$HERE/output" "$ROOT_DIR/dist/Windows-Latest"

dotnet publish "$ROOT_DIR/agent/src/LectureAgent/LectureAgent.csproj" \
  --configuration Release -r win-x64 --self-contained true \
  --output "$HERE/stage/agent" --nologo -v q

# Keep the stable CentrixAgent service filename while supporting older payloads.
if [ -f "$HERE/stage/agent/Centrix.exe" ]; then
  cp "$HERE/stage/agent/Centrix.exe" "$HERE/stage/agent/CentrixAgent.exe"
elif [ -f "$HERE/stage/agent/LectureAgent.exe" ]; then
  cp "$HERE/stage/agent/LectureAgent.exe" "$HERE/stage/agent/CentrixAgent.exe"
fi

dotnet publish "$ROOT_DIR/agent/src/LectureAgent.Desktop/LectureAgentApp.csproj" \
  --configuration Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  --output "$HERE/stage/app" --nologo -v q

echo "==> [3/6] Bundling application payload and safe first-run settings"
mkdir -p "$HERE/stage/agent/wwwroot"
cp -R "$ROOT_DIR/web/dist/"* "$HERE/stage/agent/wwwroot/"

# Never package a workstation's Google tokens, OAuth client file, dashboard key,
# or Studio session. The setup wizard collects a center-specific configuration
# and stores it in the shared ProgramData directory on first run.
node - "$ROOT_DIR/agent/src/LectureAgent/appsettings.json" "$HERE/stage/agent/appsettings.json" <<'NODE'
const fs = require('fs');
const [source, destination] = process.argv.slice(2);
const settings = JSON.parse(fs.readFileSync(source, 'utf8'));
settings.Auth = { ...settings.Auth, Enabled: false, ApiKey: '' };
settings.GoogleDrive = {
  ...settings.GoogleDrive,
  Enabled: false,
  CredentialsPath: '',
  TokenPath: 'data/google-drive-token',
  RootFolderPath: ''
};
settings.YouTube = {
  ...settings.YouTube,
  Enabled: false,
  CredentialsPath: '',
  TokenPath: 'data/youtube-token'
};
settings.StudioApi = { ...settings.StudioApi, Enabled: false, IdToken: '' };
if (settings.Kestrel && settings.Kestrel.Endpoints) {
  delete settings.Kestrel.Endpoints.Https;
  settings.Kestrel.Endpoints.Http = { Url: 'http://0.0.0.0:5200' };
}
fs.writeFileSync(destination, `${JSON.stringify(settings, null, 2)}\n`);
NODE
# `dotnet publish` may copy config content files from the source project. OAuth
# client credentials are supplied per-center through the setup wizard instead.
rm -f "$HERE/stage/agent/config/google_credentials.json"

cp "$ROOT_DIR/agent/scripts/windows/"*.bat "$HERE/stage/agent/" 2>/dev/null || true
cp "$ROOT_DIR/agent/scripts/windows/"*.vbs "$HERE/stage/agent/" 2>/dev/null || true

mkdir -p "$HERE/stage/app/Assets"
cp "$ROOT_DIR/agent/src/LectureAgent.Desktop/Assets/app.ico" "$HERE/stage/app/Assets/"
cp "$ROOT_DIR/agent/src/LectureAgent.Desktop/Assets/app.png" "$HERE/stage/app/Assets/"
cp "$ROOT_DIR/agent/src/LectureAgent.Desktop/Assets/app.ico" "$HERE/stage/app/"
cp "$ROOT_DIR/agent/src/LectureAgent.Desktop/Assets/app.png" "$HERE/stage/app/"

echo "==> [4/6] Zipping the full payload"
cd "$HERE/stage"
rm -f ../payload.zip
zip -rq ../payload.zip agent app
mv ../payload.zip "$ROOT_DIR/agent/src/LectureAgent.Setup/payload.zip"

echo "==> [5/6] Publishing single-file Centrix.exe"
cd "$ROOT_DIR/agent"
dotnet publish src/LectureAgent.Setup/LectureAgent.Setup.csproj \
  --configuration Release -f net8.0-windows -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  --output "$HERE/output" --nologo -v q

rm -f "$HERE/output/Centrix.pdb" "$HERE/output/Centrix.xml" "$HERE/output/Centrix-Setup.pdb" "$HERE/output/Centrix-Setup.xml" "$HERE/output/LectureAgent-Setup.pdb" "$HERE/output/LectureAgent-Setup.xml"
VERSION="$(sed -n 's|.*<Version>\([^<]*\)</Version>.*|\1|p' "$ROOT_DIR/agent/src/LectureAgent.Setup/LectureAgent.Setup.csproj" | head -1)"
VERSION="${VERSION:-1.0.0}"
if [ -f "$HERE/lecture-agent.iss" ]; then
  ISS_VER="$(sed -n 's/.*#define MyAppVersion "\([^"]*\)".*/\1/p' "$HERE/lecture-agent.iss" | head -1)"
  if [ -n "$ISS_VER" ]; then
    VERSION="$ISS_VER"
  fi
fi

if [ -f "$HERE/output/Centrix.exe" ]; then
  cp "$HERE/output/Centrix.exe" "$HERE/output/Centrix-$VERSION.exe"
  cp "$HERE/output/Centrix.exe" "$ROOT_DIR/dist/Centrix.exe"
  cp "$HERE/output/Centrix.exe" "$ROOT_DIR/dist/Centrix-Setup.exe"
  cp "$HERE/output/Centrix.exe" "$ROOT_DIR/dist/Centrix-Setup-$VERSION.exe"
  cp "$HERE/output/Centrix.exe" "$ROOT_DIR/dist/Windows-Latest/Centrix.exe"
  cp "$HERE/output/Centrix.exe" "$ROOT_DIR/dist/Windows-Latest/Centrix-Setup.exe"
  echo "==> [6/6] Done: $HERE/output/Centrix-$VERSION.exe"
  echo "==> Copied to $ROOT_DIR/dist/Centrix.exe and $ROOT_DIR/dist/Centrix-Setup.exe"
elif [ -f "$HERE/output/LectureAgent-Setup.exe" ]; then
  cp "$HERE/output/LectureAgent-Setup.exe" "$HERE/output/Centrix-Setup-$VERSION.exe"
  cp "$HERE/output/LectureAgent-Setup.exe" "$ROOT_DIR/dist/Centrix.exe"
  cp "$HERE/output/LectureAgent-Setup.exe" "$ROOT_DIR/dist/Centrix-Setup.exe"
  cp "$HERE/output/LectureAgent-Setup.exe" "$ROOT_DIR/dist/Windows-Latest/Centrix-Setup.exe"
  echo "==> [6/6] Done: $HERE/output/Centrix-Setup-$VERSION.exe"
  echo "==> Copied to $ROOT_DIR/dist/Centrix-Setup.exe"
fi
