#!/usr/bin/env bash
# Usage (Git Bash on Windows, from spikes/resource-usage): win/wsl.sh sync | win/wsl.sh fetch | win/wsl.sh "<command>"
# Runs a command in the WSL working copy (~/spar-spikes/resource-usage/repo/spikes/resource-usage). "sync" copies first.
distro=${SPAR_WSL_DISTRO:-Ubuntu-24.04}
if [ "$1" = sync ] || [ "$1" = fetch ]; then MSYS_NO_PATHCONV=1 wsl -d "$distro" --cd "$(pwd -W)" -e bash "wsl/$1.sh"; exit $?; fi
MSYS_NO_PATHCONV=1 wsl -d "$distro" -e bash -c "cd ~/spar-spikes/resource-usage/repo/spikes/resource-usage && $*"
