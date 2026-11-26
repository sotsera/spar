# Working on MyApp

MyApp is a Spar desktop app: a C# backend in `MyApp/` and a React page in `MyApp/frontend/`, shown in one native window (WebView2 on Windows, WebKitGTK on Linux). The page calls the backend through contracts, C# classes marked `[SparContract]`, whose TypeScript client the build generates into `MyApp/obj/spar/client`.

## Commands

Run them from this folder. `dotnet tool restore` once makes `dotnet spar` available.

| Command | What it does |
|---|---|
| `dotnet build` | Builds everything, type-checks the page and regenerates its client. |
| `dotnet test` | The fast tests in `MyApp.Tests`, and the end-to-end tests in `MyApp.EndToEndTests` when the app has them. |
| `npm test` in `MyApp/frontend` | The page's tests. |
| `dotnet spar dev --project MyApp` | Runs the app's Development build until stopped; start it in the background. |
| `dotnet spar reload --project MyApp` | Rebuilds and restarts it: build errors at file and line, or the new run once ready. |
| `dotnet spar tree`, `screenshot`, `click`, `fill`, `wait`, `call`, `calls`, `logs` | Look at and drive the running app (`--project MyApp`; `dotnet spar tree --help`). |
| `dotnet spar snap --all --project MyApp.EndToEndTests` | Captures the scenes of the end-to-end tests, when the app has them: PNG, accessibility tree and calls. |
| `dotnet spar doctor` | Checks the machine and the project, with the fix for each finding. |

## Skills

Spar's skills explain how a Spar app is written, run, tested and published, at the version this app uses:

- with a plugin-aware agent, install the `spar` plugin from Spar's marketplace;
- otherwise, `dotnet spar skills --project MyApp` copies them into `.agents/skills/`.

## Rules

- Contracts return `Result` values; read them with an exhaustive `switch`, and never create one with `default`.
- Nothing slow runs on the window's UI thread: commands, I/O and waits belong in the backend's services.
- Commit `MyApp/MyApp.contracts.snapshot` and `MyApp/frontend/package-lock.json` with the changes that update them.
