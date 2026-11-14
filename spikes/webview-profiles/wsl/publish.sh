#!/usr/bin/env bash
# Usage (inside WSL, in the working copy's spikes/webview-profiles): bash wsl/publish.sh <gtk3|gtk4>
# Strict NativeAOT publish (warnings are errors through the repository's Directory.Build.props), offline restore.
set -eu
source wsl/env.sh
gtk=$([ "$1" = gtk4 ] && echo 4 || echo 3)
dotnet publish Profiles.Linux/Profiles.Linux.csproj -c Release -p:SparGtk=$gtk -o "Profiles.Linux/bin/publish-$1" -nologo -clp:Summary
