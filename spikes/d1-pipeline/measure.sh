#!/usr/bin/env bash
# Measures candidate A build behaviour on CoreCLR Debug builds. Run from this directory with Git Bash.
# Each step prints elapsed ms, whether the client tool ran, and the generated outputs that changed.
set -euo pipefail
cd "$(dirname "$0")"

app=Spike.App
client=$app/obj/Debug/net11.0/win-x64/spar/client
dll=$app/bin/Debug/net11.0/win-x64/Spike.App.dll
service=$app/NotesService.cs
cp "$service" "$service.orig"
trap 'mv -f "$service.orig" "$service"' EXIT

stamp() { stat -c %Y.%N "$1" 2>/dev/null || echo missing; }

build() {
  local label=$1 before_dll before_ts start elapsed log tool
  before_dll=$(stamp "$dll"); before_ts=$(stamp "$client/notes.ts")
  start=$(date +%s%N)
  log=$(dotnet build "$app/$app.csproj" --nologo -v:m 2>&1) || true
  elapsed=$(( ($(date +%s%N) - start) / 1000000 ))
  tool=$(grep -c 'spike-tool client:' <<<"$log" || true)
  printf '%-34s %6d ms  tool-runs=%s  dll-changed=%s  notes.ts-changed=%s\n' "$label" "$elapsed" "$((tool / 2))" \
    "$([[ $(stamp "$dll") != "$before_dll" ]] && echo yes || echo no)" \
    "$([[ $(stamp "$client/notes.ts") != "$before_ts" ]] && echo yes || echo no)"
  grep -E ': error ' <<<"$log" | sed 's/^/    /' | sort -u | head -3 || true
}

dotnet build-server shutdown >/dev/null 2>&1 || true
rm -rf "$app/obj" "$app/bin"
build "clean build"
build "no-op build"
build "no-op build (again)"

sed -i 's/cancellationToken.ThrowIfCancellationRequested();/cancellationToken.ThrowIfCancellationRequested(); _ = 1;/' "$service"
build "method body edit"

sed -i 's|    \[SparCommand\]\n    public ValueTask<int> CountAsync|&|' "$service"
cat >> "$service" <<'EOF'

[SparContract("extra")]
public sealed class ExtraService
{
    [SparCommand]
    public Task<string> PingAsync(string message) => Task.FromResult(message);
}
EOF
build "add contract"
ls "$client" | tr '\n' ' '; echo

sed -i 's/PingAsync/EchoAsync/' "$service"
build "rename command"
grep -o 'echo\|ping' "$client/extra.ts" | sort -u | tr '\n' ' '; echo

sed -i 's/\[SparContract("extra")\]//' "$service"
build "remove contract"
ls "$client" | tr '\n' ' '; echo

sed -i 's/public Task<string> EchoAsync(string message)/public Task<string> EchoAsync(Dictionary<string, int> message)/; s/^public sealed class ExtraService/[SparContract("extra")]\npublic sealed class ExtraService/' "$service"
sed -i 's/Task.FromResult(message);/Task.FromResult("x");/' "$service"
build "unsupported type (expect error)"

mv -f "$service.orig" "$service"; cp "$service" "$service.orig"
build "restore original (old timestamp)"
touch "$service"
build "restore original (saved)"
grep -c 'class ExtraService' "$app/obj/Debug/net11.0/win-x64/spar/client/contracts.json" >/dev/null 2>&1 && echo "    stale contract still present" || echo "    contracts.json matches the original source"

start=$(date +%s%N)
log=$(dotnet msbuild "$app/$app.csproj" -nologo -t:Compile -p:DesignTimeBuild=true -p:SkipCompilerExecution=true \
  -p:ProvideCommandLineArgs=true -p:BuildProjectReferences=false -v:m 2>&1) || true
printf '%-34s %6d ms  tool-runs=%s\n' "design-time compile" "$(( ($(date +%s%N) - start) / 1000000 ))" \
  "$(( $(grep -c 'spike-tool client:' <<<"$log" || true) / 2 ))"
