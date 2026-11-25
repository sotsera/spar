#!/usr/bin/env bash
# M2a's devtools against a running Development build of the Notebook (docs/evidence/m2a-devtools.md): every spar command
# once, the way an agent would use them, with each command line, its output and its exit code. The app is started
# elsewhere (smoke-windows.sh on a private desktop, linux.sh on a private Xvfb); this waits for its discovery file.
#   smoke.sh <the Notebook's project folder> <the spar command, e.g. dotnet src/Spar.Tool/bin/Debug/net11.0/Spar.Tool.dll>
# The screenshot goes to $SMOKE_SHOT (default: obj/spar-devtools/ in the project).
set -u
project=$1
shift
spar=("$@")
title="Devtools smoke $(date -u +%H%M%S)"

step() {
  echo "\$ spar $*"
  (cd "$project" && "${spar[@]}" "$@")
  echo "exit $?"
  echo
}

for _ in $(seq 1 240); do
  [ -f "$project/obj/spar-devtools.json" ] && break
  sleep 0.5
done

step version
step tree
step click --role button --name "New page" --nth 0
step wait --role dialog --name "New page"
step fill --label Title "$title"
step click --role button --name "Create page"
step wait --role heading --name "$title" --nth 0
step tree
step calls
step call pages.list
step call pages.get '{"id":"no-such-page"}'
step contracts
step logs --limit 15
step logs --source page
if [ -n "${SMOKE_SHOT:-}" ]; then
  step screenshot --out "$SMOKE_SHOT"
else
  step screenshot
fi

# Refusals: two headings match without --nth (ambiguous); a button that never appears runs out of time (timeout); a
# reference the last tree did not give is refused at once (stale_target); fill without its text is a usage error (64).
step wait --role heading --name "$title"
step click --role button --name "No such button" --timeout 500
step click e9999
step fill e1
