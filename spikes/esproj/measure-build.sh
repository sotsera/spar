#!/usr/bin/env bash
# Incremental build behaviour: which steps run for which edit, for one frontend mode.
# Usage (Git Bash, from anywhere): measure-build.sh <Drive|Reference|Targets> [extra msbuild args...]
# Builds Esproj.App in Debug with SparDevServerBuild=false (bundle built, as in ../react/measure-build.sh).
set -euo pipefail
mode=$1; shift; extra=("$@")
here=$(cd "$(dirname "$0")" && pwd)
cd "$here/Esproj.App"
service=NoteStores.cs
contract=NotesService.cs
header=../Esproj.Frontend/src/Header.tsx
work=$(mktemp -d); command -v cygpath >/dev/null && work=$(cygpath -m "$work")
cp "$service" "$work/service.orig"; cp "$contract" "$work/contract.orig"; cp "$header" "$work/header.orig"
trap 'cp "$work/service.orig" "$service"; cp "$work/contract.orig" "$contract"; cp "$work/header.orig" "$header"; rm -rf "$work"' EXIT

build() {
  local label=$1 start ms log
  start=$(date +%s%N)
  dotnet build --nologo -v:q -p:SparDevServerBuild=false -p:SpikeFrontendMode="$mode" "-bl:$work/b.binlog" "${extra[@]}" >"$work/out.txt" 2>&1 \
    || { grep -E 'error' "$work/out.txt" | head -5; exit 1; }
  ms=$(( ($(date +%s%N) - start) / 1000000 ))
  dotnet msbuild "$work/b.binlog" -noconlog "-flp:logfile=$work/b.txt;verbosity=detailed" >/dev/null 2>&1 || true
  log="$work/b.txt"
  has() { grep -q -E "$1" "$log" && echo yes || echo no; }
  printf '%-30s %6d ms  client=%-3s npm=%-3s vite=%-3s pack=%-3s csc=%s\n' "$label" "$ms" \
    "$(has 'Building target "SparGenerateClient" (completely|partially)')" \
    "$(has 'Building target "(RunNpmInstall|SparNpmCi)" completely')" \
    "$(grep -c 'built in [0-9]' "$log" | sed 's/^0$/no/; s/^1$/yes/')" \
    "$(has 'Building target "SparPackFrontend" completely')" \
    "$(has 'Building target "CoreCompile" completely')"
}

build "warm-up"
build "no-op"
build "no-op 2"
sed -i 's/_notes.Count);/_notes.Count + 0);/' "$service"; build "C# method-body edit"
sed -i 's|<h1>Notes</h1>|<h1>My notes</h1>|' "$header"; build "frontend edit (Header.tsx)"
cat >> "$contract" <<'EOF'

[SparContract("extras")]
public sealed class ExtrasService
{
    [SparCommand]
    public Task<string> PingAsync(NoteId id) => Task.FromResult(id.Id);
}
EOF
build "contract edit (new contract)"
cp "$work/service.orig" "$service"; cp "$work/contract.orig" "$contract"; cp "$work/header.orig" "$header"; touch "$service" "$contract" "$header"
build "restore originals"
build "no-op again"
