#!/usr/bin/env bash
# M2c's spar snap against the Notebook (docs/evidence/m2c-spar-snap.md), the way an agent would use it, from the app's
# own folder: the scenes listed, all of them captured (with the build), all of them captured again into another folder
# without building, the two captures of each scene compared byte for byte, one scene's tree, an unknown scene, and
# --json. Each command line, its output and its exit code are printed.
#   smoke.sh <the Notebook's project folder> <the spar command, e.g. src/Spar.Tool/bin/Debug/net11.0/Spar.Tool.exe>
set -u
project=$(cd "$1" && pwd)
shift
spar=("$@")
repo=$(cd "$(dirname "$0")/../../.." && pwd)
second="$repo/artifacts/m2c-spar-snap/second-$$"
first="$project/../Notebook.EndToEndTests/obj/spar-snap"

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

step snap --list
step snap --all
step snap --all --no-build --out "$second"

echo "# The two captures of each scene, byte for byte (calls.json without its times and durations):"
for scene in empty groceries new-page-dialog; do
  for file in page.png tree.json problems.txt; do
    if cmp -s "$first/$scene/$file" "$second/$scene/$file"; then same=same; else same=DIFFERENT; fi
    echo "  $scene/$file: $same"
  done
  # The calls' times and durations are the runs' own; the rest (the commands, their order and outcomes) is compared.
  strip() { grep -vE '"(started|milliseconds)"' "$1"; }
  if diff -q <(strip "$first/$scene/calls.json") <(strip "$second/$scene/calls.json") > /dev/null; then same=same; else same=DIFFERENT; fi
  echo "  $scene/calls.json without times: $same"
done
echo

echo "# groceries/tree.txt:"
sed 's/^/  /' "$first/groceries/tree.txt"
echo
echo "# groceries/calls.json, the commands in order:"
grep -o '"command": "[^"]*"' "$first/groceries/calls.json" | sed 's/^/  /'
echo

step snap no-such-scene --no-build
step snap empty --no-build --json
rm -rf "$second"
