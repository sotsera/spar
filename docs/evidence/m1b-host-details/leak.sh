#!/usr/bin/env bash
# Builds and runs the teardown leak probe of docs/evidence/m1b-host-details.md on the desktop. The probe is built
# from a copy outside the repository (RUNNER_DIR), with Spar's hand-declared WebView2 interfaces copied in beside it,
# and published as NativeAOT with WebView2Loader.dll next to it (artifacts/m1b-host-details/leakprobe/, also what the
# Windows Sandbox run stages). On the desktop windows are only offscreen or hidden, never shown on the screen; each run
# gets LOCALAPPDATA and its profile under artifacts/webview2-profiles/m1b-host-details/.
#   RUNNER_DIR=<folder outside the repository> docs/evidence/m1b-host-details/leak.sh build
#   docs/evidence/m1b-host-details/leak.sh run <name> [probe options...]
set -eu
repo=$(cd "$(dirname "$0")/../../.." && pwd)
out=$repo/artifacts/m1b-host-details/leakprobe
case "$1" in
  build)
    runner=${RUNNER_DIR:?set RUNNER_DIR to a folder outside the repository}/leakprobe
    rm -rf "$runner" && mkdir -p "$runner/Interop"
    cp "$repo"/docs/evidence/m1b-host-details/LeakProbe/*.cs "$repo"/docs/evidence/m1b-host-details/LeakProbe/*.csproj "$runner/"
    cp "$repo"/src/Spar.Windows/Interop/*.cs "$runner/Interop/"
    (cd "$runner" && dotnet publish -c Release -o "$out" -p:Version=1.0.0)
    cp "$HOME/.nuget/packages/microsoft.web.webview2/1.0.4314.35/runtimes/win-x64/native/WebView2Loader.dll" "$out/"
    ;;
  run)
    name=$2
    shift 2
    root=$repo/artifacts/webview2-profiles/m1b-host-details/leak-$name
    rm -rf "$root" && mkdir -p "$root"
    LOCALAPPDATA=$(cygpath -w "$root") "$out/LeakProbe.exe" --out "$(cygpath -w "$repo/artifacts/m1b-host-details/logs/leak-$name.log")" \
      --profile "$(cygpath -w "$root/profile")" "$@"
    echo "leak-$name: $?"
    ;;
esac
