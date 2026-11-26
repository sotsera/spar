---
title: Working with coding agents
description: Give a coding agent Spar's skills, and let it run, inspect and drive a Spar app while it changes it.
---

# Working with coding agents

A coding agent can work on a Spar app the way you do: build it, run it, look at its window, call its commands, read its logs, and check its tests. Spar gives it two things for that: skills that explain how a Spar app is written, run, tested and published, and the `spar` tool's commands, which let it see and drive the running app.

## The skills

Four skills, in the Agent Skills format, each a folder with a `SKILL.md`:

| Skill | What it covers |
|---|---|
| `spar-app` | The project, `Program.cs`, contracts and results, the page's calls, page security, logging. |
| `spar-dev-loop` | `spar dev` and `spar reload`, the devtools, `spar snap`, `spar doctor`. |
| `spar-testing` | Command, page, browser, window and black-box tests, and scenes. |
| `spar-shipping` | Publishing, the runtime choices, OpenTelemetry. |

They describe the Spar version they come with, so keep them at your app's version.

**With an agent that installs plugins,** add Spar's marketplace and install its plugin, which carries the skills. In Claude Code:

```text
/plugin marketplace add sotsera/spar
/plugin install spar@spar
```

**With any other agent,** copy the skills into the app's repository from the `spar` tool, which carries them at its own version:

```sh
dotnet spar skills
```

It writes them to `.agents/skills/` beside the app's solution, and changes only files whose content changed, so run it again after updating Spar. `dotnet spar skills --list` shows the skills and where they would go.

A new app from the template also has an `AGENTS.md` at its root: the commands to build, test and run it, where the skills come from, and the rules that matter most.

## The tools

The `spar` tool comes with the app's local tool manifest (`dotnet tool restore` once). The loop the skills teach:

1. Start the app with `dotnet spar dev` in the background ([`spar dev`](spar-dev.md)).
2. Look at it: `dotnet spar tree` lists the page's elements, `dotnet spar screenshot` saves the page as a PNG ([devtools](devtools.md)).
3. Change the code, then `dotnet spar reload`: the build's errors at file and line, or the new run once its page is ready.
4. Drive it: `dotnet spar click`, `fill` and `wait`; check `dotnet spar calls` and `dotnet spar logs`.
5. Capture a state for review with a scene: `dotnet spar snap` ([scenes](spar-snap.md)).

An agent that works through MCP tools rather than a shell can use the same commands through `spar mcp`, which you configure in its MCP client ([devtools, "From an MCP client"](devtools.md#from-an-mcp-client)). The command line stays the main way in: it needs no configuration, and the skills teach it.

## Keeping the agent's work safe

- The devtools exist only in Development builds and listen on a socket only your user can open; a published app has none of their code.
- `dotnet spar dev --private` runs the app where you cannot see it, so an agent's clicks never land on your desktop while you work.
- An agent's commands run as you, with your files and network: give it the permissions you would give any tool you run.
