# M3b: serial groups, binary data and settings files

Recorded 2026-11-27. What it shows: the six decisions of "M3b's decide-first questions" built and verified at every layer they touch, on Windows and Linux: serial groups in the dispatcher and the bridge; settings records with their generated serializers, migrations, a local copy for a network file and the installation file beside the executable; browser data kept in memory and folders removed at exit; binary data from the backend as handles and resources on all three hosts, the SDK and the typed test clients; the Notebook's remembered view and last page and its images; and the app model's three new sections through real Spar. The 12 MB budget is measured on both desktop bridges.

Code, on branch `m3`: `d9ffabe` (serial groups), `a4980c3` (settings files), `0ce746a` (browser data in memory, removal at exit, the installation file), `6368cd2` and `b0ea761` (the folder files' own folder, and the tool's link to it), `6a5db06` (binary data, the Notebook, the app model, the installation file's logging section and `spar doctor`).

Machine: the development PC, Windows 11 Pro 10.0.26320, 32 logical CPUs, SDK 11.0.100, Node 24, WebView2 Runtime 158 as installed, Playwright 1.64.1 with its Chromium; Spar's Linux CI image (`spar-linux-ci:eval`, Ubuntu 24.04, WebKitGTK 2.52) in Docker Desktop for the Linux window, NativeAOT and black-box tests. Windows test windows were offscreen; Linux windows on a private Xvfb, the core limit at 1 byte. No window reached the monitor.

## Commands

```bash
dotnet test --project tests/Spar.Tests                                     # runtime, bridge, blobs, settings, installation file
dotnet test --project tests/Spar.Generator.Tests                           # SPAR019, SPAR026, SPAR027, the emitted handlers
dotnet test --project tests/Spar.Sdk.Tasks.Tests
dotnet test --project tests/WireShapes.Tests                               # typed clients: files, settings
dotnet test --project tests/WireShapes.EndToEndTests                       # the browser over loopback
dotnet test --project tests/Spar.WindowTests                               # WebView2, every window test
dotnet test --project tests/Spar.WindowTests -c Release -- --filter-method "*twelve_megabyte*"
dotnet publish tests/Spar.WindowTests -c Release -p:SparTestLane=Aot       # then the executable with -method "*twelve_megabyte*"
dotnet test --project samples/Notebook.EndToEndTests
dotnet test --project tests/AppModel.Tests
dotnet test --project tests/Spar.Tool.Tests                                # spar doctor, the skills' excerpts
cd src/Spar.Client && npm run check && npm test                            # TypeScript 6 and 7
cd samples/Notebook/frontend && npx tsc --noEmit -p . && npx vitest run
docker run … spar-linux-ci:eval sleep infinity                             # as in eng/linux-ci/run.sh's header
docker exec … bash /src/eng/linux-ci/run.sh copy client build window windowaot apps blackbox blackboxdev
```

## Results on Windows

- **Runtime and bridge** (`Spar.Tests`): 915 tests, 908 passed, 7 skipped (the Linux-only ones), at `6a5db06`'s tree. New: serial groups 6, settings 10, the installation file 13, blobs 27 (a handle served until released, gone with its document, the two limits failing only the new call, resources per load and their refusals, the loopback key, a stream's temporary file, the inline types, malformed media types). One full run failed `StreamSessionTests.A_subscription_drops_its_oldest_values_and_reports_the_gap` (one value of four in its batch); it passed five runs in a row alone, and the stream path did not change in M3b.
- **Generator** (`Spar.Generator.Tests`): 151 passed: SPAR019's two cases, SPAR026's three (a request, a stream's items, a declared error), SPAR027's three (a value that is not a blob, a request with a list, a stream), and the emitted code of a blob result, a record holding blobs and a resource.
- **Client step** (`Spar.Sdk.Tasks.Tests`): 69 passed.
- **WireShapes:** the typed clients 96 passed (files 4: handles from bytes and from a stream, a blob in a record, a resource and a null one; settings 2); the browser over loopback 29 passed (files 5: a handle read twice and 404 after its release, from bytes and from a stream; a CSV in a record; a resource's two loads and a 404; 200 images in `<img>` elements under the default policy).
- **Window tests on WebView2** (`Spar.WindowTests`): 242 tests, 140 passed, 102 skipped (the Linux and Sandbox ones); new: the blob route (a 1 MB handle read twice, a 404 after its release) and 100 resources in `<img>` elements, and the installation file's three black-box tests on the Production executable with `WindowHost.spar.json` beside it: a close leaves no profile, cache or data and the log names each value the file set; what a kill left is removed at the next start; an unknown key stops the start with exit code 70.
- **NativeAOT:** `Spar.WindowTests` published with the AOT lane, no warning; its blob and budget tests 3 of 3.
- **The Notebook end to end:** 33 tests, 29 passed, 4 skipped (Sandbox), the new journey included (a PNG dropped into the editor shows in the preview from its resource, its file is beside the page, and after a reload the page and the chosen view come back). One full run failed `A_page_changed_in_another_window_is_never_overwritten_silently` while the app was disposed: a bridge session's `Dispose` disposed its token source while another thread's `Close` was still cancelling it. Fixed in `6a5db06` (the source is disposed only after a finished close); the next full run passed.
- **The Notebook's page tests:** 19 passed (3 new: the remembered view and page, a remembered page that is gone, an image added and one refused), the page's types under TypeScript 7.
- **SDK:** 385 tests (12 new for blobs: the handle, its validation, the loopback key in every address, `release`, `bytes`, a resource's `r=`, a page-test blob), and the TypeScript 6 and 7 checks.
- **The app model** (`AppModel.Tests`): 11 passed: 42 blocks copied verbatim (6.17's page example included, under TypeScript 6 and 7), the stand-in build and its `Program` run, and real Spar serving `preferences` and `reports` with the others.
- **`spar doctor`** (`Spar.Tool.Tests`): 149 passed, the installation file's finding listed with its values and an unknown key reported as an error.

## Results on Linux

To be filled in from the window lane in Spar's CI image at `6a5db06`.

## The binary data budget

A 12 MB `byte[]` handle (12,582,912 bytes) read by the page with `blob.bytes()` from the host's origin, timed by the page from the call to the last byte, five runs after a first one left out, Release (`tests/Spar.WindowTests/BudgetTests.Blobs.cs`); the budget is at most 150 ms on both hosts (decision "M3b's decide-first questions", item 5).

| Host | CoreCLR | NativeAOT | The spike's origin read |
|---|---|---|---|
| WebView2 (Windows) | 70.90 ms (67.40–80.70) | 67.60 ms (66.40–69.00) | 70.7 ms |
| WebKitGTK (Linux, CI image) | not measured (the lane's CoreCLR step runs Debug) | 34.00 ms (29.00–35.00) | 31 ms |

The same bytes as a `byte[]` in the message took 305 ms and 330 ms in the spike.

## Found while building

- **WebView2 answers only with a whole buffer** (`WebResourceRequested` reads the stream on the UI thread): a blob is read in full on the thread pool, then `SHCreateMemStream` copies it on the UI thread; for 12 MB that copy is part of the 67–71 ms. WebKitGTK copies it into `GBytes` on the loop thread. Range requests are not answered, so a `<video>` from a handle plays but cannot seek before it has loaded.
- **The page's console reports a 404 itself** on WebKitGTK and in Chromium, and not in WebView2's page session; the tests that make one on purpose allow it.
- **`react-markdown` drops unknown URL schemes,** so the Notebook's `image:<id>` needs its own `urlTransform`.
- **The race in a bridge session's disposal** (above), older than M3b: the handles' close before the cancel widened it.

## Not verified

- A network settings file on a real share and an unreachable one (the tests use a folder and a file held open without sharing); a Citrix server.
- The removal at exit after a killed session on Linux (to be read from the Linux black-box run), and at a Windows session end (left to the next start, as decided).
- CI on `m3` (no pull request).
