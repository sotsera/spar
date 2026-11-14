#!/usr/bin/env bash
# Usage (in the container, from this folder): publish.sh <gtk3|gtk4> [extra msbuild args]
# Strict NativeAOT publish (warnings are errors via the repo's Directory.Build.props) with the self-test and the
# optional telemetry layer, timed, with the executable size.
set -eu
variant=$1; shift
gtk=$([ "$variant" = gtk4 ] && echo 4 || echo 3)
start=$(date +%s%N)
dotnet publish Linux.App/Linux.App.csproj -c Release -p:SparGtk=$gtk -p:SparSelfTest=true "$@" -nologo -clp:ErrorsOnly\;Summary
ms=$(( ($(date +%s%N) - start) / 1000000 ))
dir=Linux.App/bin/Release/net11.0/linux-x64/publish
[ "$variant" = gtk4 ] && dir=Linux.App/bin/gtk4/Release/net11.0/linux-x64/publish
echo "SPAR_PUBLISH $variant ${ms}ms $(stat -c %s $dir/Linux.App) bytes $dir"