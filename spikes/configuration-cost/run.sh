#!/usr/bin/env bash
# The configuration-cost runs (RESULTS.md), against the five published variants of ConfigCost. Runs ConfigRun.cs from a
# copy outside the repository: windows shown without activation and moved offscreen, no input sent, closed with
# WM_CLOSE. Each app run gets LOCALAPPDATA in its own folder under artifacts/webview2-profiles/configuration-cost/; its
# app.log is copied beside its run log under artifacts/configuration-cost/logs/.
#   RUNNER_DIR=<folder outside the repository> [VARIANTS="0 1"] run.sh levels|reload|startup <set>
# Variants: 0 baseline, 1 JSON file for logging, 2 = 1 + environment variables, IConfiguration, bound options,
# 3 = 2 with reloadOnChange, 4 = 1 with the file handed over by AddJsonStream.
set -u
repo=$(cd "$(dirname "$0")/../.." && pwd)
runner=${RUNNER_DIR:?set RUNNER_DIR to a folder outside the repository}
cp "$repo/spikes/configuration-cost/ConfigRun.cs" "$runner/"
exe() { echo "$repo/spikes/configuration-cost/ConfigCost/bin/v$1/Production/Release/net11.0/win-x64/publish/ConfigCost.exe"; }
logs=$repo/artifacts/configuration-cost/logs
profiles=$repo/artifacts/webview2-profiles/configuration-cost
app=com.example.spikes.ConfigCost

# The app's file, in its data root: the standard logging shape and the options section.
settings() {
  printf '{"Logging":{"LogLevel":{"Default":"Warning","ConfigCost.ProbeService":"Debug","ConfigCost.Ticker":"%s"}},"Probe":{"Greeting":"from-file","Count":3,"Tags":["a","b"]}}' "$1"
}

run() {
  local name=$1 variant=$2 until=$3 timeout=$4 after=$5
  shift 5
  rm -rf "${profiles:?}/$name"
  mkdir -p "$profiles/$name/$app"
  settings Information > "$profiles/$name/$app/appsettings.json"
  [ -n "${SPAR_LOGGING_JSON:-}" ] && printf '%s' "$SPAR_LOGGING_JSON" > "$profiles/$name/$app/logging.json"
  (cd "$runner" && dotnet run ConfigRun.cs -- --exe "$(exe "$variant")" --root "$profiles/$name" --log "$logs/$name.log" \
    --until "$until" --timeout "$timeout" --after "$after" "$@")
  echo "$name: $?"
  find "$profiles/$name" -name 'app*.log' -path '*logs*' -exec cp {} "$logs/$name.app.log" \;
}

case "${1:-levels}" in
  levels)
    # Which lines reach app.log under {"Default":"Warning","ConfigCost.ProbeService":"Debug"}; the options' values.
    for v in ${VARIANTS:-0 1 2 3 4}; do
      run "levels-v$v" "$v" "probe: done" 30 1000 --env CONFIGCOST_Probe__Greeting=from-env
    done
    ;;
  spar-file)
    # Spar's own logging.json beside the app's file: its default level, then a category rule for Spar's lines.
    SPAR_LOGGING_JSON='{"level":"Debug"}' run spar-file-level 1 "probe: done" 30 1000
    SPAR_LOGGING_JSON='{"categories":{"Spar":"Information"}}' run spar-file-category 1 "probe: done" 30 1000
    ;;
  reload)
    # A timer logs Debug under ConfigCost.Ticker every 250 ms; 1.5 s after ui-ready the file raises it to Debug.
    for v in 1 2 3; do
      run "reload-v$v" "$v" "probe: done" 30 6000 --env CONFIG_COST_TICK=1 \
        --write "$profiles/reload-v$v/$app/appsettings.json" --content "$(settings Debug)" --write-after 1500
    done
    ;;
  startup)
    # Interleaved rounds: v0 v1 v2 v3, eight times; the first round is left out of the medians.
    set_name=${2:-1}
    for round in 1 2 3 4 5 6 7 8; do
      for v in 0 1 2 3; do
        run "startup/s$set_name-v$v-$round" "$v" "ui-ready" 30 1000
      done
    done
    ;;
esac
