#!/usr/bin/env bash
# Usage (Git Bash on Windows, from spikes/webview-profiles): win/wsl.sh sync | win/wsl.sh "<command>"
# Runs a command in the WSL working copy (~/spar-spikes/webview-profiles/repo/spikes/webview-profiles). "sync" copies first.
distro=${SPAR_WSL_DISTRO:-Ubuntu-24.04}
if [ "$1" = sync ]; then MSYS_NO_PATHCONV=1 wsl -d "$distro" --cd "$(pwd -W)" -e bash wsl/sync.sh; exit $?; fi
MSYS_NO_PATHCONV=1 wsl -d "$distro" -e bash -c "cd ~/spar-spikes/webview-profiles/repo/spikes/webview-profiles && $*"
