#!/usr/bin/env bash
# Usage: tools/host-hmr.sh <windows|gtk3|gtk4>
# Fast Refresh inside a desktop host: serves the frontend with Vite (dev, Spar plugin), opens it in the host with
# ?stay=1, then edits the title and the CSS and restores both. The page re-posts its results on every refresh
# (artifacts/csp-navigation/vite/hmr-<host>.jsonl); compare their times with the edit times printed here.
set -u
export MSYS_NO_PATHCONV=1 VITE_CONFIG_NATIVE_IGNORE_WARNING=true SPAR_ALLOW_DOCKER=1
host=$1
cd "$(dirname "$0")/.."
node tools/vite-serve.mjs hmr-$host >"../../artifacts/csp-navigation/vite/serve-hmr-$host.out" 2>&1 < <(sleep 120) &
pid=$!
for i in $(seq 1 40); do curl -s -o /dev/null http://127.0.0.1:5273/ && break; sleep 0.5; done
if [ "$host" = windows ]; then
  tools/run-desktop.sh hmr-windows base "/?stay=1" --dev-url http://127.0.0.1:5273/ --timeout 14 &
else
  linux/in.sh "linux/run.sh $host hmr-$host base '/?stay=1' --dev-url http://host.docker.internal:5273/ --timeout 14" &
fi
run=$!
app=frontend/src/App.tsx; css=frontend/src/App.css
mkdir -p obj; bak=obj/hmr-backup.tsx; cp $app $bak
cp $css "$bak.css"
sleep 5
echo "EDIT_TITLE at=$(date +%s%3N)"; sed -i 's/Spar CSP spike v1/Spar CSP spike v2/' $app
sleep 3
echo "EDIT_CSS at=$(date +%s%3N)"; sed -i 's/rgb(20, 30, 40)/rgb(50, 60, 70)/' $css
sleep 3
cp "$bak" $app; cp "$bak.css" $css
wait $run
kill $pid; wait $pid 2>/dev/null
cat "../../artifacts/csp-navigation/vite/hmr-$host.jsonl" <(grep -h "PAGE {" ../../artifacts/csp-navigation/windows/hmr-$host.log 2>/dev/null | sed "s/^.*PAGE //") 2>/dev/null | grep -o "\"now\":[0-9]*,\"title\":\"[^\"]*\",\"cssColor\":\"[^\"]*\",\"bootId\":\"[^\"]*\""
