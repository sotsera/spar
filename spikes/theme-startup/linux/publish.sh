#!/usr/bin/env bash
# Usage (in the container, in /work/spikes/theme-startup): linux/publish.sh <gtk3|gtk4>
# Strict NativeAOT publish (warnings are errors through the repository's Directory.Build.props), timed, with the size.
set -eu
variant=$1
gtk=$([ "$variant" = gtk4 ] && echo 4 || echo 3)
start=$(date +%s%N)
dotnet publish Theme.Linux/Theme.Linux.csproj -c Release -p:SparGtk=$gtk -nologo -clp:ErrorsOnly\;Summary
ms=$(( ($(date +%s%N) - start) / 1000000 ))
dir=Theme.Linux/bin/Release/net11.0/linux-x64/publish
[ "$variant" = gtk4 ] && dir=Theme.Linux/bin/gtk4/Release/net11.0/linux-x64/publish
chmod -R a+rX Theme.Linux/bin
echo "SPAR_PUBLISH $variant ${ms}ms $(stat -c %s $dir/Theme.Linux) bytes $dir"
