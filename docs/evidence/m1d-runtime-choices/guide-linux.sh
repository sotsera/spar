#!/usr/bin/env bash
# M1d step 7 (docs/evidence/m1d-runtime-choices.md): the publishing-choices guide's recipes in WSL (Ubuntu-24.04), from
# Spar's packages packed in the copy docs/evidence/m1c-blackbox/wsl.sh keeps (run its `sync client` first), in
# /var/tmp/spar-guide-m1d7, outside it. The environment is that script's: its caches, the unpacked Xvfb wrapper on PATH,
# HOME and every XDG folder under a root of this run, DISPLAY and WAYLAND_DISPLAY unset unless `start` starts its own
# Xvfb (:196, with a D-Bus session from dbus-run-session), so nothing opens on WSLg's display. The project blocks come
# from docs/guide/publishing-choices.md itself, by their markers. From Git Bash:
#   MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu-24.04 -- bash /mnt/<repo>/docs/evidence/m1d-runtime-choices/guide-linux.sh <step>
# Steps, in this order: pack new aot globalization fd sf `start aot|fd|sf [runs]` sql members.
set -uo pipefail
prlimit --core=1 --pid $$
here=$(cd "$(dirname "$0")" && pwd)
base=/var/tmp/spar-m1c-blackbox
repo=$base/repo
cache=/var/tmp/spar-linux/cache
G=/var/tmp/spar-guide-m1d7
homes=$G/home-root
export DOTNET_ROOT=$cache/dotnet PATH=$base/bin:$cache/dotnet:$PATH
export NUGET_PACKAGES=$cache/nuget-packages NUGET_HTTP_CACHE_PATH=$cache/nuget-http npm_config_cache=$cache/npm
export LD_LIBRARY_PATH=$cache/libs/usr/lib/x86_64-linux-gnu${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=false
unset DISPLAY WAYLAND_DISPLAY GTK_A11Y NO_AT_BRIDGE AT_SPI_BUS_ADDRESS DBUS_SESSION_BUS_ADDRESS CI BROWSER
for v in $(env | grep -o '^\(SPAR_\|JSC_\|WEBKIT_\)[^=]*' || true); do unset "$v"; done
mkdir -p "$homes"/{home,cache,config,data,state} /dev/shm/spar-guide-m1d7 && chmod 700 /dev/shm/spar-guide-m1d7
export HOME=$homes/home XDG_CACHE_HOME=$homes/cache XDG_CONFIG_HOME=$homes/config XDG_DATA_HOME=$homes/data XDG_STATE_HOME=$homes/state XDG_RUNTIME_DIR=/dev/shm/spar-guide-m1d7
hive=$G/hive
guide=$repo/docs/guide/publishing-choices.md
app=$G/work/Notes
proj=$app/Notes/Notes.csproj
timed() { local s=$(date +%s%N); "$@"; local e=$?; echo "STEP exit $e in $(( ($(date +%s%N)-s)/1000000 )) ms: $*"; return $e; }
V=$(cat "$G/version" 2>/dev/null || true)
snippet() { awk -v m="<!-- project-snippet: $1 -->" '$0==m{f=1;next} f&&/^```xml/{p=1;next} p&&/^```/{exit} p' "$guide"; }
with() {
  local project=$1; shift
  local body; body=$(for s in "$@"; do snippet "$s"; echo; done)
  awk -v add="$body" '/^<\/Project>/{print add} {print}' "$project.orig" > "$project"
}
sizes() { find "$1" -type f -printf '%s %P\n' | sort -k2; echo "files: $(find "$1" -type f | wc -l), bytes: $(find "$1" -type f -printf '%s\n' | awk '{s+=$1} END {print s}')"; }

# Starts the executable on a private Xvfb with a D-Bus session, until app.log has its ui-ready line, then SIGTERM.
start_once() {
  local exe=$1 log=$G/start-$(date +%s%N).log display=:196
  rm -rf "$homes/data/com.example.Notes"
  Xvfb $display -nolisten tcp -noreset >/dev/null 2>&1 & local xvfb=$!
  sleep 1
  (cd "$(dirname "$exe")" && DISPLAY=$display exec setsid dbus-run-session -- "$exe" > "$log" 2>&1) & local runner=$!
  local deadline=$((SECONDS + 60)) found=0
  while [ $SECONDS -lt $deadline ]; do
    if grep -q "ui-ready at" "$homes/data/com.example.Notes/logs/app.log" 2>/dev/null; then found=1; break; fi
    sleep 0.1
  done
  grep -h "ui-ready at\|ERROR" "$homes/data/com.example.Notes/logs/app.log" 2>/dev/null | head -2 | cut -c1-200
  pkill -TERM -s $runner 2>/dev/null; sleep 2
  pkill -KILL -s $runner 2>/dev/null
  kill $xvfb 2>/dev/null; wait 2>/dev/null
  return $((1 - found))
}

case $1 in
  pack)
    V=0.1.0-dev.guide$(date -u +%Y%m%d%H%M%S); rm -rf "$G/feed" "$G/work" "$G/hive" "$homes"; mkdir -p "$G/feed" "$G/work" "$homes"/{home,cache,config,data,state}; echo "$V" > "$G/version"
    for p in Spar Spar.Windows Spar.Linux Spar.Loopback Spar.Testing Spar.Testing.Browser Spar.Sdk Spar.Templates Spar.Tool; do
      (cd "$repo" && timed dotnet pack "src/$p/$p.csproj" -c Release -p:VersionSuffix=${V#0.1.0-} -o "$G/feed" -nologo -nr:false -v:q) || exit 1
    done
    cat > "$G/nuget.config" <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="spar" value="$G/feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="spar"><package pattern="Spar" /><package pattern="Spar.*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
XML
    ;;
  new)
    cd "$G/work"
    timed dotnet new install "Spar.Templates@$V" --add-source "$G/feed" --debug:custom-hive "$hive"
    timed dotnet new spar-app -n Notes --no-end-to-end-tests --debug:custom-hive "$hive"
    cp "$proj" "$proj.orig"
    cd Notes && timed dotnet build Notes -nologo -v:m | tail -3 && timed dotnet tool restore ;;
  aot|globalization|fd|sf)
    case $1 in aot) with "$proj" ;; globalization) with "$proj" globalization ;; fd) with "$proj" coreclr-framework-dependent ;; sf) with "$proj" coreclr-single-file ;; esac
    out=$G/publish-$1; rm -rf "$out"
    cd "$app" && timed dotnet publish Notes -c Release -o "$out" -nologo | tail -3
    sizes "$out"
    cd "$app/Notes" && timed dotnet spar doctor --app "$out/Notes" | sed -n '/^Build identity/,$p'
    cp "$proj.orig" "$proj" ;;
  start)
    for i in $(seq 1 "${3:-5}"); do start_once "$G/publish-$2/Notes"; done
    find "$homes/home/.net" -type f -printf 'extracted: %s %P\n' 2>/dev/null | head ;;
  sql)
    cd "$G/work" && rm -rf Ledger && timed dotnet new spar-app -n Ledger --no-end-to-end-tests --debug:custom-hive "$hive" >/dev/null
    lp=$G/work/Ledger/Ledger/Ledger.csproj; cp "$lp" "$lp.orig"; bash "$here/sql-probe.sh" "$G/work/Ledger/Ledger" Ledger || exit 1
    with "$lp" sqlclient-package globalization sqlclient-single-file
    cd "$G/work/Ledger" && rm -rf "$G/publish-sql"
    timed dotnet publish Ledger -c Release -o "$G/publish-sql" -nologo > "$G/sql-strict.log" 2>&1
    grep -oE "(error|warning) IL[0-9]+: Assembly '[^']+'" "$G/sql-strict.log" | sort | uniq -c
    with "$lp" sqlclient-package accept-aot-warnings globalization sqlclient-single-file
    rm -rf "$G/publish-sql"
    timed dotnet publish Ledger -c Release -o "$G/publish-sql" -nologo > "$G/sql-recipe.log" 2>&1 || { echo "the recipe publish failed"; exit 1; }
    grep -oE "(error|warning) IL[0-9]+: Assembly '[^']+'" "$G/sql-recipe.log" | sort | uniq -c
    sizes "$G/publish-sql"
    cd "$G/publish-sql" && timed timeout 30 ./Ledger --sql-probe 1
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 timed timeout 30 ./Ledger --sql-probe 1 ;;
  members)
    cd "$G/work/Ledger" && timed dotnet publish Ledger -c Release -p:TrimmerSingleWarn=false -o "$G/publish-members" -nologo > "$G/sql-members.log" 2>&1
    grep -E "(error|warning) IL[0-9]+" "$G/sql-members.log" | sed 's/ \[.*//' | sort -u | grep -oE "(error|warning) IL[0-9]+" | sort | uniq -c
    echo "distinct: $(grep -E "(error|warning) IL[0-9]+" "$G/sql-members.log" | sed 's/ \[.*//' | sort -u | wc -l)" ;;
  *) echo "unknown step $1"; exit 2 ;;
esac
