#!/usr/bin/env bash
# Usage (Git Bash, from spikes/page-heartbeat): docker/build.sh
# Publishes Beat.Linux (NativeAOT, linux-x64) inside the existing image spar-linux-desktop:webkit2.52 (SDK 11.0.100,
# clang, WebKitGTK 6.0 headers), in a throwaway container. Restore is offline: the Windows user's NuGet global packages
# folder is mounted read-only as the only source. Output: Beat.Linux/bin/publish/Beat.Linux.
set -eu
export MSYS_NO_PATHCONV=1
repo=$(cd ../.. && pwd -W)
nuget=$(cygpath -w "$USERPROFILE/.nuget/packages")
docker run --rm --name spar-page-heartbeat-build \
  -v "$repo:/repo" -v "$nuget:/nuget-src:ro" -w /repo/spikes/page-heartbeat \
  -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_NOLOGO=1 -e NUGET_PACKAGES=/tmp/nuget -e MSBUILDNODEHANDSHAKESALT=page-heartbeat \
  spar-linux-desktop:webkit2.52 \
  bash -c 'dotnet publish Beat.Linux -c Release -o Beat.Linux/bin/publish -nologo -clp:NoSummary -p:RestoreSources=/nuget-src -p:UseSharedCompilation=false 2>&1 | grep -E "error|warn|->" | sort -u; ls -la Beat.Linux/bin/publish/Beat.Linux; ldd Beat.Linux/bin/publish/Beat.Linux'
