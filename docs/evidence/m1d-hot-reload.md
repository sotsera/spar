# M1d step 3: `dotnet watch` and Hot Reload (evidence)

Recorded 2026-11-22. Plan M1d, build step 3; decisions "M1d step 3's shape: dotnet watch and Hot Reload" (user
decisions, 2026-11-22) and the implementation record "dotnet watch and Hot Reload (implementation record; choices for
review)". Design: `docs/design/dev-loop.md`, section 4, "Hot Reload signal" and "`dotnet watch`".

## Setup

- Windows 11 Pro 10.0.26320, SDK 11.0.100 (`dotnet watch` 11.0.100, its Hot Reload agent from the SDK's
  `DotnetTools/dotnet-watch/11.0.100`), Node 24.21.0, Vite 8.3.8, Playwright's headless Chromium (build 1248, the copy the
  repository's browser tests use).
- Linux: WSL2 Ubuntu-24.04 (kernel 6.18.58.1), the same SDK and Playwright copy; `DISPLAY` and `WAYLAND_DISPLAY`
  unset (nothing on WSLg's display), the core limit 1 byte. The tree is the copy `docs/evidence/m1c-blackbox/wsl.sh sync
  client build` keeps in `/var/tmp/spar-m1c-blackbox/repo` (written `<copy>`); every edit of a Linux run is made in that
  copy, never in the working tree.
- The app: the Notebook's Debug build under `dotnet watch`, its page from Vite through the app's origin (step 1). Its own
  page now calls `onBackendUpdate(() => refresh())`, so a backend update fetches the list again.
- **Data roots:** every run sets them absolute: `LOCALAPPDATA` under `artifacts/test-host/m1d-hot-reload/<run>/` (loopback)
  or `artifacts/webview2-profiles/m1d-hot-reload/<run>/` (windows, so the WebView2 profile is there); on Linux `HOME` and
  every XDG folder under `<copy>/artifacts/linux-homes/m1d-hot-reload/<run>/`. A census of the user's `%LOCALAPPDATA%`
  before and after found no new `com.example.Notebook*` entry (the one there dates from step 2's run, 2026-11-21 23:33).
- **Runners:**
  - [`watch-run.mjs`](m1d-hot-reload/watch-run.mjs) (both OSes): `dotnet watch --non-interactive --verbose -lp Loopback`,
    `SPAR_LOOPBACK_BROWSER=none` (no browser of the user's opens), the page in Playwright's headless Chromium, one page
    "Welcome" written into the data folder; then the edits below, each written into the tree, checked to have changed the
    file, and restored. A restart's next edit waits until the reloaded page kept its backend 5 s (below, the loop guard).
  - [`WatchDesktop.cs`](m1d-hot-reload/WatchDesktop.cs) (Windows): `dotnet watch -lp Desktop` started on a desktop of the
    run's own (`CreateDesktop`; dotnet watch, the app, WebView2 and Vite inherit it), so no window reached the monitor,
    took the focus or got input. The page's side is read through a probe the run adds to `main.tsx`: 50 ms after each
    update it requests `/__spar-probe/backend-updated/<revision>/toasts-<n>/…`, whose path the proxy's Debug line in
    `app.log` carries, `n` being the toasts the overlay shows. With `--exe` it starts the Debug apphost instead, as an
    IDE's F5 does, and `suspend:<ms>` suspends the app's process as a debugger's break does.
  - [`wsl.sh`](m1d-hot-reload/wsl.sh) (Linux): `watch` (`watch-run.mjs`), `desktop` (`dotnet watch -lp Desktop` on a
    private Xvfb with a private D-Bus session, the same probe), and `interactive` / `interactivenoshape` (`dotnet watch`
    as a terminal runs it, interactive, in a pseudo-terminal whose input nobody writes).
- Raw logs in [`m1d-hot-reload/logs/`](m1d-hot-reload/logs/): the repository's path written `<repo>`, the Linux copy's
  `<copy>`, the user's folders and name `<user>`, the loopback tokens `…`. Each line is the runner's clock (ms), then
  `run:` (the runner), `watch:` (dotnet watch's output, the app's standard output in it) or `app:` (`app.log`).

## Results

### The Hot Reload signal

| Check | Windows | Linux (WSL) |
|---|---|---|
| A method body (`MarkdownPageStore.ListAsync`'s summary title) is applied by Hot Reload | pass, loopback and window | pass, loopback and window |
| The host sends `backend-updated` with the revision and the edited type (`Notebook.Pages.MarkdownPageStore`), logged `Hot Reload applied revision N (…); sent to …` | pass: revisions 1–4 in a run, to the current document | pass |
| The page's `onBackendUpdate` handler runs: the Notebook fetches the list again and shows the patched code's title "Welcome (edited)", then "Welcome" after the restore | pass (loopback, Chromium) | pass (loopback, Chromium) |
| The toast "Backend updated: MarkdownPageStore (revision N)" shows | pass in Chromium (loopback) and in the WebView2 window (probe: `toasts-1`, then `toasts-2` with the first still shown) | pass in Chromium (loopback) and in the WebKitGTK window (probe: `toasts-1`) |
| `configureSpar({ backendUpdateToast: false })` in the page's `spar.ts`: the handler still runs, no toast within 3 s | pass (Chromium; window probe `toasts-0` for revisions 3 and 4) | pass (Chromium) |
| CSP or Trusted Types messages in the page's console | 0 | 0 |

**Times, from the file's write** (loopback: the runner's clock; window: the write's and the log line's UTC times):

| | Windows loopback | Windows window | Linux loopback | Linux window |
|---|---|---|---|---|
| First edit after the start, to `dotnet watch`'s "changes applied" | 1.76 s | — | 2.69 s | — |
| First edit, to the host's line / the toast | 1.75 s / 1.79 s | 0.73 s / 0.78 s (probe) | 2.65 s / 2.72 s | 0.83–0.94 s (host's line, polled 50 ms) |
| Later edits, to the host's line | 85–110 ms | 78–150 ms | 67–174 ms | — |
| Later edits, to the toast or the list's refetch | 87–117 ms | 56–60 ms after the host's line (probe, incl. its 50 ms) | 93–335 ms | — |

The plan's budget, "Hot Reload of a method body applied ≤ 4 s", is met: 0.07–2.7 s, the first edit of a session the
slowest (`dotnet watch` loads its workspace then). A screenshot of the toast in headless Chromium (Linux, light scheme):
[`toast-linux.png`](m1d-hot-reload/logs/toast-linux.png).

### Contract edits restart

Each edit, then its restore (so two restarts each):

| Edit | Windows loopback | Linux loopback |
|---|---|---|
| `dto`: `PageSummary` gains `string? Excerpt = null` (and the page tests' summaries `excerpt: null`) | restart; the client's `pages.d.ts` gains `readonly excerpt: string \| null`; the page reloads by itself and works | the same |
| `error`: `PageError.TitleTaken`'s `Title` made nullable | restart; `readonly title: string \| null` | the same |
| `command`: `PagesService` gains `CountAsync` | restart; `readonly count: (…)` | the same |
| `limit`: `[SparCommand(TimeoutSeconds = 30)]` on `SaveAsync` (no client change) | restart | the same |
| Every one of these 8 restarts | `dotnet watch`: `error ENC0020: Renaming enum value 'Shape_…' requires restarting the application` (`Spar.ContractShape.g.cs`) | the same |
| Write to "Restart is needed" | 43–92 ms | 31–78 ms |
| Write to the new run listening | 9.1–10.9 s (one 14.6 s) | 3.1–4.8 s |
| Write to the page reloaded by itself | 10.2–12.3 s (one 15.6 s) | 3.9–5.0 s |

In the window: on Windows a DTO edit and its restore each restarted the app, its window back at `ui-ready` 10.9 and 10.8 s
after the write, the client regenerated (`readonly excerpt: string | null`, then gone); on Linux the DTO edit's restarted window reached `ui-ready` 6.8–8.3 s after the write in three runs (polled 100 ms).

**Where a Windows restart's time goes** (`--verbose`): "Terminating process … (Ctrl+C)", "Waiting for process … to exit
within 5s", "Terminating process … (Kill)", exit code -1, then about 3.5 s of build and 1.2 s to `ui-ready`. The
Notebook is `WinExe`: it has no console, so the Ctrl+C `dotnet watch` sends to `dotnet run`'s console never reaches it,
and the kill ends the whole tree, the Vite server too, so each run started a new one (0.8 s) and the page's Vite client
logged its lost server once (step 1's `SharedWorker` violation, in the Chromium console as "This document requires
'TrustedScriptURL' assignment"). On Linux `dotnet watch` sends `SIGTERM`, the app logs "SIGTERM received: the app stops."
and exits 0 within about 10 ms.

### Why the shape is needed

- **Without it** (the generator's shape left out for one run, then restored; [`windows-without-shape.run.log`](m1d-hot-reload/logs/windows-without-shape.run.log)):
  the same edits gave only `warning ENC0118: Changing 'auto-property' might not have any effect until the application is
  restarted` (the generated catalog's initializer). `dotnet watch --non-interactive` restarted on them anyway (its
  `[auto-restart]` lines), but **an interactive `dotnet watch` patched the error-case edit**: "C# and Razor changes
  applied in 477ms", and the host logged revision 1 for `Notebook.Pages.PageError`, `Spar.Generated.SparSerializers`,
  `Spar.Generated.SparContracts` and `PageError+TitleTaken`: the serializer changed in the running app while the page's
  generated client and the catalog's fingerprint did not ([`interactivenoshape.out.log`](m1d-hot-reload/logs/interactivenoshape.out.log)).
- **With it**, the same interactive session reported `ENC0020` and asked "Do you want to restart your app? Yes (y) / No (n)
  / Always (a) / Never (v)", applying nothing ([`interactive.out.log`](m1d-hot-reload/logs/interactive.out.log)).
- **Step 2's three edits:** the recorded run's last edit wrote the file back unchanged (its harness replaced
  `</SparAppId>`, a project-file anchor, in `MarkdownPageStore.cs`), so "No managed code changes to apply" was right; a
  new optional parameter on a private method is an edit Hot Reload supports. The static-to-instance edit, run again here
  (`instance`, Windows loopback): `error ENC0004: Updating the modifiers of method requires restarting the application`,
  and `dotnet watch` restarted.

### Nothing of it in Production

- **The executable:** the Notebook published in Release, 5,244,928 B (step 2 recorded 5,244,416 B; `HEAD` changed only
  `Sdk.targets` since), has none of `BackendUpdates`, `SparContractShape`, `MetadataUpdateHandler` (UTF-8) or
  `backend-updated`, `Hot Reload applied revision` (UTF-16); the build tests' boundary test checks them, with controls in
  `Spar.dll` and `Spar.Loopback.dll`, on Windows and in WSL.
- **The page:** its Production bundle has none of `backend-updated`, `data-spar-dev-overlay`, `Backend updated`,
  `onBackendUpdate`; the generated package (the control) has all three markers.
- **The guard folds with or without `?.`:** a scratch Vite 8.3.8 build of `if (import.meta.env.DEV)`,
  `import.meta.env?.DEV`, `import.meta.env?.DEV === true`, `typeof import.meta.env === "object" && …` and
  `import.meta.env !== undefined && …` dropped every branch. The SDK uses `import.meta.env?.DEV === true`: with
  `import.meta.env.DEV`, WireShapes' test page, which loads the SDK without Vite, threw at `connectSpar` and 15 of its
  17 browser tests failed.

### F5 without an IDE

- **No command-line managed debugger was used:** none is installed (no `netcoredbg`, no Debugging Tools), and the
  debugger VS Code's C# extension ships (`vsdbg`) is licensed for use with Visual Studio and VS Code only. The F5 runs
  themselves are rows of the plan's "Checks that need the user at the PC", with the steps below.
- **The profiles as an IDE starts them** (`WatchDesktop.cs --exe`): the Debug apphost
  `bin/Development/Debug/net11.0/win-x64/Notebook.exe` started directly, not through `dotnet run`, with
  `DOTNET_MODIFIABLE_ASSEMBLIES=debug` as Visual Studio sets for Hot Reload: `Desktop` with the project folder as working
  directory reached `ui-ready` in 1.05 s; `Loopback` (`SPAR_HOST=Loopback`) with the output folder as working directory
  listened in 68 ms and its development server was ready in 0.67 s. The paths come from the runtime configuration, so the
  working directory does not matter.
- **A debugger's break:** the window's whole process suspended 30 s (`NtSuspendProcess`, as a debugger stops every
  thread), resumed, and watched 25 s more: no hung-page dialog, no warning in `app.log` (the heartbeat counts unanswered
  ticks, and a suspended process ticks none).

### Guided steps for the user's F5 checks

Both rows are in the plan's table. Visual Studio 18 (Community, installed) and VS Code with C# Dev Kit 3.40 and the C#
extension 2.160 (installed):
1. Open `Spar.slnx` (VS Code: the folder), make Notebook the startup project, pick the `Desktop` profile, F5: the build
   runs Spar's steps under the IDE's MSBuild and the window shows the notebook.
2. A breakpoint in `PagesService.ListAsync` hits when the page loads its list (F5 in the window reloads it); wait 30 s,
   continue: no "not responding" dialog.
3. In `MarkdownPageStore.ListAsync`, add `+ " (edited)"` after `page.Title`, save, apply Hot Reload: the toast
   "Backend updated: MarkdownPageStore (revision 1)" shows and the list shows the new title.
4. In `PageTypes.cs` add `string? Excerpt = null` to `PageSummary` (and `excerpt: null` to the two summaries in
   `frontend/src/notebook.test.tsx`): the IDE reports ENC0020 and applies nothing; restart, and the window comes back.
5. The `Loopback` profile: the address is printed; open it in a browser; stop debugging: the app exits.
Undo the edits after.

## Tests

- `tests/Spar.Tests/BackendUpdatesTests.cs`: the update as the page receives it (a state machine and a lambda's closure
  named by their type, once), the payload, every listener reached with the next revision until disposed, the event a
  session sends, and the runtime's call ignored without the switches.
- `tests/Spar.Generator.Tests/ContractShapeTests.cs`: the shape only in Development; four edits that keep it (a body, a
  helper, a new private method, a comment) and fourteen that change it (DTO properties added, retyped, made nullable,
  renamed; an error case added, its payload, its code; a command's name, limit, request, cancellation, a command added;
  the contract's name; the page security in `Program.cs`).
- `src/Spar.Client/test/backendUpdate.test.ts`: the handlers, their order and removal, a throwing handler, unreadable
  payloads, the payload read field by field (a `__proto__` key included), the toast's text, dismissal, 8 s, three at
  most, no `style` attribute or `<style>` element, the opt-out and its reset, `fakeSpar().backendUpdate`, and the
  production path (`vi.stubEnv("DEV", false)`).
- `samples/Notebook/frontend/src/notebook.test.tsx`: a backend update fetches the list again.
- `tests/Spar.BuildTests`: the Production boundary's new markers (`BackendUpdates`, `SparContractShape`,
  `backend-updated`, `Hot Reload applied revision`) and page markers (`backend-updated`, `data-spar-dev-overlay`,
  `Backend updated`), with controls in `Spar.dll`, `Spar.Loopback.dll` and the generated package.

## Follow-up: the parent's console and the reload guard

Recorded later on 2026-11-22, on top of `e03a935`; decision "Step 3's two follow-ups" (user decisions) and the
implementation record "Step 3's follow-ups: the parent's console and the reload guard". The "before" numbers are the
runs above, made with the same runners on the committed step.

### Windows: `dotnet watch`'s Ctrl+C now closes the app

| | Before (step 3) | After |
|---|---|---|
| Window, DTO edit to the restarted `ui-ready` | 10.8–10.9 s | 5.4–6.0 s ([`windows-desktop-console.run.log`](m1d-hot-reload/logs/windows-desktop-console.run.log)) |
| Window, `dotnet watch`'s Ctrl+C to the app's exit | 5 s wait, then killed (exit -1) | 77 and 71 ms, exit 0, through "close requested (User)" |
| Loopback, contract edit to the new run listening (8 restarts) | 9.1–14.6 s | 4.3–5.4 s ([`windows-loopback-console.run.log`](m1d-hot-reload/logs/windows-loopback-console.run.log)) |
| Loopback, contract edit to the page reloaded by itself | 10.2–15.6 s | 5.0–6.5 s |
| The development server across restarts | killed with the tree each time, a new one each run (about 0.8 s) | reused by every run (one Vite pid for the whole session, checked in 23–34 ms) |
| The page's console when the backend restarts | Vite's lost-server `TrustedScriptURL` violation once per restart | none |

- **What runs:** each Development run logs "Attached to the parent's console (Development): its Ctrl+C closes the app as
  its window's close does."; on the Ctrl+C the window logs "Ctrl+C from the parent's console: the app closes." and its
  close request (`User`), the loopback run "SIGINT received: the app stops.", and `dotnet run` exits 0.
- **Without `dotnet watch`** (`WatchDesktop.cs --exe` with its `ctrlc` step, a Ctrl+C to the console the app attached
  to, as a terminal sends): the window closed 39 ms after it, exit 0; the loopback run stopped 7 ms after it, exit 0
  ([`windows-ctrlc-desktop.run.log`](m1d-hot-reload/logs/windows-ctrlc-desktop.run.log),
  [`windows-ctrlc-loopback.run.log`](m1d-hot-reload/logs/windows-ctrlc-loopback.run.log)).
- **The loopback run's first try failed:** with the attach placed just before its own `SIGINT` registration, the handler
  never ran and the Ctrl+C ended the process (exit `0xC000013A`, `STATUS_CONTROL_C_EXIT`) in all 8 restarts (that log was replaced by the run above). Moving the
  attach to the start of the run, before anything registers a signal handler, fixed it. The window host's handler is
  the process's first, and worked from the start (pitfalls).
- **Without a parent console** (`--free-console true`: the run let go of its own console before starting the apphost, as
  an IDE's F5 or Explorer has none): no attach line, `ui-ready` in 1.2 s, nothing else changed
  ([`windows-f5-no-console.run.log`](m1d-hot-reload/logs/windows-f5-no-console.run.log)). A process with a console of
  its own (a test process) never attaches: `BackendUpdatesTests.A_process_with_its_own_console_is_left_as_it_is`.
- **No console window:** attaching joins the parent's console; it creates none. Under `dotnet watch` that console is
  `dotnet watch`'s (here without a window: the runner starts it with `CREATE_NO_WINDOW` on a desktop of its own); in a
  terminal, the terminal's.
- **Standard output and input:** `dotnet run` hands its standard handles down to the app, so they are valid before the
  attach, and `AttachConsole` leaves valid handles as they are: the app's lines went on reaching `dotnet watch`'s output
  in every run. The development server's input (step 1's choice 6) is the app's own when it is a pipe, otherwise a pipe
  of its own, as before: the attach does not change which. Under `dotnet watch` the server outlived every app run and
  ended with the `dotnet watch` session (it was gone when the runner ended that session's tree), consistent with its
  input being the session's pipe (inferred; its own log was overwritten by the next run's server). The server runs in a
  hidden console of its own (`CreateNoWindow`), so the Ctrl+C sent to `dotnet run`'s console never reaches it.

### Linux: quick restarts no longer stop the page's reloads

`wsl.sh watch` with `EDITS=dto,error,command,limit SETTLE=300` (the next write 0.3 s after each reload, where step 3's
runs had to wait 5.5 s): 8 contract restarts, the page reloaded by itself after every one (3.9–8.2 s after the write;
the new run 3.4–7.1 s), and no "keeps stopping" or "stopped before the page could connect" line
([`linux-loopback-fast.run.log`](m1d-hot-reload/logs/linux-loopback-fast.run.log)). Before the follow-up, the same
quick edits stopped the reloads at the third restart. Windows' loopback run above also wrote 0.3 s after each reload.

A backend that keeps failing before the page connects is checked by `src/Spar.Client/test/restart.test.ts` only (the
third reload in a row into a document that got no `open` stops the reloads): no real backend was made to fail that way.

## Not verified

- F5 in Visual Studio and VS Code (the user's table); a managed debugger attached from the command line.
- Interactive `dotnet watch` on Windows (run only in WSL's pseudo-terminal), and its "Always" answer.
- A contract library's edit (`SparContracts`) under `dotnet watch`: the app's shape is computed from it, but no run edited one.
- The toast's look on Windows (one Linux screenshot only).
- A real terminal's Ctrl+C (the runner's `ctrlc` step sends the same event to a console without a window), and closing
  the terminal (`CTRL_CLOSE_EVENT`, not handled: Windows ends the attached app after its timeout).
- A backend that keeps failing before the page connects, in a real browser (unit tests only).
- An app whose own `Program.cs` registers a signal handler before `app.Run()`: by the pitfall above, the console's Ctrl+C
  may then not reach Spar's handler.
