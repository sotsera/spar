#!/usr/bin/env bash
# Usage (Git Bash, from the repository root): spikes/startup-prerequisites/docker/summarize.sh [label regex]
# One line per Linux case run: exit code, launch to first new window, the problem found, the graphical way that
# showed it, where the journal entry went, and the app's own time from Main to its report.
set -u
only=${1:-.}
for d in artifacts/startup-prerequisites/linux/*/; do
  name=$(basename "$d")
  [[ $name =~ $only ]] || continue
  s=$(cat "$d/summary.txt" 2>/dev/null | sed 's/label=[^ ]* //')
  j="$d/app.jsonl"
  kind=$(grep -o '"evt":"problem","kind":"[A-Za-z]*"' "$j" 2>/dev/null | sed 's/.*kind":"//; s/"//')
  way=$(grep '"graphical"' "$j" 2>/dev/null | grep '"shown":true' | grep -o '"way":"[^"]*"' | sed 's/"way":"//; s/"//')
  skipped=$(grep -o '"skipped":"[^"]*"' "$j" 2>/dev/null | sed 's/"skipped":"//; s/"//')
  journal=$(grep -o '"evt":"journal","result":"[^"]*"' "$j" 2>/dev/null | sed 's/.*result":"//; s/"//' | cut -c1-40)
  report=$(grep -o '"t":[0-9.]*,"evt":"stderr"' "$j" 2>/dev/null | sed 's/"t":\([0-9.]*\).*/\1/')
  ready=$(grep -o '"t":[0-9.]*,"evt":"ready"' "$j" 2>/dev/null | sed 's/"t":\([0-9.]*\).*/\1/')
  printf '%-34s %-56s kind=%-9s shown=%-11s journal=%-14s report_at=%sms%s\n' "$name" "$s" "${kind:-none}" "${way:-${skipped:-none}}" \
    "${journal:-}" "${report:-}" "${ready:+ ready_at=${ready}ms}"
done
