# M1a: contract diagnostics in the editor

Recorded 2026-11-16. What it shows: VS Code's C# language server reports Spar's contract diagnostics on an unsaved edit, at the right token, within a quarter of a second, and clears them when the edit is undone. The windows were checked the same evening with the user at the PC ("Follow-up: the windows"): both draw the squiggles, Visual Studio only with ReSharper's code analysis off. Decisions: "Editor diagnostics through VS Code's language server; the windows still to check", "Squiggles in both windows; ReSharper's code analysis hides them in Visual Studio".

## Why not in the windows

The user allowed the PC to be used while away. Computer use grants editors at the "click" tier (no typing), and its approval dialog needs someone at the PC: the request for Visual Studio and VS Code was denied with nobody there to approve it. The windows are left for the user (plan, "Checks that need the user at the PC").

## How it ran

`eng/editor-diagnostics.cs`, a file-based app run from outside the repository, drives `Microsoft.CodeAnalysis.LanguageServer.exe` from the C# extension 2.160.4 (VS Code 1.146.0, C# Dev Kit 3.40.215) over stdio, as the editor does: `initialize`, `project/open` with `samples/Notebook/Notebook.csproj`, `textDocument/didOpen` of `Pages/PagesService.cs`, then `textDocument/didChange` with the whole text plus two commands (nothing written to disk), and `textDocument/diagnostic` for each provider the server registered, every 250 ms until both diagnostics appear. Then the original text again, until they clear.

The two commands:

```csharp
[SparCommand]
public Task TagAsync(string[] tags) => Task.CompletedTask;

[SparCommand]
public Task<DateTime> NowAsync() => Task.FromResult(DateTime.UtcNow);
```

## Results (observed, Windows 11, three runs)

| Step | Result |
|---|---|
| Project loaded | 3.5–3.8 s after `initialize` |
| The file as committed | no SPAR diagnostic, no error |
| The unsaved edit | 0.21–0.23 s: SPAR015 at 68:35–68:39 (`tags`) and SPAR024 at 71:27–71:35 (`NowAsync`), from the `DocumentAnalyzerSemantic` provider |
| The edit undone | 0.17–0.18 s: none left |
| The file on disk | unchanged |

SPAR024 arrives as an error: the repository builds with warnings as errors, and the language server applies the project's settings.

## Found

The first run's SPAR024 message named the type `'global::System.DateTime'`; it now uses the C# display name (`'System.DateTime'`, or the list or nullable type that carries it), and a generator test checks the message.

## Follow-up: the windows (same evening, with the user at the PC)

The user approved computer use for Visual Studio and VS Code. Both are granted at the "click" tier (no typing), so each edit was written to disk from outside and the editor reloaded the unchanged buffer; the user trusted the folders in VS Code. Same two commands as above, then the file restored.

**The app checked is a package consumer,** as an app author has it: Spar packed from the working tree (`0.1.0-dev.ide.*`) into a feed of its own, and a one-project app (`<Project Sdk="Spar.Sdk">`, one class contract) in `%TEMP%\spar-ide`, outside any repository, with its own `global.json`, `nuget.config` and package folder. The Notebook sample could not be used in VS Code (below).

| Window | Result |
|---|---|
| VS Code 1.146.0, C# Dev Kit 3.40.215, C# 2.160.4 | Project loaded (status bar "Projects: Notes.App"). After the edit: a red squiggle under `tags` (SPAR015, error, 16:35) and a green one under `NowAsync` (SPAR024, warning, 19:27), both in Problems, within 5 s of writing the file. After the restore: "No problems have been detected", within 4 s. (`m1a-editor-diagnostics/vscode-package-app.jpg`) |
| Visual Studio 2026 18.12.12405.243, ReSharper code analysis on (the user's setup) | SPAR015 and SPAR024 in the Error List as IntelliSense entries within 6 s, but **no squiggle drawn**. The same for the .NET SDK's own CA1822, raised to a warning in the app's `.editorconfig`: listed, not drawn. A compiler error (CS0029) and Visual Studio's built-in IDE analyzers (IDE0130) were drawn. (`m1a-editor-diagnostics/visual-studio-with-code-analysis.jpg`) |
| Visual Studio, ReSharper's "Enable code analysis" unticked by the user | Red squiggle under `tags`, green under `NowAsync`; hovering `tags` shows "SPAR015: The request of command 'TagAsync' is 'string[]'…". After the restore, both gone from the editor and the Error List. (`m1a-editor-diagnostics/visual-studio-without-code-analysis.jpg`, `visual-studio-undone.jpg`) |

So both windows draw Spar's squiggles. With ReSharper's code analysis on, Visual Studio draws none for any analyzer the project references (Spar's or the SDK's), only the Error List entries: ReSharper's own setting, not a Spar behaviour. Which ReSharper option would show them was not found; its Code Inspection settings have "Enable code analysis" and "Treat inspections matching Roslyn analyzers as compiler inspections".

**Ruled out on the way:** that Visual Studio leaves out an analyzer with compilation-level actions (`ContractAnalyzer` registers a compilation-end action for library problems and a compilation action for `Program.cs`). Split into two analyzers, Visual Studio still drew nothing with ReSharper on; the language server sends Spar's diagnostics on `DocumentAnalyzerSemantic` either way. The split was reverted.

**Found:**

- **C# Dev Kit does not load the Notebook sample:** "The project file … is in unsupported format (for example, a traditional .NET Framework project). It need be converted to new SDK style", and "0 project(s) are loaded, and 1 failed to load". The sample imports `Sdk.props` and `Sdk.targets` itself (source mode) instead of naming an SDK in `<Project Sdk="…">`. The file then opens as a loose file, with no analyzer and no squiggle. An app from packages is not affected. The headless check above does not see this, because it opens the project with `project/open` directly.
- **A folder opened through its 8.3 short path** (`C:\Users\USERNA~1\…`) loaded twice in VS Code once the project was added to the workspace (once per spelling), and the open file stayed a loose file, with no squiggle. Opened by its long path, it loaded once and worked.
- **`dotnet msbuild -t:Compile` alone fails in an app** with CS0016 ("Could not find a part of the path …\generated\Spar.Generator\…"): the folder for emitted generator files is not created by that target alone. `dotnet build` passes. Not a user path.

## Not covered

- The Notebook sample (source mode) in VS Code: Dev Kit rejects its project file (above). Visual Studio with the Notebook was not tried.
- Which ReSharper setting, if any, lets Visual Studio draw analyzer squiggles while ReSharper's code analysis is on.
- Go-to-definition from a page's call into the C# command in the windows (checked through `tsserver` before).
- A library's diagnostics, which have no location and come at compilation end.
- Edits typed in the editor (unsaved): the windows were checked with edits written to disk; the language-server run above covers unsaved edits.
