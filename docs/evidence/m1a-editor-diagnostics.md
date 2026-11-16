# M1a: contract diagnostics in the editor

Recorded 2026-11-16. What it shows: VS Code's C# language server reports Spar's contract diagnostics on an unsaved edit, at the right token, within a quarter of a second, and clears them when the edit is undone. What it does not show: the squiggles drawn in the VS Code and Visual Studio windows. Decision: "Editor diagnostics through VS Code's language server; the windows still to check".

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

## Not covered

- The squiggles in the VS Code window and in Visual Studio 2026 (18.12.12405.243). The spike saw Visual Studio draw no squiggle for a generator diagnostic; Spar's diagnostics now come from a `DiagnosticAnalyzer`, which this run confirms for VS Code's server only.
- Go-to-definition from a page's call into the C# command in the windows (checked through `tsserver` before).
- A library's diagnostics, which have no location and come at compilation end.
