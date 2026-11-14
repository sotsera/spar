#!/usr/bin/env bash
# Usage (inside WSL, in the working copy's spikes/resource-usage): bash wsl/publish.sh
# Strict NativeAOT publish (warnings are errors through the repository's Directory.Build.props), offline restore.
set -eu
source wsl/env.sh
dotnet publish Res.Linux/Res.Linux.csproj -c Release -o Res.Linux/bin/publish -nologo -clp:Summary
