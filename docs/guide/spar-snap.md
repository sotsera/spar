---
title: Capture scenes with spar snap
description: Write the states of a Spar app worth a look as scenes, and capture each as a picture, its accessibility tree and its calls.
---

# Capture scenes with spar snap

A scene is a state of your app worth looking at: the empty start, a dialog with something typed in, a page full of data. You write it once, in C#, in the app's end-to-end test project; `spar snap` then captures it whenever you or a coding agent want to see it, the same way every time:

```sh
dotnet spar snap new-page-dialog
```

Each capture is a folder, `obj/spar-snap/new-page-dialog/` in the end-to-end test project:

| File | What it holds |
|---|---|
| `page.png` | The page, 1280 × 800 pixels. |
| `tree.txt`, `tree.json` | The page's elements as assistive technology sees them: role, accessible name, text, value and state, as `spar tree` shows them ([Reading the tree](devtools.md#reading-the-tree)). |
| `calls.json` | Every command the page called, in order, with its time and outcome. |
| `problems.txt` | What went wrong while the scene ran: Spar's warnings and errors, the page's errors and its console messages. Empty when nothing did. |

The `spar` tool comes with a new app's local tool manifest; run `dotnet tool restore` once first (see [Getting started](getting-started.md), section 8).

## Writing a scene

A scene is a static method of the end-to-end test project marked `[SparScene("name")]`. It starts the app through the scene and brings the page to the state to capture, with the same API as a window test; the capture is taken when the method returns. This one is from the Notebook sample, which has a "New page" button in its sidebar and another on its empty page (hence `First`):

```csharp
using Spar.Testing;
using static Spar.Testing.SparAssertions;

namespace Notebook.EndToEndTests;

public static class NotebookScenes
{
    [SparScene("new-page-dialog")]
    public static async Task NewPageDialog(SparScene scene)
    {
        var app = await scene.StartAsync<Program>();
        await app.Page.GetByRole(SparRole.Button, new() { Name = "New page" }).First.ClickAsync();
        var dialog = app.Page.GetByRole(SparRole.Dialog, new() { Name = "New page" });
        await dialog.GetByLabel("Title").FillAsync("Groceries");
    }
}
```

- **Names** are lower case letters, digits and hyphens, each used once in the project. A scene method is `static`, returns `Task` and takes one `SparScene`; the build reports one that does not (SPAR110 to SPAR112). In a test class, make it `internal`, so the test framework does not take it for a test.
- **Options and data:** `scene.StartAsync<Program>(o => o.Services(…))` takes the same options as a window test, so a scene can replace a service or fill data before the page loads.
- **Every scene is also a test.** `dotnet test` runs each scene, and its capture, as a window test named after it, so a scene that no longer reaches its state fails like any other test. These tests run in the CoreCLR lane, not the NativeAOT one.

## The same picture every time

On the same computer, a scene gives the same `page.png`, byte for byte, every time it runs:

- **The page** is 1280 × 800 CSS pixels at a scale of 1, whatever the monitor's scale (`scene.Size` changes it), in the light theme, with `prefers-reduced-motion: reduce` on Windows, and rendered without the graphics card, whose anti-aliasing varies slightly between runs.
- **The clock:** the app's `TimeProvider` and the page's `Date` start at 15 January 2026, 09:30 UTC (`scene.Time` changes it) and then run in real time, so timers and timeouts work and "edited 5 minutes ago" reads the same every run. A label that counts seconds can still differ, since a scene's steps take a little more or less time from one run to the next.
- **Before the capture,** the pointer moves to the page's bottom-right corner, every animation is finished (an endless one stops at its start), the caret is hidden, and the capture waits until the page has been idle for 200 ms: no command running or just finished, no animation, no `setTimeout` still to fire within 10 s (so a layer the page removes after 400 ms is gone in every capture, and so is a message it hides after 5 s), fonts loaded. A page still busy after 10 s is captured as it is, and `problems.txt` says what was still busy.

On another computer, or between Windows and Linux, the fonts and the rendering differ, so the pictures do too; compare captures made on the same machine.

## The command

Run it from the app's folder or the end-to-end test project's: from the app's, it finds the test project beside it that has scenes.

| Command | What it does |
|---|---|
| `spar snap --list` | Lists the scenes. |
| `spar snap <name>...` | Captures the named scenes. |
| `spar snap --all` | Captures every scene. |

| Option | What it does |
|---|---|
| `--project <path>` | The app's project or its end-to-end test project, when it is not the current folder. |
| `--out <folder>` | Where the scenes' folders go, instead of `obj/spar-snap/`. |
| `--no-build` | Uses the last Debug build of the test project instead of building it. |
| `--json` | The result as JSON. |

It builds the end-to-end test project first, unless `--no-build`. Each scene runs in a window no one sees: offscreen on Windows, on a private X server on Linux. The exit code is 0 when every scene was written, 2 when a scene or the build failed (its error is printed), and 64 for a wrong command line or a scene the project does not have.
