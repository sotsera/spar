#!/usr/bin/env bash
# Usage (Git Bash on Windows, from spikes/linux-wsl-desktop): win/wsl.sh "<command>"
# Runs a command in the WSL working copy (~/spar-wsl-desktop/spikes/linux-wsl-desktop). "win/wsl.sh sync" syncs first.
distro=${SPAR_WSL_DISTRO:-Ubuntu-24.04}
if [ "$1" = sync ]; then MSYS_NO_PATHCONV=1 wsl -d "$distro" --cd "$(pwd -W)" -e bash wsl/sync.sh; exit $?; fi
MSYS_NO_PATHCONV=1 wsl -d "$distro" -e bash -c "cd ~/spar-wsl-desktop/spikes/linux-wsl-desktop && $*"
