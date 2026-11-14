#!/usr/bin/env bash
# Usage (in the container, from spikes/csp-navigation): linux/publish.sh <gtk3|gtk4>
# Strict NativeAOT publish of the WebKitGTK host (warnings are errors), timed, with the executable size.
set -eu
variant=$1
gtk=$([ "$variant" = gtk4 ] && echo 4 || echo 3)
start=$(date +%s%N)
dotnet publish Csp.Linux/Csp.Linux.csproj -c Release -p:SparGtk=$gtk -nologo -clp:ErrorsOnly\;Summary
dir=Csp.Linux/bin/Release/net11.0/linux-x64/publish
[ "$variant" = gtk4 ] && dir=Csp.Linux/bin/gtk4/Release/net11.0/linux-x64/publish
echo "SPAR_PUBLISH $variant $(( ($(date +%s%N) - start) / 1000000 ))ms $(stat -c %s $dir/Csp.Linux) bytes"
