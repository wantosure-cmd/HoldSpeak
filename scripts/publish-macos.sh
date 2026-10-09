#!/usr/bin/env bash
set -euo pipefail
export DOTNET_CLI_TELEMETRY_OPTOUT=1 AVALONIA_TELEMETRY_OPTOUT=1
rid="${1:-osx-arm64}"
case "$rid" in osx-arm64|osx-x64) ;; *) echo 'Unsupported Mac runtime' >&2; exit 1;; esac
root="$(cd "$(dirname "$0")/.." && pwd)"
out="$root/artifacts/HoldSpeak-$rid"
bundle="$out/HoldSpeak.app"
mkdir -p "$bundle/Contents/MacOS"
dotnet publish "$root/src/HoldSpeak.App/HoldSpeak.App.csproj" -c Release -r "$rid" --self-contained true -p:HoldSpeakPlatform=Mac -o "$bundle/Contents/MacOS"
cp "$root/packaging/macos/Info.plist" "$bundle/Contents/Info.plist"
chmod +x "$bundle/Contents/MacOS/HoldSpeak.App"
if command -v codesign >/dev/null; then codesign --force --deep --sign - "$bundle"; fi
echo "Development bundle (not notarized): $bundle"
