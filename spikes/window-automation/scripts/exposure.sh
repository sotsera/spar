#!/usr/bin/env bash
# Option A's port exposure: while a window-test process holds its page open, another local process (curl, then node)
# reaches the port without credentials; after the process exits, what is left (port file, listener).
# Git Bash, repository root. Output: artifacts/window-automation/exposure.log
set -u
exe=spikes/window-automation/OptionA/bin/Release/net11.0/win-x64/OptionA.exe
out=artifacts/window-automation
log="$out/a-exposure-run.log"
mkdir -p "$out"
"$exe" journey --runs 1 --hold-ms 8000 --keep-root > "$log" 2>&1 &
pid=$!
until grep -q "^HOLD port=" "$log" 2>/dev/null; do sleep 0.2; done
port=$(grep "^HOLD port=" "$log" | sed 's/^HOLD port=\([0-9]*\).*/\1/')
{
  echo "port=$port (from the test process's log)"
  echo "--- curl /json/list from another process"
  curl -s "http://127.0.0.1:$port/json/list" | tr -d '\n' | cut -c1-400; echo
  echo "--- node exposure.mjs from another process"
  node spikes/window-automation/scripts/exposure.mjs "$port"
} > "$out/exposure.log" 2>&1
wait "$pid"
root=$(ls -dt artifacts/webview2-profiles/window-automation/a-* | head -1)
{
  echo "--- after the test process exited"
  echo "port file left: $(test -f "$root/EBWebView/DevToolsActivePort" && echo yes || echo no) ($(head -1 "$root/EBWebView/DevToolsActivePort" 2>/dev/null))"
  echo "curl to the old port: $(curl -s -m 3 -o /dev/null -w '%{http_code}' "http://127.0.0.1:$port/json/version"; echo " exit=$?")"
  echo "listening on the old port: $(netstat -ano | grep -c "127.0.0.1:$port .*LISTENING")"
  rm -rf "$root"
  echo "root removed: $(test -d "$root" && echo no || echo yes)"
} >> "$out/exposure.log" 2>&1
cat "$out/exposure.log"
