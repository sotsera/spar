#!/usr/bin/env bash
# Every measurement of the spike, in order. Git Bash, repository root. Logs go to artifacts/window-automation/;
# copies with paths redacted go to spikes/window-automation/results/.
# Needs: the page built (cd spikes/window-automation/page && npm ci --ignore-scripts && npx vite build), and the
# library's Chromium 1248 for the Browser surface (it was already in the default Playwright cache; nothing downloaded).
set -u
spike=spikes/window-automation
out=artifacts/window-automation
rm -rf "$out"
mkdir -p "$out"
a="$spike/OptionA/bin/Release/net11.0/win-x64/OptionA.exe"
bjit="$spike/OptionB/bin/Release/net11.0/win-x64/OptionB.exe"
baot="$spike/OptionB/bin/publish/OptionB.exe"

dotnet build "$spike/OptionA" -c Release > "$out/build-a.log" 2>&1
dotnet build "$spike/OptionB" -c Release --no-incremental > "$out/build-b.log" 2>&1
# The JIT run first: the NativeAOT publish below replaces the build output folder's apphost.
"$bjit" journey --runs 20 > "$out/b-jit-journey.log" 2>&1
rm -rf "$spike/OptionB/obj/Release/net11.0/win-x64/native"
dotnet publish "$spike/OptionB" -c Release -o "$spike/OptionB/bin/publish" > "$out/publish-b-aot.log" 2>&1

runs=${RUNS:-60}
"$baot" journey --runs "$runs" --screenshots > "$out/b-aot-journey.log" 2>&1
"$a" journey --runs "$runs" --screenshots > "$out/a-journey.log" 2>&1
"$a" browser --runs "$runs" --screenshots > "$out/a-browser-surface.log" 2>&1
"$baot" actions > "$out/b-aot-actions.log" 2>&1
"$a" actions > "$out/a-actions.log" 2>&1
"$baot" coverage > "$out/b-aot-coverage.log" 2>&1
"$a" coverage > "$out/a-coverage.log" 2>&1
for anchor in private default none; do "$baot" isolation --anchor "$anchor"; done > "$out/b-aot-isolation.log" 2>&1

# Two test processes at once, each on its own root.
"$a" journey --runs 15 > "$out/a-parallel-1.log" 2>&1 &
p1=$!
"$a" journey --runs 15 > "$out/a-parallel-2.log" 2>&1 &
p2=$!
wait $p1 $p2
"$baot" journey --runs 15 > "$out/b-parallel-1.log" 2>&1 &
p1=$!
"$baot" journey --runs 15 > "$out/b-parallel-2.log" 2>&1 &
p2=$!
wait $p1 $p2

bash "$spike/scripts/exposure.sh" > /dev/null

# Redacted copies (repository path, user profile). The trace archive (a-trace.zip) carries the test source path in
# trace.stacks: it was copied by hand with that path replaced.
node "$spike/scripts/redact.mjs"
echo "done; leftover webview processes of this spike: $(powershell -NoProfile -Command "(Get-CimInstance Win32_Process -Filter \"Name='msedgewebview2.exe'\" | Where-Object { \$_.CommandLine -like '*window-automation*' }).Count")"
