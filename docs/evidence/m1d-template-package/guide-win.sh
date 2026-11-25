#!/usr/bin/env bash
# M1d step 6a (docs/evidence/m1d-template-package.md): the getting-started guide's commands on Windows, from packages
# only, in %TEMP%\spar-guide-m1d6a, outside the repository. Spar's eight packages are packed from this tree at a version
# of their own into a feed there, whose nuget.config sits in the folder above the new app (an app from nuget.org needs
# none). NuGet's and npm's caches are the package tests' (%TEMP%\spar-package-tests), the template hive is the run's,
# Playwright's browsers go to the run's folder, and LOCALAPPDATA (the apps' folders and WebView2 profiles) is under
# artifacts/webview2-profiles/m1d6a-guide/. The window runs (run, watch) use ../m1d-hot-reload/WatchDesktop.cs, so
# no window reaches the monitor. From Git Bash, in this order:
#   guide-win.sh pack|new|run|watch|test|pagetest|e2ebuild|chromium|loopback|e2e|aot|publish|exe|uninstall
set -uo pipefail
repo=$(cd "$(dirname "$0")/../../.." && pwd)
here=$(cd "$(dirname "$0")" && pwd)
logs=$repo/artifacts/m1d-template-package/windows
G=$(cygpath -u "$TEMP")/spar-guide-m1d6a
shared=$(cygpath -u "$TEMP")/spar-package-tests
V=$(cat "$G/version" 2>/dev/null || true)
mkdir -p "$logs"
profiles=$repo/artifacts/webview2-profiles/m1d6a-guide
export NUGET_PACKAGES=$(cygpath -w "$shared/nuget-packages") NUGET_HTTP_CACHE_PATH=$(cygpath -w "$shared/http-cache") npm_config_cache=$(cygpath -w "$shared/npm-cache")
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=false
export SPAR_TEST_ARTIFACTS=$(cygpath -w "$repo/artifacts") PLAYWRIGHT_BROWSERS_PATH=$(cygpath -w "$G/ms-playwright")
export LOCALAPPDATA=$(cygpath -w "$profiles/local")
hive=$(cygpath -w "$G/hive")
timed() { local s=$(date +%s%N); "$@"; local e=$?; echo "STEP exit $e in $(( ($(date +%s%N)-s)/1000000 )) ms: $*"; return $e; }
app=$G/work/Notes
case $1 in
  pack)
    V=0.1.0-dev.guide$(date -u +%Y%m%d%H%M%S)
    rm -rf "$G"; mkdir -p "$G/feed" "$G/work" "$profiles/local"; echo "$V" > "$G/version"
    for p in Spar Spar.Windows Spar.Linux Spar.Loopback Spar.Testing Spar.Testing.Browser Spar.Sdk Spar.Templates; do
      (cd "$repo" && timed dotnet pack "src/$p/$p.csproj" -c Release -p:VersionSuffix=${V#0.1.0-} -o "$(cygpath -w "$G/feed")" -nologo -nr:false -v:q) || exit 1
    done
    # Spar's packages from the run's feed, everything else from nuget.org: what a published Spar needs no file for.
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
    timed dotnet new list spar --debug:custom-hive "$hive"
    timed dotnet new spar-app -n Notes --debug:custom-hive "$hive"
    timed dotnet new spar-app -h --debug:custom-hive "$hive" | sed -n '/Template options/,$p'
    find Notes -type f | sort ;;
  run)
    timed dotnet run "$(cygpath -w "$repo/docs/evidence/m1d-hot-reload/WatchDesktop.cs")" -- --project "$(cygpath -w "$app/Notes")" --root "$(cygpath -w "$profiles/run")" \
      --log "$(cygpath -w "$logs/run.log")" --command "dotnet run" --steps "until:ui-ready;ctrlc;sleep:3000" ;;
  watch)
    timed dotnet run "$(cygpath -w "$repo/docs/evidence/m1d-hot-reload/WatchDesktop.cs")" -- --project "$(cygpath -w "$app/Notes")" --root "$(cygpath -w "$profiles/watch")" \
      --log "$(cygpath -w "$logs/watch.log")" --command "dotnet watch" \
      --steps "until:ui-ready;sleep:2000;edit:Greetings/GreetingService.cs|Hello, |Hi, ;until:Hot Reload applied;sleep:1000;ctrlc;sleep:3000" ;;
  loopback)
    # dotnet run -lp Loopback, its page in Playwright's headless Chromium (after e2ebuild and chromium).
    pw=$(find "$app/Notes.EndToEndTests/bin" -type d -path '*/.playwright/package' | head -1)
    LOCALAPPDATA=$(cygpath -w "$profiles/loopback/local") timed node "$(cygpath -w "$here/loopback-check.cjs")"       "$(cygpath -w "$app/Notes")" "$(cygpath -w "$pw")" "$(cygpath -w "$logs/loopback.log")" ;;
  exe)
    publish=$app/Notes/bin/Production/Release/net11.0/win-x64/publish
    timed dotnet run "$(cygpath -w "$repo/docs/evidence/m1d-hot-reload/WatchDesktop.cs")" -- --project "$(cygpath -w "$app/Notes")" --root "$(cygpath -w "$profiles/exe")"       --log "$(cygpath -w "$logs/exe.log")" --exe "$(cygpath -w "$publish/Notes.exe")" --cwd "$(cygpath -w "$publish")" --steps "until:ui-ready;sleep:1000" ;;
  test) cd "$app" && timed dotnet test --project Notes.Tests ;;
  pagetest) cd "$app/Notes/frontend" && timed npm test ;;
  e2ebuild) cd "$app" && timed dotnet build Notes.EndToEndTests && find Notes.EndToEndTests/bin -name playwright.ps1 ;;
  chromium) cd "$app" && timed pwsh Notes.EndToEndTests/bin/Development/Debug/net11.0/win-x64/playwright.ps1 install chromium ;;
  e2e)
    cd "$app"
    for s in Browser Window BlackBox; do timed dotnet test --project Notes.EndToEndTests --filter-trait Surface=$s; done ;;
  aot) cd "$app" && rm -rf aot-tests && timed dotnet publish Notes.Tests -c Release -p:SparTestLane=Aot -o Notes.Tests/bin/aot-tests && timed Notes.Tests/bin/aot-tests/Notes.Tests.exe ;;
  publish) cd "$app" && timed dotnet publish Notes -c Release && find Notes/bin -path '*publish*' -name 'Notes*' -newer "$G/nuget.config" ;;
  uninstall) cd "$G/work" && timed dotnet new uninstall Spar.Templates --debug:custom-hive "$hive" ;;
esac
