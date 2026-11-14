# SqlClient on Linux for the SQL tool: spike results

**Question.** Spar's first real app is a SQL Server tool (monitor instances, compare databases, copy data between
instances). Would it work on Linux, under NativeAOT (warnings accepted on the command line for this app only) and
under CoreCLR? How does it compare with the Windows numbers of `spikes/sqlclient-tool`?

**Answer: works, with caveats.** Every scenario of the Windows spike ran on Linux under NativeAOT and CoreCLR with the
same data, checksums, schema differences, committed-row counts and exception types. Throughput is the same as on
Windows within run-to-run noise. The caveats are deployment and platform facts, not failures:

- **Only SQL authentication (or other token-based authentication) is practical.** Integrated Security goes through
  GSSAPI/Kerberos (`libgssapi_krb5.so.2`). Without a Kerberos realm it fails with "The target principal name is
  incorrect. Cannot generate SSPI context." There is no NTLM fallback to a Windows login, as there is on Windows.
- **TCP only.** Remote named pipes throw `PlatformNotSupportedException`. There is no shared memory (`lpc:`), so
  `.`, `(local)` and `localhost` mean TCP to the local machine.
- **Hard native dependencies: ICU and OpenSSL.** Without ICU the process fails fast at the first culture-sensitive
  call, and invariant mode is rejected by SqlClient. Without `libssl.so.3` the process **aborts** (exit 134, not an
  exception) at the first connection, because the login is always TLS-wrapped.
- **Process CPU for a bulk copy is about 2.3× Windows** (26–28 s against 11–12 s for 1M rows), at the same wall
  time. Disabling thread-pool spin-waiting (`DOTNET_ThreadPool_UnfairSemaphoreSpinLimit=0`) brought it down to
  11.6 s (AOT) and 13.4 s (JIT) in one run each. The cause is inferred, not profiled.
- **The AOT exe needs glibc ≥ 2.34** (built on Ubuntu 24.04). It ran unchanged on Ubuntu 22.04 (glibc 2.35, ICU 70).
- The AOT warnings are **the same 53 unique lines as on Windows**; the build is a single 17.4 MB executable with no
  SNI sidecar (on Linux SqlClient has only its managed networking).

Environment:

- Windows 11 Pro 10.0.26320 host, Intel i9-13900K (32 logical CPUs). Docker Desktop 29.9.3, Linux containers on WSL2
  (kernel 6.18.58.1-microsoft-standard-WSL2), 32 CPUs and 47 GB visible to Docker.
- Harness container from the existing local image `spar-linux-desktop:webkit2.52` (7.08 GB; Ubuntu 24.04.5, .NET SDK
  11.0.100 under `/opt/dotnet11`, clang, zlib, ICU 74.2, OpenSSL 3.0.13, libgssapi-krb5-2 1.20.1, libsecret 0.21.4,
  dbus 1.14.10). No new image was built.
- Two SQL Server containers from the local `mcr.microsoft.com/mssql/server:2022-latest` (2.34 GB, `--pull never`,
  `MSSQL_MEMORY_LIMIT_MB=4096`), all three on the user-defined network `spar-sqllinux-net`. The servers report
  16.0.4295.3 Developer Edition, 32 CPUs, committed target 3 213 MB, collation `SQL_Latin1_General_CP1_CI_AS`
  (identical to the Windows spike).
- The harness reaches the servers over the Docker bridge (`tcp:spar-sqllinux-a,1433`). The Windows spike went
  through Docker Desktop's loopback port forwarding instead. To separate the network path from the OS, the Windows
  exes of `spikes/sqlclient-tool` (run, not modified or rebuilt) were also run against these same containers through
  their published loopback ports: the "Windows, same session" columns below.
- All numbers were observed on 2026-11-13 in this folder. Other sessions may have been using the machine. Raw output
  is in `artifacts/results/` (one file per runtime, mode and variant), plus `run-console.txt`,
  `windows-console.txt`, `spin0-console.txt` and `secret-console.txt`.

## What was built

| Path | Role |
|---|---|
| `SqlTool.App/` | Copy of `spikes/sqlclient-tool/SqlTool.App`. Changes for Linux only: `RuntimeIdentifier=linux-x64`; no managed-SNI variant; `Mem` reads `/proc/self/status` (`VmHWM` as peak working set, `RssAnon` in place of peak private bytes, which Linux does not have); the Windows `sspi` mode is replaced by `probe <env\|transport\|tls\|auth\|globalization\|secret>` in `Linux.cs`; `AllowUnsafeBlocks` for the `[LibraryImport]` libsecret stubs. Every scenario's code is otherwise unchanged. |
| `Directory.Build.props`, `Directory.Packages.props` | Import the repository files first. `SqlToolRuntime=aot\|jit` with separate `bin/`/`obj/`. SqlClient pinned to 7.1.2. |
| `run.sh` | Host side (Git Bash): `up` (network, two SQL containers, harness container), `exec <steps>`, `windows` (same-session Windows reference), `down`, `all`. The SA password is generated in the script, passed with `-e NAME` (no value on the command line) and never printed or written to a file. |
| `inside.sh` | Container side. Copies the sources from the read-only `/src` mount into the container's own `/work` (no Windows `bin/`/`obj/` shared), builds, publishes and runs each step. NuGet uses the existing `spar-linux-desktop-nuget` volume. |

Data: identical to the Windows spike (same `Setup.cs`): `SparToolSrc.dbo.Orders` on A, 1 000 000 rows, 18 columns,
195.5 MB of payload, generated in 9.6 s (Windows 9.9 s); setup 18.8 s (Windows 19.2 s).

## Build and NativeAOT publication

| Item | Linux (linux-x64) | Windows (`sqlclient-tool`) |
|---|---|---|
| Publish command | `dotnet publish -c Release -p:SqlToolRuntime=aot -p:TreatWarningsAsErrors=false -p:IlcTreatWarningsAsErrors=false` (app-level acceptance on the command line; no suppressions in project files) | same |
| Unique trim/AOT warning lines | **53**, identical to Windows after normalising (`artifacts/warnings-aot.txt`): IL2026 × 27, IL2057 × 6, IL2067 × 3, IL2070 × 3, IL2072 × 8, IL2075 × 2, IL2111 × 1, IL3050 × 3 | 53 |
| Repository rule (`TreatWarningsAsErrors=true`) | fails in C# compile: the app's own IL2026 (`DataTable.Load`, getschema probe) | same |
| Compile lax, `IlcTreatWarningsAsErrors=true` | fails in ILC with **52 unique IL errors** (51 from SqlClient and dependencies, 1 app) | not run (the `sqlclient-aot` spike: ILC fails) |
| AOT publish time (full ILC run, warm NuGet) | 9.9 / 9.2 / 9.2 s | 12.4 s (1 run) |
| AOT output | **one file**: `SqlTool.App` 17 409 064 bytes, plus `SqlTool.App.dbg` 24 375 080 bytes (symbols, not needed to run) | `SqlTool.App.exe` 17 115 136 bytes + `Microsoft.Data.SqlClient.SNI.dll` 555 416 bytes (+ pdb) |
| `readelf -d` NEEDED | `libm.so.6`, `libc.so.6`, `ld-linux-x86-64.so.2` only | — |
| Libraries loaded at run time (`dlopen`, from `/proc/self/maps`) | ICU (`libicuuc/libicui18n/libicudata.so.74`) always; `libssl.so.3` + `libcrypto.so.3` at the first connection; `libgssapi_krb5.so.2` (+ `libkrb5`, `libk5crypto`, `libkrb5support`) only for Integrated Security. No `.so` is needed beside the exe: the runtime's native shims are linked into it. | — |
| glibc | highest symbol version `GLIBC_2.34`; built against 2.39 | — |
| CoreCLR (framework-dependent) output | 36 files, 5.6 MB; uses `runtimes/unix/lib/net9.0/Microsoft.Data.SqlClient.dll` and loads `libSystem.Net.Security.Native.so`, `libSystem.Security.Cryptography.Native.OpenSsl.so` from the shared runtime | 38 files, 7.1 MB |
| `InvariantGlobalization` | must be `false` (below) | must be `false` |

The JIT build also needs `-p:TreatWarningsAsErrors=false` for the app's own analyzer IL2026 (`IsAotCompatible=true`),
as on Windows.

**SNI.** `Microsoft.Data.SqlClient.SNI.runtime` 7.1.2 contains only `runtimes/win-{x86,x64,arm64}/native` DLLs.
On Linux the `runtimes/unix` SqlClient assembly is used, which only has the managed SNI. The server sees
`net_transport=TCP`, `client_interface_name='Core Microsoft SqlClient Data Provider'`, TDS `0x74000004`.

## 1. SqlBulkCopy A → B (1M rows, streamed from a `SqlDataReader`)

Medians of 3 for `default` and `b100k`; other variants one run each. "Same session" is the Windows AOT/JIT exe
against these containers (3 runs of `b100k`).

| Variant | Linux AOT ms | Linux JIT ms | Windows AOT / JIT ms (earlier spike) | Windows same session AOT / JIT ms | Identical on B |
|---|---:|---:|---:|---:|---|
| `default` (runs AOT 11 919 / 9 906 / 10 838, JIT 11 010 / 12 239 / 11 311) | **10 838** (92 270 rows/s) | **11 311** (88 411) | 14 067 / 11 673 | — | yes ×6 |
| `default-tablock` | 10 241 | 10 538 | 11 543 / 10 303 | — | yes |
| `b10k` | 11 301 | 14 070 | 13 603 / 10 887 | — | yes |
| `b100k` (runs AOT 10 710 / 11 178 / 10 267, JIT 12 387 / 11 200 / 12 759) | **10 710** (93 372) | **12 387** (80 732) | 14 509 / 11 770 | **11 141 / 10 229** (runs 12 120 / 11 141 / 11 024; 10 690 / 10 229 / 9 633) | yes ×6 (+6 Windows) |
| `b100k-stream` (`EnableStreaming`, `SequentialAccess`) | 15 083 | 15 049 | 17 265 / 16 408 | — | yes |
| `b100k-tablock` | 9 652 | 11 815 | 11 921 / 11 535 | — | yes |
| `b100k-internaltx` | 10 948 | 9 408 | 12 795 / 10 710 | — | yes |
| `b100k-externaltx` | 9 597 | 12 078 | 13 310 / 11 406 | — | yes |

Memory and CPU per process (non-streaming variants):

| | Linux AOT | Linux JIT | Windows AOT / JIT (earlier) | Windows same session AOT / JIT |
|---|---:|---:|---:|---:|
| Peak working set (Linux `VmHWM`) | 52–54 MB | 97–98 MB | 42–44 / 75 MB | 43.5–43.9 / 75.3–75.7 MB |
| Anonymous RSS at end (Linux) / peak private (Windows) | 27.7–27.8 MB | 31.9–33.1 MB | 29–31 / 37–38 MB | 30.7–31.1 / 37.2–38.3 MB |
| Managed allocations for 1M rows | 1 823–1 835 MB | 1 821–1 842 MB | 1 834 / 1 829–1 839 MB | 1 835 / 1 830–1 838 MB |
| Gen0 GCs | 103–104 | 103–104 | ≈103 / ≈104 | 102 / 104 |
| **Process CPU** | **25.9–28.7 s** | **23.2–33.7 s** | 12.0–14.4 / 26–32 s | **10.9–11.9 / 23.9–29.0 s** |
| `b100k-stream` allocations, CPU | 17.1 GB, 50.0 s | 17.1 GB, 51.6 s | 17.1 GB both | — |

- **Wall time on Linux equals Windows within noise** once the network path is the same kind (Windows same session:
  AOT 11.1 s, JIT 10.2 s; Linux: AOT 10.7 s, JIT 12.4 s). The earlier Windows run was slower overall (other load).
  The AOT-vs-JIT order is not stable across sessions (±15% per run).
- **Linux AOT uses 2.3× the CPU of Windows AOT** for the same copy. One run each with
  `DOTNET_ThreadPool_UnfairSemaphoreSpinLimit=0` (`spin0-console.txt`): AOT 11 950 ms wall, **11.6 s CPU**; JIT
  13 004 ms wall, **13.4 s CPU**. So most of the extra CPU looks like thread-pool spin-waiting around the async
  socket I/O. Inferred; not profiled, and one run cannot show whether wall time changed.
- The peak working set is about 10 MB (AOT) and 20 MB (JIT) higher on Linux. `VmHWM` and the Windows peak working
  set are not the same measure, so treat this as indicative.
- The longest gap between progress events (every 100 000 rows) was 0.9–1.4 s under AOT and 2.0–2.5 s under JIT.

## 2. Cancelling a bulk copy mid-way

Same trigger as Windows: 50 000-row batches, cancel when progress reports 310 000 rows (`none-timed`: after 1.5 s).
After the exception a separate connection inspects B.

| Variant | Rows on B, Linux AOT / JIT | Windows AOT / JIT | Cancel → exception, Linux AOT / JIT (ms) | Windows AOT / JIT (ms) | Exception |
|---|---|---|---|---|---|
| `none` | **310 000 / 310 013** | 310 002 / 310 012 | 29 / 33 | 51 / 37 | `TaskCanceledException` |
| `none-timed` | 147 538 / 48 182 (last progress 140 000 / 40 000) | 103 462 / 45 319 | 124 / 127 | 34 / 168 | `TaskCanceledException` |
| `none-drain` | 310 000 / 310 014 | 310 001 / 310 027 | 30 / 36 | 38 / 48 | `TaskCanceledException` |
| `internal` | **300 000 / 300 000** | 300 000 / 300 000 | 35 / 41 | 39 / 36 | `TaskCanceledException` |
| `external` (then `Rollback()`) | **0 / 0** | 0 / 0 | 26 / 27 | 35 / 42 | `TaskCanceledException` |
| `abort` (`e.Abort = true`) | 310 000 / 310 000 | 310 000 / 310 000 | 36 / 33 | 34 / 39 | `OperationAbortedException` |
| `batch0` | **310 000 / 310 023** | 310 001 / 310 023 | 841 / 826 | 1 005 / 884 | `TaskCanceledException` |
| `batch0-internal` | **0 / 0** | 0 / 0 | 911 / 895 | 1 045 / 989 | `TaskCanceledException` |
| `batch0-external` | 0 / 0 | 0 / 0 | 733 / 771 | 809 / 806 | `TaskCanceledException` |
| `batch0-abort` | 310 000 / 310 000 | 310 000 / 310 000 | 757 / 818 | 830 / 793 | `OperationAbortedException` |
| `batch0-tablock` | 310 000 / 310 014 | 310 000 / 310 025 | 381 / 258 | 388 / 279 | `TaskCanceledException` |

- **Same semantics as Windows**: without a transaction the rows sent are kept (including part of the in-flight batch
  under JIT); `UseInternalTransaction` keeps whole batches only; an external transaction keeps nothing after rollback.
- Destination: in every run the first probe found no request for the destination session and no `BULK` request on B,
  **1.7–2.7 ms (AOT) and 8.4–17.7 ms (JIT) after the exception** (Windows 0.7–11.6 ms). The destination connection
  stayed `Open` and ran `SELECT 1`.
- Source: as on Windows, the source `SELECT` on A was still running (`ASYNC_NETWORK_IO`) after the bulk copy threw,
  in 21 of 22 runs. `SqlCommand.Cancel()` + reader dispose took **1.6–7.4 ms** (Windows 18–98 ms) and left it idle.

## 3. Cancelling long-running queries

| Case | Linux AOT / JIT: cancel → exception (ms) | Linux AOT / JIT: request gone (ms after cancel) | Windows AOT / JIT: exception; gone (ms) |
|---|---|---|---|
| `WAITFOR`, async token | 0.6 / 4.6 | 1.8 / 6.6 | 10.4 / 6.7; 12.3 / 9.6 (same session: 1.0 / 5.3; 1.6 / 8.2) |
| `WAITFOR`, sync + `SqlCommand.Cancel()` | 0.5 / 3.1 | 0.8 / 5.2 | 4.0 / 4.5; 8.2 / 7.4 |
| CPU-bound, `MAXDOP 1` | 2.3 / 6.5 | 2.6 / 8.6 | 3.0 / 7.3; 3.8 / 9.6 |
| CPU-bound, `MAXDOP 4` (`CXCONSUMER`, cpu ≈ 8 s) | 6.8 / 9.7 | 7.1 / 11.7 | 7.0 / 15.1; 7.7 / 18.0 |
| Blocked on `LCK_M_S` | 0.6 / 3.4 | 0.9 / 5.4 | 1.1 / 3.7; 1.9 / 6.2 |
| Streaming 1M rows, `ReadAsync(token)` | **0.1 / 1.3** | **still running; gone 520 / 531 ms** (after the deliberate 500 ms wait and reader dispose) | 0.1 / 1.8; 579 / 590 |
| Read 1 000 rows, dispose reader (drains) | dispose **987 / 1 005 ms** | — | 946 / 1 000 |
| Same, `SqlCommand.Cancel()` first | dispose **0.4 / 8.4 ms** | — | 19 / 20 |

The exception is the same as on Windows: `SqlException` Number 0, Class 11, "A severe error occurred on the current
command…" for command cancellation (10 runs); `TaskCanceledException` for `ReadAsync` with a cancelled token. Every
connection was `Open` and reusable afterwards. `ReadAsync(token)` cancellation is client-side only, as on Windows.

## 4. Reading 1M rows

Median of 3 passes per process; checksums identical to Windows for every access pattern.

| Variant | Linux AOT median ms (rows/s) | Linux JIT median ms | Windows AOT / JIT (earlier) | Windows same session AOT / JIT |
|---|---:|---:|---:|---:|
| `typed-async` | 1 968 (508 021) | 1 694 | 2 247 / 2 046 | — |
| `typed-sync` | **971** (1 029 774) | 1 013 | 1 379 / 1 453 | **979 / 897** |
| `seq-async` | **968** (1 033 113) | 706 | 1 295 / 1 421 | **995 / 897** |
| `seq-sync` | 994 | 1 003 | 1 504 / 1 290 | — |
| `seq-stream-async` | 2 030 | 1 872 | 1 956 / 2 144 | — |
| `values-sync` | 992 | 1 001 | 1 339 / 1 173 | — |

- Same session, Linux and Windows read at the same speed (≈1 s, ≈200 MB/s of payload). The earlier Windows numbers
  were 30–50% slower, so most of that gap was the session, not the OS.
- Managed SNI is not slower here: on Windows it was 24% slower than native SNI for `typed-async`; on Linux (managed
  only) `typed-async` matches the Windows native-SNI result.
- Allocations per pass equal Windows: `typed-sync` 327 MB, `typed-async` 637 MB, `seq-stream-async` 6.0 GB.
- First-pass penalty: JIT first passes were 1.3–2.2× the median (e.g. `seq-async` 1 547 ms); AOT first passes were
  within 10% of the median.
- Peak working set 50–54 MB (AOT) and 92–101 MB (JIT); Windows 40–44 / 74–81 MB. Process CPU for 3 passes:
  AOT 5.0–9.3 s, JIT 4.4–10.0 s (Windows AOT 3.4–6.9 s; same session `typed-sync` 2.8 s against Linux 5.0 s).
  `spin0` did not change the sync-read CPU (4.8 s).

## 5. Schema comparison through catalog views, and metadata APIs

- **5 730 / 5 727 objects, 28 differences, identical to Windows and between AOT and JIT.** The only textual
  differences from the Windows output are the random suffixes of system-named primary keys
  (`PK__OnlyLeft__3214EC06…`), as expected.
- Extraction of both databases: AOT 325.5 ms first, then 97.4–99.1 ms; JIT 278.4 ms, then 106.7–150.8 ms
  (Windows AOT 344.8 / 99.5–107.3; JIT 263.9 / 100.9–149.1). In-memory diff 2.0 (AOT) / 11.6 (JIT) ms.
- `getschema`: all 7 checks pass under AOT and JIT with output identical to Windows. AOT timings: `GetSchemaTable`
  8.2 ms, `GetColumnSchema` 0.8 ms, `GetSchema()` 0.5 ms, `GetSchema("Tables")` 5.2 ms, `GetSchema("Columns")`
  11.8 ms, `DataTable.Load` 10k rows 51.5 ms (Windows 18.1 / 3.0 / 4.0 / 6.8 / 13.4 / 66.9 ms).

## 6. DMV polling every second

Same batch as Windows (1 986 performance counters, requests with SQL text, 127 wait types), 30 polls per phase.

| Phase | Linux AOT median / p95 / max (ms) | Linux JIT | Windows AOT / JIT median (ms) | Server CPU per poll, Linux AOT / JIT (Windows) |
|---|---|---|---|---|
| Idle, persistent | 5.09 / 7.48 / 8.45 | 5.15 / 17.20 / 23.45 | 9.42 / 9.62 | 1.80 / 1.67 ms (1.37 / 1.43) |
| Idle, pooled open per poll | 5.20 / 5.88 / 6.00 | 5.13 / 6.04 / 6.28 | 8.60 / 8.98 | — |
| Under load (4 CPU-bound queries) | 6.44 / 9.11 / 9.61 | 5.93 / 8.02 / 8.21 | 9.30 / 10.17 | 1.10 / 1.60 ms (1.10 / 2.07) |

- 3.5 MB allocated per poll, as on Windows. Whole process: AOT 1 484 ms CPU, 52.7 MB peak; JIT 2 467 ms, 104 MB
  (Windows AOT 766 ms, 43 MB; JIT 2 203 ms, 76 MB).
- The lower latency than the earlier Windows run is consistent with the faster bridge path (§3 same-session WAITFOR
  numbers point the same way); not separately measured for polling.

## 7. Linux-only checks

### Transport (`probe transport`, SQL authentication, `ConnectTimeout=5`)

| Data source | AOT and JIT result |
|---|---|
| `tcp:spar-sqllinux-a,1433`, `spar-sqllinux-a,1433`, `spar-sqllinux-a` | pass: TCP, TLS (`encrypt=TRUE`), open 5–6 ms warm (first open 61 ms AOT, 95 ms JIT) |
| `np:spar-sqllinux-a`, `np:\\spar-sqllinux-a\pipe\sql\query` | **fail immediately: `PlatformNotSupportedException` "Access to remote named pipes is not supported on this platform."** |
| `np:.` | fail after 5.0 s: "Named Pipes Provider, error: 40", inner `TimeoutException` (local pipes map to a Unix domain socket; no server) |
| `lpc:spar-sqllinux-a`, `lpc:.` | fail after 4.5 s: SqlException 11001, "TCP Provider", inner `SocketException` "Name or service not known": **no shared-memory provider** |
| `.`, `(local)`, `localhost` | fail after 4.5 s: TCP to the harness itself (no server there), "TCP Provider, error: 35" |
| `admin:spar-sqllinux-a` (DAC) | fail after 4.5 s, "TCP Provider, error: 35" (remote DAC not enabled; not pursued) |

A refused TCP connection took about 4.5 s of the 5 s timeout to surface, not milliseconds. A monitor that probes
many unreachable instances should keep connect timeouts short. (Not compared on Windows.)

### TLS and OpenSSL (`probe tls`; OpenSSL 3.0.13)

| Case | AOT and JIT |
|---|---|
| `Encrypt=Optional` (± trust) | pass; `encrypt_option=FALSE` (only the login is encrypted) |
| `Encrypt=Mandatory`, no trust | **fail**: `AuthenticationException` "Certificate failed chain validation. Error(s): 'self-signed certificate, [Status: UntrustedRoot]" (OpenSSL's wording) |
| `Encrypt=Mandatory;TrustServerCertificate=true` | pass, `encrypt_option=TRUE` (the harness setting) |
| Same + `HostNameInCertificate=not-the-server.example` | pass (the host name is not checked when the certificate is trusted) |
| `Encrypt=Strict` (± trust) | fail: "SSL Provider, error: 31 - Encryption(ssl/tls) handshake failed", inner `IOException` "unexpected EOF". The `sqlclient-aot` spike saw the same failure from Windows against this image, so it is the server configuration, not Linux. |
| `libssl.so.3`/`libcrypto.so.3` removed | **process abort**: "No usable version of libssl was found", exit 134, at the first connection (AOT and JIT). Code that does not connect runs fine. |

### Integrated authentication (`probe auth`)

No KDC, no `/etc/krb5.conf`, no ticket cache (by design of this spike).

| Case | With `libgssapi_krb5.so.2` | Library removed |
|---|---|---|
| `Integrated Security=true`, TCP (± explicit `ServerSPN=MSSQLSvc/host:1433`) | SqlException Number 0, Class 11: "The target principal name is incorrect.  Cannot generate SSPI context." after 35–97 ms (first) / 4–8 ms | AOT: "A type initializer threw an exception…"; JIT: "The type initializer for 'NetSecurityNative' threw an exception." (both SqlException Class 11) |
| `Integrated Security=true`, `np:` | `PlatformNotSupportedException` (named pipes, before authentication) | same |
| `Authentication=ActiveDirectoryIntegrated` | same as Integrated Security ("target principal name is incorrect") | same type-initializer error |
| `Authentication=ActiveDirectoryDefault` | SqlException 18456 "Login failed for user ''" (no Azure provider package referenced) | same |
| SQL authentication | unaffected: `ping` passes and `libgssapi_krb5` is never loaded | passes |

What SqlClient requires for Kerberos on Linux: `libgssapi_krb5.so.2` (Ubuntu `libgssapi-krb5-2`; present in this
image), plus a configured realm (`/etc/krb5.conf` or `KRB5_CONFIG`), a ticket (`kinit`, or a keytab), and an SPN
registered for the server. None of these was set up. The Windows-only behaviour of falling back to NTLM for a local
Windows login does not exist here.

### Globalization (`probe env`, `probe globalization`, `deps`)

- ICU 74 is used (`tr-TR` upper-cases `i` to U+0130; `ja-JP` native name resolves). Varchar columns with code pages
  1252, 932 and 1254 (`Latin1_General_CI_AS`, `Japanese_CI_AS`, `Turkish_CI_AS`) round-trip exactly when written as
  literals or `nvarchar` parameters, and are decoded correctly on read (AOT and JIT).
- Explicit `SqlDbType.VarChar` parameters are encoded with the code page of the **connection's database** collation
  (1252 for `master`), so Japanese became `???????` and Turkish lost its dotless/dotted letters. This is SqlClient's
  documented parameter behaviour, not a Linux difference, but the copy tool must send `nvarchar` (or the right
  collation) for non-1252 varchar data.
- `Encoding.GetEncoding(1252/932/…)` is unavailable to the app (no `CodePagesEncodingProvider` registered); SqlClient
  decodes code pages internally regardless. Same as .NET on Windows.
- `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`: `SqlConnection.Open` throws `NotSupportedException: Globalization
  Invariant Mode is not supported.` under AOT and JIT. Under AOT the variable did **not** switch the runtime to
  invariant mode (with ICU removed it still failed fast looking for ICU), so SqlClient evidently reads the variable
  itself (inferred from the behaviour).
- **ICU removed**: both runtimes terminate (exit 134) at the first culture-sensitive call: "Couldn't find a valid ICU
  package installed on the system. Please install libicu…". So `libicu` is a hard runtime dependency of the tool on
  Linux. App-local ICU was not tried.

### Portability of the AOT executable

`objdump -T` shows `GLIBC_2.34` as the highest required symbol version. The exe built on Ubuntu 24.04 (glibc 2.39)
was copied into the SQL Server container (Ubuntu 22.04.5, glibc 2.35, ICU 70, OpenSSL 3) and ran `probe env` and
`ping` successfully there; the copy was removed afterwards. Distributions older than glibc 2.34 were not tried.

### Credential storage candidate: libsecret from NativeAOT (`probe secret`)

`[LibraryImport]` declarations for the non-variadic libsecret API (`secret_schema_newv`,
`secret_password_storev_sync` / `lookupv_sync` / `clearv_sync`) plus GLib hash tables, `libsecret-1.so.0` and
`libglib-2.0.so.0` loaded by name. The probe stores, reads back and clears one secret in the `session` collection.

| Environment | AOT and JIT |
|---|---|
| No D-Bus session bus (headless container) | fail: GError code 0 "Cannot autolaunch D-Bus without X11 $DISPLAY" |
| `dbus-run-session`, no Secret Service installed | fail: code 2 "The name org.freedesktop.secrets was not provided by any .service files" |
| `dbus-run-session` + `gnome-keyring` 46.1 installed (D-Bus activates the daemon on demand; also with an explicit `gnome-keyring-daemon --unlock --components=secrets`) | **pass**: store 5.6–10.5 ms, lookup 0.7–1.3 ms (value matches), clear 0.6–0.7 ms |

So libsecret is callable from NativeAOT without reflection. It needs a D-Bus session bus and a Secret Service
provider (gnome-keyring, KeePassXC, KWallet's bridge), normally present in a desktop session but not in a headless
one. Only the in-memory `session` collection was tested; the persistent `login`/default collection needs an unlocked
keyring and was not tested.

## Failures and their causes

No workload scenario failed. The failures listed in §7 are the probes' expected negative cases. During development:

- The first strict-publish check stopped in the C# compile on the app's own IL2026, so ILC never ran. The step was
  extended with a compile-lax / ILC-strict publish to get the ILC result (52 errors).
- The first globalization probe counted the varchar-parameter row as a failure; it was reclassified as `INFO`
  (documented parameter encoding, above) and an `nvarchar`-parameter row was added.
- The no-ICU check initially left the unversioned dev symlinks (`libicuuc.so`) in place; the hide list was extended.

## Downloads, images and shared state

- No image was pulled or built. Reused: `spar-linux-desktop:webkit2.52` (7.08 GB) and
  `mcr.microsoft.com/mssql/server:2022-latest` (2.34 GB).
- NuGet restore into the existing `spar-linux-desktop-nuget` volume: 19 new package folders (SqlClient 7.1.2 and
  dependencies), **about 45 MB** (450 MB → 495 MB). The volume was kept.
- Inside the throwaway harness container only: `apt-get update` (53 MB of lists) and `gnome-keyring` (1.9 MB of
  archives, 8.5 MB installed). Gone with the container.
- Containers `spar-sqllinux-a`, `spar-sqllinux-b`, `spar-sqllinux-harness` and network `spar-sqllinux-net` were
  removed at the end (`down: containers left: 0, network left: 0`). No other container was touched. SQL Server logs
  are in `artifacts/results/container-*.log`.

## Observed, skipped, not tested

**Observed:**

- JIT build and AOT publish (3 timed runs) inside the Linux container; warning lists; strict and ILC-strict publish
  outcomes; output layout, NEEDED libraries, glibc symbol versions.
- Every scenario of the Windows spike under AOT and JIT: setup, 8 bulk variants (+2 repeats of `default` and
  `b100k`), 11 bulk-cancel variants, 8 query-cancel cases, 6 read variants, schema, getschema, monitor.
- Same-session Windows reference (existing Windows exes, not rebuilt): `b100k` × 3, `typed-sync`, `seq-async`,
  `waitfor-async`.
- One run each of `b100k` and `typed-sync` with thread-pool spinning disabled.
- Linux probes: transport prefixes, TLS combinations, Integrated Security / Entra modes, globalization, invariant mode,
  removal of ICU / GSSAPI / OpenSSL, libsecret with and without a bus and a Secret Service, the AOT exe on Ubuntu
  22.04.

**Skipped:**

- Repeats beyond the two 3-run bulk variants; other variants ran once (assume ±15%).
- Profiling the extra CPU on Linux (only the spin-limit experiment was run).
- A managed-SNI comparison on Linux (it is the only SNI there).

**Not tested:**

- Kerberos with a real KDC, keytab or AD-joined host; Entra ID authentication with the Azure extension package.
- `Encrypt=Strict` against a server configured with a proper certificate; certificate validation against a trusted CA.
- Native Linux (bare metal or a VM other than WSL2), a real network, ARM64, distributions other than Ubuntu 22.04/24.04
  (musl/Alpine, older glibc).
- App-local ICU; running without `libicu` in invariant mode (SqlClient forbids it anyway).
- The persistent libsecret collection, KWallet/KeePassXC as providers, and libsecret inside a real desktop session.
- Everything already listed as not tested in `spikes/sqlclient-tool/RESULTS.md` (MARS, `SqlBatch`, triggers, long
  monitoring runs, the warning code paths…).

## Reproduce

```sh
# Git Bash on Windows with Docker (Linux containers); images must already be local.
spikes/sqlclient-linux/run.sh all           # up, every step, Windows reference, down (≈20 min)
spikes/sqlclient-linux/run.sh up            # then iterate:
spikes/sqlclient-linux/run.sh exec sync build wait probes
spikes/sqlclient-linux/run.sh windows
spikes/sqlclient-linux/run.sh down
```
