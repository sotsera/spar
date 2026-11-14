#!/usr/bin/env bash
# Incremental build behaviour of the React app: which steps run for which edit. Run from this folder in Git Bash.
set -euo pipefail
cd "$(dirname "$0")/React.App"
service=NotesService.cs
header=frontend/src/Header.tsx
cp "$service" /tmp/spar-service.orig
cp "$header" /tmp/spar-header.orig
trap 'cp /tmp/spar-service.orig "$service"; cp /tmp/spar-header.orig "$header"' EXIT

build() {
  local label=$1 start log ms
  start=$(date +%s%N)
  log=$(dotnet build --nologo -v:d 2>&1) || { echo "$log" | grep -E 'error' | head -5; exit 1; }
  ms=$(( ($(date +%s%N) - start) / 1000000 ))
  ran() { grep -q -E "^\s*$1:|Target \"$1\"|$1" <<<"$log" && grep -q -E "(Building target \"$1\" completely|Target \"$1\" in project .* \(|^$1:)" <<<"$log" && echo yes || echo no; }
  printf '%-30s %6d ms  client=%-3s frontend=%-3s pack=%-3s csc=%s\n' "$label" "$ms" \
    "$(grep -q 'Building target "SparGenerateClient" completely\|Building target "SparGenerateClient" partially' <<<"$log" && echo yes || echo no)" \
    "$(grep -q 'Building target "SparBuildFrontend" completely' <<<"$log" && echo yes || echo no)" \
    "$(grep -q 'Building target "SparPackFrontend" completely' <<<"$log" && echo yes || echo no)" \
    "$(grep -q 'Building target "CoreCompile" completely' <<<"$log" && echo yes || echo no)"
}

build "warm-up"
build "no-op"
sed -i 's/_notes.Count);/_notes.Count + 0);/' "$service"; build "C# method-body edit"
sed -i 's|<h1>Notes</h1>|<h1>My notes</h1>|' "$header"; build "frontend edit (Header.tsx)"
sed -i 's|    \[SparCommand\]\n    public Task<int> CountAsync|&|' "$service"
cat >> "$service" <<'EOF'

[SparContract("extras")]
public sealed class ExtrasService
{
    [SparCommand]
    public Task<string> PingAsync(NoteId id) => Task.FromResult(id.Id);
}
EOF
build "contract edit (new contract)"
cp /tmp/spar-service.orig "$service"; cp /tmp/spar-header.orig "$header"; touch "$service" "$header"
build "restore originals"
build "no-op again"
