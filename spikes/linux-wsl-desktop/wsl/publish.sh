#!/usr/bin/env bash
# Usage (inside WSL, in the working copy's spikes/linux-wsl-desktop): bash wsl/publish.sh <gtk3|gtk4>
# Strict NativeAOT publish (warnings are errors through the repository's Directory.Build.props), offline from the
# Windows global packages folder (nuget.config written by sync.sh), timed, with the executable size.
set -eu
variant=$1
gtk=$([ "$variant" = gtk4 ] && echo 4 || echo 3)
export PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export NUGET_PACKAGES="${NUGET_PACKAGES:-$(cd ../.. && pwd)/.nuget/packages}"
start=$(date +%s%N)
dotnet publish Wsl.Linux/Wsl.Linux.csproj -c Release -p:SparGtk=$gtk -nologo -clp:Summary
ms=$(( ($(date +%s%N) - start) / 1000000 ))
dir=Wsl.Linux/bin/Release/net11.0/linux-x64/publish
[ "$variant" = gtk4 ] && dir=Wsl.Linux/bin/gtk4/Release/net11.0/linux-x64/publish
echo "SPAR_PUBLISH $variant ${ms}ms $(stat -c %s $dir/Wsl.Linux) bytes $dir"
