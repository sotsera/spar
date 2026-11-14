#!/usr/bin/env bash
# Usage (inside WSL, in the working copy's spikes/linux-memory): bash wsl/publish.sh <gtk3|gtk4>
# Strict NativeAOT publish (warnings are errors through the repository's Directory.Build.props), offline restore.
set -eu
source wsl/env.sh
variant=$1
gtk=$([ "$variant" = gtk4 ] && echo 4 || echo 3)
start=$(date +%s%N)
dotnet publish Mem.Linux/Mem.Linux.csproj -c Release -p:SparGtk=$gtk -nologo -clp:Summary
ms=$(( ($(date +%s%N) - start) / 1000000 ))
echo "SPAR_PUBLISH $variant ${ms}ms $(stat -c %s "$(bash wsl/exe.sh "$variant")") bytes"
