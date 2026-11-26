# Resource-usage spike: results

> A spike. Its code was deleted with `spikes/` at the start of M2; it is in git history at [`spikes/resource-usage/`](https://github.com/sotsera/spar/tree/24f5db0942833047c81fa692908410596f3b8960/spikes/resource-usage) (commit `24f5db0`), and this record and its `results/` moved here. Paths under `spikes/` below name that history.

Recorded 2026-11-14.

**Requirement (user).** A C# service reports how much disk the web engine's data takes and how much memory the backend and the web engine use, so an app can show it: read once (a settings page) or live (a status bar, through a subscription that samples on a timer and keeps only the newest value). Each measure (backend memory, web-engine memory, each data folder) can be read or subscribed to on its own. Sampling never runs on, or blocks, the UI thread.

**Questions.**
1. Windows memory: which processes belong to the app's WebView2 browser, how to list them, their kinds, and which per-process measure matches Task Manager. Are processes of another app on the same user-data folder counted? What does one sample cost, on and off the STA, and which calls must be on the STA?
2. Backend memory: `Environment.WorkingSet`, `GC.GetGCMemoryInfo`, `GC.GetTotalMemory(false)`, private bytes. Which to report under NativeAOT, and their cost.
3. Linux memory: WebKit's processes under the app's process tree, PSS and private memory from `/proc`, cost, and `/proc` access across the bubblewrap sandbox.
4. Disk: size of the WebView2 user-data folder and profile, and of WebKitGTK's data and cache folders, while the engine runs; walk methods and their cost; `webkit_website_data_manager_fetch`.
5. One memory measure with the same meaning on both OSes.
6. Sampling intervals, change threshold, and whether the bridge's subscription model serves it without allocation churn.

## Summary

| Question | Answer |
|---|---|
| 1. Windows processes | `ICoreWebView2Environment8.GetProcessInfos` (slot 18) lists **5 processes**: browser, renderer, GPU and 2 utilities (network and storage service). The crashpad handler is not listed (only a command-line scan finds it). `ICoreWebView2Environment13.GetProcessExtendedInfos` (slot 25) maps the renderer to its frame URL. |
| 1. Measure | **`PROCESS_MEMORY_COUNTERS_EX2.PrivateWorkingSetSize`** from `GetProcessMemoryInfo`. In 25 of 25 processes it was **identical** to the performance counter "Working Set - Private", which is Task Manager's "Memory" column. Small page: **75.0 MB** for the browser's processes (74.8–77.1, 5 runs) plus **13.5 MB** for the host. `PrivateUsage` (commit, "Private Bytes") is 150 MB; the working set is 327 MB. |
| 1. Same folder, other app | **Counted.** A second instance on the same user-data folder shares the browser: each instance's list held both renderers (94.6 MB each instead of 75). With a separate folder, a separate browser and no overlap. One folder per app, plus single instance, avoids it. |
| 1. Cost and thread | `GetProcessInfos` must run on the STA: from a pool thread it returns `0x802A000C` (`UI_E_WRONG_THREAD`). It costs **29 µs**. Reading memory runs on any thread: **2.5 µs for all 5 processes** with handles kept open, 0 bytes allocated. `ProcessInfosChanged` (on the STA) tells when to re-list. `Process.GetProcessById(...).PrivateMemorySize64` costs **15.9 ms** for 5 processes. |
| 2. Backend | Report the **host process's private working set** (EX2 on Windows, 135 ns) or **anonymous resident memory** (`/proc/self/statm`, 2.3 µs) on Linux, with the GC heap as detail. `Process.PrivateMemorySize64` costs **3.2 ms** on Windows (a snapshot of every process) and reads **about 70 GB** on Linux (a virtual size): never use it. `GC.GetGCMemoryInfo` is as of the last GC (0 before the first one). |
| 3. Linux | The app's tree: `WebKitNetworkProcess`, `WebKitWebProcess`, `xdg-dbus-proxy` and 4 `bwrap`. Found through `/proc/PID/task/*/children` (107 µs) or a full `/proc` scan (215 µs); same set 5 of 5. Every process, including the web process inside its PID namespace, was **readable without errors**. Small page: web process PSS 88.8 MB, Private_Dirty 20.2 MB; descendants PSS 110 MB, USS 58 MB, **Private_Dirty 27.9 MB**; app (UI) process Private_Dirty 42.4 MB. |
| 3. Cost | `smaps_rollup` of 7 processes: **0.55 ms** (0.42 ms for the app's own). `statm` with kept handles and `pread`: **4.6 µs**, 0 bytes. `statm` resident − shared equals Private_Dirty within 0.4 MB. |
| 4. Disk, Windows | Realistic profile (1 MB localStorage, 8 MB IndexedDB, 4 MB Cache Storage, 4 MB HTTP cache): folder **26.3 MB in 228 files, walked in 0.9 ms**; the profile subfolder 19.7 MB. Large profile (148.6 MB, 673 files): **0.9–1.1 ms**. 20,000 files: 3.4–5.1 ms. No walk error while the browser runs; locked files do not matter, because nothing is opened. **But directory entries lag for files open for writing:** 13 s after the fill, the per-file size saw 1.0 MB more (the localStorage log) than every directory-enumeration method. |
| 4. Disk, Linux | Realistic: data 10.4 MB + cache 12.1 MB in 143 files, **0.2 ms**; large 195 MB in 1,279 files, 1.2 ms; 20,000 files 19 ms. **`webkit_website_data_manager_fetch` does give sizes** for disk cache, localStorage, IndexedDB and DOM cache: 19.0 MB in 1.7 ms (large: 158.9 MB in 13.6 ms), with under 0.6 ms on the main thread. It covers 81–84 % of the folder walk. |
| 5. Cross-OS measure | **"Private memory": resident memory that only the app's processes hold and that no file backs.** Windows: EX2 `PrivateWorkingSetSize` (Task Manager's "Memory"). Linux: `statm` resident − shared (≈ `Private_Dirty`). Small page: Windows **88.5 MB** (engine 75.0 + host 13.5); Linux **70.7 MB** (engine 28.3 + app process 42.4). The two hosts render differently (see the table in section 5). |
| 6. Intervals | Memory: **default 1 s, floor 250 ms.** Disk: **default 30 s, floor 5 s**, or read on request. Threshold: push when a value moves by **more than 1 % or 1 MiB**. The bridge's bounded drop-oldest subscription with **capacity 1** gives latest-value-wins. The sampler's read and write allocated **0 bytes per tick** (Windows, and Linux with kept handles). |

## Versions and method

| Item | Value |
|---|---|
| Windows | Windows 11 Pro 10.0.26320, i9-13900K (32 logical CPUs), 96 GB, Intel UHD 770 + GTX 1650, NTFS 4 KB clusters on the repository drive |
| WebView2 | Runtime 157.0.4341.46; SDK headers Microsoft.Web.WebView2 1.0.4314.35; hand-declared `[GeneratedComInterface]` interfaces, slots counted with `tools/vt.mjs` |
| .NET | SDK 11.0.100, NativeAOT win-x64 and linux-x64, repository `Directory.Build.props` (warnings as errors, code style enforced): **0 warnings, 0 errors, no suppressions**. `Res.Win.exe` 2,933,248 bytes; `Res.Linux` 2,744,016 bytes |
| Linux | WSL 2 Ubuntu 24.04, kernel 6.18.58.1, WebKitGTK 2.52.6 (6.0 API), GTK 4.14.5, GLib 2.80.0, bubblewrap 0.9.0. GTK's **Broadway** backend on a private display, so no window reached the Windows desktop. Broadway has no GL, so **WebKit ran with hardware acceleration off** (logged by WebKit). Web-process sandbox on (always on in 6.0). Private `HOME`, XDG folders and `XDG_RUNTIME_DIR` under `~/spar-spikes/resource-usage`, and a private D-Bus session per run |
| Node | 24.21.0 (Windows) and 24.22.3 (WSL), for the loopback test server only |

**What was built** (all in this folder):

| Part | Role |
|---|---|
| `Res.Win/` | NativeAOT WebView2 host: one offscreen (`-32000,-32000`), never-activated window (`WS_EX_NOACTIVATE`, `SW_SHOWNOACTIVATE`). The user-data folder is under `artifacts/webview2-profiles/resource-usage/<name>`, passed explicitly. Reputation checking is off (from `spikes/webview-profiles`). Pages are served from the embedded `www/` on `https://app.localhost`. Scenarios: `memory`, `disk`, `shared`; plus a `walk` verb without a browser. Window, UI context, loader targets and the controller and `ICoreWebView2` declarations are copied from `spikes/webview-profiles`. |
| `Res.Win/Interop/WebView2.cs` | Declared here: `ICoreWebView2Environment8` (IID `d6eb91dd`; slots 3–18: `AddBrowserProcessExited` 12, `GetUserDataFolder` 15, `add/remove_ProcessInfosChanged` 16/17, `GetProcessInfos` 18). `ICoreWebView2Environment13` (IID `af641f58`; `GetProcessExtendedInfos` 25). `ICoreWebView2ProcessInfo` (`84fa7612`; 3 `ProcessId`, 4 `Kind`) and its collection (`402b99cd`). `ICoreWebView2ProcessExtendedInfo` (`af4c4c2e`; 3 `ProcessInfo`, 4 `AssociatedFrameInfos`) and its collection (`32efa696`). The frame-info collection (`8f834154`), its iterator (`1bf89e2d`) and `ICoreWebView2FrameInfo` (`da86b8a1`). The completion handler `f45e55aa`, `ProcessInfosChangedEventHandler` `f4af0c39`, and web messages (`ICoreWebView2` slots 34/35, args `0f99a40c`). The created environment is wrapped from its raw pointer, and Environment8 and 13 are feature-detected by cast (both were present). |
| `Res.Win/Win32.cs` | `K32GetProcessMemoryInfo` with `PROCESS_MEMORY_COUNTERS_EX2` (96 bytes, asserted at start-up), `K32QueryWorkingSet` (private, shared and proportional pages), the command line (`NtQueryInformationProcess` class 60), and two walks: `FindFirstFileExW` (`FindExInfoBasic`, `FIND_FIRST_EX_LARGE_FETCH`) and `GetFileInformationByHandleEx(FileFullDirectoryInfo)`, which gives logical and allocated sizes in bulk. |
| `Res.Win/EngineProcesses.cs` | Sketch of the split: the list is refreshed on the STA when `ProcessInfosChanged` fires, one handle is kept per process, and `Read()` on any thread allocates nothing. |
| `Res.Linux/` | NativeAOT GTK 4 + WebKitGTK 6.0 host: one window and view, one `WebKitNetworkSession` with data and cache folders under `artifacts/resource-usage/linux/<name>` (in the WSL working copy). Pages come from `spar://app` (the `www/` folder) or from the loopback server. Native imports and the GLib context are copied from `spikes/webview-profiles`; the `fetch`, `WebKitWebsiteData` and `lstat` imports were checked against the installed headers. `lstat` offsets (`st_mode` 24, `st_size` 48, `st_blocks` 64, 144 bytes) were checked with ctypes. |
| `Res.Linux/ProcMem.cs` | `smaps_rollup`, `statm` and `NSpid` readers. Descendants by a full `/proc` scan and by `/proc/PID/task/*/children`. `LinuxEngine` keeps `statm` and `smaps_rollup` handles and re-reads them with `pread` at offset 0. |
| `Shared/` | Options and statistics, backend counters (`Backend.cs`), .NET walks (`Walk.cs`: `FileInfo`, a `FileSystemEnumerator` with a value transform, and a per-file size), the walk-scale verb, and **`Sketch.cs`**, the sampler sketch (see section 6; **a sketch, not an API proposal**). |
| `www/` | `small.html` (the same page as `spikes/linux-memory`; it reports two animation frames after load), and `fill.html`/`fill.js`, which write incompressible localStorage, IndexedDB (1 MB `ArrayBuffer`s), Cache Storage and cacheable fetches, then report `navigator.storage.estimate()`. |
| `tools/serve.mjs` | Loopback-only (`127.0.0.1`) server for the disk scenario: `www/`, plus `/blob` with random, `Cache-Control: public, max-age=86400` bytes. |
| `scripts/run-win.sh`, `wsl/*.sh`, `win/wsl.sh` | Harnesses. The Windows memory run reads the performance counters (`Get-Counter`) while the host waits on a go-file, and the host logs EX2 just before and after. |
| `tools/summarize.mjs` | Writes `results/summary-win.txt` and `results/summary-linux.txt` (medians and ranges). |

```sh
# Git Bash, in spikes/resource-usage
dotnet publish Res.Win -c Release -o Res.Win/bin/publish
bash scripts/run-win.sh memory 5; bash scripts/run-win.sh shared 3; bash scripts/run-win.sh disk 3
Res.Win/bin/publish/Res.Win.exe walk --path ../../artifacts/resource-usage/walk-scale --counts 1000,5000,20000
win/wsl.sh sync; win/wsl.sh 'bash wsl/publish.sh; bash wsl/run-linux.sh memory 5; bash wsl/run-linux.sh disk 3'; win/wsl.sh fetch
node tools/summarize.mjs results
```

**Runs.** Memory: 5 runs per OS, each a new process with a fresh folder, measured 3 s after the small page reported. Disk: 3 runs × 2 fills per OS, fresh folders. Shared folder: 3 runs. Timings are medians over 10–200 iterations inside each run; the tables give the median of the run medians and the range across runs. Nanosecond costs come from batches of 1,000 calls (Windows' `Stopwatch` resolution is 100 ns). The WSL runs found no other WebKit process in the distribution (`results/linux/runs.txt`, "FOREIGN 0"). On Windows, 14 `msedgewebview2` processes of other apps ran throughout; none appeared in this spike's lists.

**UI-thread probe.** A pool thread posts a ping to the UI thread about every millisecond (the next only after the last arrived), and the UI thread records the dispatch delay. Three 3-second phases: idle, a full sample every 100 ms on the UI thread, and the same on a pool thread.

Evidence: `results/win/*.log` and `results/linux/*.log` (one file per run), `results/win/memory-N.counters.txt` (performance counters; other apps' processes appear by pid only), `results/summary-win.txt`, `results/summary-linux.txt`, `results/*/walk-scale.log`.

## Results: Windows

### 1. Processes and their memory

**Which processes.** `GetProcessInfos` returned the same 5 kinds in 5 of 5 runs: `BROWSER` (0), `RENDERER` (1), `GPU` (4), and two `UTILITY` (2). A command-line scan (every `msedgewebview2` whose command line carries `--user-data-dir=<folder>\EBWebView`) found these as the network and storage services, plus a **crashpad handler that `GetProcessInfos` never lists**. The crashpad handler's memory was not measured.

`GetProcessExtendedInfos` completed in 0.10 ms (20 calls per run). It associated exactly one frame (`https://app.localhost/small.html`) with the renderer and none with the other processes. That is what tells two apps' renderers apart on a shared browser (see below), and only by URL.

**Per kind, small page** (kB; median and range of 5 runs; the utility row holds both utilities):

| Kind | Private working set | Private usage (commit) | Working set | Proportional (`QueryWorkingSet`) |
|---|---|---|---|---|
| browser | 31,028 (30,576–31,588) | 39,648 | 126,808 | 44,630 |
| renderer | 11,332 (11,320–14,072) | 20,984 | 55,704 | 17,572 |
| GPU | 21,628 (21,544–21,744) | 69,832 | 79,080 | 29,763 |
| utility (network 8.6 MB, storage 4.3 MB) | 6,316 (4,252–8,612) | 11,664 | 35,368 | 10,390 |
| **All 5** | **75.0 MB** (74.8–77.1) | 150 MB (150–153) | 327 MB (326–328) | 111 MB (110–111) |
| Host process (backend) | 13.5 MB (13.2–13.5) | 16.4 MB | 32.9 MB | – |

- **Task Manager's number.** The host read `\Process(msedgewebview2*)\Working Set - Private`, `Private Bytes` and `Working Set` with `Get-Counter` and matched them by `ID Process`. EX2 values taken just before and just after the read were equal in **25 of 25 processes**, to the kB:
  - `PrivateWorkingSetSize` = "Working Set - Private", which is Task Manager's "Memory (private working set)";
  - `PrivateUsage` = "Private Bytes" = `Process.PrivateMemorySize64`;
  - `WorkingSetSize` = "Working Set".
- **Not checked by eye:** Task Manager's own grouping of the WebView2 processes under the app. It needs the user's desktop and is listed below.
- **`QueryWorkingSet`'s private pages** (74.6 MB) agree with EX2's private working set within 1 %. Its proportional estimate (111 MB, share counts saturate at 7) is the nearest thing to Linux PSS, but it costs 5.4 ms for 5 processes and needs `PROCESS_VM_READ`.
- **Access:** `PROCESS_QUERY_LIMITED_INFORMATION` alone was enough for `GetProcessMemoryInfo` on every browser process, sandboxed renderer included (5 of 5 processes per run, error 0). `QueryWorkingSet` with `PROCESS_QUERY_INFORMATION | PROCESS_VM_READ` also worked on all of them.
- **What a user would call "memory used":** the private working set. It is what Task Manager shows, it excludes DLL and font pages shared with every other process (the reason the working set is 4× larger), and it excludes committed memory that is not resident. `PrivateUsage` doubles the GPU process (70 MB commit against 22 MB resident).

**Another app on the same user-data folder** (3 runs, `results/win/shared-*.log`):
- Instance A ran alone on folder X for 4 s, then instance B started on X and instance C on folder Y.
- B attached to A's browser (same browser pid) and got its own renderer.
- From then on, **both A's and B's `GetProcessInfos` returned 6 processes, both renderers included**, and both summed 94.6 MB instead of 75. The extended infos showed A's renderer with `?instance=a` and B's with `?instance=b`.
- When B left, A was back to 5 processes. C, on its own folder, had its own browser and never saw A or B.
- So processes are attributed **per browser, that is per user-data folder**, not per app. Spar's one folder per app plus single instance (`spikes/webview-profiles`) gives a correct count. Two instances with identical options would double-count the shared processes.

**`ProcessInfosChanged`** fired 5 times during start-up and the first navigation, and once at the close, always on the STA. After the page was up it did not fire again in the 25 s of measurements, so a list cached on that event stays valid.

### 2. Cost of a sample, and which thread

| Operation (5 processes) | Thread | Median (range of 5 runs) | Allocated |
|---|---|---|---|
| `GetProcessInfos` + read the list | STA | 28.6 µs (28.0–29.2) | 11.1 kB |
| same, from a pool thread | pool | fails: `0x802A000C` `UI_E_WRONG_THREAD` | – |
| `GetProcessExtendedInfos` to completion | STA | 0.10 ms | – |
| list + `OpenProcess` + EX2 + `CloseHandle` for each | STA | 38.0 µs (37.7–40.6) | 11.2 kB |
| `OpenProcess` + EX2 + `CloseHandle` for each | pool | 7.4 µs | 0 |
| **EX2 on kept handles** | **pool** | **2.5 µs** | **0** |
| `Process.GetProcessById(pid).PrivateMemorySize64` for each | pool | 15.9 ms (15.2–16.5) | 25 kB |
| `QueryWorkingSet` for each | pool | 5.4 ms | 0 |
| command-line scan of all `msedgewebview2` | pool | 3.2 ms | 453 kB |

- **On the STA:** only `GetProcessInfos`, `GetProcessExtendedInfos` and the `ProcessInfosChanged` subscription. COM objects of the environment refuse other threads.
- **Seen in passing, not relied on:** the returned collection object could be read from a pool thread (`GetCount` and `GetValueAtIndex` returned `S_OK`).
- **Everything else is plain Win32 on process handles** and runs anywhere.
- **The design that follows:** re-list on the STA when `ProcessInfosChanged` fires (about 60 µs including opening new handles), and read on a pool thread.
- **UI probe:**
  - idle: ping max 0.4 ms (median of runs);
  - a full sample on the STA every 100 ms: ping max 1.0 ms (0.3–2.5), each sample 0.1 ms;
  - sampling on a pool thread: ping max 1.1 ms (0.4–3.6), the same noise as idle.

  A Windows sample is too cheap for the STA to notice either way. The rule "not on the UI thread" holds anyway, for the Linux numbers below and for disk.

### 3. Backend memory (host process, NativeAOT)

| Read | Value (small page) | Cost | Allocated |
|---|---|---|---|
| EX2 `PrivateWorkingSetSize` (self) | 17.8 MB (18.6 after a GC) | 135 ns | 0 |
| EX2 `PrivateUsage` (self) | 20.7 MB | 138 ns | 0 |
| `Environment.WorkingSet` | 37.2 MB (= EX2 `WorkingSetSize`) | 125 ns | 0 |
| `Process.GetCurrentProcess().PrivateMemorySize64` | 20.7 MB (= `PrivateUsage`) | **3.2 ms** (also with a cached `Process` and `Refresh()`) | 3.5 kB |
| `GC.GetTotalMemory(false)` | 12.5 MB before any GC, 2.3 MB after one | 13.5 ns | 0 |
| `GC.GetGCMemoryInfo()` heap / committed | **0 / 0 before the first GC**; 2.4 / 14.5 MB after | 25 ns | 288 B |
| `GC.GetTotalAllocatedBytes(false)` | 12.5 MB | 2.6 ns | 0 |

The host's private working set (13.5 MB right after start, 17.8 MB after the measurements) is the backend's whole footprint on Windows: the managed heap, the AOT image's private pages and the WebView2 loader. `GC.GetGCMemoryInfo` describes the last GC, so it stays empty until a GC has run. `Process.PrivateMemorySize64` takes a snapshot of every process on the machine, which explains its 3.2 ms.

### 4. Disk

Fills, through `http://127.0.0.1:<port>/fill.html` from the loopback server:
- **realistic:** 1 MB localStorage, 8 MB IndexedDB, 4 MB Cache Storage, and 40 × 100 kB fetches into the HTTP cache;
- **large:** 4 MB localStorage, 64 MB IndexedDB, 32 MB Cache Storage, and 400 × 100 kB fetches.

Measured 3 s after the fill ("running"), again 13 s after it ("later"), and after the browser exited ("exited"). Warm file-system cache. 3 runs each.

| | Files | Logical | Allocated | Walk (median, any directory method) | Per-file sizes |
|---|---|---|---|---|---|
| Realistic, user-data folder, running | 228–229 | 26,268 kB | 26,830 kB | **0.9 ms** (first walk 0.9–1.5 ms) | 26,407 kB, 1.6 ms |
| Realistic, profile (`EBWebView\Default`), running | 203 | 19,745 kB | 20,294 kB | 0.6–0.7 ms | 19,885 kB, 1.2 ms |
| Realistic, user-data folder, later | 233 | **26,367 kB** | 26,998 kB | 0.9–1.1 ms | **27,393 kB**, 1.8 ms |
| Realistic, user-data folder, exited | 231 | 27,364 kB | 27,458 kB | 0.9–1.0 ms | 27,364 kB |
| Large, user-data folder, running | 673 | 148,582 kB | 149,331 kB | 0.9–1.1 ms (`FindFirstFileEx` first walk 10 ms) | 148,721 kB, 3.0 ms |
| Large, profile, running | 647 | 142,060 kB | 142,798 kB | 0.7–0.8 ms | 2.6 ms |
| Large, user-data folder, exited | 676 | 152,748 kB | 153,174 kB | 0.9–1.1 ms | 3.0 ms |

Methods compared on the same folder: `DirectoryInfo.EnumerateFiles` + `Length`, a `FileSystemEnumerator<T>` with a value transform, `FindFirstFileExW` with `FIND_FIRST_EX_LARGE_FETCH`, `GetFileInformationByHandleEx(FileFullDirectoryInfo)`, and a per-file size (`new FileInfo(path).Length`).

- **All directory-enumeration methods agree to the byte and cost the same** (0.6–1.1 ms). Only `FileFullDirectoryInfo` also gives allocated sizes in the same pass (+0.4 % to +2 % here).
- **Walk cost follows directories more than files.** 228 and 673 files cost the same, because both profiles have about 80 folders. Without a browser (`results/win/walk-scale.log`, 2 kB files in folders of 100):
  - 1,000 files: 0.2 ms;
  - 5,000 files: 0.9 ms;
  - 20,000 files: 3.4 ms (`FileFullDirectoryInfo`) to 5.1 ms (`FileInfo`), about 0.2 µs per file;
  - per-file sizes: 2.6–3.4 µs per file (67 ms for 20,000).
- **Locked files do not matter.** Two files per profile could not be opened even with `FileShare.ReadWrite | Delete` (by their count and the LevelDB layout, the `LOCK` files). Every walk still counted them with 0 errors, because a directory walk opens no file.
- **Directory entries lag behind files that are open for writing.**
  - At "later", every directory method reported 26,367 kB, while the per-file sizes reported 27,393 kB. The 1.0 MB difference is the localStorage LevelDB log, which `Local Storage` showed as 0 kB in the directory listing until the browser closed it.
  - After the exit both agree (27,364 kB).
  - NTFS updates the size in the directory entry lazily for open files. The live size comes only from the file itself, at 3 µs per file.
  - At "running" (3 s) the difference was only 139 kB: Chromium had not yet committed localStorage to disk.
- **The page's view is smaller.** `navigator.storage.estimate()` reported 12.6 MB (IndexedDB 8.40 MB and Cache Storage 4.20 MB) for the realistic fill. It ignores localStorage, the HTTP cache and Chromium's own folders (`GrShaderCache` 4.7 MB, `GPUCache`, `DawnGraphiteCache`, `DawnWebGPUCache`, `ShaderCache` about 0.5 MB each, `BrowserMetrics` 1.3 MB). Large: 100.7 MB against a 142 MB profile.
- **Where the bytes are** (realistic, running):
  - inside the profile: `Cache` (HTTP cache) 4.8 MB in 47 files, `IndexedDB` 8.2 MB, `Service Worker` (Cache Storage) 4.1 MB;
  - outside the profile, in `EBWebView`: `GrShaderCache` 4.7 MB and `BrowserMetrics` 1.3 MB.
- **So for a Spar app:**
  - "the app's web data" is the user-data folder, which includes about 6.5 MB of browser-wide caches;
  - "site data" is the profile folder.

## Results: Linux (WebKitGTK 6.0)

### 1. Processes, sandbox and memory

**The app's tree** (5 of 5 runs, the same both ways):
- the app process (pid P);
- `WebKitNetworkProcess` (parent P);
- `bwrap` (P) → `bwrap` → `xdg-dbus-proxy`;
- `bwrap` (P) → `bwrap` → `WebKitWebProcess`.

There is no GPU process: GTK 4 on Broadway has no GL, and WebKitGTK 2.52 started none. WebKitGTK 6.0 has **no API that returns these pids** (the headers have none; `webkit_web_view_get_page_id` is not a process id). So the tree is found through `/proc`.

**Sandbox and `/proc`.**
- `NSpid` shows the inner `bwrap` processes as pid 1 in a new PID namespace, and `xdg-dbus-proxy` and `WebKitWebProcess` as pid 2 in theirs (for example `486/2`).
- The app process still read `smaps_rollup`, `statm` and `status` of every one of them without an error, in 5 of 5 runs.
- bwrap runs unprivileged here (user namespaces, not setuid), so the processes stay dumpable to their user.

**Small page** (kB; median and range of 5 runs):

| Process | PSS | USS (Private_Clean + Private_Dirty) | Private_Dirty | RSS |
|---|---|---|---|---|
| WebKitWebProcess | 88,822 (88,758–88,975) | 48,152 | **20,176** | 146,400 |
| WebKitNetworkProcess | 21,850 | 10,496 | **7,636** | 49,508 |
| xdg-dbus-proxy | 853 | 492 | 440 | 5,728 |
| 4 × bwrap | 184 each | 70 each | 70 each | 1,826 each |
| **Descendants** | **110 MB** | **58.0 MB** | **27.9 MB** | 204 MB |
| App (UI) process | 109 MB (108–111) | 69.0 MB | **42.4 MB** (41.9–44.4) | 165 MB |

- **PSS is mostly file pages.** The descendants' PSS is 28.1 MB anonymous and 81.6 MB file-backed: libwebkit, JSC, ICU and the residual Mesa and LLVM pages that `spikes/linux-memory` described.
- **USS includes clean library pages** that only one process maps: 28 MB of Private_Clean in the web process. Windows would never count those as private.
- **Private_Dirty is the anonymous memory.** It equals Pss_Anon within 0.2 MB.
- **`statm` gives the same number faster.** Resident − shared (`statm` fields 2 and 3) was 28.3 MB for the descendants and 42.4 MB for the app process: within 0.4 MB of Private_Dirty (the bwrap processes share some dirty pages after their fork).
- **The app process is where GTK and WebKit's UI process live**, so on Linux the "backend" process carries 42 MB of private memory. The managed heap in it is 0.1–0.3 MB, and `GC.GetTotalMemory` 8.5 MB before a GC.

### 2. Cost of a sample

| Operation | Thread | Median (range of 5 runs) | Allocated |
|---|---|---|---|
| descendants through `/proc/PID/task/*/children` | main | 107 µs (102–124) | 13.4 kB |
| descendants through a full `/proc` scan (43–77 processes in the distribution) | main | 215 µs (201–439) | 140 kB |
| `smaps_rollup` for 7 processes, opened each time | main / pool | 564 / 554 µs | 0.9 kB |
| `smaps_rollup` of the app process itself | pool | 420 µs | 56 B |
| `statm` for 7 processes, opened each time | pool | 15.7 µs | 0.8 kB |
| **`statm` for 7 processes, kept handles, `pread` at 0** | **pool** | **4.6 µs** (4.3–4.7) | **0** |
| `smaps_rollup` for 7 processes, kept handles | pool | 516 µs | 0 |
| re-find the tree and reopen the handles | pool | 126 µs | 15.5 kB |
| children + `smaps_rollup` (a full PSS sample) | pool | 688 µs | 14.3 kB |
| `Process.GetProcessById(pid).PrivateMemorySize64` for 7 processes | pool | 178 µs | 71 kB |

- **Kept handles work.** A kept `/proc` handle re-read with `pread(fd, …, 0)` returned the same PSS as a fresh open in 5 of 5 runs, so the kernel regenerates the file on each read from offset 0.
- **`smaps_rollup` is the expensive read.** It walks every mapping, so the app process (with the GC's large reserved ranges) alone takes 0.4 ms.
- **UI probe:**
  - idle: ping max 0.2 ms;
  - a full PSS sample on the GLib main thread every 100 ms: ping max **1.6 ms (1.5–2.0)**, each sample 1.4 ms;
  - the same sample on a pool thread: ping max **0.4 ms (0.3–0.5)**.

  This is the case where sampling on the UI thread is visible.

### 3. Backend memory (app process, NativeAOT)

| Read | Value | Cost | Allocated |
|---|---|---|---|
| `statm` resident − shared (self) | 42.4 MB | 2.3 µs | 112 B |
| `smaps_rollup` Private_Dirty / PSS (self) | 50.6 / 117 MB | 0.41–0.42 ms | 56 B |
| `Environment.WorkingSet` | 174 MB (RSS) | **5.0 µs** | **3.1 kB** |
| `Process.PrivateMemorySize64` | **about 70 GB** (67,140–70,931 MB) | 79 µs | 28.8 kB |
| `GC.GetTotalMemory(false)` | 8.5 MB, 0.3 MB after a GC | 12 ns | 0 |
| `GC.GetGCMemoryInfo()` committed | 18.3 MB | 25 ns | 288 B |

`Process.PrivateMemorySize64` on Linux is a virtual size: the AOT runtime's reserved address space makes it about 70 GB. It is useless for display. `Environment.WorkingSet` is RSS, including every shared library page, and allocates on each read.

### 4. Disk and `webkit_website_data_manager_fetch`

The same fills, on `http://127.0.0.1` (the custom scheme stores no HTTP cache and refuses Cache Storage, as `spikes/webview-profiles` found). `navigator.storage.estimate()` is not available in this WebKitGTK; the page reported nothing for it.

| | Files | Logical | Allocated (`st_blocks`, folders included) | Walk (median) |
|---|---|---|---|---|
| Realistic: `data/` | 7 | 10,428 kB | 10,476 kB | < 0.1 ms |
| Realistic: `cache/` | 136 | 12,137 kB | 12,372 kB | 0.1–0.2 ms |
| Realistic: both | 143 | **22,565 kB** | 22,856 kB | 0.2 ms |
| Large: both | 1,279 | **195,330 kB** | 197,008 kB | 1.2–1.4 ms |

- **Walks:**
  - On Linux every .NET walk stats each file. `FileSystemEnumerator`, `FileInfo` and `lstat` cost about the same: 1.0–1.3 µs per file in the scale test, 19–25 ms for 20,000 files.
  - Sizes are live (no lagging directory entries).
  - No errors.
- **Where the bytes are** (realistic):
  - `data/storage` (IndexedDB and localStorage) 10.4 MB;
  - `cache/WebKitCache` (HTTP cache) 8.0 MB in 121 files;
  - `cache/CacheStorage` 4.1 MB;
  - `cache/HSTS` 12 kB.
- **`webkit_website_data_manager_fetch(WEBKIT_WEBSITE_DATA_ALL)`:**
  - **Sizes.** One entry, `127.0.0.1`. Realistic: disk-cache 4,115,030, local-storage 2,204,016, indexeddb 8,474,624 and dom-cache 4,194,304 bytes, total 18,987,974. Large: 41.2, 17.0, 67.3 and 33.6 MB, total 158.9 MB. So in 2.52 it reports sizes for these four types, not only for the disk cache.
  - **Speed.** Realistic: 1.7 ms (1.5–2.2) from the call to the callback. Large: 13.6 ms (12.6–14.7).
  - **Main thread.** The call itself returns in under 0.1 ms (max 0.6 ms). The work happens in the network process, and the callback runs on the main loop.
  - **Agreement with the walk:** 84 % (realistic) and 81 % (large). The rest is the cache's own metadata and the databases' overhead. Per-origin sizes are something only the API gives.
  - **localStorage is counted in UTF-16:** 1 M characters are reported as 2.2 MB.
- **After the view was destroyed** (session still alive), 2 WebKit processes remained for the 10 s the host waited, in 6 of 6 runs. The folder sizes then equalled "later" within 60 kB.

## 5. One measure for both OSes (proposal)

**Name:** private memory. **Meaning:** resident memory that only the app's own processes hold and that no file backs: heaps, the JS heap, the DOM, and the GPU process's system-memory buffers. Shared library and font pages and non-resident commit are excluded.

| | Windows | Linux |
|---|---|---|
| Per process | `PROCESS_MEMORY_COUNTERS_EX2.PrivateWorkingSetSize` | `/proc/PID/statm` resident − shared (= RssAnon), ≈ `smaps_rollup` Private_Dirty |
| What the OS shows | Task Manager "Memory", counter "Working Set - Private" (verified equal) | Not verified against GNOME System Monitor or KDE (needs a real desktop) |
| Cost, all engine processes, kept handles | 2.5 µs | 4.6 µs |
| Thread | any (the list: STA) | any |

| Small page, MB | Windows: engine | Windows: host | Windows: app total | Linux: engine | Linux: app process | Linux: app total |
|---|---|---|---|---|---|---|
| **Private memory** | **75.0** | **13.5** | **88.5** | **28.3** | **42.4** | **70.7** |
| USS | – | – | – | 58.0 | 69.0 | 127 |
| PSS / proportional estimate | 111 | – | – | 110 | 109 | 219 |
| Commit (private bytes) | 150 | 16.4 | 166 | – | – | – |
| Working set / RSS | 327 | 32.9 | 360 | 204 | 165 | 369 |

- **Same meaning:** on both OSes these are pages the app's processes wrote and alone own. Clean library pages are excluded on both.
- **Different split:** Windows keeps the rendering stack in the browser's processes, with a separate GPU process (21.6 MB) on a real GPU. Linux keeps GTK and WebKit's UI process in the app process, and here had no GPU process and software rendering.
- So **report "app total" as the headline**, with "engine" and "app process" as its parts, and the per-kind breakdown (browser, renderer, GPU, network, other) as detail. "Backend memory" on its own then means the app process: the host on Windows; host plus GTK and WebKit UI on Linux. The managed heap (`GC.GetTotalMemory(false)`, 13 ns) is a further detail.
- **USS and PSS** are worth offering only on Linux, as detail. PSS needs `smaps_rollup` (0.5 ms), and Windows has no cheap equivalent (`QueryWorkingSet`, 5.4 ms).
- **The GPU process belongs in the engine total.** It is the app's own and ran 21.6 MB private (70 MB commit). Shared memory was not separated out: `SharedCommitUsage` was 67 MB on Windows, and Pss_Shmem was 0 on Linux without a GPU.
- **Not measured:** the same page on Linux with WebKit's hardware acceleration on. `spikes/linux-memory` measured about +90 MB anonymous per web process on llvmpipe.

## 6. Sampling design

**Costs that set the numbers** (warm, median):

| Measure | Read, off the UI thread | On the UI thread |
|---|---|---|
| Engine memory, Windows | 2.5 µs (kept handles) | `GetProcessInfos` 29 µs, only when `ProcessInfosChanged` fires |
| Engine memory, Linux | 4.6 µs (`statm`, kept handles); 0.69 ms with PSS | nothing (the fetch API is not needed for memory) |
| Re-finding the Linux tree | 107–126 µs, 13–16 kB | – |
| Backend memory | 135 ns (Windows), 2.3 µs (Linux) | – |
| Data folder, realistic profile | 0.2–1 ms | – |
| Data folder, 20,000 files | 3.4–5 ms (Windows), 19 ms (Linux) | – |
| WebKitGTK per-type sizes | – | under 0.6 ms for the call; 1.7–14 ms to the callback |

**Recommendations** (for the API design with the user):
- **Memory interval: default 1 s, floor 250 ms.**
  - Cost does not limit it: a Windows sample is 2.5 µs and a Linux `statm` sample 4.6 µs.
  - The floor is about usefulness: a status bar cannot show more than a few updates per second.
  - PSS on Linux, if offered, costs 0.7 ms per sample; keep its floor at 1 s.
  - On Linux, re-find the tree every few seconds or when a view or web process is created or ends, not every tick. WebKit offers no event for new processes; `web-process-terminated` covers crashes only.
- **Disk interval: default 30 s, floor 5 s,** or read on request only.
  - Today's profiles walk in about 1 ms warm, so 30 s is generous. Cold-cache cost was not measured, and profiles grow (the HTTP cache is one file per entry).
  - On Windows, size the folder with directory enumeration (`FileFullDirectoryInfo` gives logical and allocated sizes in one pass). Accept that files the browser keeps open show a stale size until closed: here 1 MB of a 27 MB folder. Per-file sizes cost 15× more.
  - On Linux, the folder walk is the measure. `webkit_website_data_manager_fetch` is an optional per-origin and per-type breakdown. It must be called on the main thread, and it returns asynchronously.
- **Change threshold:** push a value only when it moves by **more than 1 % or 1 MiB**, whichever is larger. At idle the Windows totals were stable to the kB across the counter read, and Linux PSS moved by under 0.3 MB in 25 s. With this threshold the sampler pushed 1 value in 51 ticks on an idle page.
- **Subscription:**
  - **Model.** The bridge's subscription is a bounded drop-oldest buffer. With **capacity 1** it is latest-value-wins. The sketch (`Shared/Sketch.cs`) wrote every tick into such a channel while the reader took 300 ms per value: 51 ticks, 33 dropped, 17 received, the reader always seeing the newest.
  - **Gap events.** The bridge's visible `gap` events would report every overwritten sample, which is noise for this measure. The contract needs a way to say "coalesced, no gap events", or the page ignores them. This is a design point for the user.
  - **Allocation.** The read and the channel write allocated **0 bytes per tick** on Windows, and on Linux with kept handles; re-finding the Linux tree every tick allocates 14 kB. Process-wide, the timer and async machinery added 77 B per tick (Windows). Serializing and posting each pushed value is extra and was not measured here.
  - **Sampling only while subscribed** fits the sketch: the timer task runs only while it has a reader.

## Problems met

1. **Log interleaving.** The harness first appended the performance-counter lines to the host's log while the host was still writing it, and the host's later writes overwrote them. The counters now go to their own file (`memory-N.counters.txt`).
2. **`Get-Counter` instance names.** `CounterSample.InstanceName` gave the same name for every `msedgewebview2` instance, so all of them merged into one row. The instance is now taken from the counter path (`msedgewebview2#N`).
3. **Summary parsing.** The first summary read the sample cost inside `sampleCost=(…)` as the ping median, which made off-thread sampling look as slow as on-thread. The summarizer now ignores parenthesised groups; the tables above use the corrected summary.
4. **Small fixes:** the composed interop file still referenced a navigation handler type that was not declared (it is now a placeholder slot). A shell heredoc removed a backslash from a character literal (now `Path.DirectorySeparatorChar`). `FileSystemEnumerator` has no constructor with an `isNormalized` argument.

## Not tested

- **Cold file-system cache:** flushing it needs administrator rights, so every walk ran warm. The first walk after the fill (0.9–10 ms) is the closest seen.
- **Windows Defender's effect** on the per-file method (it opens each file); directory enumeration opens none.
- **Several windows,** cross-site iframes (more renderers), a heavy page, a renderer crash, and `ProcessInfosChanged` during navigation to another site.
- **Linux with GL:** WSLg, X11 or a real GPU (a WSLg window would reach the user's desktop), and so no WebKit GPU process and no hardware-accelerated numbers. **4.1 / GTK 3** was not measured (6.0 only, as asked).
- **The crashpad handler's memory** (not listed by `GetProcessInfos`). The process kinds `SANDBOX_HELPER` and `PPAPI_*` did not occur.
- **A long-lived profile** with thousands of HTTP-cache entries, beyond the synthetic 20,000-file walk.
- **Serialization and posting cost** of a pushed sample over the real bridge. The sketch measured the sampler and the channel only.
- **Whether `GetProcessExtendedInfos` exists on the oldest Runtime Spar will support.** It was present on 157; feature-detect it.

## Checks that need the user at the PC

| Check | Why a person is needed |
|---|---|
| Task Manager (Processes tab) for a running Spar app: does the app's group total, with its WebView2 processes, equal the reported "app total" private memory? | Reading Task Manager on the user's desktop; the counters behind it were verified equal here. |
| GNOME System Monitor and KDE System Monitor "Memory" columns against the Linux private-memory number | Needs a real Linux desktop. |

## Proposed pitfalls (for `docs/pitfalls.md`)

- `GetProcessInfos` returns `UI_E_WRONG_THREAD` (`0x802A000C`) off the STA; list on the STA when `ProcessInfosChanged` fires, and read memory on any thread with kept process handles.
- `GetProcessInfos` does not list the crashpad handler. Processes are per browser, that is per user-data folder: a second app or instance on the same folder is counted too.
- `Process.PrivateMemorySize64` takes 3.2 ms on Windows (a snapshot of every process) and reads about 70 GB on Linux (a virtual size). Use `GetProcessMemoryInfo` (EX2) or `/proc/PID/statm`.
- `GC.GetGCMemoryInfo()` describes the last GC: 0 before the first one.
- NTFS directory entries show a stale size for files still open for writing (1 MB of localStorage log at 0 kB). Directory-enumeration size walks under-report until the file is closed.
- `EnumerationOptions` skips Hidden and System entries by default; a size walk must set `AttributesToSkip = 0`.
- `navigator.storage.estimate()` covers only IndexedDB and Cache Storage on WebView2 (12.6 of 26 MB here), and is missing on WebKitGTK.
- `webkit_website_data_manager_fetch` in WebKitGTK 2.52 reports sizes for the disk cache, localStorage (UTF-16 bytes), IndexedDB and DOM cache, about 80 % of the folder size.
- `smaps_rollup` costs about 70 µs per process (0.4 ms for the .NET app process itself); `statm` resident − shared gives the anonymous memory in about 1 µs and matched Private_Dirty within 0.4 MB.
