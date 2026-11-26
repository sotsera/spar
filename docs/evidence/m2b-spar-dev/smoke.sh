#!/usr/bin/env bash
# M2b's spar dev against the Notebook (docs/evidence/m2b-spar-dev.md), the way an agent would use it: spar dev --private
# in the background, then reloads with nothing changed, with a C# error, with a TypeScript error and fixed again, the
# devtools against each new run, a second spar dev refused, the app ended from outside and started again by a reload,
# and spar dev --stop. Each command line, its output and its exit code are printed. The two files it breaks are put
# back from copies, and the script checks that git sees no change.
#   smoke.sh <the Notebook's project folder> <the spar command, e.g. src/Spar.Tool/bin/Debug/net11.0/Spar.Tool.exe>
# The caller sets the app's data folders (LOCALAPPDATA on Windows, HOME and XDG_* on Linux) beforehand.
set -u
project=$(cd "$1" && pwd)
shift
spar=("$@")
repo=$(cd "$(dirname "$0")/../../.." && pwd)
work="$repo/artifacts/m2b-spar-dev/work-$$"
mkdir -p "$work"
dev_log="$work/spar-dev.txt"

step() {
  echo "\$ spar $*"
  local start end
  start=$(date +%s.%N)
  (cd "$project" && "${spar[@]}" "$@")
  local code=$?
  end=$(date +%s.%N)
  echo "exit $code ($(awk "BEGIN { printf \"%.1f\", $end - $start }") s)"
  echo
}

wait_for() {
  for _ in $(seq 1 600); do
    grep -qE "$1" "$dev_log" 2>/dev/null && return 0
    sleep 0.5
  done
  echo "(gave up waiting for: $1)"
}

echo "\$ spar dev --private &"
(cd "$project" && exec "${spar[@]}" dev --private) > "$dev_log" 2>&1 &
dev=$!
wait_for '^running:|^build failed|^no run|exited with|did not report'
grep -vE '^spar (info|debug)|^ *(> |VITE|➜|\?|[0-9:]+ \[vite\])' "$dev_log"
echo

step reload
step version
step tree

csharp="$project/Pages/PageTypes.cs"
typescript="$project/frontend/src/main.tsx"
cp "$csharp" "$work/PageTypes.cs"
cp "$typescript" "$work/main.tsx"

echo "# A C# error: a method that returns a string as an int, without its semicolon."
printf '\ninternal static class Broken { public static int Value() => "text" }\n' >> "$csharp"
step reload
cp "$work/PageTypes.cs" "$csharp"

echo "# A TypeScript error in the page."
printf '\nexport const broken: number = "text";\n' >> "$typescript"
step reload --json
cp "$work/main.tsx" "$typescript"

echo "# Both put back."
step reload
step version

echo "# A second spar dev for the same project."
step dev

echo "# The app ended from outside (its window closed, a crash): a reload starts it again."
pid=$(cd "$project" && "${spar[@]}" version | sed -n 's/^pid: //p')
if [ -n "$pid" ]; then
  if command -v taskkill >/dev/null 2>&1; then taskkill //F //PID "$pid" >/dev/null; else kill -9 "$pid"; fi
fi
wait_for 'exited with .*; spar reload builds and starts it again'
grep -E 'exited with .*; spar reload' "$dev_log"
step reload

step dev --stop
wait "$dev"
echo "spar dev exit $?"
echo

echo "# spar dev's own output, without the app's lines:"
grep -vE '^spar (info|debug)|^ *(> |VITE|➜|\?|[0-9:]+ \[vite\])' "$dev_log"
echo
echo "# Left behind:"
ls "$project/obj/spar-dev.json" "$project/obj/spar-devtools.json" 2>&1 | sed 's/^/  /'
(cd "$project" && git status --short -- . 2>/dev/null | sed 's/^/  changed: /')
rm -rf "$work"
