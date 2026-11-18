#!/usr/bin/env bash
# Builds and runs the Windows probe (Win/) of spikes/webview-restart-wait from Git Bash.
#   scripts/win.sh build
#   scripts/win.sh run <name> [probe options...]     one probe process; raw log in results/win/<name>.log
#   scripts/win.sh relaunch <name> <count> <pause-ms> [probe options...]
#       <count> probe processes one after another on the same root, each one cycle (--reps 0) and --exit now, with
#       <pause-ms> between one process's exit and the next one's start (a relaunched app)
# Profiles go under artifacts/webview2-profiles/webview-restart-wait/<name>/ and are deleted after the run once no
# lock file is left; the raw logs are redacted (<repo>) as they are copied into results/win/.
set -eu
spike=$(cd "$(dirname "$0")/.." && pwd)
repo=$(cd "$spike/../.." && pwd)
bin=$repo/artifacts/webview-restart-wait/bin
logs=$repo/artifacts/webview-restart-wait/logs
results=$spike/results/win
mkdir -p "$logs" "$results"
winrepo=$(cygpath -w "$repo")

redact() {
  # The repository's path in both spellings (backslashes and dots matched by '.'), and the user's profile path if a
  # browser log carries one.
  sed -e "s#$(printf '%s' "$winrepo" | sed 's#[\\.]#.#g')#<repo>#gI" -e "s#$repo#<repo>#gI" \
      -e 's#C:\\Users\\[^\\ "]*#<userprofile>#gI' -e 's#[A-Z]*~1#<user>#gI' "$1" > "$2"
}

cleanup_root() {
  local root=$1
  for _ in $(seq 1 100); do
    if ! ls "$root"/*/EBWebView/lockfile >/dev/null 2>&1; then
      rm -rf "$root" && return 0
    fi
    sleep 0.2
  done
  echo "root still locked: $root" >&2
}

case "$1" in
  build)
    (cd "$spike/Win" && dotnet publish -c Release -o "$bin") > "$logs/build.log" 2>&1
    tail -3 "$logs/build.log"
    ;;
  run)
    name=$2
    shift 2
    root=$repo/artifacts/webview2-profiles/webview-restart-wait/$name
    rm -rf "$root" && mkdir -p "$root"
    "$bin/RestartWait.exe" --root "$(cygpath -w "$root")" --out "$(cygpath -w "$logs/$name.log")" "$@" || echo "exit $?"
    redact "$logs/$name.log" "$results/$name.log"
    grep -E "summary|FAILED|left:" "$results/$name.log" | sed "s/^/$name: /"
    cleanup_root "$root"
    ;;
  relaunch)
    name=$2 count=$3 pause=$4
    shift 4
    root=$repo/artifacts/webview2-profiles/webview-restart-wait/$name
    rm -rf "$root" && mkdir -p "$root"
    : > "$logs/$name.log"
    for i in $(seq 1 "$count"); do
      start=$(date +%s%3N)
      "$bin/RestartWait.exe" --root "$(cygpath -w "$root")" --out "$(cygpath -w "$logs/$name-$i.log")" --reps 0 --exit now "$@" || echo "exit $?"
      end=$(date +%s%3N)
      { echo "== process $i: wall $((end - start)) ms"; cat "$logs/$name-$i.log"; } >> "$logs/$name.log"
      rm -f "$logs/$name-$i.log"
      sleep "$(awk "BEGIN{print $pause/1000}")"
    done
    redact "$logs/$name.log" "$results/$name.log"
    grep -E "cycle 0:|wall" "$results/$name.log" | sed "s/^/$name: /" | tail -n 6
    cleanup_root "$root"
    ;;
esac
