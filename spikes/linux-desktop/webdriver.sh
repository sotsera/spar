#!/usr/bin/env bash
# Usage (in the container, from this folder): webdriver.sh <gtk3|gtk4>
# Drives the published app through WebKitWebDriver: the driver launches the app with --automation, the app hands its
# view to the automation session, and the journey uses WebDriver element commands (fill, click, read).
set -u
variant=$1
dir=$PWD/Linux.App/bin/Release/net11.0/linux-x64/publish
[ "$variant" = gtk4 ] && dir=$PWD/Linux.App/bin/gtk4/Release/net11.0/linux-x64/publish
data=$(realpath ../../artifacts)/webview2-profiles/linux-webkit-$variant-webdriver
log=../../artifacts/linux-desktop/webdriver-$variant.log
WebKitWebDriver --port=4444 >"$log.driver" 2>&1 &
driver=$!
sleep 0.5
wd() { curl -s -X "$1" "http://127.0.0.1:4444$2" -H 'Content-Type: application/json' ${3:+--data "$3"}; }
now() { date +%s%N; }
t0=$(now)
session=$(wd POST /session "{\"capabilities\":{\"alwaysMatch\":{\"webkitgtk:browserOptions\":{\"binary\":\"$dir/Linux.App\",\"args\":[\"--automation\",\"--data-dir\",\"$data\"]}}}}")
echo "new session ($(( ($(now)-t0)/1000000 ))ms): $session" | cut -c1-400
id=$(sed -E 's/.*"sessionId":"([^"]+)".*/\1/' <<<"$session")
echo "url: $(wd GET /session/$id/url)"
t0=$(now); echo "navigate: $(wd POST /session/$id/url '{"url":"spar://app/"}') ($(( ($(now)-t0)/1000000 ))ms)"
# Record whether the click the page receives is trusted.
wd POST /session/$id/execute/sync '{"script":"document.querySelector(\"button[type=submit]\").addEventListener(\"click\", e => document.body.dataset.trusted = e.isTrusted, {once:true}); return true;","args":[]}' >/dev/null
el() { wd POST /session/$id/element "{\"using\":\"css selector\",\"value\":\"$1\"}" | sed -E 's/.*:"([^"]+)"\}\}$/\1/'; }
t0=$(now)
input=$(el 'form label input'); button=$(el 'button[type=submit]')
echo "fill: $(wd POST /session/$id/element/$input/value '{"text":"Buy bread"}')"
echo "click: $(wd POST /session/$id/element/$button/click '{}')"
for _ in $(seq 1 100); do
  text=$(wd POST /session/$id/execute/sync '{"script":"return document.querySelector(\"ul[aria-label=Notes] li\")?.textContent ?? \"\"","args":[]}')
  grep -q 'Buy bread' <<<"$text" && break; sleep 0.01
done
echo "journey $(( ($(now)-t0)/1000000 ))ms: $text"
echo "click isTrusted: $(wd POST /session/$id/execute/sync '{"script":"return document.body.dataset.trusted ?? \"no click seen\"","args":[]}')"
t0=$(now); shot=$(wd GET /session/$id/screenshot); echo "screenshot $(( ($(now)-t0)/1000000 ))ms, $(wc -c <<<"$shot") base64 chars"
echo "delete: $(wd DELETE /session/$id)"
kill $driver; wait $driver 2>/dev/null
echo "webkit processes left: $(pgrep -f 'WebKit(Web|Network)Process' | wc -l), app left: $(pgrep -f Linux.App | wc -l)"
