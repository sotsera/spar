#!/usr/bin/env bash
# M1d step 6a (docs/evidence/m1d-template-package.md): the getting-started guide's commands in WSL (Ubuntu-24.04), from
# Spar's packages packed in the copy docs/evidence/m1c-blackbox/wsl.sh keeps (run its `sync client` first), in
# /var/tmp/spar-guide-m1d6a, outside it. The environment is that script's: its caches, the unpacked Xvfb wrapper on PATH,
# HOME and every XDG folder under a root of this run, DISPLAY and WAYLAND_DISPLAY unset unless a step starts its own
# Xvfb (:197, with a D-Bus session from dbus-run-session), so nothing opens on WSLg's display. From Git Bash:
#   MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu-24.04 -- bash /mnt/<repo>/docs/evidence/m1d-template-package/guide-linux.sh <step>
# Steps, in this order: pack new nodisplay run watch test pagetest e2ebuild loopback e2e aot publish exe where uninstall;
# then linux-stop.sh lists (and with `kill` ends) any process still running with this run's XDG_RUNTIME_DIR.
set -uo pipefail
prlimit --core=1 --pid $$
here=$(cd "$(dirname "$0")" && pwd)
base=/var/tmp/spar-m1c-blackbox
repo=$base/repo
cache=/var/tmp/spar-linux/cache
G=/var/tmp/spar-guide-m1d6a
homes=$G/home-root
export DOTNET_ROOT=$cache/dotnet PATH=$base/bin:$cache/dotnet:$PATH
export NUGET_PACKAGES=$cache/nuget-packages NUGET_HTTP_CACHE_PATH=$cache/nuget-http npm_config_cache=$cache/npm
export PLAYWRIGHT_BROWSERS_PATH=$cache/ms-playwright
export LD_LIBRARY_PATH=$cache/libs/usr/lib/x86_64-linux-gnu${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=false
unset DISPLAY WAYLAND_DISPLAY GTK_A11Y NO_AT_BRIDGE AT_SPI_BUS_ADDRESS DBUS_SESSION_BUS_ADDRESS CI BROWSER
for v in $(env | grep -o '^\(SPAR_\|JSC_\|WEBKIT_\)[^=]*' || true); do unset "$v"; done
export SPAR_TEST_ARTIFACTS=$repo/artifacts
mkdir -p "$homes"/{home,cache,config,data,state} /dev/shm/spar-guide-m1d6a && chmod 700 /dev/shm/spar-guide-m1d6a
export HOME=$homes/home XDG_CACHE_HOME=$homes/cache XDG_CONFIG_HOME=$homes/config XDG_DATA_HOME=$homes/data XDG_STATE_HOME=$homes/state XDG_RUNTIME_DIR=/dev/shm/spar-guide-m1d6a
hive=$G/hive
app=$G/work/Notes
timed() { local s=$(date +%s%N); "$@"; local e=$?; echo "STEP exit $e in $(( ($(date +%s%N)-s)/1000000 )) ms: $*"; return $e; }
V=$(cat "$G/version" 2>/dev/null || true)

# Runs "$@" in the app folder on a private Xvfb with a D-Bus session, until app.log has $until, then SIGTERM.
desktop() {
  local until=$1 edit=${2:-}; shift 2
  local display=:197 log=$G/desktop-$(date +%s).log
  Xvfb $display -nolisten tcp -noreset >/dev/null 2>&1 & local xvfb=$!
  sleep 1
  (cd "$app/Notes" && DISPLAY=$display exec setsid dbus-run-session -- "$@" > "$log" 2>&1) & local runner=$!
  local deadline=$((SECONDS + 240)) found=0 edited=0
  while [ $SECONDS -lt $deadline ]; do
    if grep -q "ui-ready at" "$log" 2>/dev/null && [ -n "$edit" ] && [ $edited = 0 ]; then
      sleep 2; sed -i 's/Hello, /Hi, /' "$app/Notes/Greetings/GreetingService.cs"; edited=1; echo "edit written at $(date +%T.%N)"
    fi
    if grep -q "$until" "$log" 2>/dev/null; then found=1; break; fi
    sleep 0.2
  done
  grep -E "ui-ready at|Hot Reload applied|App id|ERROR|WARN|changes applied|Loopback" "$log" | cut -c1-200
  echo "found '$until': $found"
  [ -n "$edit" ] && sed -i 's/Hi, /Hello, /' "$app/Notes/Greetings/GreetingService.cs"
  # setsid made the runner its session's leader (exec: the subshell is setsid itself): end the whole session.
  pkill -TERM -s $runner 2>/dev/null; sleep 3
  pkill -KILL -s $runner 2>/dev/null
  kill $xvfb 2>/dev/null; wait 2>/dev/null
  local record=$app/Notes/obj/spar-dev-server.json
  [ -f "$record" ] && kill -TERM "$(python3 -c "import json,sys;print(json.load(open('$record'))['pid'])")" 2>/dev/null
  return $((1 - found))
}

case $1 in
  pack)
    V=0.1.0-dev.guide$(date -u +%Y%m%d%H%M%S); rm -rf "$G"; mkdir -p "$G/feed" "$G/work"; echo "$V" > "$G/version"
    mkdir -p "$homes"/{home,cache,config,data,state}
    for p in Spar Spar.Windows Spar.Linux Spar.Loopback Spar.Testing Spar.Testing.Browser Spar.Sdk Spar.Templates; do
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
    timed dotnet new spar-app -n Notes --debug:custom-hive "$hive"
    find Notes -type f | sort | head -40 ;;
  nodisplay) cd "$app/Notes" && timed dotnet run ;;
  run) desktop "ui-ready at" "" dotnet run ;;
  watch) desktop "Hot Reload applied" edit dotnet watch ;;
  loopback)
    pw=$(find "$app/Notes.EndToEndTests/bin" -type d -path '*/.playwright/package' | head -1)
    node "$here/loopback-check.cjs" "$app/Notes" "$pw" "$G/loopback.log" ;;
  test) cd "$app" && timed dotnet test --project Notes.Tests ;;
  pagetest) cd "$app/Notes/frontend" && timed npm test ;;
  e2ebuild) cd "$app" && timed dotnet build Notes.EndToEndTests && find Notes.EndToEndTests/bin -name playwright.ps1 ;;
  e2e) cd "$app" && for s in Browser Window BlackBox; do timed dotnet test --project Notes.EndToEndTests --filter-trait Surface=$s; done ;;
  aot) cd "$app" && timed dotnet publish Notes.Tests -c Release -p:SparTestLane=Aot -o Notes.Tests/bin/aot-tests && timed Notes.Tests/bin/aot-tests/Notes.Tests ;;
  publish) cd "$app" && timed dotnet publish Notes -c Release && ls -la Notes/bin/Production/Release/net11.0/linux-x64/publish/ ;;
  exe) desktop "ui-ready at" "" "$app/Notes/bin/Production/Release/net11.0/linux-x64/publish/Notes" ;;
  where) find "$homes" -name app.log; find "$homes" -maxdepth 3 -type d | head -20 ;;
  uninstall) cd "$G/work" && timed dotnet new uninstall Spar.Templates --debug:custom-hive "$hive" ;;
esac
