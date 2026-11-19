# Spar v7 plan

Revised 2026-11-13 (second pass the same day, after the M0 spikes and the design discussions), corrected 2026-11-14 after a review of the spikes, and reviewed against the decision log on 2026-11-15, when M1 was split into four slices (decision "Plan and guidance review; M1 split"). Later the same day the test strategy was decided, the design sections moved to `docs/design/`, and budgets, a definition of done and a risk list were added (decisions "Test strategy: the app runs inside the test process" and "Plan split, additions and the M1a scope"). It replaces the first draft of 2026-11-12, which this repository's history does not hold. Evidence and reasons are in `docs/decisions.md`; surprising behaviours are in `docs/pitfalls.md`.

## Goal

Spar is a C# framework for desktop apps with a web frontend: WebView2 on Windows and WebKitGTK on Linux. The backend is C#, published by default as one NativeAOT executable on .NET 11. C# contracts generate the TypeScript client, so the frontend calls the backend through typed functions.

Spar is built for agents first: **an agent should be able to make a change, verify it, and know which build it is looking at.** People use the same tools.

The first real consumer is a SQL Server tool (monitor instances, compare databases, copy data with saved recipes). It is built as a separate app that uses Spar only through its packages.

## Inputs to this revision

- **M0 spikes** (`spikes/`, moving to `docs/evidence/`):
  - the compile pipeline (D1), MSBuild tasks, and IDE behaviour;
  - D8 automation, loopback, the React app, the desktop host;
  - serializers (benchmark and fuzzing), packaging, test frameworks, Aspire;
  - SqlClient (AOT, tool features, Linux), native services, CsWinRT, `.esproj`;
  - bridge streaming and push, contract syntax, TypeScript results and compiled contracts;
  - the Linux host, asset packing, OpenTelemetry.
- **The earlier v7 attempt** (`old/spar.v7`):
  - agent-first framing, and a settled-decisions table;
  - the three call outcomes, and "a request belongs to a document";
  - a test level that runs in a browser without the desktop host;
  - file-size limits, analyzer settings, and portable skills.
- **The WebView2 investigation runbook:** host milestones, a readiness message, the teardown checklist, failure recovery, and how to choose a diagnostic tool. The runbook is not in this repository. What the plan took from it is written out here (section 3 "Teardown" and "Failure recovery", section 5 "Required host tests", M4 "Performance budgets"), and this plan is the record (user, 2026-11-15).
- **External research:** .NET 11 MSBuild, the .NET skills catalogue and its evaluation format, Aspire 13, CsWinRT 3.0, and agent tooling in other UI frameworks.

## Settled decisions

Details and evidence in `docs/decisions.md`.

| Decision | Reason |
|---|---|
| Two desktop hosts from the start: Windows (WebView2) and Linux (WebKitGTK). The core is host-neutral; each host supplies its channel, transport, origin checks, automation and native services. | User decision. The Linux spike worked on both WebKitGTK APIs, and the SQL tool's data layer matched Windows on Linux. |
| NativeAOT by default. Per app, the author may publish on CoreCLR or accept their own AOT warnings. Spar's own code stays warning-free. Never fall back silently. | User decision. SqlClient is not AOT-clean (49–53 ILC diagnostics) but works with them accepted. |
| .NET 11 (SDK 11.0.100), C# 15. `InvariantGlobalization=true` by default, the NativeAOT default; an app turns it off in its own project file (+260,096 B on the minimal baseline app of `spikes/sqlclient-aot`). SqlClient needs it off, so the SQL Server tool turns it off; Spar itself never does (user, 2026-11-14). | Current release; `closed` hierarchies; measured cost. |
| Plain `dotnet build`, `run`, `test` and `publish`, plus IDE builds, work without Spar tooling. The CLI build is the source of truth after external edits. | Agents and people use tools they know; neither IDE regenerates on external edits. |
| A source generator emits all contract C#: dispatch, catalog and serializers. A pre-compile step emits only the generated client. They share one analysis library, and a `DiagnosticAnalyzer` reports located diagnostics. | D1 and the IDE checks: live diagnostics with squiggles, no build needed first. |
| Wire serialization: generated `Utf8JsonReader`/`Utf8JsonWriter` code, with the fuzzing requirements (R0–R4). | Faster to read, 47% smaller than System.Text.Json metadata; at least as fast to write once tuned. |
| Spar build steps are compiled, thread-safe MSBuild tasks (`Runtime="NET"`). Checked by `spikes/msbuild-task-analyzer` (SDK 11.0.100 = MSBuild 18.12.2): the task project references `Microsoft.Build.TaskAuthoring.Analyzer` 18.13.0 (`PrivateAssets="all"`; it runs, about 35 ms, no build-time difference) with Framework and Utilities.Core 18.12.2 as compile-only references. Tasks take the full thread-safe shape now: `[MSBuildMultiThreadableTask]`, `IMultiThreadableTask`, and a `TaskEnvironment` constructor. Under `-mt` only tasks with the attribute stay in-process (32 projects: 255–262 ms against 474–514 ms with `-m:8`); others move to TaskHost processes. The analyzer misses static mutable state, so that stays a review and test rule; its code fixes are not shipped. Its option and four severity raises (MSBuildTask0001, 0009, 0011 and 0013 to warning) are agreed and in the root `.editorconfig` (user, 2026-11-14). `TaskHostFactory` is used only in Spar's own checkout. Variant builds run in-proc through the `MSBuild` task, with per-variant `obj`/`bin`, `RemoveProperties`, and a cross-process lock. | Measured: 7–30 ms warm steps; in-proc variants 1.6–2.2 s faster than `Exec` per call. |
| One `SparVariant` property selects the build variant: `Production`, `Development` or `Test`. Debug builds `Development`, Release builds `Production`, and a test project builds its referenced app as `Test` (the test-host hook, page automation, the UI-thread stall probe) whatever its configuration; `SparVariant` can still be set explicitly. `Test` replaces `DesktopTest` and the spikes' `ManagedTest`, and `Editor` is dropped (decisions "Test strategy: the app runs inside the test process" and "Polished sample apps: a Markdown notebook; the test variant"). In this plan "Debug" means the `Development` variant and "Release" the `Production` variant. | The earlier mode-flag matrix was the main source of fragility. |
| A call has three outcomes: success, a declared typed error, or **outcome unknown**. Cancellation is reported only when the backend confirms it. No automatic retries; subscriptions resume only when their contract allows it. | A lost reply must never be read as "didn't happen". |
| Results are opaque in TypeScript: `await cmd(req).match({ success, errors, failure? })` or `unwrap()`. `failure` is a function (every failure) or an object by `CallError` kind (only those kinds). Other failures go to the app-wide interceptor `onFailure`, and `match` returns `undefined` for them. `unwrap` throws and never reaches the interceptor. `observe` sees every failure. Neither hook can retry or change a result (2026-11-14). | User decision; checked with `tsc` and both language servers. |
| One DI scope per command, from Microsoft DI (`Microsoft.Extensions.DependencyInjection`; user decision 2026-11-14, +0.3–0.9 MB in AOT). Every request belongs to one document, with sender checks. On navigation or close, stop admitting work, cancel, and drop late replies. | Proven in every spike. |
| The desktop origin is `https://app.localhost` on Windows and `spar://app/` on Linux, the same for every app, and never changes between versions. | User decision. `.local` added about 2 s per load after a network change; `app.localhost` loaded in 54 ms and needs no name resolution. The profile is already per app, so one name for every app mixes no data. |
| Tests run the app's own `Program.cs` inside the test process, with service overrides as ordinary DI code and one of three surfaces in front of the backend: none, a browser over loopback, or the app's real window. A launched executable is used only for the Production binary as a black box and for behaviour across processes (section 5). | User decision (2026-11-15). `spikes/test-host`: 0.2–0.6 ms command-level starts; real windows in the test process on both OSes under CoreCLR and NativeAOT, with nothing leaked. |
| Isolated data and WebView profiles per test run, under `artifacts/`. Wait for the browser to exit before reusing or deleting a profile. | Repeatable tests; the browser sometimes takes about 6.5 s to exit. |

## Open questions

| Question | State, and where it is answered |
|---|---|
| Bridge transport per host. | Leaning, and the working design of section 2c: `postMessage` for everything on Windows; the script message handler plus `evaluate_javascript` on Linux; `fetch` unary plus one channel per document on loopback. Confirmed on the real hosts in M1b, M1c and M1d. |
| The remaining test points: trusted input in the in-process window, profiles for window tests (a shared environment with InPrivate controllers, or a folder per test), streams and cancellation through the typed test client, the black-box data root, where page tests run, and issues D to K. | The strategy is decided (section 5). Each remaining point is decided with the user when its slice reaches it (section 5, "Open issues"), with a spike where needed. Includes the performance regression suites (user, 2026-11-14): BenchmarkDotNet for runtime hot paths and a build-timing suite against stored baselines; which run in CI, and how noise is handled, is issue K. |
| API design for native features (tray, custom title bar, close to tray, placement, file open and save, folder picker, drag and drop, clipboard, print and print to PDF, keyboard shortcuts, single instance, resource usage; notifications later): as easy and pleasant as possible in C# and TypeScript. | User requirement (2026-11-14). Principles and first ideas in section 3, "Native services". Designed with the user one feature at a time, each with a compiled example in `docs/app-model.md` before implementation. |
| Agent devtools scope (Linux has no CDP), and CLI against MCP. | M2 evaluation. |
| CI: GitHub Actions (user, 2026-11-15; decision "CI on GitHub Actions; the first slice"). The workflow is in `.github/workflows/ci.yml` and first runs at the first push. Open: pinning actions to commit SHAs before that push, and whether the window tests (M1b) run on the hosted Windows runner or need a self-hosted one with a desktop session; Linux window tests use the container image from M1c. | First push; M1b and M1c. |
| arm64: `win-arm64` and `linux-arm64` are mapped by `SparPlatforms`, but no spike ran on arm64. | Decide in M4 whether arm64 is qualified or documented as untested. |
| The two-stage close: a close request with page and backend guards before teardown, and an automated mode that skips prompts for tests and agents. The dialog and window decisions already rely on it. | Adopted from the research review (user, 2026-11-15). Designed with the user at the start of M1b, with a compiled example in `docs/app-model.md` first. |
| Where Spar's packages are published, with what version scheme and release steps. The SQL Server tool lives in its own repository and uses Spar through packages only; M1a's package-only consumer test uses an isolated local feed. | Needed by M3. Not discussed yet. |
| The minimum Windows version. The README names Windows 11, screen-capture protection mentions Windows 10 2004, and no spike ran on Windows 10. | Decided provisionally (user, 2026-11-17): Windows 10 22H2, build 19045; older gets the start-up message and exit 7, and Windows 11 features are detected at run time. Built in M1b step 2 (decision "Start-up: prerequisites, single instance and theme"). M4 confirms it on a real Windows 10 22H2. |
| Upgrades and data migration: how an installed app moves to a new version: the version of its settings and data folders and their migration, the web engine profile after an app update, what a downgrade does, and how a failed migration is reported. | Not discussed yet. Needed before the distribution decision in M4; the settings-files decision at the start of M3 is the first to depend on it. |
| C# Hot Reload: applied in 0.9–3.9 s in the spikes; contract changes restart. | M1d. |
| A Spar-owned minimal OTLP exporter. | Parked. |
| Topics from the review of earlier versions and Tauri that are not in a milestone yet: the items marked "Discuss" or "Later" there, and the "Adopt" items for M2 to M4 (devtools rules, child-process handling, settings files, delivery). | `docs/research-review.md`. The items it marks "Adopt (M1)" are in the M1 slices (user, 2026-11-15); the others move into this plan when their milestone starts. |

## Design

The design is in one file per area under `docs/design/` (user, 2026-11-15; decision "Plan split, additions and the M1a scope"). Section numbers are global: "section 3c" or "plan section 3c" in this plan, a design file, a skill or an earlier entry of `docs/decisions.md` means the heading of that number in the file below. Load only the file a task needs.

| Section | Topic | File |
|---|---|---|
| 1 | Build and packaging | [`docs/design/build.md`](design/build.md) |
| 2 | Contracts and the generated client | [`docs/design/contracts.md`](design/contracts.md) |
| 2b | TypeScript SDK (`src/Spar.Client`) | [`docs/design/contracts.md`](design/contracts.md) |
| 2c | Bridge protocol | [`docs/design/bridge.md`](design/bridge.md) |
| 3 | Desktop hosts: Windows (WebView2) and Linux (WebKitGTK) | [`docs/design/hosts.md`](design/hosts.md) |
| 3b | Page security: navigation lock, Content Security Policy and response headers | [`docs/design/security.md`](design/security.md) |
| 3c | Security defaults and overrides | [`docs/design/security.md`](design/security.md) |
| 3d | Light and dark theme without flicker | [`docs/design/theme.md`](design/theme.md) |
| 4 | Development loop | [`docs/design/dev-loop.md`](design/dev-loop.md) |
| 5 | Testing | [`docs/design/testing.md`](design/testing.md) |
| 6 | Agent facilities | [`docs/design/agents.md`](design/agents.md) |
| 6b | Observability: optional OpenTelemetry, backend and client | [`docs/design/agents.md`](design/agents.md) |
| 7 | Repository and guidance | [`docs/design/repository.md`](design/repository.md) |

## Milestones

### Checks that need the user at the PC

Agents run nothing here unattended: each check needs the user present, real hardware, sound, or a change to the user's own settings. **Real input** (mouse, keyboard, drag and drop, focus) is not a hard requirement (user, 2026-11-14): it may be simulated or driven in Windows Sandbox, a Linux container or VM, or WSL, or on the user's desktop through computer use after asking whether they are using the monitor. Rows that need only real input can move out of this table once one of those routes covers them. Run them together when the user is at the computer, record the results in `docs/decisions.md`, and remove the item.

| Check | Why a person is needed | Source |
|---|---|---|
| Narrator (and NVDA if installed) on the page-drawn title bar and a sample page | Speech plays on the speakers; listening is the check | `spikes/windows-sandbox-checks` |
| Title bar at 125 %, 150 % and 200 %, a DPI change while open, and two monitors at different scales | Needs the user's display scaling; Windows Sandbox follows the host monitor | `spikes/windows-titlebar`, `spikes/windows-sandbox-checks` |
| The custom frame's drop shadow and DWM hover visuals on native buttons | Visual judgement on the real desktop; the Sandbox shows no shadow | `spikes/windows-titlebar` |
| Real webcam and microphone on WebView2: the Windows privacy prompt for unpackaged apps, allow, deny, and turning the camera off from the app | A real device and the user's privacy prompt | plan 3b, `spikes/csp-navigation` |
| Real webcam and microphone on WebKitGTK and the camera portal | Same, on Linux | plan 3b |
| A light Windows theme and a real system theme switch (`WM_SETTINGCHANGE`), now on the Windows host: the window colour and the title bar follow the switch, and the page follows through the profile's Auto scheme (built in M1b step 2, not run) | Changes the user's theme | plan 3d, `docs/evidence/m1b-startup.md` |
| Wayland input under WSLg: dragging the page-drawn title bar, double-click, edge resize, on GTK4 | Only real mouse input works on WSLg Wayland | `spikes/linux-integration` |
| Dropping files from Explorer or a Linux file manager onto the page | Real drag and drop | `spikes/linux-integration`, M3 |
| Visual Studio and VS Code: CSP analyzer squiggles as you type (contract diagnostics checked 2026-11-16 on an app from packages, decision "Squiggles in both windows; ReSharper's code analysis hides them in Visual Studio"; the Notebook too, once it named `Spar.Sdk`, decision "The repository's projects name Spar.Sdk"). In VS Code also: completion popups, the lightbulb fixes, rename preview, the TypeScript Native Preview extension, read-only generated packages. In both: go-to-definition from a page's call (`orders.place`) into the C# command, through the generated `.d.ts.map` (checked through `tsserver` only) | Interactive editor UI | decisions "Opaque results in TypeScript", "Declaration maps and the contract snapshot", plan 3b |
| Visual Studio's Dependencies node for a test project that names its app with `SparTestedApplication`: the app listed under Projects, and the reference's properties shown | Interactive IDE UI | decision "SparTestedApplication: the test project names its app" |
| The Notebook sample's look and feel in both themes: layout, type, colours, the dialogs and the conflict banner (screenshots in `docs/evidence/m1a-notebook-page/`) | Visual judgement of polish | decision "Notebook page and the frontend build" |
| Notifications as they appear on screen (Windows toast or balloon, Linux) | Visual, and they reach the user's desktop | research review, M3 |
| On a real Linux desktop, if one is available: GNOME and KDE tray hosts, a real GPU, HiDPI, IME | Hardware and desktops not reachable from WSLg | `spikes/linux-wsl-desktop`, `spikes/linux-integration` |
| The start-up error dialogs (task dialog and message box) at 125 %, 150 % and 200 %, and across monitors with different scales | Needs the user's display scaling; the Sandbox stays at 96 DPI | `spikes/startup-prerequisites` |
| Narrator reading the start-up error dialog | Speech plays on the speakers | `spikes/startup-prerequisites` |
| The start-up dialog's link and download button opening Microsoft's WebView2 page | Needs a connected PC; the Sandbox guest had no network | `spikes/startup-prerequisites` |
| On a real GNOME or KDE desktop: the GTK and kdialog start-up messages, Orca, a critical notification staying until dismissed, the journal entry after a `.desktop` launch | Desktops not reachable from WSL or Docker | `spikes/startup-prerequisites` |
| Task Manager's app-group "Memory" total against the reported app total | Visual comparison on the user's desktop | `spikes/resource-usage` |
| GNOME and KDE System Monitor "Memory" against the Linux app total | Real desktops not reachable from WSL | `spikes/resource-usage` |
| The "Wait / Reload page" and "Wait / Close anyway" dialogs over a hung page on the real desktop, a real click into a hung page, and the unowned "Keep waiting / Exit now" prompt coming to the front after the window closed (built in M1b step 3; pressed only by `TDM_CLICK_BUTTON` so far, `docs/evidence/m1b-hangs.md`) | Visual judgement, and the focus and input behaviour on the user's own desktop | `spikes/page-heartbeat`, `docs/evidence/m1b-hangs.md` |
| A relaunch from Explorer, a shortcut and a file association bringing the running window to the front; the same user in a second session (Remote Desktop) | Real foreground rules exist only on the user's desktop (Windows Sandbox grants the foreground to every process), and a second session is the user's | `spikes/windows-single-instance` |
| Focus on the real desktop: typing right after a click on the taskbar button, after the hung-page dialog and after a restore from the taskbar; an IME's candidate window after the window moved (built in M1b step 6, checked with real input in Windows Sandbox only, `docs/evidence/m1b-host-details.md`) | Real foreground rules exist only on the user's desktop (Windows Sandbox grants the foreground more freely), and an IME is the user's own setting | `docs/evidence/m1b-host-details.md` |
| Sleep and resume, lock and session switch with a page open, where Windows Sandbox or a Linux VM cannot cover them (decision "Power and session events") | Puts the user's machine to sleep or locks it | `spikes/page-heartbeat` |

### Definition of done

A feature is done when every item that applies to it is true (user, 2026-11-15; decision "Plan split, additions and the M1a scope"):
- **Decided:** the decision is in `docs/decisions.md` (a user decision where this plan says so), and the plan and its design file agree with it.
- **Shown:** a public API has a compiled example in `docs/app-model.md`, and its check passes.
- **Tested** at the levels of section 5 that can show its failures: at least a unit or command test; a browser test for page-facing behaviour; a window test on both hosts for host behaviour; the NativeAOT lane passes. A feature that exists on one host only says so in the plan.
- **Safe:** its security default follows section 3c, with the C# opt-in and its documentation for anything a default blocks.
- **Documented:** the user-facing page (plain Markdown, compiled examples), the skill that covers the area, and new pitfalls.
- **Checked by a person, where needed:** rows in "Checks that need the user at the PC" for what an agent cannot run.
- **Measured:** the budgets it touches are measured (below), and the evidence file records the environment, commands, numbers and what was not verified.
- **Clean:** no warnings, no suppressions without the user's agreement, and `eng/check-size.cs` passes.

### Provisional budgets

Checked at each M1 slice exit from the first slice on, instead of first in M4 (user, 2026-11-15). The numbers come from the spikes, with headroom; each slice measures them with the protocol of M4 (release builds, medians and ranges). A budget missed at an exit is recorded with its cause, then fixed or changed with the user's agreement, never passed silently. M4 replaces them with measured budgets.

| Measure | Budget | Spike value | First checked |
|---|---|---|---|
| No-op build of the Notebook sample | ≤ 1.5 s | 0.4–1.2 s (decisions "D1: Compilation pipeline" and "Package-only consumer") | M1a; with the frontend step in M1d (about 0.86 s with an incremental target) |
| Command test: app start, median | ≤ 2 ms | 0.2–0.6 ms (`spikes/test-host`); measured in M1a, Windows Release: 0.20–0.24 ms CoreCLR, 0.14–0.20 ms NativeAOT (decision "M1a budgets measured; the NativeAOT lane builds into bin/aot") | M1a |
| Browser test: app, context and journey, median | ≤ 250 ms | 97–144 ms (`spikes/test-host`); measured in M1a, Windows Release: 122–137 ms on the WireShapes page (same decision) | M1a |
| NativeAOT console harness (dispatch, serializers, codec, bridge session) | ≤ 3.5 MB (3 MB until 2026-11-16) | 2.24 MB with generated serializers (decision "D1: Compilation pipeline"); 3.10 MB measured in M1a (decision "Console harness built; its budget raised to 3.5 MB; lone surrogates on read") | M1a |
| Small unary round trip over loopback, median | ≤ 2 ms | 0.7 ms (decision "Bridge streaming, cancellation and push"); measured in M1a, Windows Release: 0.70 ms, timed by the page (same decision) | M1a |
| Notebook NativeAOT executable, Windows / Linux | ≤ 6 MB (5 MB until 2026-11-17) / ≤ 8 MB | 3.68 MB (`spikes/react`) / 6.9 MB (`spikes/linux-desktop`); 4.77 MB measured after M1b step 5 (decision "Security defaults' choices accepted") | M1b / M1c |
| Launch to `ui-ready`, warm, Release, Windows / Linux GTK4 | ≤ 400 ms / ≤ 350 ms | 261–370 ms (`spikes/react`) / 271–292 ms under Xvfb (`spikes/linux-desktop`) | M1b / M1c |
| Small unary round trip on the desktop bridge, median | ≤ 1 ms | 0.2 ms (WebView2), 0.20–0.29 ms (WebKitGTK) | M1b / M1c |
| App process working set, Windows, NativeAOT | ≤ 60 MB | 37 MB (`spikes/react`) | M1b |
| Window test: open, journey and close, median, Windows / Linux | ≤ 700 ms / ≤ 500 ms | 466–472 ms / 297–301 ms (`spikes/test-host`); measured in M1b step 8a, Windows, 8-step journey: 306 ms CoreCLR, 298 ms NativeAOT (decision "Window tests on Windows (implementation record; choices for review)") | M1b / M1c |
| Total memory with the web engine, Linux GTK4 (PSS) | recorded, no budget yet | 413–426 MB (`spikes/linux-desktop`) | M1c |
| Hot Reload of a method body applied | ≤ 4 s | 0.9–3.9 s | M1d |

### Risks

The biggest risks, each with what retires it. Update the table when a risk is retired or a new one appears.

| Risk | Why it matters | Retired by |
|---|---|---|
| Visual Studio shows no squiggle for a generator diagnostic (seen in the spike) | Contract mistakes would surface only at build time in one IDE | Retired: Visual Studio draws Spar's squiggles; ReSharper's code analysis hides every referenced analyzer's (decision "Squiggles in both windows; ReSharper's code analysis hides them in Visual Studio") |
| In-process window tests cannot drive trusted input, or isolate profiles cheaply | Levels 5 and 7 would need more launched executables, slower and with no replacements | Retired on Windows: the Window surface drives trusted input in InPrivate windows on one shared browser per test process, about 300 ms per test (M1b step 8a); Linux in M1c |
| A native engine abort ends the whole test process | One broken environment loses every result of a run | The engine prerequisite check before the first window test, and window tests as their own run (M1b, M1c) |
| Variant and incremental builds turn fragile or slow, in the IDE or under `-mt` | The earlier mode-flag matrix was the main source of fragility | `Spar.BuildTests` and the no-op budget from M1a |
| The package-only consumer differs from the in-repository build | The SQL Server tool uses Spar through packages only | The package consumer test from M1a, with the generality fixtures |
| WebKitGTK behaves differently in ways the design missed (no back-pressure on scheme streams, the sandbox in CI, software GPUs, no in-process trusted input) | The Linux host could lag Windows or need app-specific branches | M1c, and streams in M3 |
| The minimum WebView2 Runtime lacks an interface the host needs | Users on older Runtimes would fail at start | M1b's provisional minimum with feature detection, confirmed in M4 |
| The security defaults do not hold on the published executable | The strict defaults are a core promise | The section 3c checks on the published executable in M1b and M1c |
| NativeAOT size and start-up grow as features land | Small, fast executables are a stated goal | The provisional budgets at every slice exit |
| SqlClient under NativeAOT and on Linux (accepted warnings, Kerberos-only integrated authentication) | The first real consumer depends on it | The SQL Server tool in M3 |
| No upgrade or data-migration design | An app update could lose or corrupt user data | The open question, decided before M4 |
| Windows Sandbox cannot host the tests that need it (a NativeAOT test executable in the guest, start time, one sandbox at a time, no CI runner) | Real-input, focus and system-registration checks (tray, notifications, deep links) would fall back to the user's desktop | The Sandbox spike before M1b (section 5, issue J) |

### M0 close-out

1. **Serializer and error syntax:** confirmed (generated code; `closed record`).
2. **Review all guidance once this plan is revised** (user rule). **Done on 2026-11-15** (decision "Guidance review applied"); repeat it whenever the plan changes materially.
   - Check every skill, `AGENTS.md`, `.claude/settings.json`, the README and `.editorconfig` against this plan and `docs/decisions.md`.
   - Update what is stale, remove what no longer applies, and add what the decisions require.
   - Repeat the review whenever the plan changes materially. It was repeated on 2026-11-15 after the first commit (decision "Plan and guidance review; M1 split").
   - The Linux host skill, `spar-webkitgtk-hosting`, was added next to `spar-webview2-hosting`.
3. **Rebuild the guidance on the earlier attempt's skeleton. Done on 2026-11-15:** `AGENTS.md`, `CLAUDE.md`, the skills, the analyzer settings, and `eng/check-size.cs`, written new for this repository.
   - `agent-setup.md` is not added: `.claude/settings.json` enables the plugins it listed, `AGENTS.md` ("Agent plugins") says what they are for, and section 6 holds the skill evaluations.
4. **Docs:**
   - spike results move to `docs/evidence/`, together with step 8. **Open;** it does not block M1a;
   - the runbook is not added (user, 2026-11-15). This plan is the record of what was taken from it ("Inputs to this revision").
5. **D8 automation:** done (in-process CDP).
6. **Linux real-desktop spike** in the user's WSL Ubuntu with WSLg: done (`spikes/linux-wsl-desktop`). It explained the slow GTK3 start and recommended 6.0, which the user then chose. Remaining, in the checks table above: a real GPU, HiDPI and IME on a native desktop.
7. **Decide the remaining user questions** that block M1. **Done:** Node in Debug, the contract snapshot and the wire details were decided on 2026-11-14, and the `Program.cs` shape on 2026-11-15. The other open questions are decided when their milestone reaches them (decision "Remaining decisions wait for their milestones"), as the "decide first" list of each M1 slice.
8. **Delete `spikes/`** once its evidence is in `docs/evidence/`. **Open;** it does not block M1a.
   - **Two spikes hold checks that the guidance requires,** so they stay until M1a has rebuilt those checks under `tests/`: `spikes/app-model-check` (the compile check of `docs/app-model.md`) and `spikes/frontend-frameworks` (the React, Angular and Vue checks of the SDK).
   - **How evidence is kept (user decision, 2026-11-14):**
     - `spikes/` is versioned in git without its generated folders, and raw evidence lives in each spike's `results/`.
     - Deleting a spike first moves its `RESULTS.md` and `results/` to `docs/evidence/<name>/` and updates the links, which `docs/decisions.md` and `docs/pitfalls.md` use throughout.
     - Before the M0 commits are squashed, the full history is kept in an archive tag or a bundle (proposal).
   - **Raw evidence that sat inside generated-looking folders** was copied into each spike's `results/` on 2026-11-14 (decision "Spike evidence is versioned"): `d3-variants/obj/`, `ts-results/obj/editor/`, `ts-compiled-contracts/obj/`, `d8-automation/artifacts/results/`, and the inputs under `npm-ignore-scripts/*/obj/spar/client`.
   - **The root `artifacts/` folders are not versioned and are disposable** (user, 2026-11-15). 22 of them, 583 MB of logs and screenshots, hold the raw data behind several RESULTS tables, for example `artifacts/react/` and `artifacts/browser-telemetry/`. The tables and numbers in each `RESULTS.md` are the record; a clean of `artifacts/` loses only the raw files behind them.
   - **Dependencies between spikes,** which constrain the deletion order (delete together, or the providers last):
     - `d1-pipeline` is used by `react` and `loopback` (`Spike.targets`, `Spike.Runtime`) and by `linux-desktop` (`Spike.Generator`, `Spike.Runtime`). The bridge protocol prototype lives there (section 2c). `browser-telemetry` holds its own newer copies of `d1-pipeline`, `react`, `loopback` and `linux-desktop`.
     - `react` is used by `aspire` (its `React.App` and `frontend/`).
     - `loopback` is used by `test-frameworks` (`AppPathMetadata.targets`, `LoopbackApp.cs`, the built `Loop.App` Debug exe), and `linux-loopback/linux-changes.diff` patches it.
     - `linux-loopback/Dockerfile` builds the image `spar-linux-loopback:sdk11.0.100`, on which `linux-desktop/Dockerfile` builds `spar-linux-desktop:webkit2.52`. The Linux container runs of `asset-packing`, `csp-navigation`, `theme-startup`, `sqlclient-linux` and `browser-telemetry` use that image. These two Dockerfiles are the only recipe for the Linux CI image (section 5, M1).
     - `linux-wsl-desktop` copies `theme-startup/frontend/dist`; `sqlclient-linux` runs the built exes of `sqlclient-tool`.
     - `contract-syntax` borrowed `tsc` from `react/React.App/frontend/node_modules`, and `wire-details` from `npm-ignore-scripts/react/frontend/node_modules`.
9. **Before the first commit: references and provenance review** (user request, 2026-11-14). **Scan done on 2026-11-15:** no copied code (decision "Provenance scan before the first commit").
   - **What may stay (user, 2026-11-14):**
     - references to official documentation;
     - references to earlier Spar versions, unless what they describe came from another framework.
   - **References to other frameworks** (Tauri, Electron, Wails and the others the research cites):
     - **They stay in the design records as evidence** (user decision, 2026-11-14), and never appear in user-facing documents (`AGENTS.md`).
     - At the first commit, check that skills, code comments and file names name no framework.
   - **Licences found:** Tauri MIT and Apache-2.0; Wails MIT (registry pages, to verify against the official repositories); Electron MIT.
   - **Found on 2026-11-14,** excluding `node_modules`, `obj`, `bin` and `artifacts`:
     - **Tauri:** a `T` column in `docs/research-review.md` (about 15) and a few mentions in the plan and decisions.
     - **Electron:** only as a measured reference in `spikes/linux-memory` (a small Electron app written for the comparison), its numbers in the plan, decisions and pitfalls, and VS Code's `ELECTRON_RUN_AS_NODE` in two TypeScript spikes, which is tool usage.
     - **webview/webview and saucer:** once each, in the WebKitGTK decision entry.
     - **Wails:** in the WebKitGTK decision entry.
     - No other framework (Photino, Neutralino, Flutter, MAUI, Avalonia, Blazor, Dioxus) is mentioned.
   - **No copied code from other frameworks:** check that no spike or guidance file copies code from Tauri or the other frameworks.
     - Method: a similarity scan (for example a copy-paste detector run against their sources) plus a manual look at the hits.
     - Code shared between spikes inside this repository, and with earlier Spar versions, is not the target of this check.
   - The results are recorded in `docs/decisions.md` before the squash and first push.

**Exit:** every open question that M0 owns is answered and recorded. The repository builds, and `eng/check-size.cs` passes. `dotnet test` has nothing to run until M1a adds the first test project.

**State on 2026-11-15:** steps 1 to 3, 5 to 7 and 9 are done. Moving the evidence (step 4) and deleting the spikes (step 8) remain, and neither blocks M1a.

### M1: build machinery, then the Notebook sample on both hosts

**Split into four slices** (user, 2026-11-15; decision "Plan and guidance review; M1 split"). Each slice has its own exit and its own evidence, and the next one starts when that exit is met.
- **The Notebook sample grows through all four** (decision "Polished sample apps: a Markdown notebook; the test variant"): a polished Markdown notebook, not a minimal demo. In M1 it has notebooks and pages stored as Markdown files under its data folder, editing with live preview, tags, search, backlinks, and real error states (a page changed elsewhere, a name already taken, a missing file), on React, TypeScript, Vite, Tailwind CSS and Radix. Streams, dialogs, drag and drop, secondary windows and printing come with M3. Its spike-derived budgets below are a floor: a real app's bundle is larger, so a missed budget is first checked against what the sample adds.
- Each slice starts by settling its "decide first" points with the user, and by adding the project skill its work needs (`AGENTS.md`, "Project skills"): testing (with the in-process test host and its loopback bridge) and the C# side of the bridge in M1a, page security in M1b, the development loop in M1d.
- Items marked (RR) come from `docs/research-review.md` and were adopted on 2026-11-15. Limits and other numbers are set by measurement in the slice, never taken from earlier versions.
- The slice contents were assembled by the 2026-11-15 review from the old M1 list, the "in M1" notes across this plan and the research review. Moving an item between slices needs no decision; dropping one does.

#### M1a: build machinery, contracts and the core, with no window

**Decide first:** nothing left; every point was decided on 2026-11-15.

**Build:**
1. **SDK and tasks:**
   - `Spar.Sdk` with `SparVariant`, `SparPlatforms` and the task assembly: inventory, client, pack, lease, and in-proc variant builds with a deterministic `MSBuildRestoreSessionId` and a design-time guard;
   - the generator, the analyzer and their tests: diagnostics with locations, the clash matrix with a referenced assembly, generated-output snapshots, the differential `tsc` test (built; no separate manifest, decision "No contract manifest for now; the differential tsc test built"). Generated sources use `\n` only, and an invalid command skips only itself, never its whole contract;
   - `src/Spar.Client` (section 2b): the protocol, `Call` and `Result`, the failure hooks, fake channels, the type tests, and the React, Angular and Vue checks that now live in `spikes/frontend-frameworks` (rebuilt in the SDK's tests, `src/Spar.Client/test/frameworks`; decision "The frontend-framework checks in the SDK's tests");
   - Brotli frontend packing, after a per-file Deflate control on the real bundle (RR), with the asset checks and the embedded third-party notices of section 3c (RR);
   - the documentation check of `docs/app-model.md`, moved from `spikes/app-model-check` into `tests/` and run by `dotnet test` (test type 17) (moved: `tests/AppModel`, decision "The documentation check under tests/"; against stand-ins, and the C# that Spar can build also through real Spar in `tests/AppModel/Real`).
2. **Core:**
   - dispatch, the three outcomes, a DI scope per command with `ValidateScopes = true`, cancellation (parameter plus `ISparCall`);
   - the bridge protocol with `traceparent`. Every message kind gets its header fields and conformance vectors. Unary calls, cancellation and host-to-page events are implemented here; streams and subscriptions in M3;
   - bridge limits and admission (RR): a maximum message size, calls in flight per document and a bounded send queue; capacity reserved before parsing, and the document and command checked before converters, DI or the handler run; increasing call ids, with a duplicate or lower id rejected; separate capacity for control messages (cancel, close, readiness); parsing off the UI thread (built, except parsing off the UI thread, which comes with the desktop hosts; decision "Bridge limits built");
   - exception redaction in Release (RR): an unhandled exception reaches the page as a correlation id, with details only in development, and an oversized result fails only its own call (built: the correlation is the call's trace id, the details go to the app's `ILogger`; decision "Bridge limits built");
   - the BCL instrumentation (section 6b) (built; decision "Bridge instrumentation built");
   - **the test host** (section 5), with the surfaces `None` and `Browser` (the per-app log and the injectable clock built; decision "Test host: the per-app log and the clock"; the fixture rules built, decision "Fixture rules: the teardown hook"): the app's `Program.cs` run in the test process, service overrides, the data root per app instance, Spar's outward services faked, the external-dependency marker, the per-app log, typed C# test clients generated per contract (RR), and the fixture rules (RR): assertion, capture and teardown failures are reported together, unresolved cleanup is not a pass, an empty test selection fails, and skips are explicit;
   - **the loopback bridge** that the `Browser` surface needs, in test and development variants only (moved from M1d; decision "Plan split, additions and the M1a scope"): the token in the URL fragment, the `Host` and `Origin` checks (403 `forbidden-host`), one channel per document for every host message and the page's messages sent in order one request at a time (decision "Loopback transport accepted; Browser surface in its own package"; built, with the `Browser` surface in `Spar.Testing.Browser`), and a listener on both loopback addresses or one that advertises its real address;
   - the static-state test of Spar's assemblies (built; decision "Static-state test built"), and the production boundary test of section 5 on a published console harness (the test host and the fakes absent) (built: `tests/apps/Spar.Harness`, with the bridge kept; on Windows and on Linux in WSL, decision "Linux runs in WSL for M1a; what Linux found"; decision "Console harness built; its budget raised to 3.5 MB; lone surrogates on read").
3. **Repository:**
   - CI with the build and test lanes on Windows and Linux, the `-mt` lane, and `eng/check-size.cs`;
   - the first `Spar.BuildTests`: isolation, "authored files untouched", a clean clone (built: a clean copy, the unchanged build and the Production boundary of the Notebook; decision "Build tests: a clean copy, the step matrix from binary logs, the production boundary");
   - the package-only consumer test from the first day (section 1), with the generality fixtures (RR): a folder name with a space, an assembly name different from the project, a custom app id, a non-default frontend folder, a path with non-ASCII characters (built: the packages and the consumer with a space, non-ASCII letters and a different assembly name, its tests, the analyzer, design time, NativeAOT, and an update and downgrade under a warm server, then its page in `ui/` and an end-to-end project with the Browser surface; the custom app id waits for the app identity (M1b); decisions "Packages and the package-only consumer test built" and "The generated package keeps version 0.0.0; the consumer gets a page"; a second consumer under central package management, decision "Spar.Sdk's own package references are implicit, the test framework included"; a third whose app serves the interface contract of a library package, decision "Interface contracts run; a missing implementation stops the start; a contract library from a package");
   - the test-SDK shape (RR): a `SparTestedApplication` item that becomes a project reference before restore, tried on `Spar.Sdk` before it is fixed (built, with SPAR120–SPAR122; the `Projects.X` marker comes with the black-box fixture in M1b; decision "SparTestedApplication: the test project names its app").

**Exit:**
- `dotnet build` and `dotnet test` pass in the repository and in the package-only consumer, on Windows and Linux, in CI (on Windows and on Linux in WSL, CI's steps: met, decision "Linux runs in WSL for M1a; what Linux found"; and in CI's own run on GitHub Actions, both OSes, decision "CI's first runs on GitHub Actions").
- The Notebook's contracts round-trip through real serialization and dispatch in command tests (level 2), and through the TypeScript SDK against a fake channel. The conformance vectors pass in C# and in TypeScript. (met: the Notebook's command tests; its page tests against `@spar/app/testing`'s fake backend, which speaks the real protocol, decision "Page tests: @spar/app/testing"; the vectors in `tests/Spar.Tests` and `src/Spar.Client/test/vectors.test.ts`.)
- Browser tests (level 4) run the Notebook's pages in Chromium against the in-process backend over loopback, on Windows and Linux (met on both, decision "Linux runs in WSL for M1a; what Linux found").
- The command tests pass in the NativeAOT lane (`xunit.v3.aot`) with no warnings. (met on Windows and on Linux in WSL, decision "The source package on Linux".)
- The M1a budgets are measured and recorded ("Provisional budgets") (all met on Windows, decision "M1a budgets measured; the NativeAOT lane builds into bin/aot", and on Linux, decision "Linux runs in WSL for M1a; what Linux found").
- A console harness around the core (dispatch, serializers, protocol codec) publishes with NativeAOT with no warnings on both OSes, and gives the same results as on CoreCLR (met on both, decision "Linux runs in WSL for M1a; what Linux found").
- Visual Studio and VS Code show contract diagnostics as you type, with squiggles in both (met for an app from packages: VS Code's language server reports them 0.2 s after an unsaved edit, both windows draw them, Visual Studio only with ReSharper's code analysis off, decisions "Editor diagnostics through VS Code's language server; the windows still to check" and "Squiggles in both windows; ReSharper's code analysis hides them in Visual Studio"; the samples and Spar's test projects name `Spar.Sdk` so C# Dev Kit loads them, decision "The repository's projects name Spar.Sdk").
- Evidence is recorded for each result.

#### M1b: the Windows host

**Decide first:** all decided by 2026-11-17, as each item notes.
- the two-stage close and its automated mode (RR), with a compiled example in `docs/app-model.md` first (decided, decision "Two-stage close"; `docs/app-model.md` 6.11);
- the Windows single-instance mechanism (decided: a lock file plus a named pipe with an explicit DACL and a check of the server process, decision "Windows single instance: a lock file plus a pipe"), then the relaunch handler and its exit codes (decided, decision "The relaunch handler"; `docs/app-model.md` 3.14);
- what "Wait" does after the hung-page dialog (section 3, "Hung pages"; decided: it asks again after about 20 s, then 40 s, 80 s and every 160 s, and a close shows "Wait / Close anyway", decision "What "Wait" does after the hung-page dialog");
- a provisional minimum WebView2 Runtime: the oldest one with the source-kinds filter of `ICoreWebView2_22` and the other interfaces section 3 lists. M4 confirms it (decided: 139, with no fallbacks, decision "M1b's remaining questions without a spike");
- the UI-thread stall probe (section 3; decided: Development and Test, and an opt-in in Production, decision "M1b's remaining questions without a spike");
- the app id's validation rules and the folder names per channel (section 3c; decided: a required reverse-DNS `SparAppId`, folders named from it, `.Development` for Debug, decision "M1b's remaining questions without a spike");
- styles added at run time: whether app-author docs recommend constructed stylesheets, and whether Spar offers a per-document style nonce as a C# option, with how hosts hand it to the page (decision "Styles added at run time: a nonce and constructed stylesheets both work"; decided: both, `csp.StyleNonce()` writing `<meta property="csp-nonce">`, decision "M1b's remaining questions without a spike");
- the policy for `<input type="file">` (section 3c; decided: blocked, with `AllowPagePicker()`, confirmed by `spikes/window-tests`, with the File System Access APIs removed on Windows, decision "M1b's remaining questions without a spike" and decision "Window-test profiles, trusted input, the page's pickers and the Sandbox runner");
- profiles for window tests and trusted input in the in-process window (section 5, issues D and I), each after a spike (decided: InPrivate on one shared environment per test process, and CDP `Input.*`, decision "Window-test profiles, trusted input, the page's pickers and the Sandbox runner");
- the black-box data root (section 5, issue M), after a security review (decided: the environment only, decision "M1b's remaining questions without a spike"; built in step 8b);
- the generated `Projects.X` marker of the test-SDK shape (RR), with its members taken from what the black-box fixture needs, such as the tested app's published executable (decision "SparTestedApplication: the test project names its app"; decided: a generated class per tested app, decision "M1b's remaining questions without a spike");
- the Windows Sandbox runner (section 5, issue J, a proposal), after its spike (decided: two lanes, SYSTEM without windows and the user session with them, one sandbox per run, internal to `Spar.Testing` in M1b, decision "Window-test profiles, trusted input, the page's pickers and the Sandbox runner").

**Build:**
1. **The host:** STA ownership, instrumentation, asset serving on `https://app.localhost`, the bridge with document scoping, teardown behind the close request, and failure recovery. (first slice built: `src/Spar.Windows`, `spar.OnCloseRequested`, `SparPlatforms` wired, the WebView2 transport in the SDK, and the Notebook in a window as a 4.5 MB NativeAOT executable; decision "The Windows host, first slice (implementation record; choices for review)", `docs/evidence/m1b-host.md`. Left for later steps: `WM_ENDSESSION`, the page's close guard, `CloseReason.App` and the UI-thread stall probe.)
2. **Start-up:** the prerequisite checks and the error dialog with the fixed exit codes; single instance before the environment exists, through the lock file and the pipe; the window shown at once in the theme colour, with the host layers of section 3d and the cost of the transparent background measured. (built: the checks, the task dialog, `spar.OnStartupProblem`, the per-user `host.log` (`app.log` since step 7), single instance with `spar.OnRelaunch`, every 3d host layer and `spar.Window(w => w.Background(…).Size(…).MinSize(…))`; the transparent background measured at no cost; decision "Start-up: prerequisites, single instance and theme (implementation record; choices for review)", `docs/evidence/m1b-startup.md`. Left: the page's theme style and its CSP hash, read from `Program.cs` by the build, and the SDK's `setColorScheme`.)
3. **Hangs:** the hung-page heartbeat, and the shutdown hang policy (RR): after about 10 s of incomplete shutdown, a native "Keep waiting / Exit now" prompt. The library never kills the process on its own. (built: `ping`/`pong` in the protocol, the heartbeat with "Wait / Reload page" and "Wait / Close anyway" and the Wait schedule, `spar.OnPageUnresponsive`, the UI-thread stall probe with `spar.Diagnostics(d => d.KeepStallProbe())`, and the shutdown prompt with exit code 10; alarms 15.5–19.2 s after a hang, reload 85–140 ms, no false alarm on idle pages or work up to 10 s; decision "Hangs: heartbeat, stall probe and shutdown prompt (implementation record; choices for review)", `docs/evidence/m1b-hangs.md`. Left: the window tests' use of the stall data, step 8. The page's close guard is built since step 8's follow-up: decision "Window tests: closing, session-end budget, test theme and any-character keys".)
4. **Page security (section 3b):** the navigation lock; the CSP declared in C# with its analyzer (SPAR101–SPAR104); the response headers on every response, worker scripts included; `Permissions-Policy` and the deny-by-default permission gate; both violation channels with flood limits, their numbers taken from a flood test; SRI and Trusted Types on the Release bundle. The host-injected script that the violation listener needs (section 3b) also takes over the heartbeat's answer from the SDK, so the heartbeat is armed when the document is created again, as section 3 designed it (decision "The page hang hook gets its async form; the heartbeat's start moves to step 4"). (built: `spar.ContentSecurity` and `spar.Permissions` as decided, one valid policy composed by `PagePolicy`, SPAR101–SPAR104 and the SPAR105 warning, the policy in the generated catalog and `csp.json`, the Development check; on Windows the headers on every response with the worker check passed, the navigation lock with new windows always handled and allowed links opened by `ShellExecuteW`, the permission gate with `SavesInProfile = false`, the injected script with the heartbeat's answer and the listener, `report-uri` with size and rate limits, one deduplicated log; the Vite plugin's SRI, import map and external-integrity check; `media-src 'self'` added to the base policy; flood numbers from 100,000 identical and 100,000 distinct violations; a page hanging before it connects reported 19.6–19.7 s after its document opened; the Notebook 4,940,288 B with 0 violations; decision "Page security (implementation record; choices for review)", `docs/evidence/m1b-page-security.md`. Left: user-initiated links (no real input here), the development server's policy header (M1d), `--js-flags=--noexpose_wasm`, `LaunchingExternalUriScheme`.)
5. **Security defaults (section 3c),** each verified on the published executable together with its C# opt-in: the Production browser settings; the ignored environment overrides, including whether clearing a variable in-process is enough; the app identity and its channel-separated folders (RR); the per-user profile folder, with a check of the folder actually in use (RR). The production boundary test proves that no test, development, loopback or devtools code ships. (built: `spar.Browser(b => …)` with its eight opt-ins, each logged at start-up; the Production settings, `DownloadStarting`, `ContextMenuRequested`, history navigation cancelled in `NavigationStarting`, and the picker interception (`Page.enable`, `Page.setInterceptFileChooserDialog`) with `--disable-blink-features=FileSystemAccessLocal`; Development with devtools and the reload keys through `AcceleratorKeyPressed`; eight WebView2 variables removed in-process, which is enough (no port, no moved folder on the NativeAOT executable without the opt-in; port and folders with it); `SparAppId` with SPAR130, the folders and AppUserModelID per channel, `spar.Folders(f => f.Profile(…))` with exit 70, and the folder in use checked; the boundary test now refuses every DevTools Protocol method but the two picker calls; the Notebook 4,999,680 B, 320 B under its budget; decision "Security defaults (implementation record; choices for review)", `docs/evidence/m1b-security-defaults.md`. Left: the browser keys and the context menu on a Production executable, which need real input (Windows Sandbox), and the SmartScreen net log of step 9.)
6. **Host details (RR):** forward `WM_SETFOCUS` to `MoveFocus` and move focus into the WebView on activation; keep the controller's `IsVisible` in step with the minimized, hidden and tray states; call `NotifyParentWindowPositionChanged` when the window moves; recheck the handle leak seen when a visible window is torn down. (built: `ViewWindowState`, with `WM_SETFOCUS` to `MoveFocus`, focus on restore and on a late controller, never while a dialog owns it, and `MoveFocusRequested` bringing Tab back into the page; `IsVisible` from `WM_WINDOWPOSCHANGED`, with the tray's hook documented there; a move notified on every move; checked with real input in Windows Sandbox against a baseline of the host before the step, where the keyboard reached the page only after a click into it and a `<select>` opened from the keyboard after a move appeared at the old place; a hidden window's page now `hidden` and throttled, where it kept 60 frames; the leak did not reproduce in 100 cycles, so the mitigation is not adopted; the Notebook 5,010,944 B; decision "Host details (implementation record; choices for review)", `docs/evidence/m1b-host-details.md`. Left: IME candidates, the page-drawn title bar's overlay and focus, M3's tray state.)
7. **Logging:** the per-user log file, which the start-up failures already need. Rotation, crash records and `diagnostics.collect()` come in M3. (built: one file, `app.log` in the logs folder, replacing step 2's `host.log`, for the host's lines from its first moment and every `ILogger` of the app's services through Microsoft.Extensions.Logging's factory and Spar's file provider, written from a bounded queue that drops and counts, or no file with `spar.Logging(l => l.WithoutFile())`; `spar.Logging(l => l.Level(…).Level(category, …))` with Information or Debug by default and `logging.json` in the data root read strictly at start, as the factory's filter rules for every provider, the app's own `AddLogging` included; plain lines in UTC with further lines indented; the page's `console.error` calls (in a Development build from the DevTools Protocol's `Runtime.consoleAPICalled`, so devtools' links stay the page's), uncaught exceptions and unhandled rejections through the injected script and the `errors` message, logged under `Spar.Page`, 100 groups and 1,000 errors per document; 100,000 errors of any kind 0.17–0.59 s of page time, Spar's reporting 3–18 µs each, one bridge message per flood; a line 0.26 µs to the caller; the test host's file in the test's own logs folder; `logging.json`'s level and categories applied after everything the app configured (a post-configuration), so they beat the `"Default"` of an app's own configuration, which Microsoft.Extensions.Configuration gives an app that adds it (+351 KB measured; Spar takes no configuration package), and `spar.ResolveFolders()`, the folders the services get, for `Program.cs` to build such a configuration; the Notebook 5,182,464 B (+171,520 B; +78,848 B for Microsoft.Extensions.Logging over Spar's first own factory), 5,184,512 B after the configuration fixes, start-up unchanged within noise; decisions "Logging (implementation record; choices for review)", "Logging's follow-up (implementation record)" and "Configuration stays the app's choice (implementation record; choices for review)", `docs/evidence/m1b-logging.md`, `spikes/configuration-cost/RESULTS.md`. Left: rotation and crash records (M3), the Linux writer's two-process append (M1c).)
8. **Tests:** the `Window` surface on Windows (the WebView2 window on the test process's UI thread, offscreen and never activated, page automation through in-process CDP), with the leftover-process assertion; the required host tests of section 5, plus OS session end and a long install path (RR); the black-box level on the Release executable, which has no devtools, with a UI Automation smoke test (RR); if adopted, Spar's Windows Sandbox runner (section 5, issue J), used for the real-input and first-click checks and the offline Production run. In three slices (decision "Step 8's shape: window tests driven by Spar's own API"): 8a the `Window` surface with Spar's own API and the host tests, 8b the black-box level, 8c the Sandbox runner. (8a built: `SparTestSurface.Window` and `app.Page` in `Spar.Testing` (`SparPage`, `SparLocator`, `SparAssertions`, the host-neutral page engine `PageEngine.js` in an isolated world, its vitest tests), the host's test-only part `Spar.Windows/Testing` (one UI thread, one environment per browser arguments with a default-profile anchor, an InPrivate controller per window, the renderer-exit check), the window split into `WindowsApp` and `AppWindow`, `WM_ENDSESSION` as a non-cancellable phase held up to 10 s, the Test variant handing `Run` its host; `tests/Spar.WindowTests` with every required host test but the close during a stream (M3), the session end and a 255-character install path; seven Notebook window journeys, both lanes; about 300 ms per window test against the 700 ms budget; the Notebook 5,200,896 B with no test code; decision "Window tests on Windows (implementation record; choices for review)", `docs/evidence/m1b-window-tests.md`. Follow-up built (user decisions 2026-11-18; decision "Window tests: closing, session-end budget, test theme and any-character keys", `docs/evidence/m1b-window-close.md`): the page's close guard (`windows.onCloseRequested` in the generated `@spar/app/windows`, the `close-request`, `close-answer` and `close-ended` messages, "Wait / Close anyway" for a silent or hung page) and `app.Window.CloseAsync` with the test's answers; the session end within 4 s (guards 3 s, the page's 2 s); window tests light by default (`o.ColorScheme`); `PressAsync` and typing for any character, by Unicode scalar value; the Notebook's page guard saves on close and asks when it cannot. Left: scenes and a fixed clock, frames, drag and drop, network waits, dead keys and other layouts; a real sign-out on Windows 11, with 8c's Sandbox runner. 8b built (user decisions 2026-11-19, decision "Step 8b's shape: the black-box fixture"; implementation record "The black-box fixture (implementation record; choices for review)", `docs/evidence/m1b-black-box.md`, `spikes/blackbox-desktop`): `SparBlackBox.LaunchAsync(Projects.X, o => …)` in `Spar.Testing` on the app's Production NativeAOT executable, which `<SparBlackBoxTests>true</SparBlackBoxTests>` publishes in-process beside the tests (the generated `Projects` class, `SparBlackBoxPublish=false`), its data root through `LOCALAPPDATA`, `APPDATA`, `TEMP` and `TMP` only, a job object that ends it with the test process, a desktop of the test process's own (50 of 50 spike launches, nothing on the monitor, focus never taken), ready on the host's `ui-ready` log line (a format now pinned) and the window, `app.Window` and `WaitForDialogAsync` over hand-declared UI Automation that runs under NativeAOT; the Notebook's six black-box tests (start and close, Invoke, files, the hand-off, a failed relaunch's message and exit 8, a profile folder that cannot be created, its message and exit 70) and twelve of the fixture's own (a network `LOCALAPPDATA` refused with exit 70 among them, after review), both lanes; about 0.4 s to ready and 2.5 s per Notebook test; 680 tests in 20 runs per suite and lane with 0 failures; the Notebook still 5,234,688 B. Left: the Linux fixture (AT-SPI) with the Linux host (M1c); real input and focus-dependent checks with 8c's Sandbox runner, which sets the fixture's placement switch; why Invoke takes 2 s on the hidden desktop; a crash record at the next start, when crash records exist.)
9. **Checks this plan deferred to M1:** the decided CSP run with the React bundle on `https://app.localhost` (done in step 4: 0 violations); `--host-resolver-rules` through `AdditionalBrowserArguments`; the SmartScreen flag against a net log; `--js-flags=--noexpose_wasm`; the CSP header on worker scripts (done in step 4: it applies); the minimum Runtime of the find API; the page's `online` and `offline` events in Windows Sandbox.

**Exit:**
- The Notebook sample runs on Windows as a single-file NativeAOT executable with the bundle embedded, and its size is recorded. Debug uses the same path through `SparDevServer=false` until M1d.
- The required host tests, the window tests (CoreCLR and NativeAOT lanes), the black-box smoke test and the production boundary test pass.
- The M1b budgets are measured and recorded.
- Each section 3c default is verified on the published executable with its opt-in.
- Evidence is recorded for each result.

#### M1c: the Linux host

**Decide first:**
- how the host detects a software GPU, and the name of the C# option that turns acceleration back on (section 3, "Software GPUs");
- the `connect-src` value for `spar://app` (section 3b);
- whether Spar publishes the Linux test image or only its Dockerfile, and whether Windows machines may use WSLg for window tests (section 5, issue D).

**Build:**
1. **The host,** the same as M1b over WebKitGTK 6.0: the main-loop thread, the `spar://app` scheme handler, the script-message bridge in an isolated script world with a relay in the top document only (RR), document scoping, teardown with the main loop kept running until the web processes exit, and recovery on `web-process-terminated`. Foreign iframes are tested against the bridge.
2. **Start-up:** the prerequisite checks, with the trial `bwrap` run, the messages and the same exit codes; single instance through the lock file and the Unix socket; the theme layers of section 3d; the software-GPU default.
3. **Hangs:** the heartbeat, with `webkit_web_view_terminate_web_process` before the one reload, and a hung web process terminated before quitting.
4. **Page security:** the navigation lock in `decide-policy`; the same CSP and headers, with `Content-Type` repeated in the header set; the `permission-request` gate, which is the only one on Linux; the data and cache folders passed to the network session explicitly.
5. **WebAssembly and worker follow-ups** (`spikes/webkit-wasm-off`): `JSC_useWasm` set through libc `setenv` before GTK starts, `false` by default and `true` with the opt-in; a real-host test that asserts `typeof WebAssembly` both ways, in the page and in a worker; shared and service workers and web-process restarts.
6. **Security defaults (section 3c)** on the published executable: no developer extras, the ignored environment overrides, and the GPU workaround variables still working.
7. **Tests:** the `Window` surface on Linux (the GTK thread owned by the test process, under Xvfb, XTEST for trusted input), with the engine prerequisite check before the first window test; the same host-lifecycle suite as Windows; one Linux CI image (non-root, user namespaces, Xvfb), built from the two spike Dockerfiles and `spikes/test-host/linux/Dockerfile` (M0 close-out step 8); the page's `online` and `offline` events in a container or VM.

**Exit:**
- The Notebook sample runs on Linux as a single-file NativeAOT executable, with its size recorded.
- The host tests and the window tests (CoreCLR and NativeAOT lanes) pass on Linux, headless in a container in CI, and both hosts pass the same host-lifecycle suite.
- The M1c budgets are measured and recorded.
- The start-up check refuses a WebKitGTK older than the provisional minimum (2.48) with exit code 4.
- Evidence is recorded for each result.

#### M1d: the development loop, loopback, the template and the tools

**Decide first:**
- where page tests run (section 5, issue B);
- whether typed page-side mocks generated from the contracts land here or in M2 (RR);
- whether the build identity (a publish receipt, or an identity embedded in the executable) lands here or in M2 (RR).

**Build:**
1. **Debug through the app's own origin** on both hosts (section 4): the host fetches from the Vite dev server, with the proxy rules, Vite's nonce under the strict CSP through the proxy, the CSP header on worker scripts from the Vite plugin, and the Linux path through the custom scheme. `SparDevServer=false` keeps the embedded path.
2. **Loopback in the development loop,** on the bridge M1a built: the dev session (one address and token across restarts), and the `Desktop` and `Loopback` launch profiles.
3. **`dotnet watch` and Hot Reload:** the Hot Reload signal with its toast, and a check that contract and DTO edits always restart instead of patching (RR). F5 debugging of the `Desktop` profile in Visual Studio and VS Code (RR).
4. **Tests in the template:** the two test projects of the consumer model with the template defaults of section 5, and `@spar/app/testing` for page tests.
5. **The page side of start-up (section 3d):** the theme style and the optional splash in the Vite plugin, with the animation-frame mechanism confirmed.
6. **User project and tooling:**
   - the template `dotnet new spar-app` (React + TypeScript + Vite first, `--platforms windows,linux`), with `TreatWarningsAsErrors` and `MSBuildTreatWarningsAsErrors` on: `frontend/` inside the app project (`SparFrontendPath`), `Spar.Sdk` in `global.json`, the launch profiles, the local tool manifest;
   - the fixed `@spar/app` `file:` dependency and the Spar Vite plugin;
   - the two test projects;
   - the basic `spar doctor`, which also finds a Node installed through a version manager (RR), and `spar init frontend`;
   - a from-scratch guide.
7. **Runtime choices:** documented and tested. They are per-app settings in the app's own project file; Spar never turns them on for an app (user, 2026-11-14). The SqlClient items are the SQL Server tool's settings, shown as a documented recipe, not Spar defaults.
   - CoreCLR publication (framework-dependent, and self-contained single file);
   - an app accepting its own AOT warnings. With the SDK default `TrimmerSingleWarn=true`, SqlClient 7.1.2 reports only 4 assembly-level warnings (IL2104 ×3, IL3053), so a suppression of those hides whole assemblies (`spikes/sqlclient-aot`);
   - `InvariantGlobalization=false`;
   - **SqlClient as a single file on Windows** (`spikes/sqlclient-aot`): `<RuntimeHostConfigurationOption Include="Switch.Microsoft.Data.SqlClient.UseManagedNetworkingOnWindows" Value="true" Trim="true" />` plus `<PackageReference Include="Microsoft.Data.SqlClient.SNI.runtime" ExcludeAssets="all" PrivateAssets="all" />`. The publish folder is then the exe alone, and every feature check passed. Without the switch the SNI DLL is a mandatory sidecar: the exe alone fails at the first `Open` with `DllNotFoundException` for `Microsoft.Data.SqlClient.SNI.dll`, and the package has no static library. Not tested with managed networking: integrated authentication, named pipes, shared memory.

**Exit:**
- `dotnet run`, `watch`, `test` and `publish` work in the repo and in the package-only consumer, on Windows and Linux.
- A new app made from the template builds, runs, tests and publishes from packages only, and F5 starts it under the debugger in Visual Studio and VS Code.
- The template's command, browser and window tests pass on both OSes, and its NativeAOT lane publishes and passes.
- The M1d budgets are measured and recorded.
- Evidence is recorded for each result.

### M2: agent facilities

- Devtools endpoint, `spar dev` with reload, `spar snap`, scenes and `TimeProvider`, the agent guide, and the evaluation. Exactly one interactive surface survives.
- The optional OpenTelemetry layer, client telemetry through the backend, and the Aspire dev experience.
- If M1 shows agents need it sooner, a minimal `spar dev` (build plus reload) can move into M1.

### M3: communication breadth and the SQL Server tool

- **Streams and subscriptions** on both hosts, on the message kinds M1a defined: credits, in-band failures, the `events` slot, coalescing and resumption (section 2c and the table below), with trace context on them (section 6b).
- **Native services on both hosts,** each built from its decided API in section 3 and its example in `docs/app-model.md`:
  - file and folder dialogs with grants, drag and drop, the clipboard, credentials;
  - the page-drawn title bar, window placement, secondary and modal windows, zoom and find in page, screen-capture protection;
  - the tray and close to tray; actions, context menus and shortcuts; native and JavaScript dialogs; opening links and files outside the app;
  - printing and print to PDF, taskbar progress and badges, power and session events, network status, resource usage;
  - log rotation, crash records and `diagnostics.collect()`, on the log file M1b added.

  The order follows what the SQL Server tool needs first. Each one's remaining host checks are listed with it in section 3.
- **Decided when M3 starts** (decision "Remaining decisions wait for their milestones"): serial groups for commands that must not overlap, a binary wire type, settings files, and the features still marked "to discuss" in section 3 (a native menu bar, notifications, deep links and file associations).
- **Windows Sandbox tests for app authors** (section 5, issue J, a proposal): the Sandbox trait, the fixture and one-off guest tasks on top of Spar's runner, decided with the user when M3 starts, because the tray, notifications and deep links register themselves with the system and must not be tested on the developer's machine.
- **The second consumer is the SQL Server tool** (its own repository, Spar through packages only). It shows that no framework code is app-specific, and exercises long cancellable copies, large result sets, live monitoring and credentials.
- **Its own settings stay in its own project** (user, 2026-11-14): `InvariantGlobalization=false`, the single-file SqlClient switch and SNI exclusion, and the AOT warnings it accepts are the SQL Server tool's choices. They are not Spar defaults or assumptions. Spar documents them as a recipe for apps that use SqlClient.
- Its data-layer findings are already known (see `docs/pitfalls.md`):
  - transactions for cancellable bulk copies. `BatchSize = 0` is not atomic: without a transaction a cancel still kept about 310,000 rows, and it was slow to cancel (0.8–1.0 s, 0.3–0.4 s with `TableLock`) against 34–51 ms with 50,000-row batches. A large copy that must cancel quickly uses bounded batches (`spikes/sqlclient-tool`);
  - `SqlCommand.Cancel()` on the source;
  - `nvarchar` parameters;
  - Kerberos-only integrated authentication on Linux;
  - the ICU and OpenSSL checks.

**Subscription resumption** (user decision):

| Level | Who | Setting | Default |
|---|---|---|---|
| Contract | Backend author, in C# | `[SparSubscription(Resume = SubscriptionResume.Automatic \| Manual)]` | `Automatic`: a subscription must not have side effects. Anything that does, such as draining a queue or creating server-side sessions, is marked `Manual`. |
| Call | Page, in TypeScript | `{ resume: "automatic" \| "manual" }` | The contract's value. The page may only be more cautious: `Manual` subscriptions are typed as `resume?: "manual"`. |
| App | SDK configuration | `configureSpar({ resume: "manual" })` | Lowers the default; it never raises it above a contract's value. |

- **Automatic resume:** re-subscribe with backoff (for example 250 ms doubling to 5 s) when the bridge is back. Emit `reconnecting`, then `reset` before the first new event, then `resumed`. Give up after a limit (for example 30 s) with an outcome-unknown `CallError` (`lost`).
- **One-shot streams are never resumed, and unary commands are never retried.**
- **A resume cursor** is a later, author-opt-in extension.

### M4: release qualification

- **Production run:** the Sandbox test level (Windows), and a clean-machine Linux run.
- **Performance budgets:** measured with one protocol: the host's milestones, release builds, one change per comparison, medians and ranges. Regression suites (BenchmarkDotNet for runtime paths, a build-timing harness for builds) compare each change with stored baselines; see the test-framework topic.
- **Runtime support:** the minimum supported WebView2 Runtime and WebKitGTK versions, confirming or replacing the provisional minimums of M1b and M1c (WebKitGTK 2.48), with fallback when newer interfaces are missing. Also whether arm64 is qualified or documented as untested.
- **Distribution decision:** Windows (single exe, installer?) and Linux (deb, rpm, Flatpak or AppImage, with WebKitGTK as a dependency).
- **Signing and privacy (section 3c):** a signed executable and Linux packages; privacy defaults for telemetry and log redaction.

## Verification

Each milestone's exit lists observed results only. Each result has an evidence file in `docs/evidence/` giving the environment (SDK, Runtime, OS, hardware), the commands, the numbers, and what wasn't verified. Earlier results don't verify later changes.
