#!/usr/bin/env bash
# One scenario of docs/evidence/m1b-hangs.md: HangProbe.cs run from a copy outside the repository (the repository's
# build props would apply inside it), against a Notebook build, with LOCALAPPDATA in a fresh folder under
# artifacts/webview2-profiles/m1b-hangs/. Usage: run.sh <build> <label> [--env K=V]... -- <steps>
#   <build>: dev (CoreCLR Debug, SparDevServer=false: Development), aot (the published NativeAOT Production
#   executable in artifacts/m1b-hangs/notebook), kept (the copy that keeps the stall probe, NativeAOT Production).
set -u
R="${SPAR_REPO:?set SPAR_REPO to the repository root}"
P="${SPAR_PROBE:?set SPAR_PROBE to the folder holding a copy of HangProbe.cs}"
build=$1 label=$2
shift 2
case $build in
  dev) exe=$R/samples/Notebook/bin/Development/Debug/net11.0/win-x64/Notebook.exe ;;
  aot) exe=$R/artifacts/m1b-hangs/notebook/Notebook.exe ;;
  kept) exe=$R/artifacts/m1b-hangs/notebook-kept-publish/Notebook.exe ;;
  *) echo "unknown build $build"; exit 2 ;;
esac
A=$R/artifacts/webview2-profiles/m1b-hangs
root=$A/$build-$label-$(date +%H%M%S)
cd "$P" && dotnet run HangProbe.cs -- --exe "$(cygpath -w "$exe")" --root "$(cygpath -w "$root")" \
  --log "$(cygpath -w "$A/logs/$build-$label.log")" "$@"
