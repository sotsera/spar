---
title: Build and restart with spar dev
description: Keep a Spar app's Development build running, and rebuild and restart it on request with the build's errors in the answer.
---

# Build and restart with spar dev

`spar dev` keeps a Development build of a Spar app running, and rebuilds and restarts it when you ask. It is meant for a coding agent or a script that edits the app's code and needs to know, in one step, whether the change built and what the app looks like now. You run it through the `spar` tool in the app's folder:

```sh
dotnet spar dev
```

The `spar` tool comes with a new app's local tool manifest; run `dotnet tool restore` once first (see [Getting started](getting-started.md), section 8).

`spar dev` builds the app's Debug build, starts it, and stays in the foreground, printing the app's output, until you press Ctrl+C or run `dotnet spar dev --stop`. In another terminal, after changing the code:

```sh
dotnet spar reload
```

`spar reload` builds the app again and answers when the result is known:

- **When the build fails,** it lists each error and warning at its file, line and column: the C# compiler's, Spar's own checks of your contracts, and the TypeScript type-check of the page. The app that was running keeps running, so you can still look at it.
- **When the build succeeds,** the running app is closed the way the system closes it when you sign out (its close handlers get their short budget, it cannot ask anything), the new build starts in the same place and size on screen without taking the keyboard focus, and the answer comes once its page is ready: the new run, how long it took, and the build's warnings.
- **When the new build fails to start,** the answer has its exit code and its last lines of output.

```text
build failed in 2.5 s (1 error, 0 warnings)
  C:\src\Notes\Pages\PageTypes.cs(33,68): error CS1002: ; expected
the previous run is still running (pid 300, run 624f262d): fix the errors and reload
```

```text
built in 1.9 s (0 errors, 0 warnings)
running: pid 18352, run a4f5baba, ui-ready at 574.7 ms (0.8 s from its start); the previous run 624f262d
```

`--json` gives the same answer as JSON, with each diagnostic's `file`, `line`, `column`, `code` and `message`. The exit codes are those of `spar doctor`: 0 when the app is running and the build had no warnings, 1 when it had warnings, 2 when the build or the start failed, 64 for a wrong command line.

Once the page is ready, the [devtools](devtools.md) (`spar tree`, `spar click`, `spar call` and the others) work against the new run.

## What it does and does not do

- **It reloads only when asked.** It never watches your files, so a build never catches a change you are halfway through. For editing while the app runs, without a restart, use Hot Reload in your IDE or `dotnet watch` (see [Getting started](getting-started.md), section 5).
- **The page's own server is kept.** A Debug build serves its page from the frontend's Vite server, and every restart uses the same one, so the page's changes still show at once and a restart does not wait for Vite to start again.
- **It builds what your IDE builds.** The build is an ordinary Debug build into the project's `bin/`. The app runs from a copy of it under `obj/spar-dev/`, so the build never meets a file the running app holds, and your IDE can build the project while `spar dev` runs.
- **Everything it starts ends with it.** The build, the app and everything they start belong to `spar dev`: on Windows a job object, on Linux a process group of their own. Ending `spar dev` ends all of them, however it ends. Its builds use build processes of their own, which it ends too, so they never take over your IDE's builds.
- **One per project.** A second `spar dev` for the same project says where the first one runs and exits. The running one writes `obj/spar-dev.json`, which only you can read, for `spar reload` and `spar dev --stop` to find it.
- **If the app ends on its own** (you close its window, or it crashes), `spar dev` keeps running and says so; `spar reload` builds and starts it again.

## Options

| Option | What it does |
|---|---|
| `--project <path>` | The app's project file or folder, when it is not the current folder. |
| `-lp <name>`, `--launch-profile <name>` | A profile of `Properties/launchSettings.json`, with its environment variables, arguments and working folder, as `dotnet run -lp` and the IDEs use it; `dotnet spar dev -lp Loopback` runs the page in your browser. By default, the first profile that runs the project. |
| `--private` | Runs the app where you cannot see it: on Windows a desktop of its own that is never shown, on Linux a private X server (it needs Xvfb, `sudo apt install xvfb`). The app never takes your focus or your input there, and the devtools still read and drive its page, so an agent can work on the app while you use the computer. |
| `--stop` | Ends the project's running `spar dev`, its app and everything it started. |

On Linux, the window manager decides where a window opens; a restarted window keeps its place on Windows only.
