#!/usr/bin/env bash
# Runs dotnet-env/EnvCheck.cs in the existing Linux image (SDK 11.0.100), as uid 1000, with a throwaway HOME.
# Usage (Git Bash on the development machine): bash tools/dotnet-env.sh > results/dotnet-env.txt
set -euo pipefail
spike="$(cd "$(dirname "$0")/.." && pwd -W)" # Windows form of the path, for docker -v
export MSYS_NO_PATHCONV=1
docker run --rm --init -u 1000 -e HOME=/tmp/h -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_NOLOGO=1 -e UseSharedCompilation=false \
  -v "$spike/dotnet-env:/src:ro" -w /tmp --entrypoint bash spar-linux-desktop:webkit2.52 \
  -c 'mkdir -p /tmp/h /tmp/app && cp /src/EnvCheck.cs /tmp/app/ && cd /tmp/app && dotnet --version && dotnet run EnvCheck.cs'
