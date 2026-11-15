# M1a: frontend packing

Recorded 2026-11-15. What it shows: the app build packs its page into its own assembly after the asset checks, with the frontend's third-party licences beside it; the runtime reads the pack in place; and the Notebook's browser journeys run on the page its executable carries. Decisions: "Frontend packing: the Deflate control keeps Brotli" and "Frontend packing built".

## What was added

| Path | Contents |
|---|---|
| `src/Spar.Sdk.Tasks/Pack/FrontendPack.cs`, `SparPack.cs` | The format (an index, then each file Brotli at quality 11 with a fitted window, or stored when Brotli saves under 5%), the asset checks, and the task: Vite's `.vite/` folder is never packed, its `license.md` becomes the notices, Brotli-compressed. |
| `src/Spar/Hosting/SparFrontend.cs` | The reader: the index parsed from the resource in place (an `UnmanagedMemoryStream` over the image), stored files served from it, compressed ones decoded once and cached; `DecodeAll` for a host's start; `Notices()`. |
| `src/Spar/Hosting/SparContentTypes.cs` | The content types, now in their own file and linked into the task assembly, so a packed file carries the type the hosts send. |
| `src/Spar/Runtime/SparCatalog.cs`, the generator | `AppAssembly`, set by the generated catalog, so the runtime finds the app's pack even when the process is a test. |
| `src/Spar.Sdk/Sdk/Sdk.targets` | `SparFrontendPack` (reruns only when Vite ran) and `SparEmbedFrontend` (`Spar.Frontend`, `Spar.ThirdPartyNotices`); the whole Spar chain now runs before `PrepareResources`, where MSBuild settles embedded resources. |
| `src/Spar.Client/src/vite/index.ts` | `build.license: true`. |
| `src/Spar.Loopback/`, `src/Spar.Testing.Browser/` | An `ILoopbackPage` with two sources: folders and the app's packed page. `o.Page()` serves the packed page and decodes it in the background at start; `o.Page(folder)` stays for test pages. |
| Tests | `tests/Spar.Sdk.Tasks.Tests/PackTests.cs` (12: round trip, compression rule, determinism, seven refusals, case collisions, notices); `samples/Notebook.Tests/PackagingTests.cs` (2: the resources, and the notices naming React and the Radix menu); the Notebook journeys now call `o.Page()`. |

## Results (observed)

| Check | Result |
|---|---|
| The Notebook's pack | 11 files, 772,422 B into 364,205 B; notices 123,417 B into 3,057 B |
| `dotnet test --solution Spar.slnx` | 250 of 250 (Spar.Sdk.Tasks.Tests 52, Notebook.Tests 12, Notebook journeys 7 on the packed page, three runs in a row) |
| NativeAOT lane | 156 of 156; the Notebook tests read both resources from the native image |
| Production publish of the Notebook | 2,908,672 B (2,541,056 B before: the pack and the notices); still no `LoopbackHost`, `SparTestSession`, `Kestrel` or `_spar/channel` in the image; the decoder is not in it yet, because nothing in Production reads the page before the desktop host |
| Unchanged build | the client step, install, type-check, Vite build and packing all skip |
| `-mt` build, size check | clean; 104 source files |

## Found and fixed

- A journey checked for a page's file right after clicking "Create page"; it passed by timing until the page came from the pack. It now waits for the page to show first.

## Follow-up: notices settings and the build order (2026-11-15)

Decision "Source maps stay out of the executable; third-party notices are reported and copyleft is flagged". The build line reads "the third-party notices of 109 packages from Vite's licence file (3,057 B)" (106 MIT, 2 ISC, 1 0BSD). `SparThirdPartyNotices=false` builds with no notices; a missing file fails with SPAR111; back to the default, then an unchanged build, both behave. While checking `false`, the embedding target turned out to run before packing (see the pitfalls); with one ordered entry target, a build from an empty `obj/` and `bin/` embeds its pack on the first pass. `dotnet test` 250 of 250; `-mt` clean; the Notebook's NativeAOT tests 12 of 12.

## Not done or not verified

- **Not built:** the feature switch that trims the decoder when nothing is compressed (`spikes/asset-packing` measured it), WebView2's and the static loader's notices (they arrive with the Windows host in M1b), reading the pack from a desktop host, and a link check on Windows (creating a symbolic link needs a privilege the tests do not take; the check uses `FileInfo.LinkTarget`).
- **Not run:** Linux, CI, a clean clone.
