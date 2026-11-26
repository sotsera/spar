# The evaluation of Spar's agent surfaces

Ten tasks on a Spar app, each run by Claude Code headless in three profiles, to compare how an agent does with plain
`dotnet`, with the `spar` CLI, and with the `spar` MCP server (plan, M2e; decision "M2e's decide-first questions").
The surface that loses is deleted.

## What is here

| Path | What |
|---|---|
| `eval.yaml` | The ten stimuli, in the `eval.yaml` form of the .NET skills repository, with Spar's additions in its header. |
| `tasks/<name>/` | Each task's `seed.sh` (its starting point), `grade.sh` (the checks the agent never sees, with their files in `hidden/`) and `reference.sh` (a solution, with its files in `reference/`). |
| `lib.sh` | The scripts' helpers: `replace`, `hidden`, `check`, `fails`, `test_class`. |
| `Dockerfile` | The agent image: Spar's Linux CI image (`eng/linux-ci/`) with Claude Code (pinned), git and jq. |
| `proxy/` | An HTTPS proxy that tunnels to `api.anthropic.com` and refuses every other host. |
| `prepare.sh` | Run once with network: Spar packed into a feed, the Notebook and Pantry apps, and every package the tasks need. |
| `in-container.sh` | One run: the app, the seed, the profile, the agent, the checks. |
| `run.cs` | The runner: `images`, `prepare`, `validate`, `run`, `report`. |

## Profiles

| Profile | The agent gets |
|---|---|
| `dotnet` | The app with no Spar tool manifest, skills or agent notes. |
| `cli` | The app's tool manifest, the plugin's skills in `.claude/skills/` (the MCP section of `spar-dev-loop` left out), and the template's `AGENTS.md` (through `CLAUDE.md`). |
| `mcp` | As `cli` with the whole `spar-dev-loop`, the `spar mcp` server configured, and the CLI's devtools, `reload`, `snap` and `doctor` refused, so the agent uses the server's tools; `spar dev` stays a shell command. |

## Running it

From the repository's root, with Docker running:

```sh
dotnet run eng/eval/run.cs -- images
dotnet run eng/eval/run.cs -- prepare
dotnet run eng/eval/run.cs -- validate
```

`validate` needs no agent and no network: each task's checks must fail on the seeded app and pass with its reference
solution. Then, with credentials in the shell's environment (`ANTHROPIC_API_KEY`, or `CLAUDE_CODE_OAUTH_TOKEN` from
`claude setup-token`), which the containers receive by name and nothing writes to a file:

```sh
dotnet run eng/eval/run.cs -- run --profiles dotnet,cli,mcp --runs 3 --parallel 3 --model sonnet
```

Each run gets a fresh container on an internal network whose only way out is the proxy. Its transcript, diff, checks
and logs go to `artifacts/eval/run-<time>/<task>/<profile>-<n>/`, and the run folder gets `runs.json` and
`report.md`. A run passes when every check passes and it stayed within the task's constraints: 80 turns, 20 minutes,
and 300,000 tokens counted as input, output and cache writes (cache reads, which every turn repeats, are reported
apart). `report <folder>` writes the report again.
