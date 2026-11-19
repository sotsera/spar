#!/usr/bin/env bash
# Copies the small evidence of every session into spikes/sandbox-runner/results/ (Git Bash, repository root):
# timelines, configurations and each job's guest files, with the repository path, the profile path and SIDs redacted.
set -eu
src=artifacts/sandbox-runner/sessions
dst=spikes/sandbox-runner/results
export REPO_W=$(cygpath -aw .) REPO_M=$(cygpath -am .) HOME_W=$(cygpath -aw "$USERPROFILE") HOME_M=$(cygpath -am "$USERPROFILE")
redact() {
  perl -pe 'BEGIN { for (qw(REPO_W REPO_M HOME_W HOME_M)) { my $v = $ENV{$_}; (my $j = $v) =~ s/\x5c/\x5c\x5c/g;
      push @p, [$j, /REPO/ ? "<repo>" : "<home>"], [$v, /REPO/ ? "<repo>" : "<home>"] } }
    for my $p (@p) { s/\Q$p->[0]\E/$p->[1]/gi } s/S-1-5-21(-\d+)+/<sid>/g' "$1"
}
rm -rf "$dst/sessions"
for session in "$src"/*/; do
  name=$(basename "$session")
  mkdir -p "$dst/sessions/$name"
  for file in timeline.jsonl config.xml; do
    if [ -f "$session/$file" ]; then redact "$session/$file" > "$dst/sessions/$name/$file"; fi
  done
  for run in "$session"/out/*/; do
    [ -d "$run" ] || continue
    id=$(basename "$run")
    mkdir -p "$dst/sessions/$name/$id"
    for file in guest-env.txt exitcode.txt console.log results.trx; do
      if [ -f "$run/$file" ]; then redact "$run/$file" > "$dst/sessions/$name/$id/$file"; fi
    done
  done
done
node spikes/sandbox-runner/summarize.mjs "$src"/*/ > "$dst/summary.md"
if grep -rIl -i -e "$(basename "$USERPROFILE")" -e 'S-1-5-21' "$dst"; then echo "unredacted text left"; exit 1; fi
echo "redaction check passed"
