#!/usr/bin/env bash
# M1d step 6b (docs/evidence/m1d-spar-tool.md): the getting-started guide's tools section on Windows, from packages
# only, in %TEMP%\spar-guide-m1d6b, outside the repository. Spar's nine packages are packed from this tree at a version
# of their own into a feed there, whose nuget.config sits in the folder above the new apps. NuGet's and npm's caches
# are the package tests' (%TEMP%\spar-package-tests), the template hive and the .NET CLI's home (DOTNET_CLI_HOME, where
# dotnet tool restore writes its resolver cache) are the run's, and LOCALAPPDATA (the apps' folders and WebView2
# profiles) is under artifacts/webview2-profiles/m1d6b-guide/. The window run uses ../m1d-hot-reload/WatchDesktop.cs,
# so no window reaches the monitor. From Git Bash, in this order:
#   guide-win.sh pack|new|restore|doctor|init|scaffold|publish|identity|run
set -uo pipefail
repo=$(cd "$(dirname "$0")/../../.." && pwd)
logs=$repo/artifacts/m1d-spar-tool/windows
G=$(cygpath -u "$TEMP")/spar-guide-m1d6b
shared=$(cygpath -u "$TEMP")/spar-package-tests
V=$(cat "$G/version" 2>/dev/null || true)
mkdir -p "$logs"
profiles=$repo/artifacts/webview2-profiles/m1d6b-guide
export NUGET_PACKAGES=$(cygpath -w "$shared/nuget-packages") NUGET_HTTP_CACHE_PATH=$(cygpath -w "$shared/http-cache") npm_config_cache=$(cygpath -w "$shared/npm-cache")
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=false
export DOTNET_CLI_HOME=$(cygpath -w "$G/dotnet-cli-home") LOCALAPPDATA=$(cygpath -w "$profiles/local")
hive=$(cygpath -w "$G/hive")
timed() { local s=$(date +%s%N); "$@"; local e=$?; echo "STEP exit $e in $(( ($(date +%s%N)-s)/1000000 )) ms: $*"; return $e; }
app=$G/work/Notes
case $1 in
  pack)
    V=0.1.0-dev.guide$(date -u +%Y%m%d%H%M%S)
    rm -rf "$G"; mkdir -p "$G/feed" "$G/work" "$G/dotnet-cli-home" "$profiles/local"; echo "$V" > "$G/version"
    for p in Spar Spar.Windows Spar.Linux Spar.Loopback Spar.Testing Spar.Testing.Browser Spar.Sdk Spar.Templates Spar.Tool; do
      (cd "$repo" && timed dotnet pack "src/$p/$p.csproj" -c Release -p:VersionSuffix=${V#0.1.0-} -o "$(cygpath -w "$G/feed")" -nologo -nr:false -v:q) || exit 1
    done
    ls -l "$G/feed" | grep -i "spar.tool"
    cat > "$G/nuget.config" <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="spar" value="$(cygpath -w "$G/feed")" />
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
    timed dotnet new install "Spar.Templates@$V" --add-source "$(cygpath -w "$G/feed")" --debug:custom-hive "$hive"
    timed dotnet new spar-app -n Notes --debug:custom-hive "$hive"
    cat Notes/.config/dotnet-tools.json
    cd Notes && timed dotnet build Notes -nologo -v:m | tail -3 ;;
  restore) cd "$app" && timed dotnet tool restore && find "$G/dotnet-cli-home" -maxdepth 3 | sed "s|$G|%TEMP%/spar-guide-m1d6b|" ;;
  doctor)
    cd "$app" && timed dotnet spar doctor
    timed dotnet spar doctor --json > "$logs/doctor.json"; echo "json exit: $?"
    cd Notes && timed dotnet spar --version ;;
  init) cd "$app/Notes" && timed dotnet spar init frontend ;;
  scaffold)
    # A second app with no frontend folder: init without a terminal and without --yes writes nothing; with --yes it
    # makes the frontend with npm create vite and wires it; the app then builds, and doctor checks it.
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
    timed dotnet spar doctor --app bin/Production/Release/net11.0/win-x64/publish/Notes.exe | sed -n '/^Build identity/,$p'
    timed dotnet spar doctor --app bin/Development/Debug/net11.0/win-x64/Notes.exe | sed -n '/^Build identity/,$p' ;;
  run)
    # The window's first log line, from the Debug build (dotnet run).
    timed dotnet run "$(cygpath -w "$repo/docs/evidence/m1d-hot-reload/WatchDesktop.cs")" -- --project "$(cygpath -w "$app/Notes")" --root "$(cygpath -w "$profiles/run")" \
      --log "$(cygpath -w "$logs/run.log")" --command "dotnet run" --steps "until:ui-ready;ctrlc;sleep:3000"
    grep -h "Host started" "$logs/run.log" | head -2 ;;
esac
