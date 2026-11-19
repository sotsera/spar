#!/usr/bin/env bash
# Usage (inside WSL): bash publish.sh <repo as a /mnt path>. Copies the root build files and Linux/ into the working copy
# and publishes the probe with NativeAOT to $base/bin (every trim/AOT warning reported, none suppressed).
set -uo pipefail
source "$(cd "$(dirname "$0")" && pwd)/common.sh"
repo=$1
spike=$work/spikes/webview-restart-wait
mkdir -p "$spike" "$base/logs"
cp "$repo/global.json" "$repo/Directory.Build.props" "$repo/Directory.Build.targets" "$repo/Directory.Packages.props" "$repo/.editorconfig" "$work/"
printf '%s\n' '<?xml version="1.0" encoding="utf-8"?>' '<configuration><packageSources><clear />' \
  '<add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" /></packageSources></configuration>' >"$work/nuget.config"
cp "$repo/spikes/webview-restart-wait/Directory.Build.props" "$spike/"
rm -rf "$spike/Linux" && mkdir -p "$spike/Linux" && cp "$repo"/spikes/webview-restart-wait/Linux/*.cs "$repo"/spikes/webview-restart-wait/Linux/*.csproj "$spike/Linux/"
isolate build
cd "$spike/Linux"
dotnet publish -c Release -o "$base/bin" -nologo >"$base/logs/publish.log" 2>&1
echo "PUBLISH exit=$? size=$(stat -c %s "$base/bin/RestartWait.Linux" 2>/dev/null) bytes"
grep -E "warning|error" "$base/logs/publish.log" | sort -u | head -20
cleanup_runtime
