---
name: spar-dev-loop
description: Runs, inspects and drives a Spar app while you change it, with the `spar` tool. Use when starting a Spar app's Development build, rebuilding and restarting it after an edit, reading its page tree, screenshots, calls and logs, clicking and typing into its page, capturing end-to-end scenes, or checking the machine and project with `spar doctor`.
---

# The Spar development loop

A Spar app is a .NET backend and a web page in a native window. While you change it, the `spar` tool runs it, rebuilds it on request, and lets you look at its page and drive it from the command line. Every command here runs in the app's folder (or takes `--project <path>`).

Related skills: `spar-app` for writing contracts, `Program.cs` and the page's calls; `spar-testing` for tests and for writing scenes; `spar-shipping` for publishing.

## Before the first command

The `spar` tool comes with the app's local tool manifest (`.config/dotnet-tools.json`). Restore it once, from the solution's folder:

```sh
dotnet tool restore
```

Then run every command as `dotnet spar <command>`. `dotnet spar --help` lists them, and `dotnet spar <command> --help` shows one.

## Pick how to run the app

| You want | Run |
|---|---|
| To see the app once | `dotnet run` |
| Hot Reload while you edit (a person at the keyboard) | `dotnet watch`, or the IDE |
| To edit, rebuild on request, and look at the result (an agent or a script) | `dotnet spar dev` in the background, then `dotnet spar reload` |

`dotnet run` and `dotnet watch` use the first launch profile, `Desktop`, the window. `-lp Loopback` serves the same app to a browser instead:

<!-- source: src/Spar.Templates/content/spar-app/MyApp/Properties/launchSettings.json -->
```json
{
  "$schema": "https://json.schemastore.org/launchsettings.json",
  "profiles": {
    "Desktop": {
      "commandName": "Project"
    },
    "Loopback": {
      "commandName": "Project",
      "environmentVariables": {
        "SPAR_HOST": "Loopback"
      }
    }
  }
}
```

`dotnet watch` applies page edits in place (Vite's Fast Refresh), applies C# method bodies with Hot Reload, and restarts the app when a contract changes. `spar dev` never watches files and has no Hot Reload: it rebuilds and restarts only when you ask, so a build never catches a half-made change.

## spar dev: the supervisor

```sh
dotnet spar dev
```

It builds the app's Debug (Development) build, starts it, keeps the page's Vite server across restarts, and stays in the foreground printing the app's output until Ctrl+C or `dotnet spar dev --stop`. As an agent, **start it as a background process** and keep working in another shell.

- `--private` runs the app where nobody sees it: a desktop of its own on Windows, a private X server on Linux (it needs Xvfb, `sudo apt install xvfb`). The app never takes the user's focus or input, and the devtools still read and drive its page. Use it whenever someone may be using the computer.
- `-lp <name>` (`--launch-profile`) picks a profile of `Properties/launchSettings.json`; `-lp Loopback` runs the page in a browser.
- `--project <path>` names the app when it is not the current folder.
- `dotnet spar dev --stop` ends it, its app and everything it started.

One `spar dev` runs per project; a second one says where the first runs and exits. It writes `obj/spar-dev.json` for `reload` and `--stop` to find it. If the app exits on its own (its window closed, a crash), `spar dev` keeps running; `reload` builds and starts it again. The app runs from a copy under `obj/spar-dev/`, so your IDE can build the project meanwhile.

## spar reload: rebuild and restart

```sh
dotnet spar reload
```

It answers when the result is known (60 s at most):

- **Build failed:** every error and warning at file, line and column (the C# compiler, Spar's contract checks, the page's TypeScript type-check). The previous run keeps running, so you can still look at it.
- **Build succeeded:** the old run is closed, the new one starts in the same place and size without taking the focus, and the answer comes once its page reports `ui-ready`, with the new run's id and the build's warnings.
- **The new build failed to start:** its exit code and last lines of output.

```text
build failed in 2.5 s (1 error, 0 warnings)
  C:\src\Notes\Pages\PageTypes.cs(33,68): error CS1002: ; expected
the previous run is still running (pid 300, run 624f262d): fix the errors and reload
```

`--json` gives the same answer with each diagnostic's `file`, `line`, `column`, `code` and `message`.

| Exit code | Meaning |
|---|---|
| 0 | Running, no warnings |
| 1 | Running, with warnings |
| 2 | The build or the start failed |
| 64 | A wrong command line |

## The devtools: see and drive the running page

A Development build (`dotnet run`, `dotnet watch`, F5, or `spar dev`) carries devtools; a Release build, a published app and a test project's build have none. They listen on a local socket only your user can open, with a per-run token in `obj/spar-devtools.json`. A project can leave them out with `<SparDevtools>false</SparDevtools>`.

| Command | What it does |
|---|---|
| `dotnet spar tree [--limit N]` | The page's elements as assistive technology sees them (role, name, text, value, state), each with a reference such as `e12`. A line such as `e7 status: "Hello, Ada!"` gives, after the colon, the text an element shows where its name does not say it (a message, a paragraph, a list item; cut at 200 characters). Password values are never shown. |
| `dotnet spar screenshot [--out file]` | The page as PNG, title bar included; by default in `obj/spar-devtools/`. |
| `dotnet spar click <target>` | Waits until the element is visible, enabled and not covered, clicks it, then lists the commands that ran until the app was quiet again. |
| `dotnet spar fill <target> <text>` | Replaces the text of a field, text area or editable element. |
| `dotnet spar wait <target> [--state S]` | Waits until the element is `visible` (default), `hidden`, `attached` or `detached`. |
| `dotnet spar contracts` | The app's commands with their request, result and error types, and the contract snapshot. |
| `dotnet spar call <contract.command> [json]` | Runs a command as the page would; shows its value, its declared error or its failure. |
| `dotnet spar calls [--limit N] [--since seq]` | The last calls (up to 200), from the page, tests and `spar call`, with time and outcome. |
| `dotnet spar logs [--since 30s] [--source S] [--level L] [--limit N]` | The backend log, the page's console and errors, and CSP violations, in time order. `--source` is `backend`, `page` or `csp`; `--level` is `debug`, `info` (default), `warn` or `error`. |
| `dotnet spar version` | The run, the build (variant, git revision, uncommitted changes, contracts' fingerprint) and the host. |

`--json` prints the app's answer as JSON instead of text, on every command.

### Targets

A target is a reference from the **last** `spar tree`, such as `e12`, or one locator:

- `--role button --name "Save"` (`--name` only with `--role`);
- `--text "Save"`, `--label "Title"`, `--placeholder "Search"`, `--alt "Logo"`, `--title "Close"`;
- `--testid save-button` (the element's `data-testid`);
- `--css ".toolbar button"`.

Names and text match as a case-insensitive part unless you add `--exact`. When a locator matches several elements the command refuses with `ambiguous` and lists them; add `--nth 0` for the first. A reference is valid only for the page's current document and run: after a navigation, a page reload or `spar reload`, take `spar tree` again, or you get `stale_target`. Prefer locators for steps you repeat across reloads.

```sh
dotnet spar tree
dotnet spar click --role button --name "New page" --nth 0
dotnet spar fill --label Title "Ideas"
dotnet spar click --role button --name "Create page"
dotnet spar calls --limit 5
```

### Waiting and time limits

`click`, `fill` and `wait` wait 5 s for their element, `call` 30 s for its command; `--timeout <ms>` changes it. A timeout means unfinished, not failed: the command may still be running. Check `dotnet spar calls` before calling it again, and never repeat a command blindly.

### Exit codes and refusals

Exit code 0 when the command did what it said, 2 when the app refused it or no Development run is listening, 64 for a wrong command line. A refusal names its reason:

| Reason | What to do |
|---|---|
| `not_found` | No such command or tool; check `spar contracts`. |
| `ambiguous` | Narrow the locator, or add `--nth`. |
| `stale_target` | Take `spar tree` again. |
| `timeout` | Unfinished; look at `spar calls` or wait longer. |
| `no_page` | The window has no page yet or is closing; wait and retry. |
| `unavailable` | Page tools under Loopback; use the window profile. |
| `invalid` | Fix the request (for example `--since yesterday`). |

### Per system

- **Windows:** clicks and typing are the browser's own trusted input.
- **Linux:** they are synthetic events the page dispatches to itself, so `event.isTrusted` is `false`; `spar click` says so in its answer.
- **Loopback profile:** the page is in a browser, so `tree`, `screenshot`, `click`, `fill` and `wait` refuse with `unavailable`; `contracts`, `call`, `calls`, `logs` and `version` work.

## spar snap: capture a scene

A scene is a state of the app worth looking at, written once in C# in the app's **end-to-end test project** (see `spar-testing`). This one is from the Notebook sample:

<!-- source: samples/Notebook.EndToEndTests/Window/NotebookScenes.cs -->
```csharp
[SparScene("new-page-dialog")]
public static async Task NewPageDialog(SparScene scene)
{
    var app = await scene.StartAsync<Program>();
    await app.Page.GetByRole(SparRole.Button, new() { Name = "New page" }).First.ClickAsync();
    var dialog = app.Page.GetByRole(SparRole.Dialog, new() { Name = "New page" });
    await dialog.GetByLabel("Title").FillAsync("Groceries");
}
```

```sh
dotnet spar snap --list
dotnet spar snap new-page-dialog
dotnet spar snap --all
```

Run it from the app's folder (it finds the end-to-end project beside it) or from the end-to-end project's. It builds that project first (`--no-build` uses its last Debug build), runs each scene in a window nobody sees, and writes `obj/spar-snap/<name>/` in the end-to-end project (`--out <folder>` elsewhere):

| File | Holds |
|---|---|
| `page.png` | The page at 1280 × 800, light theme, fixed clock. |
| `tree.txt`, `tree.json` | The accessibility tree, as `spar tree` shows it. |
| `calls.json` | Every command the page called, in order. |
| `problems.txt` | Spar's warnings and errors, the page's errors and console messages; empty when nothing went wrong. |

On one machine a scene gives the same `page.png`, byte for byte, every run; compare captures only from the same machine. Exit code 0 when every scene was written, 2 when a scene or the build failed, 64 for a wrong command line or an unknown scene. `spar snap` does not need `spar dev`. Every scene is also a window test that `dotnet test` runs.

## spar doctor: check the machine and the project

```sh
dotnet spar doctor
```

It checks the .NET SDK against `global.json`, Node.js and npm, the WebView2 Runtime and NativeAOT tools (Windows) or GTK 4, WebKitGTK 6.0 and the native libraries (Linux), the frontend's wiring, the contract snapshot, the launch profiles, and the build identity of the newest build. Each finding has a stable id (such as `dotnet-sdk`, `webview2-runtime`, `linux-webkitgtk`, `frontend-node-modules`, `contract-snapshot`, `launch-profiles`, `build-identity-dirty`), a severity and its fix.

- `--json` writes every finding with `id`, `area`, `severity`, `message` and `fix`, plus `errors`, `warnings` and `exitCode`.
- `--verbose` lists every check, not only what needs attention.
- `--app <file>` reads the build identity of another executable, such as a published one, without running it.

Exit codes: 0 nothing to fix, 1 warnings, 2 errors, 64 a wrong command line, 70 the doctor itself failed. Run it first when a build or a start fails for a reason that is not in your code.

## spar init frontend: wire a Vite frontend

An app from the template has its frontend wired already; there the command says so and changes nothing. In an app without one:

```sh
dotnet spar init frontend --framework react
```

It adds the `@spar/app` dependency and Spar's Vite plugin (and `SparFrontendPath` when the folder is not `frontend/`; `--frontend <folder>` names it), then runs `npm install --ignore-scripts`. It shows every change as a diff and asks first; without a terminal it writes nothing unless given `--yes`. Run it once without `--yes` to see the diff, then with it. Exit codes: 0 written or nothing to do, 1 nothing written, 2 a step failed, 3 written except Vite's configuration (the lines to add were printed), 64 a wrong command line. Build the app after it and commit the new lockfile.

## The recommended loop

1. Start the supervisor in the background, privately if someone is at the computer:

   ```sh
   dotnet spar dev --private
   ```

2. See the page: `dotnet spar tree` for its structure, `dotnet spar screenshot` for its look.
3. Change the code (C#, contracts or the page).
4. Rebuild and restart: `dotnet spar reload`. On exit code 2, fix the errors at the file and line it printed and reload again.
5. Drive the change: `dotnet spar tree` again (the old references are stale), then `click`, `fill`, `wait`.
6. Check what happened: `dotnet spar calls --limit 10` for the commands and their outcomes, `dotnet spar logs --since 1m --level warn` for problems and CSP violations.
7. Confirm a layout on a scene: `dotnet spar snap <name>`, then look at `page.png` and `problems.txt`.
8. Finish with `dotnet spar dev --stop`.

## With the spar MCP server

`dotnet spar mcp` serves the same commands as MCP tools over stdio: `tree`, `screenshot`, `click`, `fill`, `wait`, `contracts`, `call`, `calls`, `logs`, `version`, `reload`, `snap` and `doctor`. Configure your MCP client to run `dotnet spar mcp` in the app's folder. Each tool runs exactly the CLI command, so everything above holds: the same targets, refusals and time limits.

- **Start `spar dev` yourself,** from a shell, in the background (MCP has no long-running command); the devtools and `reload` need it.
- **Arguments** mirror the options: a target is an object such as `{"target":{"role":"button","name":"Save"}}` or `{"target":{"ref":"e12"}}` (also `text`, `label`, `placeholder`, `alt`, `title`, `testId`, `css`, `exact`, `nth`); `fill` takes `text`, `wait` takes `state`, `call` takes `command` and `request` (`{"command":"pages.get","request":{"id":"a"}}`), `calls` takes `limit` and `since`, `logs` takes `since`, `source`, `level` and `limit`, `snap` takes `scenes`, `all`, `list` and `noBuild`, `doctor` takes `verbose`. Every tool takes `project`, and the devtools also `timeout`.
- **Results:** the command's text output; `screenshot` also returns the PNG as an image; an exit code other than 0 or 1 marks the result as an error.
