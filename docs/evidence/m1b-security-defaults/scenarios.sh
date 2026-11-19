#!/usr/bin/env bash
# Every scenario of docs/evidence/m1b-security-defaults.md, against the SecurityProbe app (tests/apps/SecurityProbe).
# Runs DefaultsRun.cs from a copy outside the repository (a file-based program inside it would take the repository's
# build settings). Each app run gets LOCALAPPDATA in its own folder under artifacts/webview2-profiles/. No run sends
# input to the desktop: the input checks use the Development-only SPAR_DEV_INPUT trigger (in-process DevTools Protocol).
#
# Builds, from the repository root:
#   dotnet publish tests/apps/SecurityProbe -c Release -p:SecurityProbeOptIns=true -o artifacts/m1b-security-defaults/probe-opt-ins
#   dotnet publish tests/apps/SecurityProbe -c Release
#   dotnet build tests/apps/SecurityProbe -c Debug -p:SparDevServer=false -p:SecurityProbeOptIns=true -o artifacts/m1b-security-defaults/probe-dev-opt-ins
#   dotnet build tests/apps/SecurityProbe -c Debug -p:SparDevServer=false
# and a copy of the installed WebView2 Runtime's version folder in artifacts/m1b-security-defaults/runtime-copy.
set -u
repo=$(cd "$(dirname "$0")/../../.." && pwd)
runner=${RUNNER_DIR:?set RUNNER_DIR to a folder outside the repository}
cp "$repo/docs/evidence/m1b-security-defaults/DefaultsRun.cs" "$runner/"
aot=$repo/tests/apps/SecurityProbe/bin/Production/Release/net11.0/win-x64/publish/SecurityProbe.exe
opt=$repo/artifacts/m1b-security-defaults/probe-opt-ins/SecurityProbe.exe
dev=$repo/tests/apps/SecurityProbe/bin/Development/Debug/net11.0/win-x64/SecurityProbe.exe
devopt=$repo/artifacts/m1b-security-defaults/probe-dev-opt-ins/SecurityProbe.exe
logs=$repo/artifacts/m1b-security-defaults/logs
profiles=$repo/artifacts/webview2-profiles/m1b-security-defaults
# Windows paths for the variables WebView2 reads: Git Bash would pass /e/... as E:/..., which the loader refuses.
profilesw=$(cygpath -w "$profiles")
runtime=$(cygpath -w "$repo/artifacts/m1b-security-defaults/runtime-copy")
port=49517

run() {
  local name=$1 exe=$2 until=$3
  shift 3
  rm -rf "${profiles:?}/${name:?}"
  (cd "$runner" && dotnet run DefaultsRun.cs -- --exe "$exe" --root "$profiles/$name" --log "$logs/$name.log" \
    --until "$until" --timeout 40 "$@")
  echo "$name: $?"
}

case "${1:-all}" in
  all|aot)
    run aot-defaults "$aot" "done defaults" --after 1000 --processes true --env SECURITY_PROBE_MODE=defaults
    run aot-opt-ins-defaults "$opt" "done defaults" --after 2000 --env SECURITY_PROBE_MODE=defaults \
      --seed-preferences "$profiles/aot-opt-ins-defaults/com.example.tests.SecurityProbe/WebView2/EBWebView/Default/Preferences" \
      --downloads "$profiles/aot-opt-ins-defaults/downloads"
    run aot-history "$aot" "done history" --after 500 --env SECURITY_PROBE_MODE=history
    run aot-opt-ins-history "$opt" "done history" --after 500 --env SECURITY_PROBE_MODE=history
    run aot-profile "$aot" "done defaults" --after 500 --env SECURITY_PROBE_MODE=defaults \
      --env "SECURITY_PROBE_PROFILE=$profilesw\\aot-profile\\chosen-profile"
    ;;&
  all|env)
    run aot-env-port "$aot" "done idle" --after 1500 --port $port --env SECURITY_PROBE_MODE=idle \
      --env "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=$port"
    run aot-opt-ins-env-port "$opt" "done idle" --after 1500 --port $port --env SECURITY_PROBE_MODE=idle \
      --env "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=$port"
    run aot-env-all "$aot" "done idle" --after 500 --processes true --env SECURITY_PROBE_MODE=idle \
      --env "WEBVIEW2_USER_DATA_FOLDER=$profilesw\\aot-env-all\\moved-profile" --env "WEBVIEW2_BROWSER_EXECUTABLE_FOLDER=$runtime" \
      --env WEBVIEW2_RELEASE_CHANNEL_PREFERENCE=1 --env WEBVIEW2_CHANNEL_SEARCH_KIND=1 --env WEBVIEW2_RELEASE_CHANNELS=0,1,2,3,4 \
      --env WEBVIEW2_WAIT_FOR_SCRIPT_DEBUGGER=1 --env WEBVIEW2_PIPE_FOR_SCRIPT_DEBUGGER=step5probe
    # The Development build first: a Runtime folder the loader refuses shows no dialog in the automated mode.
    run dev-opt-ins-env-folders "$devopt" "done idle" --after 500 --processes true --env SECURITY_PROBE_MODE=idle --env SPAR_AUTOMATED=1 \
      --env "WEBVIEW2_USER_DATA_FOLDER=$profilesw\\dev-opt-ins-env-folders\\moved-profile" --env "WEBVIEW2_BROWSER_EXECUTABLE_FOLDER=$runtime"
    run aot-opt-ins-env-folders "$opt" "done idle" --after 500 --processes true --env SECURITY_PROBE_MODE=idle \
      --env "WEBVIEW2_USER_DATA_FOLDER=$profilesw\\aot-opt-ins-env-folders\\moved-profile" --env "WEBVIEW2_BROWSER_EXECUTABLE_FOLDER=$runtime"
    ;;&
  all|dev)
    run dev-picker "$dev" "done picker" --after 500 --env SECURITY_PROBE_MODE=picker --env SPAR_AUTOMATED=1 \
      --env "SPAR_DEV_INPUT=wait:1500;click:120,40;wait:500;click:120,40"
    run dev-menu "$dev" "done menu" --after 500 --env SECURITY_PROBE_MODE=menu --env SPAR_AUTOMATED=1 \
      --env "SPAR_DEV_INPUT=wait:1500;right:120,120;wait:800;right:400,300"
    run dev-keys "$dev" "keys: load 1 saw" --after 10000 --env SECURITY_PROBE_MODE=keys --env SPAR_AUTOMATED=1 \
      --env "SPAR_DEV_INPUT=wait:1500;key:ctrl+p;wait:1500;key:alt+ArrowLeft;wait:1000;key:F5;wait:3000;key:ctrl+r"
    run dev-mouse-back "$dev" "done mouse-back" --after 500 --env SECURITY_PROBE_MODE=mouse-back --env SPAR_AUTOMATED=1 \
      --env "SPAR_DEV_INPUT=wait:500;click:300,300;wait:2500;back:300,300"
    run dev-opt-ins-mouse-back "$devopt" "done mouse-back" --after 500 --env SECURITY_PROBE_MODE=mouse-back --env SPAR_AUTOMATED=1 \
      --env "SPAR_DEV_INPUT=wait:500;click:300,300;wait:2500;back:300,300"
    run dev-mouse-back-same "$dev" "done mouse-back-same" --after 500 --env SECURITY_PROBE_MODE=mouse-back-same --env SPAR_AUTOMATED=1 \
      --env "SPAR_DEV_INPUT=wait:1500;back:300,300"
    run dev-profile-relative "$dev" "never" --timeout 8 --after 0 --env SECURITY_PROBE_MODE=idle --env SPAR_AUTOMATED=1 \
      --env "SECURITY_PROBE_PROFILE=relative\\profile"
    run dev-profile-unc "$dev" "never" --timeout 8 --after 0 --env SECURITY_PROBE_MODE=idle --env SPAR_AUTOMATED=1 \
      --env 'SECURITY_PROBE_PROFILE=//localhost/c$/spar-step5-profile'
    ;;&
  all|cost)
    for i in 1 2 3 4 5 6 7; do
      run aot-cost-$i "$aot" "done idle" --after 200 --env SECURITY_PROBE_MODE=idle
      run aot-opt-ins-cost-$i "$opt" "done idle" --after 200 --env SECURITY_PROBE_MODE=idle
    done
    ;;
esac
