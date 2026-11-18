#!/usr/bin/env bash
# The app case with a real Spar app: the SecurityProbe test app (tests/apps/SecurityProbe, Production NativeAOT, as
# published by the M1b runs; this spike does not rebuild it) started <count> times in a row on the same LOCALAPPDATA,
# each closed with WM_CLOSE as soon as its page reports ui-ready, and the next one started <pause> ms after the previous
# process exited. Driven by docs/evidence/m1b-page-security/SecurityRun.cs from a copy outside the repository: windows
# shown without activation and moved offscreen, no input. Raw log: results/win/<name>.log (redacted).
#   RUNNER_DIR=<folder outside the repository> scripts/spar-relaunch.sh <name> <count> <pause-ms>
set -u
spike=$(cd "$(dirname "$0")/.." && pwd)
repo=$(cd "$spike/../.." && pwd)
runner=${RUNNER_DIR:?set RUNNER_DIR to a folder outside the repository}
name=$1 count=$2 pause=$3
exe=$repo/tests/apps/SecurityProbe/bin/Production/Release/net11.0/win-x64/publish/SecurityProbe.exe
root=$repo/artifacts/webview2-profiles/webview-restart-wait/$name
logs=$repo/artifacts/webview-restart-wait/logs
mkdir -p "$runner" "$logs" "$spike/results/win"
cp "$repo/docs/evidence/m1b-page-security/SecurityRun.cs" "$runner/"
rm -rf "$root" && mkdir -p "$root"
: > "$logs/$name.log"
for i in $(seq 1 "$count"); do
  (cd "$runner" && dotnet run SecurityRun.cs -- --exe "$exe" --root "$root" --log "$logs/$name-$i.log" \
    --until "ui-ready at" --timeout 40 --after 0 --env SECURITY_PROBE_MODE=idle --env SPAR_AUTOMATED=1) > /dev/null 2>&1
  { echo "== start $i"; cat "$logs/$name-$i.log"; } >> "$logs/$name.log"
  rm -f "$logs/$name-$i.log"
  sleep "$(awk "BEGIN{print $pause/1000}")"
done
winrepo=$(cygpath -w "$repo")
sed -e "s#$(printf '%s' "$winrepo" | sed 's#[\\.]#.#g')#<repo>#gI" -e "s#$repo#<repo>#gI" -e 's#C:\\Users\\[^\\ "]*#<userprofile>#gI' \
  "$logs/$name.log" > "$spike/results/win/$name.log"
grep -E "== start|controller at|ui-ready at|exited" "$spike/results/win/$name.log" | sed "s/^/$name: /"
for _ in $(seq 1 100); do
  ls "$root"/*/WebView2/EBWebView/lockfile >/dev/null 2>&1 || { rm -rf "$root"; break; }
  sleep 0.2
done
