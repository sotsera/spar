#!/usr/bin/env bash
# Usage (Git Bash, from the repository root): spikes/startup-prerequisites/docker/build.sh
# Publishes Startup.Linux (NativeAOT, linux-x64) inside the existing image spar-linux-desktop:webkit2.52 (SDK 11.0.100
# and clang), in a throwaway container. Restore is offline: the Windows user's NuGet global packages folder is mounted
# read-only as the only source. The executable lands in Startup.Linux/bin/Release/net11.0/linux-x64/publish/.
set -eu
repo=$(pwd -W 2>/dev/null || pwd)
nuget=$(cygpath -w "$USERPROFILE/.nuget/packages")
start=$(date +%s%N)
MSYS_NO_PATHCONV=1 docker run --rm --name spar-startup-prereq-build \
  -v "$repo:/repo" -v "$nuget:/nuget-src:ro" -w /repo/spikes/startup-prerequisites \
  -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_NOLOGO=1 -e NUGET_PACKAGES=/tmp/nuget \
  spar-linux-desktop:webkit2.52 \
  bash -c 'dotnet publish Startup.Linux -c Release -nologo -clp:Summary -p:RestoreSources=/nuget-src 2>&1 | tail -6; ls -la Startup.Linux/bin/Release/net11.0/linux-x64/publish/; ldd Startup.Linux/bin/Release/net11.0/linux-x64/publish/Startup.Linux'
echo "build took $(( ($(date +%s%N) - start) / 1000000 )) ms"
