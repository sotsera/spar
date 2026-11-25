#!/usr/bin/env bash
# M1d step 6b (docs/evidence/m1d-spar-tool.md): the getting-started guide's tools section in WSL (Ubuntu-24.04), from
# Spar's packages packed in the copy docs/evidence/m1c-blackbox/wsl.sh keeps (run its `sync client` first), in
# /var/tmp/spar-guide-m1d6b, outside it. The environment is that script's: its caches, HOME and every XDG folder under a
# root of this run (so dotnet tool restore's resolver cache, under the CLI's home, is the run's too), DISPLAY and
# WAYLAND_DISPLAY unset unless the run step starts its own Xvfb (:198, with a D-Bus session from dbus-run-session), so
# nothing opens on WSLg's display. From Git Bash:
#   MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu-24.04 -- bash /mnt/<repo>/docs/evidence/m1d-spar-tool/guide-linux.sh <step>
# Steps, in this order: pack new restore doctor init scaffold publish identity run.
set -uo pipefail
prlimit --core=1 --pid $$
base=/var/tmp/spar-m1c-blackbox
repo=$base/repo
cache=/var/tmp/spar-linux/cache
G=/var/tmp/spar-guide-m1d6b
homes=$G/home-root
export DOTNET_ROOT=$cache/dotnet PATH=$base/bin:$cache/dotnet:$PATH
export NUGET_PACKAGES=$cache/nuget-packages NUGET_HTTP_CACHE_PATH=$cache/nuget-http npm_config_cache=$cache/npm
export LD_LIBRARY_PATH=$cache/libs/usr/lib/x86_64-linux-gnu${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=false
unset DISPLAY WAYLAND_DISPLAY GTK_A11Y NO_AT_BRIDGE AT_SPI_BUS_ADDRESS DBUS_SESSION_BUS_ADDRESS CI BROWSER
for v in $(env | grep -o '^\(SPAR_\|JSC_\|WEBKIT_\)[^=]*' || true); do unset "$v"; done
mkdir -p "$homes"/{home,cache,config,data,state} /dev/shm/spar-guide-m1d6b && chmod 700 /dev/shm/spar-guide-m1d6b
export HOME=$homes/home XDG_CACHE_HOME=$homes/cache XDG_CONFIG_HOME=$homes/config XDG_DATA_HOME=$homes/data XDG_STATE_HOME=$homes/state XDG_RUNTIME_DIR=/dev/shm/spar-guide-m1d6b
hive=$G/hive
app=$G/work/Notes
timed() { local s=$(date +%s%N); "$@"; local e=$?; echo "STEP exit $e in $(( ($(date +%s%N)-s)/1000000 )) ms: $*"; return $e; }
V=$(cat "$G/version" 2>/dev/null || true)

case $1 in
  pack)
    V=0.1.0-dev.guide$(date -u +%Y%m%d%H%M%S)
    rm -rf "$G/feed" "$G/work" "$G/hive" "$homes"; mkdir -p "$G/feed" "$G/work" "$homes"/{home,cache,config,data,state}; echo "$V" > "$G/version"
    for p in Spar Spar.Windows Spar.Linux Spar.Loopback Spar.Testing Spar.Testing.Browser Spar.Sdk Spar.Templates Spar.Tool; do
      (cd "$repo" && timed dotnet pack "src/$p/$p.csproj" -c Release -p:VersionSuffix=${V#0.1.0-} -o "$G/feed" -nologo -nr:false -v:q) || exit 1
    done
    ls -l "$G/feed" | grep -i "spar.tool"
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
    cat Notes/.config/dotnet-tools.json
    cd Notes && timed dotnet build Notes -nologo -v:m | tail -3 ;;
  restore) cd "$app" && timed dotnet tool restore && find "$HOME/.dotnet" -maxdepth 2 | sed "s|$G|<run>|" ;;
  doctor)
    cd "$app" && timed dotnet spar doctor
    timed dotnet spar doctor --json > "$G/doctor.json"; echo "json exit: $?"
    cd Notes && timed dotnet spar --version ;;
  init) cd "$app/Notes" && timed dotnet spar init frontend ;;
  scaffold)
    cd "$G/work" && timed dotnet new spar-app -n Pages --no-end-to-end-tests --debug:custom-hive "$hive" >/dev/null
    rm -rf Pages/Pages/frontend && cd Pages && timed dotnet tool restore >/dev/null
    cd Pages
    timed dotnet spar init frontend --framework react < /dev/null; ls frontend 2>/dev/null | head -1
    timed dotnet spar init frontend --framework react --yes < /dev/null
    timed dotnet build -nologo -v:m | tail -3
    timed dotnet spar doctor | sed -n '/^Project/,$p' ;;
  publish) cd "$app" && timed dotnet publish Notes -c Release | tail -2 ;;
  identity)
    cd "$app/Notes"
    timed dotnet spar doctor --app bin/Production/Release/net11.0/linux-x64/publish/Notes | sed -n '/^Build identity/,$p'
    timed dotnet spar doctor --app bin/Development/Debug/net11.0/linux-x64/Notes | sed -n '/^Build identity/,$p' ;;
  run)
    # The window's first log line, from the published executable on a private Xvfb with its own D-Bus session.
    display=:198 log=$G/run.log
    Xvfb $display -nolisten tcp -noreset >/dev/null 2>&1 & xvfb=$!
    sleep 1
    (cd "$app/Notes/bin/Production/Release/net11.0/linux-x64/publish" && DISPLAY=$display exec setsid dbus-run-session -- ./Notes > "$log" 2>&1) & runner=$!
    for _ in $(seq 1 150); do grep -q "ui-ready at" "$log" 2>/dev/null && break; sleep 0.2; done
    grep -E "Host started|ui-ready at" "$log" | cut -c1-260
    pkill -TERM -s $runner 2>/dev/null; sleep 3; pkill -KILL -s $runner 2>/dev/null
    kill $xvfb 2>/dev/null; wait 2>/dev/null ;;
esac
