#!/usr/bin/env bash
# Builds the Centrix Tauri installer.
#
# Prerequisites:
#   - Rust toolchain with x86_64-pc-windows-msvc target
#   - .NET 8 SDK
#   - Node.js 18+
#
# Output: web/src-tauri/target/release/bundle/nsis/Centrix-Setup_1.0.0_x64-setup.exe
#
# Usage:
#   ./scripts/build-tauri-installer.sh
#
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT_DIR="$(cd "$HERE/.." && pwd)"
WEB_DIR="$ROOT_DIR/web"
AGENT_DIR="$ROOT_DIR/agent"
RESOURCES_DIR="$WEB_DIR/src-tauri/resources"

# Ensure dotnet is on PATH
command -v dotnet >/dev/null || {
  export PATH="/usr/local/share/dotnet:$PATH"
  export DOTNET_ROOT="/usr/local/share/dotnet"
}

echo "==> [1/4] Building LectureAgent (.NET 8, win-x64)"
mkdir -p "$RESOURCES_DIR"
dotnet publish "$AGENT_DIR/src/LectureAgent/LectureAgent.csproj" \
  --configuration Release -r win-x64 --self-contained true \
  --output "$RESOURCES_DIR/agent" --nologo -v q

echo "==> [2/4] Installing web dependencies"
cd "$WEB_DIR"
npm install

echo "==> [3/4] Building React frontend (Vite)"
npm run build

echo "==> [4/4] Building Tauri installer (NSIS)"
npx tauri build

echo ""
echo "✅ Done! Installer ready at:"
find "$WEB_DIR/src-tauri/target" -name "*setup*" -o -name "*Setup*" 2>/dev/null | head -5
