#!/usr/bin/env bash
# Questions 2 and 3. Git Bash, repository root. The file-chooser runs open real native dialogs for a moment where a
# case is not intercepted (the control, the no-Page.enable case, a second WebView, the cross-site iframe without
# auto-attach); each is closed at once with WM_CLOSE, and nothing is clicked on screen.
set -u
exe=spikes/window-tests/WindowTests.Win/bin/publish/WindowTests.Win.exe
out=artifacts/window-tests
mkdir -p "$out"
"$exe" input --iterations 100 --stall-runs 20 > "$out/input.log" 2>&1
for only in control intercept race frames fsa fsa-args; do
  "$exe" filechooser --only "$only" > "$out/filechooser-$only.log" 2>&1
done
