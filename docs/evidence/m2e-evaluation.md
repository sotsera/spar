# M2e: the agent guide, the plugin, the MCP adapter and the evaluation

Evidence for decisions "M2e's decide-first questions", "M2e: the agent guide, the plugin, the MCP adapter and the evaluation (implementation record; choices for review; 2026-11-26)" and "Both devtools surfaces kept". Measured on 2026-11-26.

## Environment

Windows 11 with Docker Desktop 29.9.3 (Linux containers on WSL 2). The evaluation's agent image is Spar's Linux CI image (`eng/linux-ci/Dockerfile`, Ubuntu 24.04, the .NET SDK of `global.json`, Node 24, WebKitGTK 6.0) with Claude Code 2.1.332, git and jq. Every run used `claude-sonnet-5-5` (the `sonnet` alias), headless, with `--permission-mode bypassPermissions` inside its own container, on an internal network whose only way out was a proxy that tunnels to `api.anthropic.com` alone. Spar was packed from the working tree into a feed (`0.1.0-eval.202611262020`); NuGet packages came from a read-only fallback folder filled by the preparation; nothing else was reachable.

## What was built and checked

- **`spar mcp`:** the devtools, `reload`, `snap` and `doctor` as MCP tools over stdio with `ModelContextProtocol.Core` 2.4.0's low-level server. The NativeAOT publish of the tool (`-p:PublishAot=true`, win-x64) had no warnings. By hand against the Notebook under `spar dev --private` on Windows: `initialize`, `tools/list` (13 tools), `version` with no run (an error result naming the missing discovery file), `tree`, `screenshot` (an image block and the saved path), an ambiguous `click` (refused with both matches), `calls`, and `reload` (built in 1.4 s, the new run ui-ready at 696 ms). In the evaluation the MCP profile's agents called its tools 18 times on Linux.
- **The skills:** four, 235 to 447 lines; each of their 48 marked code blocks is an excerpt of a sample, the template or a guide (`SkillsTests`). The `Spar.Tool` package carries them under `tools/net11.0/any/skills/`.
- **Tests:** `Spar.Tool.Tests` 148 (three runs after the snap search fix), `Spar.PackageTests` on Windows 43 of 44 (the Aspire check skipped there), `Spar.BuildTests` 7 of 7.

## The evaluation

Ten tasks (round 1) as decided, then five more (round 2) once round 1 showed the tasks did not need the running app. Each task is a prompt, a seed, hidden checks the agent never sees, and a reference solution; `run.cs validate` checked, offline, that every task's checks fail on the seeded app and pass with its reference (15 of 15). Three profiles: `dotnet` (no Spar tool manifest, skills or notes), `cli` (the skills without their MCP section, the template's `AGENTS.md`), `mcp` (the skills, the notes and `spar mcp`, with the CLI's devtools refused).

| Round | Runs | dotnet | cli | mcp | Cost |
|---|---|---|---|---|---|
| 1, specified features and fixes | 90 | 30/30 | 30/30 | 30/30 | $6.90 |
| 2, bug reports whose cause shows at run time | 45 | 15/15 | 15/15 | 15/15 | $2.49 |

Medians, round 1: tokens (input, output and cache writes) 13,880 / 19,014 / 19,772, time 24 / 26 / 27 s, turns 7 / 7 / 8. Round 2: 11,340 / 13,031 / 13,245 tokens, 15 / 27 / 27 s, 5 / 6 / 7 turns. The reports and every run's numbers are in `m2e-evaluation/`.

- **Round 1 was regraded once.** Four pantry-notes runs failed a page check that found the field with `GetByLabel("Note")`, a substring match that also matched the list the agents labelled "Notes". The check now names the textbox exactly; all nine pantry-notes runs were regraded from their diffs (`run.cs regrade`), and all nine pass. Each run keeps its first result as `checks.original.txt`.
- **The devtools were rarely needed.** Round 1: the CLI agents called `spar` commands 15 times, in the damaged-page, sidebar-scene and Pantry-scene tasks; the MCP agents called MCP tools 18 times in the same three. Round 2: 10 CLI calls, all screenshots in the off-screen greeting task; no MCP tool calls. Every other task was solved by reading the code and, for the data-dependent ones, the app's data folder with `find` and `cat`.
- **The `dotnet` profile in round 1 still reached the `spar` tool** for the scene tasks, installing it from the evaluation's feed; from round 2 its feed has no `Spar.Tool`.
- **The rule of decision item 8** gave the CLI in both rounds (equal pass rates, median tokens within 10 %); the user kept both surfaces instead (decision "Both devtools surfaces kept").

## Found along the way

- **Spar.Sdk referenced Spar.Loopback from the global packages folder only,** so a Development build failed (MSB3106) when NuGet resolved Spar's packages from a fallback folder. Fixed; pitfall in "Build, MSBuild and packages".
- **`spar snap`'s search for the end-to-end project failed when a sibling folder vanished** while it read the folders (CI's Windows tests). Fixed; pitfall in "Tooling and environment".
- **`spar tree` lists names and values, not text content:** a status line's text ("Hello, Ada!") is not in the tree, so an agent cannot read a message from it.
- **Publishing a non-Production variant is not refused,** although the design decides it (`docs/design/security.md`, `SparAllowNonProductionPublish`): nothing in `src/` enforces it. Older than M2e.

## Not verified

- **Skill activation and the skills' own effect:** the profiles with skills and the one without passed alike, so the evaluation shows no effect either way; whether an agent loaded a skill was not counted, and item 9's with-and-without comparison per skill is the same comparison.
- **Agents on Windows, or other agents than Claude Code:** every run was Linux, Claude Code and Sonnet.
- **The plugin installed from the marketplace:** the repository is the marketplace; no install was run.
- **CI on the last commits.**
