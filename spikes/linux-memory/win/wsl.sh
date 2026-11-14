#!/usr/bin/env bash
# Usage (Git Bash on Windows, from spikes/linux-memory): win/wsl.sh sync | win/wsl.sh "<command>"
# Runs a command in the WSL working copy (~/spar-spikes/linux-memory/repo/spikes/linux-memory). "sync" copies first.
distro=${SPAR_WSL_DISTRO:-Ubuntu-24.04}
if [ "$1" = sync ]; then MSYS_NO_PATHCONV=1 wsl -d "$distro" --cd "$(pwd -W)" -e bash wsl/sync.sh; exit $?; fi
MSYS_NO_PATHCONV=1 wsl -d "$distro" -e bash -c "cd ~/spar-spikes/linux-memory/repo/spikes/linux-memory && $*"
