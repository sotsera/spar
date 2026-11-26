#!/usr/bin/env bash
# One run of one task (eng/eval/README.md), in a fresh agent container: the task's app copied from the preparation, its
# seed, the profile's files, then the agent (or the reference solution, or nothing), then the hidden checks. Everything
# it keeps goes to /eval/out. The task arrives as /eval/out/task.json, written by run.cs.
#   in-container.sh <mode: agent|reference|none> <profile: dotnet|cli|mcp>
set -uo pipefail

mode=$1
profile=$2
out=/eval/out
task_json=$out/task.json
name=$(jq -r .name "$task_json")
app=$(jq -r .app "$task_json")

export EVAL_SPEC=/eval/spec EVAL_LIB=/eval/spec/lib.sh EVAL_TASK=/eval/spec/tasks/$name
export NUGET_PACKAGES=/home/spar/.nuget/packages
export npm_config_offline=true
export DOTNET_CLI_TELEMETRY_OPTOUT=1

work=/home/spar/work/$app
mkdir -p /home/spar/work
cp -a "/eval/prep/apps/$app" "$work" && cd "$work" || { echo "The app could not be copied."; exit 3; }

# Offline: Spar's packages from the feed, everything else from the preparation's packages, read-only. The dotnet
# profile's feed has no Spar.Tool, so its agent cannot install the spar tool (the first round's could, and did).
feed=/eval/prep/feed
if [ "$profile" = dotnet ]; then
    feed=/home/spar/feed
    mkdir -p "$feed"
    find /eval/prep/feed -name '*.nupkg' ! -name 'Spar.Tool.*' -exec cp {} "$feed/" \;
fi

cat > nuget.config <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="spar" value="$feed" />
  </packageSources>
  <fallbackPackageFolders>
    <add key="prepared" value="/eval/prep/nuget" />
  </fallbackPackageFolders>
</configuration>
XML

# The seed: the task's starting point, which the agent sees as the app's state.
while IFS= read -r command; do
    [ -n "$command" ] || continue
    bash -c "$command" > "$out/seed.log" 2>&1 || { echo "The seed failed:"; cat "$out/seed.log"; exit 3; }
done < <(jq -r '.commands[]?' "$task_json")

# The profile (decision "M2e's decide-first questions", item 2). dotnet: no Spar tool, skills or agent notes. cli: the
# skills without the MCP section, and the template's notes. mcp: the skills, the notes, and the MCP server.
skills=$(ls /eval/prep/skills)
case "$profile" in
    dotnet)
        rm -f AGENTS.md CLAUDE.md .config/dotnet-tools.json
        ;;
    cli | mcp)
        mkdir -p .claude/skills
        for skill in $skills; do cp -r "/eval/prep/skills/$skill" ".claude/skills/$skill"; done
        if [ "$profile" = cli ]; then
            perl -0pi -e 's/\n## With the spar MCP server\n.*?(?=\n## |\z)/\n/s' .claude/skills/spar-dev-loop/SKILL.md
        fi
        if [ ! -f AGENTS.md ]; then
            sed "s/MyApp/$app/g" /eval/prep/agents-template.md > AGENTS.md
        fi
        printf '@AGENTS.md\n' > CLAUDE.md
        # As a developer's checkout has it after the notes' one-time step; the MCP server starts from it.
        dotnet tool restore > "$out/tool-restore.log" 2>&1 || { echo "dotnet tool restore failed:"; cat "$out/tool-restore.log"; exit 3; }
        ;;
esac

# The baseline the diff is taken against, after the seed and the profile.
git init -q && git add -A && git -c user.name=eval -c user.email=eval@localhost commit -qm baseline

# A display and a session bus for the app when the agent runs it itself (spar dev --private and the tests start their own).
Xvfb :90 -screen 0 1600x1000x24 -nolisten tcp > /dev/null 2>&1 &
export DISPLAY=:90
DBUS_SESSION_BUS_ADDRESS=$(dbus-daemon --session --fork --print-address 1) && export DBUS_SESSION_BUS_ADDRESS

started=$(date +%s)
case "$mode" in
    agent)
        prompt=$(jq -r .prompt "$task_json")
        model=$(jq -r .model "$task_json")
        turns=$(jq -r .maxTurns "$task_json")
        minutes=$(jq -r .maxMinutes "$task_json")
        args=(-p "$prompt" --output-format stream-json --verbose --model "$model" --max-turns "$turns" --permission-mode bypassPermissions)
        if [ "$profile" = mcp ]; then
            printf '{"mcpServers":{"spar":{"command":"dotnet","args":["spar","mcp"]}}}\n' > .mcp.json
            args+=(--mcp-config .mcp.json --strict-mcp-config)
            for tool in tree screenshot click fill wait contracts call calls logs version reload snap doctor; do
                args+=(--disallowedTools "Bash(dotnet spar $tool:*)")
            done
        else
            args+=(--strict-mcp-config)
        fi
        timeout --kill-after=30s "${minutes}m" claude "${args[@]}" > "$out/transcript.jsonl" 2> "$out/agent-stderr.log"
        echo $? > "$out/agent-exit"
        ;;
    reference)
        bash "$EVAL_TASK/reference.sh" > "$out/reference.log" 2>&1
        echo $? > "$out/agent-exit"
        ;;
    patch)
        # A regrade: an earlier run's changes, from its diff, checked again by the task's current checks.
        git apply --whitespace=nowarn "$out/diff.patch" > "$out/patch.log" 2>&1
        echo $? > "$out/agent-exit"
        ;;
    none)
        echo 0 > "$out/agent-exit"
        ;;
esac
echo $(( $(date +%s) - started )) > "$out/agent-seconds"

# Whatever the agent left running ends before the checks (kill -1 spares this script and PID 1).
kill -KILL -1 2> /dev/null
sleep 1

git add -A > /dev/null 2>&1 && git diff --cached --stat > "$out/diff-stat.txt" && git diff --cached -- . ':(exclude)*.png' > "$out/diff.patch"
git reset -q

# The hidden checks, which the agent never saw.
while IFS= read -r command; do
    [ -n "$command" ] || continue
    timeout 15m bash -c "$command" >> "$out/grade.log" 2>&1
done < <(jq -r '.graders[]' "$task_json")
grep '^CHECK ' "$out/grade.log" > "$out/checks.txt" || true
exit 0
