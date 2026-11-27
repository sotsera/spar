# M3c: files, drag and drop, the clipboard, credentials

Recorded 2026-11-27. What it shows: the eight decisions of "M3c's decide-first questions" and the five of "The drop design, from the file-drop spike" built and verified at every layer they touch, on Windows and Linux. That covers:
- grants per window, on the wire and in the generated client;
- the file and folder dialogs on the Windows STA and through `GtkFileDialog` behind the portal check;
- the page's `files` API, with drop areas on WebView2 and WebKitGTK;
- the backend's clipboard and the credential store on both OSes;
- the test host's fakes, drops in window tests and page-test files;
- the Notebook's import, export, copy as Markdown and dropped images;
- the app model's 3.6, 3.7 and 3.22 through real Spar.

No budget was set: dialogs and drops are paced by the user. The spike's drop numbers are in `docs/evidence/file-drop/RESULTS.md` and in decision "The drop design, from the file-drop spike".

Code, on branch `m3`:
- `6d10622`: the spike;
- `c9d3267`: the drop design;
- `5d32f8f`: grants, dialogs and drops on both hosts;
- `a1c9501`: the clipboard, credentials, and drops in window tests;
- `12307fa`: the Notebook, the app model, and the Linux drop fixes;
- the commit that adds this file: the XDND source's event type, the telemetry test's race, the Sandbox dialog test and the records.

Machine:
- **Windows:** the development PC, Windows 11 Pro 10.0.26320, SDK 11.0.100, Node 24, WebView2 Runtime 158 as installed, Playwright with its Chromium. Test windows were offscreen.
- **Linux:** Spar's Linux CI image (`spar-linux-ci:eval`, rebuilt with GNOME Keyring; Ubuntu 24.04, GTK 4.14.5, WebKitGTK 2.52) in Docker Desktop, for the window, NativeAOT and black-box tests. Windows ran on a private Xvfb, with the core limit at 1 byte.

No window reached the monitor, and nothing was written to the user's clipboard or credential store.

## Commands

```bash
dotnet test --project tests/Spar.Tests                       # grants, the page's file commands, drops, the fakes
dotnet test --project tests/Spar.Generator.Tests             # grant wire kinds, the grant scope, SPAR028
dotnet test --project tests/Spar.Sdk.Tasks.Tests             # PickedFile / PickedFolder in the generated TypeScript
dotnet test --project tests/WireShapes.Tests                 # typed clients with grants
dotnet test --project tests/WireShapes.EndToEndTests         # the browser over loopback
dotnet test --project tests/Spar.WindowTests                 # WebView2: drops, the dialog through the page
dotnet test --project samples/Notebook.EndToEndTests         # the Notebook's file journeys
dotnet test --project tests/AppModel.Tests
dotnet test --project tests/Spar.Tool.Tests                  # the skills' excerpts
dotnet test --project tests/Spar.SandboxTests                # the SYSTEM lane: Credential Manager
cd src/Spar.Client && npm run check && npm test
cd samples/Notebook/frontend && npx vitest run
docker exec … bash /src/eng/linux-ci/run.sh copy client build window windowaot apps blackbox blackboxdev
```

## Results on Windows

All at the tree of this commit, except where a line says otherwise.
- **Runtime and bridge** (`Spar.Tests`): at this commit, 952 tests, 943 passed, 9 skipped (the Linux-only ones). The full run before the last fixes had 951, 941 passed, 9 skipped, 1 failed.
  - The failure was `BridgeTelemetryTests.A_stream_has_one_activity_with_its_item_count_and_a_subscription_its_drops`: it reported 0 drops where 5 were expected. The test cancelled its subscription right after the first item, before the producer had written its ten. It now waits for the producer's mark, and passed 3 runs of its class; it had passed 5 runs before the change too.
  - New, grants (15): 128-bit ids; the 1,000th grant refused without revoking one; the window's close and an expiry ending them; an open dialog's grant that cannot write; a safe write that replaces the file, and a failed one that leaves it whole; a save grant creating its file; a missing file as `NotFound`; a folder grant refusing a path that leaves it, an absolute path, Windows devices and streams, and a file or folder link pointing outside; a read-write folder listing, reading, writing, creating and deleting; a read folder changing nothing.
  - New, the page's file commands (16): no dialog or drop granting a file in the browser profile, either the folder Spar asked for or the one in use; a dialog's grant carrying the name, never the path; `dismissed`, `unavailable` and `busy`; reads and writes within 4 MiB; an id the window does not hold, or another window's, refused before anything runs; a grant outliving the document and ending with the window; a file's blob; a folder's commands kept inside it; a drop becoming read-only grants, or `notAccepted`; a claim without a drop; on Linux, only a drag that ended in a trusted drop, claimed once; files attached to another command ignored; a request that is not the SDK's refused.
- **Generator** (`Spar.Generator.Tests`): 153 passed. New: a request holding a grant is read in the grant scope, and SPAR028.
- **Client step** (`Spar.Sdk.Tasks.Tests`): 69 passed.
- **WireShapes:**
  - The typed clients: 105 passed, with 9 new: a dialog's grant returned and sent back as a parameter; a grant in a record, and a save grant writing; an open grant refusing a write; a record with a file and an optional folder; a dismissed dialog as a declared error; a dialog with no answer queued as `unavailable` and a problem; another app's grant refused; the clipboard fake; the credential fake, whose secret never comes back to the page.
  - The browser over loopback: 29 passed.
- **Window tests on WebView2** (`Spar.WindowTests`): 251 tests, 144 passed, 107 skipped (the Linux and Sandbox ones). The four file tests:
  - files dropped on an area become grants the backend reads;
  - a file the area does not take is `notAccepted` with its name;
  - a drop outside every area changes nothing;
  - an open dialog's file (the fake's answer) is read by the page.
  The Sandbox dialog test was built after this run, and built without warnings.
- **The Notebook end to end:** 37 tests, 33 passed, 4 skipped (Sandbox). The four file journeys:
  - Markdown files dropped on the list become pages;
  - a file that is not Markdown is refused with its name;
  - a page exports through the save dialog and copies as Markdown;
  - an image dropped on the editor shows in the preview.
- **The Notebook's page tests:** 24 passed.
- **SDK:** 410 tests passed. They include 18 for `files` and 3 for the page-test files. The `files` tests cover the dialogs' requests and errors; a picked file's reads (`tooLarge` with its limit) and writes (a lone surrogate replaced); a folder's paths; `blob()`; a grant that only the host can have made; and the drop area: a drag of files taken and marked, the page's own drag left alone, a drop sending the area's options with the files, `notAccepted`, a channel without files, and the listeners removed.
- **The app model** (`AppModel.Tests`): 11 passed. That covers 43 blocks, with 3.22 (saved connections with `ICredentialStore`) new, and the contracts `csvExport`, `recipes` and `savedConnections` through real Spar.
- **The tool** (`Spar.Tool.Tests`): 149 passed, the new reference `plugins/spar/skills/spar-app/references/files-clipboard-credentials.md` among the checked excerpts.
- **Size:** `dotnet run eng/check-size.cs`: 850 files, no error or warning.

## Results on Linux (Spar's CI image)

Run with `eng/linux-ci/run.sh` in the rebuilt image (`artifacts/linux-ci-m3c-2.log`, `-3.log`, `-5.log`):
- **Runtime and bridge** (`Spar.Tests`): 952 tests, 923 passed, 29 skipped (the Windows-only ones; the symbolic-link checks ran). An earlier run of the same suite failed `StreamSessionTests.A_subscription_drops_its_oldest_values_and_reports_the_gap`, the flake M3b's evidence names; see "Found while verifying".
- **Window tests** (`Surface=Window`, Debug): 112 tests, 86 passed, 26 skipped. The new ones:
  - the four file tests;
  - the file dialog unavailable without a portal, with GTK's chooser never shown;
  - GNOME Keyring's daemon on the private bus keeping, replacing and deleting a secret, with the 2,560-byte limit.
  The run before the XDND fix failed the two drop tests ("The X server did not report the time of a property change").
- **The Notebook's window journeys** (Debug): 18 of 18, the four file journeys included.
- **NativeAOT:**
  - `Spar.WindowTests`: 109 tests, none failed, 4 skipped, 22 not run (filtered).
  - The Notebook: 15 tests, 1 failed: `WindowJourneyTests.Deleting_a_page_asks_first_and_removes_its_file`, whose "Delete page…" item never appeared after the click on "Page actions", in the image's first run of that executable (16.2 s for the lane, against 5.6–6.6 s after). It did not come back in 5 runs of the class (8 of 8 each) or 3 runs of the lane (15 of 15 each). The journey is M1's; seen once, not explained.
- **Black box,** at `12307fa`'s tree with the drop fixes: `Spar.WindowTests` 85 of 85; the Notebook's 7 of 7; the Development probe's 4 of 4.

## Windows Sandbox

`dotnet test --project tests/Spar.SandboxTests`, without `SPAR_SANDBOX_DESKTOP`: 32 tests, 20 passed, 12 skipped (the user-session, clean-machine and sign-out lanes), in 55 s. `CredentialManagerTests` passed in both jobs that run it, SYSTEM and Deadline, as the guest's SYSTEM account. It covered a missing secret as `NotFound`; set, read, replace; 2,561 bytes as `TooLarge` and 2,560 stored; delete twice; and `ExistsAsync` after. Nothing ran on this machine's Credential Manager.

The user-session lane's new `ClipboardTests` and `FileDialogTests` were skipped: that lane runs only with the user's permission (`SPAR_SANDBOX_DESKTOP=allow`).

## Found while verifying

- **The XDND source read X event types with their padding.** `XEvent`'s first field is an `int`, and the four bytes after it are never cleared by Xlib, so comparing the first `long` matched only while the heap held zeros there. A full lane of window tests failed both drop tests with "The X server did not report the time of a property change", in Debug and NativeAOT, while the drop tests alone had passed in 3 of 3 runs. The source now reads the type as an `int`.
- **A subscription's drained buffer went out one value first.** When the page's credit arrived after the 16 ms batching window, the first buffered value went at once ("an item after a quiet window goes at once") and the others waited for the next window. That is M3b's flake: `[16]` where `[16, 17, 18, 19]` was expected, once on Windows in M3b and once here in the Linux image. The values were never lost, only split. A drained buffer now goes as one batch, and the test waits past the window before its credit. Without the fix it failed 1 run of 1; with it, it passed the full suite on both OSes, and the stream classes 6 runs of 6 on Windows. The code is M3a's (`BridgeStream.PumpLocked`).
- **A telemetry test cancelled before its producer finished:** see "Results on Windows".
- **The serial-group race and the disposed token source** are in the decision's "Found and fixed while building".

## Not verified

- **The OS's own drag:** Explorer onto a WebView2 window (the Sandbox's user-session lane, not run) and a Linux file manager onto a WebKitGTK window.
- **The dialogs:** a Linux file dialog with a portal present, on Wayland, or under WSLg. The real Windows dialog in the Sandbox's user-session lane (written, not run).
- **The clipboard:** the real Windows clipboard, in the same lane.
- **Credentials:** the Linux store without a Secret Service in an image that has GNOME Keyring (the bus starts it on demand), and a locked keyring.
- **CI** on `m3`.
