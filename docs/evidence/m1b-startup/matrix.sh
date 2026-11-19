#!/usr/bin/env bash
# The final run of every start-up path on the four Notebook builds and the hook on WireShapes. Each app gets
# LOCALAPPDATA inside artifacts/webview2-profiles/m1b-startup/; dialogs are closed by WM_CLOSE from the probe.
set -u
P="$(cd "$(dirname "$0")" && pwd)"
R="${SPAR_REPO:?set SPAR_REPO to the repository root}"
A=$R/artifacts/webview2-profiles/m1b-startup
cd "$P"
win() { cygpath -w "$1"; }
declare -A EXE=(
  [aot-production]=$R/samples/Notebook/bin/Production/Release/net11.0/win-x64/publish/Notebook.exe
  [aot-development]=$R/samples/Notebook/bin/Development/Release/net11.0/win-x64/publish/Notebook.exe
  [clr-production]=$R/samples/Notebook/bin/Production/Release/net11.0/win-x64/Notebook.exe
  [clr-development]=$R/samples/Notebook/bin/Development/Debug/net11.0/win-x64/Notebook.exe
)
for build in aot-production aot-development clr-production clr-development; do
  for case in "missing|WEBVIEW2_BROWSER_EXECUTABLE_FOLDER=$(win $A/empty-runtime)" \
              "tooold-runtime|WEBVIEW2_BROWSER_EXECUTABLE_FOLDER=$(win $A/old-runtime)" \
              "tooold-override|SPAR_DEV_REQUIRE_WEBVIEW2=999.0.0.0" \
              "broken|WEBVIEW2_BROWSER_EXECUTABLE_FOLDER=$(win $A/broken-runtime)" \
              "os-override|SPAR_DEV_REQUIRE_WINDOWS_BUILD=99999" \
              "automated|SPAR_AUTOMATED=1|WEBVIEW2_BROWSER_EXECUTABLE_FOLDER=$(win $A/empty-runtime)"; do
    IFS='|' read -r label env1 env2 <<< "$case"
    args=(run --exe "${EXE[$build]}" --root "$A/matrix/$build-$label" --env "$env1" --close-dialog --timeout 30 --shot "$A/matrix/shots/$build-$label.bmp")
    [ -n "${env2:-}" ] && args+=(--env "$env2")
    echo "== $build $label: $(timeout 120 dotnet run Probe.cs -- "${args[@]}" 2>&1 | head -1)"
  done
done
WS=$R/tests/apps/WireShapes/bin/Production/Release/net11.0/win-x64/publish/WireShapes.exe
echo "== wireshapes hook-own: $(timeout 120 dotnet run Probe.cs -- run --exe "$WS" --root "$A/matrix/ws-hook-own" --env "WEBVIEW2_BROWSER_EXECUTABLE_FOLDER=$(win $A/empty-runtime)" --env WIRESHAPES_OWN_STARTUP_MESSAGE=1 --close-dialog --timeout 20 2>&1 | grep -E '^\[|wireshapes|own' | tr '\n' ' ')"
echo "== wireshapes hook-declines: $(timeout 120 dotnet run Probe.cs -- run --exe "$WS" --root "$A/matrix/ws-hook-declines" --env "WEBVIEW2_BROWSER_EXECUTABLE_FOLDER=$(win $A/empty-runtime)" --close-dialog --timeout 20 2>&1 | head -1)"
