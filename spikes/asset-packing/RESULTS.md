# Asset-packing spike: results

Recorded 2026-11-13. Question: how should Spar embed the built frontend in the NativeAOT executable? The user leans to Brotli. Does a Brotli pack work on Windows and on Linux, and what does it cost against the alternatives?

**Answer: Brotli works on both, and it is the right default.**
- Use one embedded resource: a small index plus per-file Brotli (quality 11), with incompressible files stored as they are.
- Decode at host start on a background thread into a cache. Serve stored entries straight from the image.
- The decoder costs about **168.5 KB** on both OSes. Brotli makes the executable smaller once the bundle holds more than about **190–230 KB of compressible text**.
  - The React spike's 227 KB bundle sits exactly at the crossover: raw is 2.1–2.6 KB smaller.
  - The 2.8 MB bundle is 1.75 MB smaller with Brotli: the executable shrinks by 44% (Windows) and 39% (Linux).
- Decoding costs 0.55–0.65 ms (React) and 4.9–5.3 ms (2.8 MB). Started at host start, it overlaps browser startup, and **page load to ready showed no measurable difference between formats on either OS**.
- A trimming feature switch removes the decoder when a build packs nothing compressed. This was tested: the executable was then 1 KB larger than raw and contained no Brotli code. So Spar can fall back to "stored" below the crossover at no cost.
- **Neither web view decodes `Content-Encoding`** on responses the host answers itself: WebView2 (`WebResourceRequested`) and WebKitGTK (custom scheme) both handed the page the compressed bytes. The host must decode on both OSes.
- **Brotli is linked statically on both OSes.** It comes from `System.IO.Compression.Native.Aot.lib` on Windows and `libSystem.IO.Compression.Native.a` on Linux, and only the code the app reaches is pulled in. The publish folder holds only the executable (plus `.pdb` or `.dbg`). A copy of the executable alone in an empty folder decoded the large pack on both OSes.
- Reject the zip format: the zip reader costs 0.5–1.0 MB, Deflate compresses about 15% worse, and the zip bytes differ between Windows and Linux builds. Separate per-file resources give nothing and cost 4–6 KB more.

## Environment

| Item | Value |
|---|---|
| Windows | Windows 11, SDK 11.0.100, NativeAOT win-x64, WebView2 Runtime 157.0.4341.46, 32 CPUs |
| Linux | Docker image `spar-linux-desktop:webkit2.52` (Ubuntu 24.04, SDK 11.0.100, clang, WebKitGTK 2.52.6 4.1 API), container `spar-asset-packing-linux` with Docker defaults, run as root, Xvfb `:99`, unsandboxed WebKit web process, NuGet volume `spar-linux-desktop-nuget` |
| Build settings | Repository `Directory.Build.props` (`TreatWarningsAsErrors`), `PublishAot`, `InvariantGlobalization`; no suppressions, no analyzer overrides. **0 warnings** in every build and publish on both OSes. |

Other agents were running SQL Server containers and builds on the same machine, so absolute timings carry some noise. Compare formats within one OS, not across OSes.

## What was built

| Part | Role |
|---|---|
| `Pack.Tasks` | The `PackFrontend` MSBuild task (`[MSBuildMultiThreadableTask]`, `IMultiThreadableTask`, paths through `TaskEnvironment`, loaded with `TaskHostFactory`). `FrontendPacker` writes the formats. Entries are sorted by ordinal path. Each output is written only when its bytes change, and stale files in the task's own output folder are deleted. |
| `Pack.targets` | `PackFrontendInventory` writes the input list, only when it changes, so a removed file repacks. `PackFrontendAssets` runs the task, with the inputs, the inventory and the task DLL as `Inputs` and a stamp as the output. `EmbedFrontendAssets` always runs and adds the `EmbeddedResource` items. It also compiles `Pack.Runtime` with `SPAR_PACK_<FORMAT>` and sets the `auto` feature switch. |
| `Pack.Runtime/FrontendAssets.cs` | The reader. It parses the index and reads resources in place: NativeAOT returns an `UnmanagedMemoryStream` over the image, which `in-place=True` confirmed on both OSes. It decodes with `BrotliDecoder.TryDecompress` into an array of the known original size, and keeps a decoded cache. Stored entries are returned from the image without a copy. |
| `Pack.Runtime/EncodingProbe.cs` | Compiled only with `-p:PackProbe=true`. It serves a probe page that fetches `br`, `gzip` and identity responses and loads `br` and `gzip` scripts, then reports what the page saw. |
| `Pack.Bench` | A minimal NativeAOT console app. Modes: `startup`, `eager`, `lazy` (first request, then a cache hit) and `decode` (first decode, then the median of 30 uncached decodes per asset). Memory comes from the `Process` API on Windows and `VmHWM` on Linux. |
| `Pack.Win` | A minimal WebView2 host: interop, handlers, `UiContext` and `Desktop.targets` copied from `spikes/react/React.Desktop`. It uses an offscreen, non-activated window and the profile `artifacts/webview2-profiles/asset-packing`. `WebResourceRequested` serves the pack. Decode modes are `--decode eager\|background\|lazy\|passthrough`, plus `--origin` and `--page`. It records the time from `Navigate` to the page's `POST /_spar/ready`, the time spent in the asset handler, and the host's private memory. It waits for the browser process to exit. |
| `Pack.Linux` | A minimal WebKitGTK 4.1 host: the needed declarations from `spikes/linux-desktop`, the `spar://app/` custom scheme, the same decode modes and the same ready signal. WebKit data goes under `artifacts/webview2-profiles/asset-packing-linux`. The host pumps the main context until WebKit's children have exited. |
| `inputs/react/` | A copy of the React spike's Release bundle (`index.html` and one 226,909-byte JS chunk). |
| `large/` | The source of the synthetic large bundle. A Vite 8.3.8 build of real libraries, imported whole: three 0.187.0, echarts 6.1.0, bootstrap 5.3.9 (JS and CSS), bootstrap-icons 1.13.2 (CSS, woff and woff2), React 19.3.1. It is built into `obj/inputs/large/` (2,798,763 bytes, 5 files). It signals ready like the React app does. |
| `scripts/` | `make-large.sh`, `publish-bench.sh`, `run-bench.sh`, `summary.mjs`, `host-win.sh`, `host-summary.mjs`, plus the container scripts `sync.sh`, `publish-linux-host.sh` and `host-linux.sh`. Logs go to `artifacts/asset-packing/`. |

Each build selects a format with `-p:PackFormat` and an input with `-p:PackInput`, and gets its own `bin/<format>-<input>/` and `obj/<format>-<input>/`.

```sh
scripts/make-large.sh
scripts/publish-bench.sh && scripts/run-bench.sh win 5 && node scripts/summary.mjs win
scripts/host-win.sh win-host 5 && node scripts/host-summary.mjs win-host
# Linux, in the container (sources synced to /work by scripts/sync.sh):
scripts/publish-bench.sh && scripts/run-bench.sh linux 5
scripts/publish-linux-host.sh && DISPLAY=:99 scripts/host-linux.sh linux-host 5
```

### Formats compared

| Format | Embedded as | Reader |
|---|---|---|
| `raw` | One resource: the index plus the files, uncompressed | Index parse only; content is served from the image |
| `brotli` | One resource: the index plus per-file Brotli, quality 11, window fitted to the file. Files that Brotli shrinks by less than 5% are stored. | `BrotliDecoder.TryDecompress`, then cached |
| `separate` | An index resource plus one resource per file (`Spar.Frontend.0000`, …), Brotli as above | Same as `brotli` |
| `zip` | The deterministic zip of the earlier spikes (Deflate at `SmallestSize`, fixed timestamps) | `ZipArchive`, then cached |
| `auto` | `brotli` or `raw`, depending on `-p:PackCompress` | The Brotli reader, with the trimming feature switch `Spar.Frontend.Brotli` (`RuntimeHostConfigurationOption`, `Trim="true"`) |
| `none` | Nothing | Nothing (the size baseline) |

Index entry: encoding, path, content type (decided at build time), offset, stored and original lengths, and a SHA-256 of the original. The hash is not checked at runtime.

## Results (observed)

### Compression of the inputs

| File | Original | Brotli q11 (ratio) | Zip Deflate (ratio) |
|---|---:|---:|---:|
| React `index-*.js` | 226,909 | 60,853 (3.73×) | 70,797 (3.21×) |
| React `index.html` | 292 | 127 | 201 |
| **React pack** | 227,201 | **61,176** embedded | 71,240 |
| Large `index-*.js` | 2,172,791 | 526,774 (4.12×) | 648,072 (3.35×) |
| Large `index-*.css` | 311,240 | 33,438 (9.31×) | 43,793 (7.11×) |
| Large `.woff` | 180,288 | stored (Brotli saved under 5%) | 179,494 |
| Large `.woff2` | 134,012 | stored | 134,001 |
| Large `index.html` | 432 | 163 | 273 |
| **Large pack** | 2,798,763 | **875,155** embedded (31.3%) | 1,006,299 (36.0%) |

- **Window size made no difference here.** The fitted window (22 bits for the 2.17 MB JS file), 22 and 24 gave the same 874,675 stored bytes, and decoding took the same time and peak memory (Linux, 3 runs each). The window matters only for files larger than 4 MB.
- **Packing time at quality 11** (task time): 225–378 ms for the React bundle, about 3.0 s for the large one (Linux). The incremental skip hides this cost after the first build.
- **Deterministic across OSes.** The `raw`, `brotli` and `separate` outputs were byte-identical on Windows and Linux (SHA-256 of every file). **The zip differed between the two OSes**, for both inputs.

### Incremental behaviour of the pack step (Windows, detailed log)

| Change | `PackFrontendAssets` | Outputs written | `CoreCompile` |
|---|---|---|---|
| Nothing | skipped | — | skipped |
| `touch` an input (same bytes) | ran (354 ms) | 0 | **skipped** |
| Input content changed | ran | 1 | ran |
| Change reverted | ran | 1 | ran |
| File added (`separate`) | ran | 3 (the index renumbers) | ran |
| File removed | ran: 1 stale file deleted, 2 written | 2 | ran |
| Then nothing | skipped | — | — |

### Executable size

The decoder cost is the size difference over the `none` build, minus the embedded bytes, minus the same figure for `raw`. Linux file sizes are page-aligned, so the Linux figures use the ELF section total (`size -A`).

**Minimal app (`Pack.Bench`)**

| Build | Windows exe | Δ vs none | Linux sections | Δ vs none | Linux file |
|---|---:|---:|---:|---:|---:|
| none | 1,133,568 | 0 | 1,879,572 | 0 | 1,647,024 |
| raw, React | 1,377,792 | 244,224 | 2,128,432 | 248,860 | 1,896,928 |
| **brotli, React** | 1,380,352 | **246,784** | 2,130,570 | **250,998** | 1,896,928 |
| separate, React | 1,384,448 | 250,880 | 2,135,655 | 256,083 | 1,896,928 |
| zip, React | 2,087,936 | 954,368 | 2,956,693 | 1,077,121 | 2,605,608 |
| raw, large | 3,949,568 | 2,816,000 | 4,700,272 | 2,820,700 | 4,469,216 |
| **brotli, large** | 2,194,432 | **1,060,864** | 2,944,554 | **1,064,982** | 2,712,032 |
| separate, large | 2,198,528 | 1,064,960 | 2,949,927 | 1,070,355 | 2,712,032 |
| zip, large | 3,023,360 | 1,889,792 | 3,891,733 | 2,012,161 | 3,539,496 |
| auto, React, `PackCompress=false` | 1,378,816 | 245,248 | — | — | — |
| auto, React, compressed | 1,380,352 | 246,784 | — | — | — |

| Code cost | Windows | Linux |
|---|---:|---:|
| Reader, index and in-place access (`raw` minus payload) | 16,827 | 21,463 |
| **Brotli decoder** on top of that | **168,781** | **168,359** |
| Separate resources on top of `brotli` | 4,096 | 5,085 |
| Zip reader on top of `raw` (minimal app) | 866,301 | about 984,000 |

- **Brotli on Linux** adds about 33 KB of `.text` (native) and puts about 122 KB more into `.rodata` (the decoder's static dictionary). The large inputs gave the same decoder cost (185,709 B on Windows; 189,827 B on Linux, including the reader).
- **The feature switch works.** The `auto` build with `PackCompress=false` is 1,024 bytes larger than `raw`, and its `.pdb` has no `BrotliDecoderDecompress` symbol (the compressed `auto` build has 8). The compressed `auto` build is exactly the size of `brotli`. This was measured on Windows only.

**Inside the real hosts** (the decoder costs the same; the zip reader is cheaper because the host already shares some of its code):

| Build | WebView2 host exe | Δ vs none | WebKitGTK host sections | Δ vs none |
|---|---:|---:|---:|---:|
| none | 2,528,768 | 0 | 1,852,244 | 0 |
| raw, React | 2,772,992 | 244,224 | 2,102,300 | 250,056 |
| brotli, React | 2,775,552 | 246,784 | 2,104,430 | 252,186 |
| separate, React | 2,779,648 | 250,880 | 2,110,404 | 258,160 |
| zip, React | 3,131,904 | 603,136 | 2,925,709 | 1,073,465 |
| raw, large | 5,345,280 | 2,816,512 | 4,674,140 | 2,821,896 |
| brotli, large | 3,589,632 | 1,060,864 | 2,918,414 | 1,066,170 |
| separate, large | 3,593,728 | 1,064,960 | 2,924,660 | 1,072,416 |
| zip, large | 4,066,816 | 1,538,048 | 3,860,749 | 2,008,505 |

In the WebView2 host the zip reader costs about 515 KB over `raw`, close to the 493,056 bytes in the decision log.

### Decode time, startup and memory (`Pack.Bench`, 5 runs; startup is 21 process runs; medians)

**Windows**

| Build | Eager decode ms (range) | Lazy first requests ms | Cache hit ms | Warm decode ms (median of 30) | Peak working set KB (Δ vs none) | Allocated KB | Process wall ms (Δ) |
|---|---:|---:|---:|---:|---:|---:|---:|
| none | — | — | — | — | 13,020 | 37 | 94.4 |
| raw, React | 0.00 | 0.00 | 0.000 | 0.04 (copy) | 13,052 (+32) | 39 | 95.9 (+1.5) |
| brotli, React | **0.64** (0.64–0.69) | 0.66 | 0.000 | 0.58 | 13,520 (+500) | 261 | 97.9 (+3.5) |
| separate, React | 0.65 | 0.65 | 0.000 | 0.56 | 13,572 (+552) | 263 | 93.3 (−1.1) |
| zip, React | 0.42 | 0.42 | 0.001 | 0.32 | 13,900 (+880) | 299 | 93.6 (−0.7) |
| raw, large | 0.00 | 0.00 | 0.000 | 0.31 (copy) | 13,064 (+44) | 40 | 99.3 (+4.9) |
| brotli, large | **5.28** (5.21–5.38) | 5.26 | 0.001 | 4.93 | 16,424 (+3,404) | 2,467 | 100.6 (+6.2) |
| separate, large | 5.07 | 5.10 | 0.001 | 4.80 | 16,504 (+3,484) | 2,471 | 95.9 (+1.6) |
| zip, large | 3.73 | 3.65 | 0.001 | 3.13 | 17,332 (+4,312) | 2,815 | 99.2 (+4.8) |

Process wall time on Windows was measured from Git Bash. Its noise (87–146 ms) hides differences of a few milliseconds, so use the in-process decode times instead.

**Linux (container)**

| Build | Eager decode ms (range) | Lazy first requests ms | Warm decode ms | Peak RSS (`VmHWM`) KB (Δ) | Allocated KB | Process wall ms (Δ) |
|---|---:|---:|---:|---:|---:|---:|
| none | — | — | — | 4,196 | 78 | 1.6 |
| raw, React | 0.00 | 0.00 | 0.06 (copy) | 4,260 (+64) | 80 | 1.6 (0.0) |
| brotli, React | **0.55** (0.53–0.56) | 0.55 | 0.45 | 4,748 (+552) | 302 | 2.3 (+0.6) |
| separate, React | 0.55 | 0.55 | 0.45 | 4,752 (+556) | 304 | 2.2 (+0.6) |
| zip, React | 0.40 | 0.38 | 0.35 | 5,376 (+1,180) | 325 | 2.2 (+0.6) |
| raw, large | 0.00 | 0.00 | 0.25 (copy) | 4,284 (+88) | 81 | 1.6 (0.0) |
| brotli, large | **4.90** (4.83–5.14) | 4.98 | 3.88 | 9,368 (+5,172) | 2,507 | 6.9 (**+5.3**) |
| separate, large | 5.01 | 4.96 | 3.92 | 9,532 (+5,336) | 2,511 | 6.8 (+5.2) |
| zip, large | 3.83 | 4.06 | 3.43 | 8,852 (+4,656) | 2,841 | 5.9 (+4.3) |

**Decode speed** (warm medians, MB of output per second):

| Content | Brotli, Windows | Brotli, Linux | Deflate (zip), Windows | Deflate, Linux |
|---|---:|---:|---:|---:|
| JS | 396–503 | 505–625 | 705–781 | 657–720 |
| CSS | 738–744 | 915–944 | 1,772 | 1,456 |

So Brotli costs about **2.0–2.5 ms per MB of decoded JS on Windows** and 1.6–2.0 ms on Linux. Deflate decodes about 1.5× faster but compresses worse. The first decode in a process, including the first touch of the resource pages, took 0.6–1.0 ms (React) and 4.5–5.3 ms (large JS).

Memory is the decoded copy of the compressed entries, held in the cache: 0.22 MB for React and 2.48 MB for the large bundle. Stored entries are never copied (`raw`: +32–88 KB).

### Served through the real hosts (5 runs per mode; medians)

**Windows, WebView2, origin `https://app.localhost`** (see the origin finding below; warm profile, offscreen window):

| Build | Decode | Navigate → ready ms (range) | Asset handler ms (all requests) | Requests | Host private KB |
|---|---|---:|---:|---:|---:|
| raw, React | (none) | 71.7 (66.8–83.2) | 2.63 | 2 | 8,684 |
| brotli, React | lazy | 69.8 (67.1–72.0) | 3.51 | 2 | 9,208 |
| brotli, React | eager | 71.0 (68.0–72.4) | 2.58 | 2 | 9,020 |
| brotli, React | background | 74.5 (66.3–5,012) | 2.77 | 2 | 9,112 |
| separate, React | lazy / eager / background | 72.8 / 78.1 / 70.9 | 3.41 / 3.02 / 3.26 | 2 | 8,860–9,116 |
| zip, React | lazy / eager / background | 76.7 / 70.0 / 69.2 | 3.46 / 2.81 / 2.66 | 2 | 9,008–9,104 |
| raw, large | (none) | 180.9 (175.9–196.5) | 4.22 | 5 | 13,352 |
| brotli, large | lazy | 191.9 (180.7–204.7) | **8.61** | 5 | 16,040 |
| brotli, large | eager | 181.0 (153.2–208.6) | 3.22 | 5 | 15,932 |
| brotli, large | background | 187.7 (180.3–194.0) | 2.94 | 5 | 16,068 |
| separate, large | lazy / eager / background | 187.6 / 176.3 / 190.1 | 8.80 / 3.41 / 3.48 | 5 | 15,792–16,044 |
| zip, large | lazy / eager / background | 180.7 / 183.1 / 182.5 | 7.21 / 3.84 / 3.50 | 5 | 15,988–16,336 |
| brotli or separate, both inputs | passthrough (`Content-Encoding: br`) | **never ready** (20 s timeout) | — | 1 + favicon | — |

- Eager decoding at host start took 2.1–2.9 ms (React) and 6.3–7.1 ms (large) in the host. In background mode it finished long before the controller was ready (about 190–260 ms after host start).
- One background run took 5,012 ms; the other four took 66–78 ms. The cause was not investigated.
- The handler time is dominated by `SHCreateMemStream` and `CreateWebResourceResponse` (about 2.5 ms for 2 requests, even with nothing to decode). Lazy decoding adds the decode time on the request path (+4.4 ms for the large bundle).

**Linux, WebKitGTK 4.1, custom scheme `spar://app/`:**

| Build | Decode | Navigate → ready ms (range) | Load started → ready ms | Asset handler ms | App PSS KB |
|---|---|---:|---:|---:|---:|
| raw, React | (none) | 598.6 (590.0–616.9) | 37.0 | 0.19 | 83,848 |
| brotli, React | lazy / eager / background | 596.4 / 601.8 / 616.5 | 40.4 / 45.3 / 58.3 | 0.80 / 0.24 / 0.24 | 84,035–85,426 |
| separate, React | lazy / eager / background | 606.7 / 619.3 / 596.6 | 39.9 / 56.0 / 44.9 | 0.80 / 0.24 / 0.24 | 83,992–84,624 |
| zip, React | lazy / eager / background | 594.7 / 618.4 / 611.9 | 38.4 / 59.3 / 56.9 | 0.71 / 0.25 / 0.25 | 84,208–85,179 |
| raw, large | (none) | 695.9 (686.6–701.6) | 136.4 | 0.19 | 85,991 |
| brotli, large | lazy / eager / background | 697.5 / 691.6 / 693.8 | 141.2 / 136.9 / 136.7 | **5.83** / 0.83 / 0.87 | 88,952–89,983 |
| separate, large | lazy / eager / background | 705.6 / 699.5 / 696.5 | 145.5 / 141.4 / 140.8 | 6.16 / 0.94 / 0.93 | 88,290–89,668 |
| zip, large | lazy / eager / background | 699.4 / 694.3 / 692.0 | 143.2 / 137.2 / 137.3 | 4.68 / 0.84 / 0.89 | 89,237–90,082 |
| brotli or separate, both inputs | passthrough | **never ready** (20 s timeout) | — | — | — |

- About 550 ms of "navigate → ready" is the unsandboxed 4.1 web-process start that the Linux desktop spike already found. "Load started → ready" excludes it.
- Raw content is passed to WebKit without a copy (`g_bytes_new_static` over the image); decoded content is copied (`g_bytes_new`).
- Eager decoding took 0.6–0.8 ms (React) and 5.1–6.6 ms (large) at host start.
- WebKit's children were gone 31–53 ms after close in all 104 runs.

**Page load did not depend on the format.** The differences between formats (±10 ms on Windows, ±10 ms on Linux) are within run-to-run noise. Lazy decoding moves 4–6 ms of the large bundle's decoding onto the first request. Eager or background decoding takes it off that path.

### `Content-Encoding` on host-answered responses (probe build, `/probe.html`)

| | WebView2 157 (`WebResourceRequested`) | WebKitGTK 2.52.6 (custom scheme) |
|---|---|---|
| `fetch` of a 2,209-character text, `Content-Encoding: br` | 200, header visible (`br`), **body 68 characters (still compressed)** | 200, header visible, **body 68 characters** |
| Same with `gzip` | body 83 characters (compressed) | body 83 characters |
| Identity | 2,209 characters, correct | 2,209 characters, correct |
| `<script>` with `br` / `gzip` | did not run | did not run |
| The real bundle with `br` (`--decode passthrough`) | `index.html` garbled, no further requests, never ready | same |

The Linux custom scheme does **not** honour `Content-Encoding`, so Linux cannot skip decoding either. Both OSes need the decoder in the host.

### How Brotli is linked

| | Windows | Linux |
|---|---|---|
| Native library given to the linker | `System.IO.Compression.Native.Aot.lib` (in `link.rsp` for every build, including `none`) | `libSystem.IO.Compression.Native.a` (in the publish command line) |
| Native code in the image | Only when reached. `BrotliDecoderDecompress` is in the `.pdb` of `brotli` builds and absent from `raw`; zlib's inflate symbols only in `zip` builds. | Same: Brotli symbols in the `.dbg` of `brotli` builds only, inflate only in `zip` builds |
| Dynamic dependencies | none added | `readelf -d` NEEDED: `libc.so.6`, `libm.so.6`, `ld-linux-x86-64.so.2` only |
| Publish folder | `Pack.Bench.exe`, `Pack.Bench.pdb` | `Pack.Bench`, `Pack.Bench.dbg` |
| Executable alone in an empty folder | decoded the large pack (5.3 ms) | decoded the large pack (5.2 ms) |

The ordinary (JIT) `dotnet build` output on Linux contains `libSystem.IO.Compression.Native.so`, copied from the runtime pack for CoreCLR runs. NativeAOT publication does not use it.

## Crossover

The decoder costs D ≈ 168.5 KB; a file with compression ratio r saves `raw × (1 − 1/r)`. Brotli gives the smaller executable when the savings exceed D:

| Content (ratio measured here) | Raw size where Brotli starts to win |
|---|---:|
| CSS (9.3×) | about 189 KB |
| JS (4.1×) | about 222 KB |
| JS (3.7×, the React bundle) | about 230 KB |
| HTML (2.3–2.6×) | about 290–300 KB |
| woff, woff2, images | never: stored as they are |

- **The React bundle is right at the crossover.** At 227 KB its savings are 166 KB, so raw is 2.6 KB smaller on Windows and 2.1 KB smaller on Linux.
- **Every larger bundle wins.** The 2.8 MB one saves 1.75 MB.
- **By time, raw always wins, but by little.** Brotli costs 1.6–2.5 ms per MB of decoded JS. Done in the background at host start, it overlapped browser startup on both OSes and did not show in page load.
- **Not measured:** the cost on the other side, a raw executable 1.75 MB larger on a cold disk cache (more pages to read at first touch).

If an app already links the Brotli decoder for another reason (for example `HttpClient` automatic decompression), D falls to about zero. This was not measured.

## Recommendation

1. **Pack format.** Use one embedded resource `Spar.Frontend`: a small binary index (path, content type, encoding, offset, stored and original length, SHA-256), then the data.
   - Compress per file with Brotli quality 11. A window fitted to the file, capped at 24, is enough; 22 gave identical output here.
   - Store a file as it is when Brotli saves less than 5%, as with fonts and images.
   - Produce it with a compiled, multithreadable MSBuild task. Write only on change and keep a stamp, as tested here: CoreCompile stays skipped on a touch and on a no-op.
2. **Compression is a build decision with a trimmed fallback.** When the total savings are below the decoder's ~170 KB (plus a margin), pack everything stored and set the trimming feature switch (`Spar.Frontend.Brotli=false`). Then the host library's decoder path is removed: tested, +1 KB against raw. Otherwise compress.
   - A simpler alternative is "always Brotli". It costs at most ~170 KB, and only in very small apps.
3. **Runtime.** Open the index at host start, which takes 0.02–0.25 ms. Decode all compressed entries on a pool thread while the browser starts, and let the first request wait if decoding has not finished.
   - Keep decoded bytes in a cache for the process lifetime. It costs as much memory as the decoded compressed entries: 0.2 MB here for React, 2.5 MB for the large bundle.
   - Serve stored entries from the image without a copy. On Linux use `g_bytes_new_static`; on Windows `SHCreateMemStream` copies anyway.
   - Lazy decoding works too, but puts 4–6 ms of the large bundle on the first request.
4. **Never rely on `Content-Encoding`.** Neither host's web view decodes it on host-answered responses.
5. **Reject zip and per-file resources.**
   - Zip's reader costs 0.5–1.0 MB, it compresses 15% worse, and its bytes are not identical across OSes.
   - Separate resources cost 4–6 KB and give nothing.

### Debug builds and `no-store`

- **Debug and Development builds embed no bundle.** The dev server serves the page, and the pack target does not run, as with `SparDevServerBuild` in the React spike. Without a pack the host reads nothing.
  - The decoder's size matters only for the NativeAOT Production publish, where it is the only build that packs.
  - The JIT `dotnet build` output carries `libSystem.IO.Compression.Native.(so|dll)` anyway.
  - A Release build run under CoreCLR uses the same reader unchanged, though that was not run here.
- **`no-store` fits the cache.** Every reload re-requests every asset (decided, and re-observed in the React spike).
  - With the decoded cache, a reload costs only the response creation: about 2.5–3.5 ms on Windows for the large bundle's 5 requests, 0.2–0.9 ms on Linux.
  - Without a cache, each reload would decode again: about 5 ms for the large bundle, 0.6 ms for React.
  - Cache-control does not affect packing. The SHA-256 in the index would allow `ETag`s later, but `no-store` makes them unnecessary.

## Other findings

- **`.local` origins can stall every page load by 2 s.**
  - Midway through the Windows run the machine's network state changed: Wi-Fi and USB tethering on a phone hotspot, plus an OpenVPN adapter.
  - From then on, with `https://app.spar.local`, **each subresource request reached `WebResourceRequested` about 2.0 s after `index.html`** (navigate → ready 2,036–2,059 ms in 34 runs, and with a fresh profile too). `--no-proxy-server` made no difference.
  - `Resolve-DnsName app.spar.local` took 1.0 s, and `Dns.GetHostAddresses` 11.2 s. Chromium resolves the host of each subresource request even though the host answers it in-process, and `.local` goes to multicast name resolution.
  - With the same build, `https://app.localhost` took 54 ms and `https://app.spar.invalid` 89 ms; `app.spar.local` again took 2,048 ms.
  - The React spike's host uses `app.spar.local`. **The desktop host's origin should be a name that never reaches system name resolution.** `.localhost` was the fastest here; its other implications (secure-context rules, cookies, how other software treats it) were not checked.
  - The Windows host measurements above therefore use `app.localhost`. The 34 delayed runs are in `artifacts/asset-packing/host-win-run-dns-delay.txt`.
- **Browser exit after close:** 81 of 104 Windows runs took 235–250 ms; 23 took about 6.4–6.6 s. This matches the slow exits in the decision log; not investigated.
- **Bench caveat:** `Pack.Bench` reports `heap-kb=0` because no GC ran during these short runs. Allocated bytes are the meaningful figure.

## Failures and causes

1. **CS8175:** a `stackalloc` span used in local functions of the packer. Replaced with a small array.
2. **Brotli decoding failed for the stored `.woff`:** the reader decoded every entry of a Brotli pack. Entries now carry their own encoding; identity entries are copied or served in place.
3. **`publish-linux-host.sh` printed nothing:** `$([ … ] && echo -probe)` returned non-zero under `set -e` and ended the script silently. Replaced with a suffix variable.
4. **The first Windows host series was invalid** (2 s per load) because of the `.local` resolution described above. It was stopped and repeated with `app.localhost`.
5. **Passthrough (`Content-Encoding: br`) never became ready on either OS**, as expected from the probe. Each host ended the run with its 20 s timeout.
6. A WebKit `WebProcess didn't exit as expected` error appeared once, in a probe run. All 104 measured Linux runs ended with no children left.

## Observed, skipped, not tested

**Observed:**
- Strict NativeAOT publishes with 0 warnings:
  - `Pack.Bench`: 9 builds on Windows and 9 on Linux, plus 2 `auto` builds on Windows and 3 window variants on Linux;
  - `Pack.Win`: 9 builds plus 1 probe build;
  - `Pack.Linux`: 9 builds plus 1 probe build.
- Bench: 5 eager and 5 lazy runs, 1 decode run (30 repeats per asset) and 21 startup runs per build, on each OS.
- Hosts: 104 Windows runs (5 per mode; passthrough once per build) and 104 Linux runs.
- Probes: on WebView2, twice (with `app.localhost` and with `app.spar.local`); on WebKitGTK, once.
- The incremental pack steps listed above.
- Pack bytes compared across OSes.
- The executable alone in an empty folder, on both OSes.

**Skipped:**
- GTK4 / WebKitGTK 6.0 and the sandboxed 4.1 web process: only unsandboxed 4.1, as root.
- Rerunning the 34 `.local`-delayed Windows runs with `.local` after the network recovered.
- The feature switch on Linux.
- A `tiny` input to measure the reader's code directly: it was derived from the payload instead.

**Not tested:**
- Cold disk cache (first run after boot) for raw against Brotli executables.
- ARM64, and other distributions or WebKitGTK versions.
- Bundles over 4 MB per file, where the window starts to matter.
- Many small files: hundreds of chunks with lazy routes, where the per-request handler cost dominates.
- Images and wasm.
- An app that already links Brotli (`HttpClient` decompression).
- Decoding without a cache under repeated reloads.
- Verifying the SHA-256 at runtime.
- A visible (on-screen) window; the CoreCLR (JIT) path of the hosts.
- The `.localhost` origin's wider implications.

## Downloads and leftovers

- **npm** (`large/`, first install): three, echarts, zrender and tslib (pulled in by echarts), bootstrap, @popperjs/core, bootstrap-icons, react, react-dom, vite 8.3.8 and its toolchain. `large/node_modules` is 150 MB on disk; npm did not report the download size. NuGet and Docker downloaded nothing: the packages, the image and the NuGet volume were already present.
- **Removed:** the container `spar-asset-packing-linux` (with its Xvfb and build servers) and the volume `spar-asset-packing-work`. No spike process remains on Windows.
- **Disposable outputs** (the repository's `artifacts/`, ignored by git):
  - `artifacts/asset-packing/` (293 KB of logs);
  - `artifacts/webview2-profiles/asset-packing` (19 MB), `asset-packing-b` (15 MB) and `asset-packing-linux` (17 KB).
- **Spike-local outputs:** `bin/` and `obj/` per project and variant, `obj/inputs/large/` and `large/node_modules/`.

## Follow-up: per-file Deflate inside the same index (2026-11-15)

The research review asked for a Deflate control on the real bundle before M1 packing: per-file Deflate inside Spar's own index, without the zip reader, which this spike had not measured. Added: a `deflate` format (encoding 3; `DeflateStream` at `SmallestSize` per file, the same 5% rule as Brotli; decoded with `DeflateStream` over the image) and a `notebook` input (the Notebook sample's Vite bundle from M1a: 11 files, 772,422 B, with 7 already-compressed `woff2` fonts). Windows only, `Pack.Bench`, strict NativeAOT. Raw logs and pack sizes in `results/deflate-control/`.

| Build | Exe | Pack | Code (exe − none − pack) |
|---|---:|---:|---:|
| none | 1,133,568 | — | — |
| raw, notebook | 1,924,096 | 773,562 | 16,966 |
| **brotli, notebook** | **1,683,456** | 364,557 | 185,331 |
| deflate, notebook | 1,729,536 | 387,053 | 208,915 |
| raw, large | 3,949,568 | 2,799,243 | 16,757 |
| **brotli, large** | **2,194,432** | 875,155 | 185,709 |
| deflate, large | 2,349,056 | 1,006,918 | 208,570 |

- **Brotli keeps the smaller executable:** 46,080 B smaller on the Notebook bundle and 154,624 B on the large one. Its payload is 6.2% and 15.1% smaller, and its decoder about 23 KB smaller (168,365 against 191,949 B over the reader). Even where an app already links zlib for something else, Brotli's payload alone stays 22–132 KB smaller.
- **Deflate decodes faster:** decode-all 0.88 against 1.53 ms (Notebook) and 3.29 against 5.05 ms (large), medians of 5; the main chunk at 739 against 417 MB/s. Both run off the UI thread at start.
- **No difference that matters at start:** process start, median of 21, 88.0 (Deflate) against 88.2 ms (Brotli), 85.8 ms with no frontend; peak working set 13.9 against 13.8 MB.
- **So the research review's result does not repeat here** (a whole executable of 1.60 MB with Deflate against 2.27 MB with Brotli, on another runtime and a synthetic corpus). Packing stays Brotli, as the recommendation above says.
