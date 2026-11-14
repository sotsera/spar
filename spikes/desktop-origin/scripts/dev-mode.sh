#!/usr/bin/env bash
# Usage (from spikes/desktop-origin): scripts/dev-mode.sh <direct|proxy> [load runs] [hmr rounds]
# Starts Vite for vite-app/ the way the React spike's DevServer.cs does (node node_modules/vite/bin/vite.js), then:
#  - loads the page <load runs> times (each run closes on /_spar/ready);
#  - runs one session that stays open while tools/hmr-driver.mjs edits the sources.
# direct: the WebView navigates to http://127.0.0.1:5199/ (only /_spar/* is intercepted, for the reports).
# proxy-default: as proxy, but with Vite's default HMR settings (the client derives the socket from the page URL).
# proxy:  the WebView stays on https://app.localhost and the host fetches every request from Vite; the HMR
#         WebSocket goes to ws://127.0.0.1:5199 directly (SPAR_HMR_DIRECT=1 in vite.config.ts).
# Logs: artifacts/desktop-origin/dev-<mode>-*.log. Vite is stopped at the end.
set -u
mode=$1; loads=${2:-5}; rounds=${3:-5}
out=../../artifacts/desktop-origin
exe=Origin.Win/bin/dev/Release/net11.0/win-x64/publish/Origin.Win.exe
if [ "$mode" = proxy-default ]; then
  args=(--proxy http://127.0.0.1:5199); unset SPAR_HMR_DIRECT
elif [ "$mode" = proxy ]; then
  args=(--proxy http://127.0.0.1:5199); export SPAR_HMR_DIRECT=1
else
  args=(--url http://127.0.0.1:5199/ --extra-filter 'http://127.0.0.1:5199/_spar/*'); unset SPAR_HMR_DIRECT
fi
port_free() { ! netstat -ano -p TCP | grep -q '127.0.0.1:5199 .*LISTENING'; }
until port_free; do node -e 'setTimeout(()=>{},200)'; done
(cd vite-app && exec node node_modules/vite/bin/vite.js > "../$out/dev-$mode-vite.log" 2>&1) &
vite=$!
until grep -Eq 'ready in|error when starting' "$out/dev-$mode-vite.log" 2>/dev/null; do node -e 'setTimeout(()=>{},100)'; done
grep -q 'error when starting' "$out/dev-$mode-vite.log" && { echo "Vite failed to start"; exit 1; }
# The Windows PID of the node process that listens on 5199 (an MSYS kill of $vite does not always reach it).
vite_pid=$(netstat -ano -p TCP | grep '127.0.0.1:5199 .*LISTENING' | awk '{print $5}' | head -1)
: > "$out/dev-$mode-load.log"
for i in $(seq 1 "$loads"); do
  timeout 60 "$exe" --profile "desktop-origin-dev-$mode" --timeout 30 "${args[@]}" | grep -E '^(TIMING|TIMEOUT|HOST-FAILURE)' | sed "s/^/run=$i /" | tee -a "$out/dev-$mode-load.log"
done
stop="$out/dev-$mode.stop"; rm -f "$stop"
timeout 120 "$exe" --profile "desktop-origin-dev-$mode" --timeout 100 --stay --stop-file "$stop" "${args[@]}" > "$out/dev-$mode-hmr-host.log" 2>&1 &
host=$!
node tools/hmr-driver.mjs "$out/dev-$mode-hmr-host.log" "$stop" "$rounds" | tee "$out/dev-$mode-hmr.json"
wait $host
taskkill //PID "$vite_pid" //T //F > /dev/null 2>&1
kill $vite 2>/dev/null
wait $vite 2>/dev/null
until port_free; do node -e 'setTimeout(()=>{},200)'; done
rm -f "$stop"
