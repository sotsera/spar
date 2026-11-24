#!/usr/bin/env bash
# M1d's exit: copies the runs' raw files from artifacts/ (win.sh's artifacts/m1d-exit/windows, wsl.sh's
# artifacts/linux-runs/m1d-exit, bisect-win.sh's artifacts/m1d-exit/bisect) into docs/evidence/m1d-exit/windows/,
# linux/ and bisect.txt, with the repository's path written <repo>, the WSL copy's <copy>, the user's folders and name
# <user> and the loopback tokens …. From Git Bash at the repository's root: bash docs/evidence/m1d-exit/collect.sh
set -uo pipefail
repo=$(cd "$(dirname "$0")/../../.." && pwd)
here=$repo/docs/evidence/m1d-exit
user=$(basename "$USERPROFILE")
redact() {
  sed -e 's|E:\\Ghidello\\Git\\spar\.v7|<repo>|g' -e 's|/mnt/e/Ghidello/Git/spar\.v7|<repo>|g' -e 's|/e/Ghidello/Git/spar\.v7|<repo>|g' \
    -e 's|E:/Ghidello/Git/spar\.v7|<repo>|g' -e 's|/var/tmp/spar-m1c-blackbox/repo|<copy>|g' \
    -e "s|C:\\\\Users\\\\$user|<user>|g" -e "s|C:/Users/$user|<user>|g" -e 's|[A-Z0-9]\{1,6\}~[0-9]|<user>|g' -e "s|$user|<user>|gI" \
    -e 's|#token=[A-Za-z0-9_-]\{8,\}|#token=…|g' -e 's|\x1b\[[0-9;]*m||g' "$1" > "$2"
}
mkdir -p "$here/windows" "$here/linux"
for f in budgets-coreclr.txt budgets-aot.txt m1a-command-coreclr-1.txt m1a-command-coreclr-2.txt m1a-command-aot-1.txt \
  m1a-command-aot-2.txt m1a-browser-1.txt m1a-browser-2.txt noop.log notebook.log harness.log hotreload.run.log \
  firstedit-1.run.log firstedit-2.run.log firstedit-3.run.log buildtests-timings.txt template-firstedit.txt \
  packagetests-timings.txt packagetests-measurements.txt; do
  [ -f "$repo/artifacts/m1d-exit/windows/$f" ] && redact "$repo/artifacts/m1d-exit/windows/$f" "$here/windows/$f"
done
for f in budgets-coreclr.txt budgets-aot.txt m1a-command-coreclr.txt m1a-command-aot.txt m1a-browser.txt noop.log \
  notebook.log harness.log hotreload.run.log firstedit-1.run.log firstedit-2.run.log firstedit-3.run.log buildtests-timings.txt \
  packagetests-timings.txt packagetests-measurements.txt; do
  [ -f "$repo/artifacts/linux-runs/m1d-exit/$f" ] && redact "$repo/artifacts/linux-runs/m1d-exit/$f" "$here/linux/$f"
done
for f in "$repo"/artifacts/linux-runs/m1d-exit/budgets-launch-*.txt; do redact "$f" "$here/linux/$(basename "$f")"; done
{
  for f in "$repo"/artifacts/m1d-exit/bisect/*.txt; do echo "== $(basename "$f")"; cat "$f"; done
} > "$here/bisect.tmp" && redact "$here/bisect.tmp" "$here/bisect.txt" && rm "$here/bisect.tmp"
grep -rliE --exclude=collect.sh "C:\\\\Users|[A-Z0-9]{1,6}~[0-9]|$user" "$here" && echo "NOT REDACTED" || echo "redacted"
