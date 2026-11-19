#!/usr/bin/env bash
# Usage (Windows, repository root): wsl -d Ubuntu-24.04 --exec bash spikes/linux-gpu-csp/wsl/build.sh
# Copies the spike into the working copy, publishes GpuProbe with NativeAOT (trim/AOT warnings fail the publish),
# copies the unpacked Xvfb tools of spikes/window-automation-linux, and copies the probe binary to the repository's
# artifacts/linux-gpu-csp/ for the containers.
set -euo pipefail
here=$(cd "$(dirname "$0")/.." && pwd)
repo=$(cd "$here/../.." && pwd)
source "$here/wsl/common.sh"
mkdir -p "$spike" "$base/logs"
cp "$repo/global.json" "$repo/Directory.Build.props" "$repo/Directory.Build.targets" "$repo/.editorconfig" "$work/"
cat >"$work/nuget.config" <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
XML
(cd "$here" && tar cf - --exclude=bin --exclude=obj --exclude=results Directory.Build.props GpuProbe tools wsl csp docker 2>/dev/null) | (cd "$spike" && tar xf -)
isolate build
cd "$spike/GpuProbe"
rm -rf bin obj
start=$(date +%s%N)
set +e
dotnet publish -c Release -o "$base/aot" -nologo -clp:Summary >"$base/logs/publish.log" 2>&1
code=$?
set -e
echo "PUBLISH exit=$code $(( ($(date +%s%N) - start) / 1000000 ))ms size=$(stat -c %s "$base/aot/GpuProbe" 2>/dev/null) bytes"
grep -E "warning|error" "$base/logs/publish.log" | sort -u | head -20 || true
mkdir -p "$repo/artifacts/linux-gpu-csp"
cp "$base/aot/GpuProbe" "$repo/artifacts/linux-gpu-csp/GpuProbe"
src=/var/tmp/spar-linux/window-automation-linux/tools
if [ ! -x "$tools/root/usr/bin/Xvfb" ]; then
  mkdir -p "$tools" && cp -a "$src/root" "$tools/"
fi
mkdir -p "$tools/realbin" "$tools/usrbin"
if [ ! -e "$tools/usrbin/xkbcomp" ]; then
  (cd "$tools/usrbin" && for f in /usr/bin/*; do ln -sfn "$tools/realbin/$(basename "$f")" "$(basename "$f")"; done && ln -sfn "$tools/root/usr/bin/xkbcomp" xkbcomp)
fi
cleanup_runtime
