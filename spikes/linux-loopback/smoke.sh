#!/usr/bin/env bash
# Usage: smoke.sh <command...>   Starts the app with --loopback --exit-with-stdin and checks the bridge with curl.
set -u
log=$(mktemp); fifo=$(mktemp -u); mkfifo "$fifo"
start=$(date +%s%N)
"$@" --loopback --exit-with-stdin <"$fifo" >"$log" 2>&1 &
pid=$!
exec 3>"$fifo"                       # hold stdin open
for _ in $(seq 1 300); do grep -q '^SPAR_READY' "$log" && break; sleep 0.01; done
ready_ms=$(( ($(date +%s%N) - start) / 1000000 ))
line=$(grep '^SPAR_READY' "$log") || { echo "no ready line"; cat "$log"; exit 1; }
echo "ready after ${ready_ms} ms (wall, incl. process start): $line"
url=$(sed -E 's/.*url=([^ ]+).*/\1/' <<<"$line"); base=${url%%/#*}; token=${url##*#token=}
build=$(sed -E 's/.*build=([^ ]+).*/\1/' <<<"$line")
call() { curl -s -o /tmp/body -w '%{http_code}' -X POST "$base/_spar/call/notes/$1" "${@:2}"; echo " $(cat /tmp/body)"; }
echo "index.html: $(curl -s -o /dev/null -w '%{http_code} %{size_download}B' "$base/")"
echo "valid save: $(call save -H "Authorization: Bearer $token" -H "X-Spar-Contracts: $build" -H 'Content-Type: application/json' --data '{"text":"hi","tags":[]}')"
echo "no token: $(call save -H "X-Spar-Contracts: $build" --data '{"text":"hi","tags":[]}')"
echo "wrong fingerprint: $(call save -H "Authorization: Bearer $token" -H 'X-Spar-Contracts: 0000000000000000' --data '{"text":"hi","tags":[]}')"
echo "missing property: $(call save -H "Authorization: Bearer $token" -H "X-Spar-Contracts: $build" --data '{}')"
echo "foreign origin: $(call save -H "Authorization: Bearer $token" -H "X-Spar-Contracts: $build" -H 'Origin: http://evil.example' --data '{"text":"hi","tags":[]}')"
closed=$(date +%s%N)
exec 3>&-                            # close stdin
wait $pid; code=$?
echo "exit after stdin close: code $code in $(( ($(date +%s%N) - closed) / 1000000 )) ms"
echo "--- app output"; cat "$log"; rm -f "$log" "$fifo"
