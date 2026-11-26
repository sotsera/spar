#!/usr/bin/env bash
# M2c's spar snap smoke on Windows (docs/evidence/m2c-spar-snap.md), from Git Bash at the repository's root, after
# dotnet build src/Spar.Tool -c Debug: smoke.sh against the Notebook with the built spar tool. The scenes' windows are
# the test host's (offscreen, never activated, with their profiles under artifacts/webview2-profiles/test-host/). The
# log, with the repository's and the user's paths replaced (../m2b-spar-dev/redact.pl), goes to logs/windows-smoke.txt.
set -u
repo=$(pwd -W)
mkdir -p artifacts/m2c-spar-snap docs/evidence/m2c-spar-snap/logs
raw="artifacts/m2c-spar-snap/windows-$(date -u +%Y%m%d-%H%M%S).txt"
bash docs/evidence/m2c-spar-snap/smoke.sh samples/Notebook "$repo/src/Spar.Tool/bin/Debug/net11.0/Spar.Tool.exe" > "$raw" 2>&1
REPO="$repo" perl docs/evidence/m2b-spar-dev/redact.pl < "$raw" > docs/evidence/m2c-spar-snap/logs/windows-smoke.txt
cp samples/Notebook.EndToEndTests/obj/spar-snap/groceries/page.png docs/evidence/m2c-spar-snap/windows-groceries.png
echo "wrote docs/evidence/m2c-spar-snap/logs/windows-smoke.txt"
