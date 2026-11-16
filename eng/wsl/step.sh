#!/usr/bin/env bash
# Runs one step of the CI lane in WSL, in the run setup.sh prepared, with CI's environment (ContinuousIntegrationBuild).
# Usage: step.sh <repo on /mnt> <run> <step>; the output also goes to <repo>/artifacts/linux-runs/<run>/<step>.log.
# Steps, in CI's order: size client appmodel build chromium test pagetests aot mt buildtests packagetests budgets.
set -uo pipefail
source_repo=$1
run=$2
step=$3
base=/var/tmp/spar-linux
cache=$base/cache
repo=$base/$run/repo
homes=$base/$run/repo/artifacts/linux-homes/$run
logs=$source_repo/artifacts/linux-runs/$run
mkdir -p "$logs"

export HOME=$homes/home XDG_CACHE_HOME=$homes/cache XDG_CONFIG_HOME=$homes/config XDG_DATA_HOME=$homes/data
export XDG_STATE_HOME=$homes/state XDG_RUNTIME_DIR=$homes/runtime
export DOTNET_ROOT=$cache/dotnet PATH=$cache/dotnet:$PATH
export NUGET_PACKAGES=$cache/nuget-packages NUGET_HTTP_CACHE_PATH=$cache/nuget-http npm_config_cache=$cache/npm
export PLAYWRIGHT_BROWSERS_PATH=$cache/ms-playwright
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 ContinuousIntegrationBuild=true
export LD_LIBRARY_PATH=$cache/libs/usr/lib/x86_64-linux-gnu${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}
unset JSC_SIGNAL_FOR_GC
cd "$repo" || { echo "STEP no clone at $repo"; exit 1; }

timed() {
  local start=$(date +%s%N)
  "$@"
  local exit=$?
  echo "STEP exit $exit in $(( ($(date +%s%N) - start) / 1000000 )) ms: $*"
  return $exit
}

aot_lane() {
  for project in tests/Spar.Tests tests/WireShapes.Tests samples/Notebook.Tests; do
    name=$(basename "$project")
    timed dotnet publish "$project" -c Release -p:SparTestLane=Aot -o "artifacts/aot/$name" || return 1
    timed "artifacts/aot/$name/$name" | tail -3
  done
}

{
  echo "STEP $step on $(uname -r), $(nproc) CPUs, dotnet $(dotnet --version), node $(node --version)"
  case $step in
    size) timed dotnet run eng/check-size.cs ;;
    client) cd src/Spar.Client && timed npm ci --ignore-scripts && timed npm run check && timed npm test && timed npm run build ;;
    appmodel) cd tests/AppModel && timed npm ci --ignore-scripts ;;
    build) timed dotnet build Spar.slnx -c Debug ;;
    chromium)
      driver=$(find tests/WireShapes.EndToEndTests/bin -path '*/.playwright/node/linux-x64/node' | head -1)
      cli=$(dirname "$(dirname "$(dirname "$driver")")")/package/cli.js
      timed "$driver" "$cli" install --only-shell chromium ;;
    test) timed dotnet test --solution Spar.slnx -c Debug --no-build ;;
    pagetests) cd samples/Notebook/frontend && timed npm test ;;
    aot) aot_lane ;;
    mt) timed dotnet build Spar.slnx -c Debug -mt -nodeReuse:false ;;
    buildtests) timed dotnet test --project tests/Spar.BuildTests ;;
    packagetests) timed dotnet test --project tests/Spar.PackageTests ;;
    budgets)
      timed dotnet test --project tests/WireShapes.Tests -c Release -- --filter-class "WireShapes.Tests.BudgetTests"
      timed dotnet test --project tests/WireShapes.EndToEndTests -c Release -- --filter-class "WireShapes.EndToEndTests.BudgetTests"
      cat artifacts/test-results/WireShapes.Tests/budgets.txt artifacts/test-results/WireShapes.EndToEndTests/budgets.txt ;;
    *) echo "unknown step $step"; exit 2 ;;
  esac
} 2>&1 | tee "$logs/$step.log"
exit "${PIPESTATUS[0]}"
