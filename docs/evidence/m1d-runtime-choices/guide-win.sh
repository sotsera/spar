#!/usr/bin/env bash
# M1d step 7 (docs/evidence/m1d-runtime-choices.md): the publishing-choices guide's recipes on Windows, from packages
# only, in %TEMP%\spar-guide-m1d7, outside the repository. Spar's nine packages are packed from this tree at a version of
# their own into a feed there, whose nuget.config sits in the folder above the new apps. NuGet's and npm's caches are
# the package tests' (%TEMP%\spar-package-tests), the template hive and the .NET CLI's home are the run's, and
# LOCALAPPDATA (the apps' folders and WebView2 profiles) is under artifacts/webview2-profiles/m1d7-guide/. The project
# snippets come from docs/guide/publishing-choices.md itself, by their markers. The window runs use
# ../m1d-hot-reload/WatchDesktop.cs, so no window reaches the monitor. From Git Bash, in this order:
#   guide-win.sh pack|new|aot|globalization|fd|sf|sql|members
#   guide-win.sh start <aot|fd|sf> [runs]   the publication started to ui-ready, [runs] times (default 5)
# `sql` makes a second app, Ledger, with the guide's SqlClient blocks and the package tests' probe (sql-probe.sh).
set -uo pipefail
repo=$(cd "$(dirname "$0")/../../.." && pwd)
here=$(cd "$(dirname "$0")" && pwd)
logs=$repo/artifacts/m1d-runtime-choices/windows
G=$(cygpath -u "$TEMP")/spar-guide-m1d7
shared=$(cygpath -u "$TEMP")/spar-package-tests
V=$(cat "$G/version" 2>/dev/null || true)
mkdir -p "$logs"
profiles=$repo/artifacts/webview2-profiles/m1d7-guide
export NUGET_PACKAGES=$(cygpath -w "$shared/nuget-packages") NUGET_HTTP_CACHE_PATH=$(cygpath -w "$shared/http-cache") npm_config_cache=$(cygpath -w "$shared/npm-cache")
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=false
export DOTNET_CLI_HOME=$(cygpath -w "$G/dotnet-cli-home") LOCALAPPDATA=$(cygpath -w "$profiles/local")
hive=$(cygpath -w "$G/hive")
guide=$repo/docs/guide/publishing-choices.md
timed() { local s=$(date +%s%N); "$@"; local e=$?; echo "STEP exit $e in $(( ($(date +%s%N)-s)/1000000 )) ms: $*"; return $e; }
# The guide's snippet <name>: the fenced block after its marker <!-- project-snippet: <name> -->.
snippet() { awk -v m="<!-- project-snippet: $1 -->" '$0==m{f=1;next} f&&/^```xml/{p=1;next} p&&/^```/{exit} p' "$guide"; }
# A copy of the new app's project with the named snippets added before </Project>.
with() {
  local project=$1; shift
  cp "$project.orig" "$project"
  local body; body=$(for s in "$@"; do snippet "$s"; echo; done)
  awk -v add="$body" '/^<\/Project>/{print add} {print}' "$project.orig" > "$project"
}
sizes() { find "$1" -type f -printf '%s %P\n' | sort -k2; echo "files: $(find "$1" -type f | wc -l), bytes: $(find "$1" -type f -printf '%s\n' | awk '{s+=$1} END {print s}')"; }
app=$G/work/Notes
proj=$app/Notes/Notes.csproj
case $1 in
  pack)
    V=0.1.0-dev.guide$(date -u +%Y%m%d%H%M%S)
    rm -rf "$G"; mkdir -p "$G/feed" "$G/work" "$G/dotnet-cli-home" "$profiles/local"; echo "$V" > "$G/version"
    for p in Spar Spar.Windows Spar.Linux Spar.Loopback Spar.Testing Spar.Testing.Browser Spar.Sdk Spar.Templates Spar.Tool; do
      (cd "$repo" && timed dotnet pack "src/$p/$p.csproj" -c Release -p:VersionSuffix=${V#0.1.0-} -o "$(cygpath -w "$G/feed")" -nologo -nr:false -v:q) || exit 1
    done
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
    timed dotnet new spar-app -n Notes --no-end-to-end-tests --debug:custom-hive "$hive"
    cp "$proj" "$proj.orig"
    cd Notes && timed dotnet build Notes -nologo -v:m | tail -3 && timed dotnet tool restore ;;
  aot|globalization|fd|sf)
    # The default (NativeAOT), NativeAOT with culture data, framework-dependent CoreCLR, and self-contained single-file CoreCLR.
    case $1 in aot) with "$proj" ;; globalization) with "$proj" globalization ;; fd) with "$proj" coreclr-framework-dependent ;; sf) with "$proj" coreclr-single-file ;; esac
    out=$G/publish-$1; rm -rf "$out"
    cd "$app" && timed dotnet publish Notes -c Release -o "$(cygpath -w "$out")" -nologo | tail -3
    sizes "$out" | tee "$logs/sizes-$1.txt"
    cd "$app/Notes" && timed dotnet spar doctor --app "$(cygpath -w "$out/Notes.exe")" | sed -n '/^Build identity/,$p'
    cp "$proj.orig" "$proj" ;;
  start)
    # No folder on PATH with a WebView2 loader (the Windows SDK's Performance Toolkit has one): it would hide a
    # publication without its own. Extraction (the single file) goes under the run's profile folder.
    out=$G/publish-$2
    PATH=$(echo "$PATH" | tr ':' '\n' | while read -r d; do [ -f "$d/WebView2Loader.dll" ] || echo "$d"; done | paste -sd:)
    for i in $(seq 1 "${3:-5}"); do
      rm -rf "$profiles/start-$2"; mkdir -p "$profiles/start-$2/extract"
      timed dotnet run "$(cygpath -w "$repo/docs/evidence/m1d-hot-reload/WatchDesktop.cs")" -- --project "$(cygpath -w "$app/Notes")" --root "$(cygpath -w "$profiles/start-$2")" \
        --log "$(cygpath -w "$logs/start-$2-$i.log")" --exe "$(cygpath -w "$out/Notes.exe")" --cwd "$(cygpath -w "$out")" \
        --env "DOTNET_BUNDLE_EXTRACT_BASE_DIR=$(cygpath -w "$profiles/start-$2/extract")" --steps "until:ui-ready;sleep:500" >/dev/null
      grep -h "app: .*ui-ready at" "$logs/start-$2-$i.log" | head -1
      find "$profiles/start-$2/extract" -type f -printf 'extracted: %s %P\n'
    done ;;
  sql)
    # The worked example: SqlClient with the guide's blocks, published with NativeAOT; first without accepting its
    # warnings (fails), then with them; the publish folder, and the probe against a closed port and in invariant mode.
    cd "$G/work" && rm -rf Ledger && timed dotnet new spar-app -n Ledger --no-end-to-end-tests --debug:custom-hive "$hive" >/dev/null
    lp=$G/work/Ledger/Ledger/Ledger.csproj; cp "$lp" "$lp.orig"; bash "$here/sql-probe.sh" "$G/work/Ledger/Ledger" Ledger || exit 1
    # Never start the app without its probe: it would open its window on this desktop.
    grep -q -- "--sql-probe" "$G/work/Ledger/Ledger/Program.cs" || { echo "no probe in Program.cs"; exit 1; }
    with "$lp" sqlclient-package globalization sqlclient-single-file
    cd "$G/work/Ledger" && rm -rf "$G/publish-sql"
    timed dotnet publish Ledger -c Release -o "$(cygpath -w "$G/publish-sql")" -nologo > "$logs/sql-strict.log" 2>&1
    grep -oE "(error|warning) IL[0-9]+: Assembly '[^']+'" "$logs/sql-strict.log" | sort | uniq -c
    with "$lp" sqlclient-package accept-aot-warnings globalization sqlclient-single-file
    rm -rf "$G/publish-sql"
    timed dotnet publish Ledger -c Release -o "$(cygpath -w "$G/publish-sql")" -nologo > "$logs/sql-recipe.log" 2>&1 || { echo "the recipe publish failed"; exit 1; }
    grep -oE "(error|warning) IL[0-9]+: Assembly '[^']+'" "$logs/sql-recipe.log" | sort | uniq -c
    sizes "$G/publish-sql" | tee "$logs/sizes-sql.txt"
    # At most 30 s each: the probe ends by itself in about 3 s, and a window would be ended.
    cd "$G/publish-sql" && timed timeout 30 ./Ledger.exe --sql-probe 1 | cat
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 timed timeout 30 ./Ledger.exe --sql-probe 1 | cat ;;
  members)
    # The per-member view of SqlClient's warnings (after sql): this publish fails, listing each one.
    cd "$G/work/Ledger" && timed dotnet publish Ledger -c Release -p:TrimmerSingleWarn=false -o "$(cygpath -w "$G/publish-members")" -nologo > "$logs/sql-members.log" 2>&1
    grep -E "(error|warning) IL[0-9]+" "$logs/sql-members.log" | sed 's/ \[.*//' | sort -u | grep -oE "(error|warning) IL[0-9]+" | sort | uniq -c
    echo "distinct: $(grep -E "(error|warning) IL[0-9]+" "$logs/sql-members.log" | sed 's/ \[.*//' | sort -u | wc -l)" ;;
esac
