#!/usr/bin/env bash
# Compares how the client/pack steps run: Exec (dotnet process), InProc (task in the MSBuild node),
# TaskHost (short-lived task host). For each mode: shut down build servers, one first build after a
# save, then N more saves. Reports whole-build ms, the client step's own ms, and reused references.
set -euo pipefail
cd "$(dirname "$0")"
runs=${1:-5}
app=Spike.App/Spike.App.csproj
service=Spike.App/NotesService.cs

one() {
  local mode=$1 start log ms client reused pid
  touch "$service"
  start=$(date +%s%N)
  log=$(dotnet build "$app" --nologo -v:m -p:SpikeTaskMode="$mode" 2>&1) || { echo "$log" | tail -5; exit 1; }
  ms=$(( ($(date +%s%N) - start) / 1000000 ))
  if [[ $mode == Exec ]]; then
    client=$(grep -oE 'spike-tool client: [0-9]+ ms' <<<"$log" | grep -oE '[0-9]+' | head -1)
    reused=$(grep -oE '[0-9]+ reused' <<<"$log" | grep -oE '[0-9]+' | head -1)
    pid=-
  else
    client=$(grep -oE 'reused, [0-9]+ ms' <<<"$log" | grep -oE '[0-9]+' | tail -1)
    reused=$(grep -oE '[0-9]+ reused' <<<"$log" | grep -oE '[0-9]+' | head -1)
    pid=$(grep -oE 'client:.*pid [0-9]+' <<<"$log" | grep -oE '[0-9]+$' | head -1)
  fi
  printf '%-9s %-6s build=%5d ms  client=%4s ms  reused=%3s  pid=%s\n' "$mode" "$2" "$ms" "$client" "$reused" "$pid"
}

dotnet build Spike.Tasks/Spike.Tasks.csproj --nologo -v:q >/dev/null
for mode in Exec InProc TaskHost; do
  dotnet build-server shutdown >/dev/null 2>&1 || true
  one "$mode" first
  for i in $(seq 1 "$runs"); do one "$mode" "edit$i"; done
done
