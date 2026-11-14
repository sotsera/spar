#!/usr/bin/env bash
# Runs inside the harness container (see run.sh). Usage: inside.sh <step>...
# Sources come from the read-only /src mount and are built in the container's own /work (no Windows bin/obj shared).
# Output goes to /out (the spike's artifacts/ folder): results/<runtime>-<mode>-<arg>.txt plus build logs.
# Connection strings arrive as SPAR_SQL_A / SPAR_SQL_B in the container environment; they are never printed.
set -u
results=/out/results
mkdir -p "$results"
work=/work/spikes/sqlclient-linux
aot=SqlTool.App/bin/aot/Release/net11.0/linux-x64/publish/SqlTool.App
jit=SqlTool.App/bin/jit/Release/net11.0/linux-x64/SqlTool.App
lib=/usr/lib/x86_64-linux-gnu
hidden=/tmp/spar-hidden-libs

ms() { echo $(( ($(date +%s%N) - $1) / 1000000 )); }

# h <runtime> <mode> [arg] [label suffix]: one harness process, output to results/, summary to the console.
h() {
  local runtime=$1 mode=$2 arg=${3:-} suffix=${4:-}
  local exe=$aot; [ "$runtime" = jit ] && exe=$jit
  local name="$runtime-$mode${arg:+-$arg}${suffix:+-$suffix}"
  local start; start=$(date +%s%N)
  "$exe" "$mode" $arg >"$results/$name.tmp" 2>&1
  local code=$?
  { echo "exit $code wallMs $(ms "$start")"; cat "$results/$name.tmp"; } >"$results/$name.txt"
  rm -f "$results/$name.tmp"
  echo "[$name] exit $code $(ms "$start") ms"
  grep -E '^(RESULT|FAIL|PASS|MEM end)' "$results/$name.txt" | sed 's/^/  /'
}

unique_warnings() { grep -oE 'warning IL[0-9]+: [^[]*' "$1" | sed 's/ *$//' | sort -u; }

hide() { mkdir -p "$hidden"; for f in "$@"; do mv "$lib/$f" "$hidden/"; done; ldconfig; }
unhide() { mv "$hidden"/* "$lib/"; ldconfig; }

step_sync() {
  mkdir -p "$work"
  cp /src/Directory.Build.props /src/Directory.Packages.props /src/global.json /src/.editorconfig /work/
  (cd /src/spikes/sqlclient-linux && tar cf - --exclude=bin --exclude=obj --exclude=artifacts .) | (cd "$work" && tar xf -)
  echo "synced"
}

step_build() {
  cd "$work"
  local start
  start=$(date +%s%N)
  dotnet build SqlTool.App -c Release -p:SqlToolRuntime=jit -p:TreatWarningsAsErrors=false -nologo >/out/build-jit.log 2>&1 \
    || { echo "JIT build failed"; tail -20 /out/build-jit.log; return 1; }
  echo "build jit $(ms "$start") ms; warnings: $(grep -cE ': warning ' /out/build-jit.log) lines"
  # App-level acceptance of the AOT warnings (user decision), on the command line only. Full ILC run each time.
  for run in 1 2 3; do
    rm -rf SqlTool.App/obj/aot/Release/net11.0/linux-x64/native
    start=$(date +%s%N)
    dotnet publish SqlTool.App -c Release -p:SqlToolRuntime=aot -p:TreatWarningsAsErrors=false \
      -p:IlcTreatWarningsAsErrors=false -nologo >/out/publish-aot-$run.log 2>&1 \
      || { echo "AOT publish failed"; tail -20 /out/publish-aot-$run.log; return 1; }
    echo "publish aot run $run $(ms "$start") ms"
  done
  unique_warnings /out/publish-aot-1.log >/out/warnings-aot.txt
  echo "warnings aot: $(wc -l </out/warnings-aot.txt) unique; by code: $(cut -d: -f1 /out/warnings-aot.txt | sort | uniq -c | tr -s ' ' | tr '\n' ',')"
  local dir=SqlTool.App/bin/aot/Release/net11.0/linux-x64/publish
  {
    echo "== publish folder"; ls -la "$dir"
    echo "== ELF header"; readelf -h "$dir/SqlTool.App" | grep -E "Class|Type|Machine"
    echo "== NEEDED"; readelf -d "$dir/SqlTool.App" | grep NEEDED
    echo "== ldd"; ldd "$dir/SqlTool.App"
    echo "== shared-object names embedded in the exe (dlopen candidates)"
    strings "$dir/SqlTool.App" | grep -E '^lib[A-Za-z0-9_+.-]*\.so(\.[0-9]+)*$' | sort -u
    echo "== jit output"; ls SqlTool.App/bin/jit/Release/net11.0/linux-x64 | wc -l; du -sh SqlTool.App/bin/jit/Release/net11.0/linux-x64
    echo "== SqlClient assembly used (jit)"; ls -la SqlTool.App/bin/jit/Release/net11.0/linux-x64/Microsoft.Data.SqlClient*.dll
    echo "== package runtimes"; find /root/.nuget/packages/microsoft.data.sqlclient/7.1.2 -path '*runtimes*' -name '*.dll' | sed 's|/root/.nuget/packages/||'
    find /root/.nuget/packages/microsoft.data.sqlclient.sni.runtime 2>/dev/null -type f | sed 's|/root/.nuget/packages/||'
  } >/out/aot-layout.txt 2>&1
  echo "aot exe $(stat -c %s $dir/SqlTool.App) bytes; files: $(ls $dir | tr '\n' ' ')"
}

# Strict publish (repository rule: warnings are errors). Known to fail on Windows; recorded for the Linux count.
step_strict() {
  cd "$work"
  rm -rf SqlTool.App/obj/aot/Release/net11.0/linux-x64/native
  # 1. The repository rule as is: the app's own IL2026 (DataTable.Load, getschema probe) stops the C# compile.
  dotnet publish SqlTool.App -c Release -p:SqlToolRuntime=aot -nologo >/out/publish-strict.log 2>&1
  echo "strict publish exit $? ; errors: $(grep -oE 'error [A-Z]+[0-9]+' /out/publish-strict.log | sort | uniq -c | tr -s ' ')"
  # 2. Compile lax, ILC strict: does ILC itself fail on the package diagnostics?
  rm -rf SqlTool.App/obj/aot/Release/net11.0/linux-x64/native
  dotnet publish SqlTool.App -c Release -p:SqlToolRuntime=aot -p:TreatWarningsAsErrors=false \
    -p:IlcTreatWarningsAsErrors=true -nologo >/out/publish-ilcstrict.log 2>&1
  echo "ilc-strict publish exit $? ; unique IL errors: $(grep -oE 'error IL[0-9]+: [^[]*' /out/publish-ilcstrict.log | sort -u | wc -l)"
  # Restore the lax publish output for later steps.
  rm -rf SqlTool.App/obj/aot/Release/net11.0/linux-x64/native
  dotnet publish SqlTool.App -c Release -p:SqlToolRuntime=aot -p:TreatWarningsAsErrors=false \
    -p:IlcTreatWarningsAsErrors=false -nologo >/out/publish-aot-relax.log 2>&1 && echo "lax publish restored"
}

step_wait() {
  cd "$work"
  local start; start=$(date +%s%N)
  for _ in $(seq 1 90); do
    if "$jit" ping 2>&1 | grep -q 'PING 11'; then echo "servers ready after $(ms "$start") ms"; return 0; fi
    sleep 2
  done
  echo "servers not ready"; return 1
}

step_setup() { cd "$work"; h aot setup 1000000; }

step_bulk() {
  cd "$work"
  for variant in default default-tablock b10k b100k b100k-stream b100k-tablock b100k-internaltx b100k-externaltx; do
    for runtime in aot jit; do h $runtime bulk $variant; done
  done
  for repeat in 2 3; do
    for variant in default b100k; do
      for runtime in aot jit; do h $runtime bulk $variant "r$repeat"; done
    done
  done
}

step_bulkcancel() {
  cd "$work"
  for variant in none none-timed none-drain internal external abort batch0 batch0-internal batch0-external \
    batch0-abort batch0-tablock; do
    for runtime in aot jit; do h $runtime bulk-cancel $variant; done
  done
}

step_querycancel() {
  cd "$work"
  for c in waitfor-async waitfor-sync cpu-async cpu-parallel-async blocked-async stream-async dispose-drain dispose-cancel; do
    for runtime in aot jit; do h $runtime query-cancel $c; done
  done
}

step_read() {
  cd "$work"
  for variant in typed-async typed-sync seq-async seq-sync seq-stream-async values-sync; do
    for runtime in aot jit; do h $runtime read $variant; done
  done
}

step_schema() { cd "$work"; for r in aot jit; do h $r schema; h $r getschema; done; }
step_monitor() { cd "$work"; for r in aot jit; do h $r monitor 30; done; }

step_probes() {
  cd "$work"
  for r in aot jit; do
    for p in env transport tls auth globalization; do h $r probe $p; done
  done
}

# Required native libraries: hide one at a time and observe the failure. Always restored.
step_deps() {
  cd "$work"
  for r in aot jit; do
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 h $r ping "" invariant-env
  done
  hide libicuuc.so libicuuc.so.74 libicuuc.so.74.2 libicui18n.so libicui18n.so.74 libicui18n.so.74.2 \
    libicudata.so libicudata.so.74 libicudata.so.74.2
  for r in aot jit; do h $r ping "" no-icu; DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 h $r ping "" no-icu-invariant; done
  unhide
  hide libgssapi_krb5.so.2 libgssapi_krb5.so.2.2
  for r in aot jit; do h $r ping "" no-gssapi; h $r probe auth no-gssapi; done
  unhide
  hide libssl.so.3 libcrypto.so.3
  for r in aot jit; do h $r ping "" no-openssl; h $r probe env no-openssl; done
  unhide
  echo "libraries restored: $(ls $lib/libicuuc.so.74 $lib/libgssapi_krb5.so.2 $lib/libssl.so.3 2>&1 | wc -l)/3"
}

# libsecret: no session bus; then a private session bus without a Secret Service; then with gnome-keyring if present.
step_secret() {
  cd "$work"
  for r in aot jit; do
    (unset DBUS_SESSION_BUS_ADDRESS; h $r probe secret no-bus)
    # Without gnome-keyring nothing provides org.freedesktop.secrets; with it, D-Bus activates the daemon on demand.
    local label=bus-only
    command -v gnome-keyring-daemon >/dev/null && label=bus-activated
    dbus-run-session -- bash -c "source /work/spikes/sqlclient-linux/inside.sh --lib; h $r probe secret $label"
    if command -v gnome-keyring-daemon >/dev/null; then
      dbus-run-session -- bash -c "printf 'spike' | gnome-keyring-daemon --unlock --components=secrets >/dev/null 2>&1; \
        source /work/spikes/sqlclient-linux/inside.sh --lib; h $r probe secret gnome-keyring"
    fi
  done
}

# Installs gnome-keyring (a download of about 2 MB plus the apt lists) into this throwaway container only.
step_keyring() {
  apt-get update -qq && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends gnome-keyring 2>&1 |
    grep -E '^(Need|After)'
}

[ "${1:-}" = --lib ] && return 0
for step in "$@"; do
  echo "== $step $(date -u +%H:%M:%S)"
  "step_${step//-/}" || { echo "step $step failed"; exit 1; }
done
