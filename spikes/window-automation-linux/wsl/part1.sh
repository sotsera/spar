#!/usr/bin/env bash
# Usage: bash wsl/part1.sh (inside WSL, after sync.sh and publish.sh). Part 1: every route by which Microsoft's library
# (Playwright, attached or launching) or a WebDriver client could drive Spar's Linux window. Xvfb :95; ports on
# 127.0.0.1 only. Output: $base/logs/part1.log (part1-<sections>.log when sections are given, e.g. "4").
set -u
sections=${1:-1234}
here=$(cd "$(dirname "$0")" && pwd)
source "$here/common.sh"
isolate part1
log=$base/logs/part1${1:+-$1}.log
mkdir -p "$base/logs" "$base/out"
exec > >(tee "$log") 2>&1
xvfb_start 95
harness=$base/aot/Harness
page=$work/page/dist

echo "== build the part-1 probe (JIT, Microsoft.Playwright)"
dotnet build "$spike/probes/Part1" -c Release -o "$base/part1" -nologo -v q 2>&1 | grep -E "error|Warn|Elapsed" | head -5
part1() { dotnet "$base/part1/Part1.dll" "$@"; }
export PLAYWRIGHT_BROWSERS_PATH=$base/ms-playwright
node_driver=$base/part1/.playwright/node/linux-x64/node
cli=$base/part1/.playwright/package/cli.js

serve() { # serve <label> <seconds> [harness args...]: the harness holding one page, in the background
  local label=$1 seconds=$2; shift 2
  dbus-run-session -- "$harness" serve --seconds "$seconds" --page "$page" --out "$base/out" "$@" >"$base/logs/part1-$label.log" 2>&1 &
  serve_pid=$!
  for _ in $(seq 1 100); do grep -q "SERVE ready" "$base/logs/part1-$label.log" 2>/dev/null && break; sleep 0.1; done
  echo "harness ($label): $(grep -m1 -E 'SERVE|FATAL' "$base/logs/part1-$label.log" || echo 'no SERVE line after 10 s (hung?)')"
}
stop_serve() { pkill -TERM -P "$serve_pid" 2>/dev/null; kill "$serve_pid" 2>/dev/null; wait "$serve_pid" 2>/dev/null; }
listening() { ss -ltn 2>/dev/null | grep -E "127.0.0.1:$1 " | head -1; }

if [[ $sections == *1* ]]; then
echo
echo "== 1. WEBKIT_INSPECTOR_SERVER (WebKit's remote inspector socket)"
for devtools in "" --devtools; do
  WEBKIT_INSPECTOR_SERVER=127.0.0.1:9227 serve "inspector$devtools" 40 $devtools
  echo "listening: $(listening 9227)"
  echo "HTTP GET /json/version: $(curl -s -m 3 -o /dev/null -w 'status=%{http_code} bytes=%{size_download}' http://127.0.0.1:9227/json/version 2>&1; echo " curl exit=$?")"
  echo "first bytes the server sends after a request:"
  timeout 3 bash -c 'exec 3<>/dev/tcp/127.0.0.1/9227; printf "GET /json/version HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n" >&3; timeout 2 head -c 64 <&3 | od -A d -c | head -4' || echo "  (nothing within 2 s)"
  part1 connect http://127.0.0.1:9227 ws://127.0.0.1:9227/ 2>&1 | grep PART1
  stop_serve
done

fi

if [[ $sections == *2* ]]; then
echo
echo "== 2. WEBKIT_INSPECTOR_HTTP_SERVER (WebKit's inspector over HTTP and WebSocket)"
for devtools in "" --devtools; do
  WEBKIT_INSPECTOR_HTTP_SERVER=127.0.0.1:9228 serve "http-inspector$devtools" 40 $devtools
  echo "listening: $(listening 9228)"
  for path in / /json /json/list /json/version /targets; do
    echo "GET $path: $(curl -s -m 3 -w ' [status=%{http_code} type=%{content_type}]' "http://127.0.0.1:9228$path" | tr -d '\n' | cut -c1-400)"
  done
  part1 connect http://127.0.0.1:9228 ws://127.0.0.1:9228/ 2>&1 | grep PART1
  node "$spike/probes/inspector-ws.mjs" http://127.0.0.1:9228 2>&1 | cut -c1-600
  stop_serve
done

fi

if [[ $sections == *3* ]]; then
echo
echo "== 3. The library's own WebKit browser type"
"$node_driver" "$cli" --version
du -sh "$PLAYWRIGHT_BROWSERS_PATH" 2>/dev/null
"$node_driver" "$cli" install webkit 2>&1 | tail -3
du -sh "$PLAYWRIGHT_BROWSERS_PATH"/* 2>/dev/null
for exe in $(find "$PLAYWRIGHT_BROWSERS_PATH" -maxdepth 4 -name 'pw_run.sh' -o -maxdepth 4 -name 'MiniBrowser' -type f | head -4); do
  echo "$exe: missing libraries: $(find "$(dirname "$exe")" -name '*.so*' -o -name 'MiniBrowser' -type f | head -50 | xargs -r ldd 2>/dev/null | grep 'not found' | sort -u | tr -s ' \t' ' ' | tr '\n' ';')"
done
python3 -I -m http.server 8765 --bind 127.0.0.1 --directory "$page" >/dev/null 2>&1 &
http_pid=$!
sleep 1
part1 launch http://127.0.0.1:8765/ 2>&1 | grep PART1
echo "-- the same launch with the system WebKitGTK 6.0 MiniBrowser as the executable"
part1 launch http://127.0.0.1:8765/ /usr/lib/x86_64-linux-gnu/webkitgtk-6.0/MiniBrowser 2>&1 | grep PART1
echo "-- the same launch with Spar's harness as the executable"
part1 launch http://127.0.0.1:8765/ "$harness" 2>&1 | grep PART1
kill "$http_pid" 2>/dev/null

fi

if [[ $sections == *4* ]]; then
echo
echo "== 4. WebDriver: WebKitWebDriver (Ubuntu ships it only in webkit2gtk-driver, for 4.1) against the 6.0 harness"
dpkg -s webkit2gtk-driver >/dev/null 2>&1 && echo "webkit2gtk-driver is installed system-wide" || echo "webkit2gtk-driver: not installed; using the unpacked package"
apt-cache search --names-only 'webkit.*driver' | sed 's/^/package: /'
echo "WebKitWebDriver links: $(ldd "$tools/root/usr/bin/WebKitWebDriver" | grep -E 'webkit|soup|javascriptcore' | awk '{print $1}' | tr '\n' ' ')"
WebKitWebDriver --version
WebKitWebDriver --help 2>&1 | sed 's/^/  /'
WebKitWebDriver --port=4445 >"$base/logs/part1-webdriver-driver.log" 2>&1 &
driver_pid=$!
sleep 0.5
node "$spike/probes/webdriver.mjs" http://127.0.0.1:4445 "$harness" "$page" 2>&1 | cut -c1-700
kill "$driver_pid" 2>/dev/null; wait "$driver_pid" 2>/dev/null
sleep 1
echo "driver log tail:"; tail -5 "$base/logs/part1-webdriver-driver.log" | cut -c1-300
fi

xvfb_stop
cleanup_runtime
sleep 0.5
echo "left: harness=$(pgrep -x Harness | wc -l) WebKitWebDriver=$(pgrep -x WebKitWebDriver | wc -l) MiniBrowser=$(pgrep -f -c 'webkitgtk-6.0/MiniBrowser|pw_run|playwright.*webkit' || true) http.server=$(pgrep -x -c python3 || true) (python3 processes)"
