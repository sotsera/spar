#!/usr/bin/env bash
# Usage (inside WSL, in the working copy's spikes/linux-integration): bash wsl/publish.sh <gtk3|gtk4>
# Strict NativeAOT publish (warnings are errors through the repository's Directory.Build.props), offline.
set -eu
variant=$1
gtk=$([ "$variant" = gtk4 ] && echo 4 || echo 3)
export PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export NUGET_PACKAGES="${NUGET_PACKAGES:-$(cd ../.. && pwd)/.nuget/packages}"
start=$(date +%s%N)
dotnet publish Integ.Linux/Integ.Linux.csproj -c Release -p:SparGtk=$gtk -nologo -clp:Summary
ms=$(( ($(date +%s%N) - start) / 1000000 ))
echo "SPAR_PUBLISH $variant ${ms}ms $(stat -c %s "$(bash wsl/exe.sh "$variant")") bytes"
