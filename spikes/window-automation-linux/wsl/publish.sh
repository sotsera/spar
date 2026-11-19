#!/usr/bin/env bash
# Usage: bash wsl/publish.sh (inside WSL, after sync.sh). NativeAOT publish of the harness (every trim/AOT warning is
# reported: none is suppressed), then a JIT build. Logs go to $base/logs.
set -uo pipefail
source "$(cd "$(dirname "$0")" && pwd)/common.sh"
isolate build
mkdir -p "$base/logs"
cd "$spike/Harness"
rm -rf bin obj
start=$(date +%s%N)
dotnet publish -c Release -o "$base/aot" -nologo -clp:Summary >"$base/logs/publish-aot.log" 2>&1
echo "PUBLISH exit=$? $(( ($(date +%s%N) - start) / 1000000 ))ms size=$(stat -c %s "$base/aot/Harness" 2>/dev/null) bytes" | tee -a "$base/logs/publish-aot.log"
grep -E "warning|error" "$base/logs/publish-aot.log" | grep -v "^ *0 " | sort -u | head -20
dotnet build -c Release -p:PublishAot=false -o "$base/jit" -nologo -clp:Summary >"$base/logs/build-jit.log" 2>&1
echo "BUILD-JIT exit=$?" | tee -a "$base/logs/build-jit.log"
