# M1d exit: the checks at the PC

Five checks from the plan's table "Checks that need the user at the PC" (the M1d rows), about 30 minutes in all. Everything is prepared by [`prepare-user-checks.sh`](prepare-user-checks.sh) (run 2026-11-24; its log in `artifacts/m1d-exit/user-checks/prepare.log`), so each check starts at once. Report for each step: pass, or what you saw instead. M1d closes when the results are recorded in `docs/decisions.md`.

**2026-11-24:** checks 1–3 were run by the agent at the user's request (decision "The IDE checks at the M1d exit"). What it could not see, and VS Code's Hot Reload, are rows of the plan's table; checks 4 and 5 are left. **Later on 2026-11-24** the user ran the rest, guided by the agent, after the agent re-ran the preparation from `9107661`: results in decision "The checks at the PC at the M1d exit".

Prepared folders (`%TEMP%` is your temporary folder):

| What | Where |
|---|---|
| Spar's packages from this tree, and the `nuget.config` that serves them to the apps below | `%TEMP%\spar-guide-m1d6b\feed`, `%TEMP%\spar-guide-m1d6b\nuget.config` |
| A new app from the template, built once in Debug, its `spar` tool restored | `%TEMP%\spar-guide-m1d6b\work\Notes` |
| A new app whose `Notes\frontend` was removed | `%TEMP%\spar-guide-m1d6b\work\Init` |
| A new app with the template's splash turned on, its first render held 1.5 s, and three buttons light / dark / system calling `setColorScheme`, published | `%TEMP%\spar-guide-m1d6b\work\Theme\Notes\bin\Production\Release\net11.0\win-x64\publish\Notes.exe` |
| The Notebook's Production executable | `artifacts\m1d-exit\notebook-win\Notebook.exe` in the repository |

**Before you start (optional, keeps Spar's development packages out of your own NuGet folder):** start Visual Studio, VS Code and the terminal of check 4 from a PowerShell where you first ran:

```powershell
$env:NUGET_PACKAGES = "$env:TEMP\spar-package-tests\nuget-packages"
$env:npm_config_cache = "$env:TEMP\spar-package-tests\npm-cache"
$env:DOTNET_CLI_HOME = "$env:TEMP\spar-guide-m1d6b\dotnet-cli-home"
```

Without them the IDEs restore the packages (version `0.1.0-dev.guide…`) into `%USERPROFILE%\.nuget\packages`. The apps you start write their data to your own `%LOCALAPPDATA%` (`com.example.Notebook.Development`, `com.example.Notebook`, `com.example.Notes.Development`, `com.example.Notes`); delete those folders afterwards if you like.

## 1. The Notebook's F5 in Visual Studio

1. Open `Spar.slnx` in Visual Studio. Set **Notebook** as the startup project and pick the **Desktop** launch profile. Press **F5**.
   Look for: the build succeeds (Spar's steps under Visual Studio's MSBuild) and the window shows the notebook.
2. Set a breakpoint in `samples/Notebook/Pages/PagesService.cs`, `ListAsync`. In the window, press F5 (reload) or create a page.
   Look for: the breakpoint hits. Wait 30 s, then Continue: the page goes on, and no "not responding" dialog appears.
3. In `samples/Notebook/Pages/MarkdownPageStore.cs`, `ListAsync`, change `page.Title` to `page.Title + " (edited)"`, save, and apply Hot Reload.
   Look for: the toast "Backend updated: MarkdownPageStore (revision 1)" in the window, and the list shows the edited titles.
4. In `samples/Notebook/Pages/PageTypes.cs`, add `string? Excerpt = null` as the last parameter of `PageSummary`, and save.
   Look for: Visual Studio reports ENC0020 ("Renaming enum value 'Shape_…' requires restarting the application") and does not apply it. Restart: the build's type-check now fails on `frontend/src/notebook.test.tsx` (`excerpt` missing), since responses write nullable properties explicitly; add `excerpt: null` to its two `PageSummary` fixtures, and the window comes back.
5. Pick the **Loopback** profile and press F5.
   Look for: the address `http://127.0.0.1:<port>/#token=…` is printed; on a new dev session the default browser is asked to open it once, and a page of the same session reloads by itself. The notebook works. Stop debugging: the app exits.

Undo the edits of steps 3 and 4 afterwards.

## 2. The Notebook's F5 in VS Code with C# Dev Kit

1. Open the repository folder; let C# Dev Kit load `Spar.slnx`. Open `samples/Notebook/Program.cs`, then Run and Debug > "C#" > Notebook, and pick the **Desktop** launch profile.
   Look for: the window shows the notebook.
2. The breakpoint of check 1, step 2.
   Look for: it hits; after 30 s and Continue, no "not responding" dialog.
3. With C# Dev Kit's Hot Reload setting on, the edit of check 1, step 3, saved.
   Look for: applied on save, the toast shows.
4. The edit of check 1, step 4, saved.
   Look for: reported as needing a restart (ENC0020), never applied; after a restart the window comes back.
5. The **Loopback** profile, as in check 1, step 5.

Undo the edits afterwards.

## 3. A new app's F5 (Visual Studio, then VS Code)

1. Open `%TEMP%\spar-guide-m1d6b\work\Notes\Notes.slnx` in Visual Studio.
   Look for: the three projects load with no warning; `Notes` is the startup project and `Desktop` its first profile.
2. Press **F5**.
   Look for: the build succeeds and the window shows the Notes page. Type a name and press **Greet**: "Hello, <name>!".
3. Set a breakpoint in `Notes\Greetings\GreetingService.cs`, `GreetAsync`, and press Greet again.
   Look for: the breakpoint hits.
4. Close Visual Studio. Open the folder `%TEMP%\spar-guide-m1d6b\work\Notes` in VS Code with C# Dev Kit and repeat steps 2 and 3 (Run and Debug > "C#" > Notes, profile **Desktop**).
5. In Test Explorer, in both IDEs.
   Look for: the fast tests (`Notes.Tests`) and the end-to-end tests (`Notes.EndToEndTests`, by their `Surface` trait) are listed, and the fast tests run and pass.

## 4. `spar init frontend`'s question in a real terminal

In a terminal (Windows Terminal with PowerShell):

1. `cd $env:TEMP\spar-guide-m1d6b\work\Init`, then `dotnet tool restore`, then `cd Notes` and `dotnet spar init frontend --framework react`.
   Look for: the new files and the two diffs, then the question "Write these changes? [y/N]".
2. Press **Enter**.
   Look for: "Nothing written.", exit code 1 (`$LASTEXITCODE`), and still no `frontend` folder.
3. Run `dotnet spar init frontend --framework react` again and answer **y**.
   Look for: the frontend is written, npm installs, exit code 0; then `dotnet build` succeeds.
4. `dotnet spar doctor` in the same terminal.
   Look for: the text report, with its `frontend-type-check` warning.

## 5. The page side of start-up on an on-screen window

1. In Explorer, open `artifacts\m1d-exit\notebook-win` in the repository and double-click `Notebook.exe`. Close it, and start it again the same way.
   Look for: the window opens in the window colour of your theme, with no white or wrong-coloured flash; on these warm starts the splash mark (a notebook outline) never shows. If you can, also start it once right after a reboot: if that start takes longer than about a second, the mark shows once and fades out.
2. Switch Windows to the other theme (Settings > Personalization > Colors), start `Notebook.exe` again, then switch back.
   Look for: the same in the other theme. (This also changes your theme setting; skip it if you prefer.)
3. Double-click `%TEMP%\spar-guide-m1d6b\work\Theme\Notes\bin\Production\Release\net11.0\win-x64\publish\Notes.exe`.
   Look for: this app holds its first render 1.5 s on purpose, so the splash (a circle) fades in after about 0.35 s, stays, and fades out when the page appears; no flash of another colour before or after it.
4. In that window press **dark**, then **light**, then **system**.
   Look for: each time the window's background, the title bar and the page switch together, with no frame where one has changed and the others have not; the status line says "Scheme: dark" (and so on).
5. Press **dark**, close the app, and start it again.
   Look for: it starts dark from its first frame, splash included. Press **system** before closing it, so it follows Windows again.
