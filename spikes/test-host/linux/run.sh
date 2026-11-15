#!/usr/bin/env bash
# Runs the Linux lane inside the spar-test-host:webkit6 container (see README in RESULTS.md):
#   docker run --rm --init --shm-size=1g --security-opt seccomp=unconfined --user 1000:1000 \
#     -v "<repo>:/src:ro" -v "<repo>/spikes/test-host/results:/out" -v spar-test-host-nuget:/nuget \
#     spar-test-host:webkit6 bash /src/spikes/test-host/linux/run.sh [runs]
# The sources are copied without bin/ and obj/, so nothing is shared with the Windows builds. HOME and the XDG
# folders of the run live under the copy's artifacts/linux-homes/<run>/, as the repository requires.
set -euo pipefail
runs=${1:-2}
run=$(date -u +%Y%m%d-%H%M%S)

work=${WORK:-/tmp/work}
mkdir -p "$work/repo"
cd /src
tar cf - --exclude=bin --exclude=obj --exclude=results \
  global.json Directory.Build.props Directory.Build.targets Directory.Packages.props .editorconfig \
  spikes/test-host spikes/react/React.Desktop/Interop spikes/linux-desktop/Linux.Desktop/Native.cs \
  | (cd "$work/repo" && tar xf -)
mkdir -p "$work/repo/artifacts"

homes=$work/repo/artifacts/linux-homes/test-host-$run
mkdir -p "$homes"/{home,cache,data,config,runtime}
chmod 700 "$homes/runtime"
export HOME=$homes/home XDG_CACHE_HOME=$homes/cache XDG_DATA_HOME=$homes/data XDG_CONFIG_HOME=$homes/config XDG_RUNTIME_DIR=$homes/runtime
export NUGET_PACKAGES=/nuget DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

Xvfb :99 -screen 0 1280x1024x24 -nolisten tcp >/dev/null 2>&1 &
export DISPLAY=:99 GDK_BACKEND=x11
sleep 1

cd "$work/repo/spikes/test-host"
{
  echo "SPIKE env: $(uname -r) webkitgtk=$(dpkg-query -W -f='${Version}' libwebkitgtk-6.0-4) gtk=$(dpkg-query -W -f='${Version}' libgtk-4-1) sdk=$(dotnet --version) cpus=$(nproc)"
  start=$(date +%s%N)
  dotnet build SampleApp.Tests 2>&1 | grep -E " error |warn|Build succeeded" | sort -u
  echo "SPIKE build $(( ($(date +%s%N) - start) / 1000000 )) ms"
} | tee /out/linux-build.txt

for r in $(seq 1 "$runs"); do
  ./SampleApp.Tests/bin/Debug/net11.0/linux-x64/SampleApp.Tests -noLogo -noColor -showLiveOutput >/out/linux-jit-run$r.txt 2>&1 || true
  grep -E "Total:" /out/linux-jit-run$r.txt || tail -20 /out/linux-jit-run$r.txt
done

{
  start=$(date +%s%N)
  dotnet publish SampleApp.AotTests -c Release 2>&1 | grep -E " error |warn" | sort -u || true
  echo "SPIKE publish $(( ($(date +%s%N) - start) / 1000000 )) ms size=$(stat -c %s SampleApp.AotTests/bin/Release/net11.0/linux-x64/publish/SampleApp.AotTests 2>/dev/null)"
} | tee /out/linux-aot-publish.txt

for r in $(seq 1 "$runs"); do
  ./SampleApp.AotTests/bin/Release/net11.0/linux-x64/publish/SampleApp.AotTests -noLogo -noColor -showLiveOutput >/out/linux-aot-run$r.txt 2>&1 || true
  grep -E "Total:" /out/linux-aot-run$r.txt || tail -20 /out/linux-aot-run$r.txt
done

echo "SPIKE leftover WebKit processes: $(pgrep -fc 'WebKit(Web|Network)Process' || true)"
