# SqlClient under NativeAOT: spike results

**Question.** Can Microsoft.Data.SqlClient be used from a NativeAOT .NET 11 app under the repository rule that
trimming and AOT warnings are errors, with no suppressions? If not, what breaks, and what is the smallest working
configuration?

**Answer: no under the strict rule. It works at runtime only with relaxed warnings.**

- No tested SqlClient version publishes under the repository policy. Microsoft.Data.SqlClient 7.1.2 (latest
  stable) produces **49 unique trim/AOT diagnostics (57 lines)**, and ILC fails. A minimal app that only opens a
  connection and runs `SELECT 1` still gets 43 of the 49, so avoiding APIs does not help. The newest preview,
  8.0.0-preview1, gets this down to 27, but none of the remaining causes are avoidable from app code.
- When the warnings are allowed on the command line, the AOT exe passed every ADO.NET check against SQL Server 2022.
  The output was identical to a JIT run of the same code.
- There is one hard runtime requirement: **`InvariantGlobalization` must be `false`.** With it `true`,
  `SqlConnection.Open` throws `NotSupportedException: Globalization Invariant Mode is not supported` before it
  touches the network, under both AOT and JIT.
- Smallest working configuration (only if the warning policy is relaxed):
  - `PublishAot=true` with `InvariantGlobalization=false`.
  - Microsoft.Data.SqlClient 7.1.2.
  - Either the native SNI sidecar DLL next to the exe, or managed networking (see below) for a single-file exe.
  - `Encrypt=true;TrustServerCertificate=true` for the dev container.

Environment: Windows 11 Pro 10.0.26320, .NET SDK 11.0.100 (ILCompiler 11.0.0), win-x64, Docker 29.9 with Linux
containers, image `mcr.microsoft.com/mssql/server:2022-latest` (already local, run with `--pull never`; the server
reports 16.00.4295, Ubuntu 22.04.5). All numbers were observed on 2026-11-13 in this folder. Other sessions were
building on the same machine at the time. Raw logs are in `artifacts/results/`.

## Versions

| Package | Version | Notes |
|---|---|---|
| Microsoft.Data.SqlClient | **7.1.2** (latest stable on NuGet; assembly version 7.0.0.0, file 7.1.2.26316) | Also tried 8.0.0-preview1.26313.4 (latest preview), 7.0.4 and 6.1.8. |
| Microsoft.Data.SqlClient.SNI.runtime | 7.1.2 (transitive) | Contains only `runtimes/win-*/native/Microsoft.Data.SqlClient.SNI.dll`. There is no static `.lib`. |
| Microsoft.Data.SqlClient.Internal.Logging | 7.1.2 (transitive) | Holds `SqlClientEventSource`. |
| System.Configuration.ConfigurationManager | 9.0.21 (transitive) | Reached through SqlClient's app.config support. |
| System.Data.SqlClient | 4.9.1 | **Deprecated.** The NuGet description reads "DEPRECATED - Use Microsoft.Data.SqlClient", and every public type is `[Obsolete]` (CS0618). |

SqlClient's own AOT and trimming notes: the package README says nothing about AOT or trimming. The assembly has no
`IsTrimmable` or `IsAotCompatible` metadata. Its only embedded `ILLink.Substitutions.xml` stubs
`LocalAppContextSwitches.get_UseManagedNetworking`, which is driven by the
`Switch.Microsoft.Data.SqlClient.UseManagedNetworkingOnWindows` feature switch so that the unused SNI path can be
trimmed. There is no `[RequiresUnreferencedCode]` or `[RequiresDynamicCode]` on its public API, so the IDE and
build analyzers report nothing. The failures only show up at ILC time. Background (not verified here):
[dotnet/SqlClient#1047](https://github.com/dotnet/SqlClient/issues/1047) has been open since 2021, and a 2025
third-party write-up ([dlosch](https://dlosch.github.io/dotnet/sql-server/aot/trimming/2025/08/22/SqlClient.html))
also concludes that there is no official AOT support.

## What was built

| Path | Role |
|---|---|
| `SqlAot.App/` | `net11.0`, `win-x64`, `PublishAot=true`, `IsAotCompatible=true`, `TrimmerSingleWarn=false` (one diagnostic per member), plain ADO.NET only. Modes: `features` (one PASS/FAIL line per check), `first-query` (timing), `tls` (Encrypt combinations). Knobs: `-p:SqlClientVersion=`, `-p:SqlProvider=Microsoft\|System`, `-p:InvariantGlobalization=`, `-p:SqlManagedSni=true`. |
| `Minimal.App/` | The same settings, with only `new SqlConnection` + `Open` + `ExecuteScalar("SELECT 1")`. |
| `Baseline.App/` | The same settings with no SqlClient. Used as the size and startup baseline. |
| `publish.ps1` | For each variant: restore, delete `obj/.../native` (forces a full ILC run), then a **strict** publish (repository policy) and, for runnable variants, a **lax** publish with `-p:TreatWarningsAsErrors=false -p:IlcTreatWarningsAsErrors=false`. Writes logs, unique warning lists and `publish-summary.txt`. |
| `run.ps1` | Builds the JIT variant into `bin/jit` and starts `spar-sqlclient-aot` from the local image with `--pull never`, on a free loopback port. The SA password is generated in-process, passed with `-e MSSQL_SA_PASSWORD` (no value on the command line) and never written out. The script then runs features, TLS and timing, and always removes the container. |

`IsAotCompatible=true` on the app only enables the analyzers for the app's own code. Those builds were clean, and
the setting has no effect on the package diagnostics, which come from ILC.

## Strict publish (the deliverable)

`dotnet publish SqlAot.App -c Release` with the repository policy inherited (`TreatWarningsAsErrors=true`, so
`IlcTreatWarningsAsErrors` follows it):

| Variant | Result | Diagnostics (lines / unique) |
|---|---|---|
| Baseline (no SqlClient) | **pass** | 0 |
| Microsoft.Data.SqlClient 7.1.2 | **fail** (ILC exit 1, MSB3073) | 57 / 49 |
| 7.1.2, SDK default `TrimmerSingleWarn=true` | **fail** | 4: IL2104 × 3 (Microsoft.Data.SqlClient, Microsoft.Data.SqlClient.Internal.Logging, System.Configuration.ConfigurationManager) + IL3053 (Microsoft.Data.SqlClient) |
| 7.1.2, `InvariantGlobalization=true` | fail | 57 / 49 (the same) |
| 7.1.2, managed SNI | fail | 57 / 49 (the same) |
| 7.1.2, `EventSourceSupport=false` | fail | 57 / 49. No change: AOT already defaults to `EventSourceSupport=false`, and the warning is at the call site. |
| 7.1.2, Minimal.App (open + `SELECT 1` only) | fail | 51 / 43 |
| 7.0.4 | fail | 57 / 49 |
| 6.1.8 | fail | 45 / 43 |
| **8.0.0-preview1.26313.4** | fail | **27 / 27** |
| System.Data.SqlClient 4.9.1 | fail **in C# compile** (CS0618 `[Obsolete]` on every type, an error under warnings-as-errors). Only a suppression would get past this. | Lax: 28 / 27 IL diagnostics |

ILC reports every diagnostic before it fails, so the strict log already holds the complete list. The lax run (used
only to get runnable output) showed the same 57 lines.

### Full warning list, Microsoft.Data.SqlClient 7.1.2 (49 unique), grouped by cause

`artifacts/results/warnings-strict-ms-7.1.2.txt` has each line as `code | member | API it calls`.

| # | Cause | Package | Diagnostics | Also in Minimal.App (open + `SELECT 1`)? | 8.0.0-preview1 |
|---|---|---|---|---|---|
| A | `DiagnosticSource.Write<T>` (RUC) in `SqlDiagnosticListener.Write{Command,Connection,Transaction}{Before,After,Error}` | Microsoft.Data.SqlClient | IL2026 × 15 | yes (the 9 command and connection ones; the 6 transaction ones only when transactions are used) | **gone** |
| B | `EventSource.WriteEvent(int, object[])` in `SqlClientEventSource.BeginExecute/EndExecute` | Microsoft.Data.SqlClient.Internal.Logging | IL2026 × 2 | yes | **gone** |
| C | Reflection-based `System.Text.Json` (RUC + RDC): `SqlVector<T>.GetString()` (Serialize), `SqlParameter.CoerceValue` (`Deserialize<float[]>` for vector parameters), `HostGuardianServiceEnclaveProvider.MakeRequest` (`Deserialize<List<byte>>`, Always Encrypted enclave attestation) | Microsoft.Data.SqlClient | IL2026 × 3, IL3050 × 3 | yes (ILC sees `CoerceValue` as reachable even without parameters) | **gone** |
| D | Authentication provider config and Azure extension loading by name: `SqlAuthenticationProviderManager..cctor` (`Assembly.GetType`), its constructor taking the config section (`Type.GetType`), `CreateAzureAuthenticationProvider` (`Activator.CreateInstance`, `GetConstructor`, `GetProperty`) | Microsoft.Data.SqlClient | IL2026, IL2057, IL2067, IL2070 × 2 | yes (static constructor) | replaced by `SqlAuthenticationProviderRegistry.EnsureBootstrapped`: IL2026 × 4, IL3050 × 3 |
| E | Configurable retry logic loaded from config: `SqlConfigurableRetryLogicLoader` (`AssemblyLoadContext.LoadFromAssemblyPath`, `Assembly.ExportedTypes`, `Type.GetType`, unannotated `CreateInstance`) | Microsoft.Data.SqlClient | IL2026 × 3, IL2057, IL2072 | yes | remains (5) |
| F | SQL CLR UDT support: `SmiMetaData.Type`, `SqlConnection.CheckGetExtendedUDTInfo` (`Type.GetType` from a server-sent name), `BinaryOrderedUdtNormalizer`, `SerializationHelperSql9`, `ValueUtilsSmi.GetUdt_LengthChecked/NullUdtInstance` (`Type.InvokeMember`), `MetaDataUtilsSmi` | Microsoft.Data.SqlClient | IL2057 × 2, IL2072 × 6, IL2075 × 2 | yes (inside the TDS metadata path) | remains (10) |
| G | `SqlDataReader.BuildSchemaTable()` touches `Type.TypeInitializer` through reflection | Microsoft.Data.SqlClient | IL2111 | yes | **gone** |
| H | app.config type loading: `System.Configuration.TypeUtil.GetType/GetImplicitType/CreateInstance/GetConstructor` | System.Configuration.ConfigurationManager | IL2026, IL2057 × 2, IL2067, IL2070 | yes (through D and E) | remains (5) |

Totals: A 15 + B 2 + C 6 + D 5 + E 5 + F 10 + G 1 + H 5 = 49. The 8.0.0-preview1 list is D′ 7 + E 5 + F 10 + H 5
= 27. Minimal.App lacks only the 6 transaction diagnostics in group A.

System.Data.SqlClient 4.9.1 (lax) has 27 unique diagnostics:

- `DiagnosticSource.Write` of anonymous types: IL2026 × 15.
- UDT reflection: IL2057 × 2, IL2067 × 2, IL2070 × 3, IL2077, IL2080.
- `SqlDataReader.GetFieldType/GetProviderSpecificFieldType` annotation mismatch: IL2093 × 2.
- `BuildSchemaTable`: IL2111.

### Mitigations tried without suppressions

| Mitigation | Effect on the strict result |
|---|---|
| Newer or older SqlClient (6.1.8, 7.0.4, 7.1.2, 8.0.0-preview1) | Still fails. The preview removes groups A, B, C and G, leaving 27. |
| `InvariantGlobalization` true/false | No effect on diagnostics. `true` breaks Microsoft.Data.SqlClient at runtime (below). |
| Managed SNI (`UseManagedNetworkingOnWindows` feature switch) | No effect on diagnostics. It removes the sidecar DLL. |
| `EventSourceSupport=false` | No effect (already the AOT default). |
| Using fewer APIs (Minimal.App) | 49 → 43. Every remaining diagnostic sits on the open/execute path, in static constructors or in metadata handling. |
| System.Data.SqlClient | Fails even earlier (obsolete type errors), and has 27 IL diagnostics of its own. It is deprecated. |

No configuration publishes cleanly under the repository rule. The remaining options are decisions for the user,
none of them tested as a deliverable:

- An agreed suppression: for example, scoped `NoWarn` for IL2104 and IL3053 with the default single-warn mode,
  which hides warnings for whole assemblies.
- Waiting for SqlClient to become trim-safe.
- A CoreCLR (non-AOT) publication for an app that needs SQL Server. Per AGENTS.md, that is a publishing decision.

## Runtime under AOT (lax publications, against SQL Server 2022 in Docker)

Connection string: `Server=127.0.0.1,<port>;User ID=sa;Password=…;Encrypt=true;TrustServerCertificate=true`.

| Check | AOT 7.1.2 (native SNI) | AOT 7.1.2 (managed SNI) | AOT 8.0.0-preview1 | JIT 7.1.2 | AOT System.Data 4.9.1 |
|---|---|---|---|---|---|
| Open; server version | pass | pass | pass | pass | pass |
| CREATE DATABASE, server collation (`SQL_Latin1_General_CP1_CI_AS`) | pass | pass | pass | pass | pass |
| CREATE TABLE | pass | pass | pass | pass | pass |
| Parameterized INSERT with explicit `SqlDbType`, plus an output parameter (`SCOPE_IDENTITY`) | pass | pass | pass | pass | pass |
| INSERT of all-NULL values (`DBNull.Value`) | pass | pass | pass | pass | pass |
| SELECT typed getters round trip: int, bigint 9 007 199 254 740 993, decimal(18,4), datetime2(7) to the tick, nvarchar(max) 10 001 chars with a surrogate pair, varbinary(max) 100 000 bytes, varchar Latin1 `café Größe €` (client encodes with code page 1252), varchar `Japanese_CI_AS` `日本語` (client decodes code page 932), uniqueidentifier | pass | pass | pass | pass | pass |
| NULL read (`IsDBNullAsync`, `GetValue` returns `DBNull`) | pass | pass | pass | pass | pass |
| `GetValue` boxed CLR types, `GetDataTypeName` | pass | pass | pass | pass | pass |
| Transaction rollback (count 2 → 3 inside → 2 after) | pass | pass | pass | pass | pass |
| Transaction commit | pass | pass | pass | pass | pass |
| Unique violation → `SqlException.Number == 2627` (Class 14, 2 errors) | pass | pass | pass | pass | pass |
| NOT NULL violation → `SqlException.Number == 515` | pass | pass | pass | pass | pass |
| Connection usable after errors | pass | pass | pass | pass | pass |
| Async with a pre-cancelled token → `TaskCanceledException` | pass | pass | pass | pass | pass |
| Async cancel of `WAITFOR DELAY 2s; INSERT …` after 300 ms → `SqlException` "Operation cancelled by user" after ~307 ms. After waiting 2.5 s the INSERT is absent, and the connection is still `Open` | pass | pass | pass | pass | pass |
| `CommandTimeout=1` → `SqlException.Number == -2`, connection `Open` | pass | pass | pass | pass | pass |
| Sync `ExecuteReader`/`Read` | pass | pass | pass | pass | pass |
| DROP TABLE | pass | pass | pass | pass | pass |

Apart from timings and generated names, the AOT outputs are line-for-line identical to the JIT output
(`features-*.txt`). Minimal.App (AOT) printed `SELECT 1 -> 1`.

Failing variants:

| Variant | Outcome |
|---|---|
| AOT 7.1.2 with `InvariantGlobalization=true` | `System.NotSupportedException: Globalization Invariant Mode is not supported.` from `SqlConnection.TryOpen`, before any network I/O. The same with managed SNI, with 8.0.0-preview1, and under JIT (seen during setup). |
| AOT 7.1.2 exe copied alone, without `Microsoft.Data.SqlClient.SNI.dll` | `TypeInitializationException` → `DllNotFoundException: Unable to load DLL 'Microsoft.Data.SqlClient.SNI.dll'` from `SNILoadHandle..cctor` on the first `Open`. |
| AOT System.Data.SqlClient 4.9.1 with `InvariantGlobalization=true` | Open, DDL and INSERT pass. The typed-getter SELECT (which includes the varchar Latin1 and Japanese columns) fails with `InvalidOperationException: Internal connection fatal error`, and the connection closes; 12 failures follow. The code page/collation cause is inferred, not isolated. |

In these two harness runs, exit code `-1073740791` (0xC0000409) comes from the harness itself: it opens its main
connection outside a check, so the exception goes unhandled and the AOT runtime fails fast.

### Encryption and TLS (`tls` mode)

| Connection options | MDS 7.1.2 AOT (native SNI) | MDS 7.1.2 AOT (managed SNI) | MDS 7.1.2 JIT | System.Data 4.9.1 AOT |
|---|---|---|---|---|
| Neither `Encrypt` nor `TrustServerCertificate` (defaults) | **fail**: certificate chain not trusted (the default is `Encrypt=Mandatory`) | **fail**: "pre-login handshake … TCP Provider, error: 35" | fail, as native | **OK, unencrypted** (old default `Encrypt=false`) |
| `Encrypt=true;TrustServerCertificate=false` | fail (untrusted chain) | fail (error 35) | fail | fail (untrusted chain) |
| `Encrypt=true;TrustServerCertificate=true` | OK, `encrypt_option=TRUE` | OK, TRUE | OK, TRUE | OK, TRUE |
| `Encrypt=false` | OK, FALSE | OK, FALSE | OK, FALSE | OK, FALSE |
| `Encrypt=Strict;TrustServerCertificate=true` (TDS 8) | fail: "forcibly closed by the remote host" | fail: "SSL Provider, error: 31" | fail, as native | n/a |

The dev container's certificate is self-signed, so the dev connection string needs
`TrustServerCertificate=true` (or `Encrypt=false`). Strict mode failed in both AOT and JIT against this container,
so the failure is not AOT-specific; it was not investigated further. With managed SNI the certificate failure has a
less helpful message ("TCP Provider, error: 35").

### Windows specifics: SNI

- The default (native SNI) publish folder contains `SqlAot.App.exe` and `Microsoft.Data.SqlClient.SNI.dll`
  (555 416 bytes), plus the `.pdb`. The SNI DLL is a **sidecar loaded by `DllImport`**, not linked statically.
  The package ships no static library, and without the DLL the first `Open` fails (above). No other files are
  needed: no runtimeconfig, and no ICU, because .NET uses the ICU that ships with Windows.
- **Single-file option:** set the shipped feature switch and drop the native asset. Two items:
  `<RuntimeHostConfigurationOption Include="Switch.Microsoft.Data.SqlClient.UseManagedNetworkingOnWindows" Value="true" Trim="true" />`
  and `<PackageReference Include="Microsoft.Data.SqlClient.SNI.runtime" ExcludeAssets="all" PrivateAssets="all" />`.
  The publish folder is then the exe alone, and every feature check passed. Not tested: Windows integrated
  authentication, named pipes and shared memory with managed networking.

## Numbers

Exe sizes (lax publications; the baseline is strict):

| Exe | Bytes | Δ vs baseline | Sidecar |
|---|---|---|---|
| Baseline, `InvariantGlobalization=false` | 1 689 088 | — | — |
| Baseline, `InvariantGlobalization=true` | 1 428 992 | — | — |
| SqlAot.App, MDS 7.1.2, native SNI | 16 408 576 | +14.72 MB | SNI.dll 555 416 |
| SqlAot.App, MDS 7.1.2, managed SNI | 16 226 304 | +14.54 MB | none |
| Minimal.App, MDS 7.1.2 | 15 967 744 | +14.28 MB | SNI.dll 555 416 |
| SqlAot.App, MDS 8.0.0-preview1 | 14 927 360 | +13.24 MB | SNI.dll 555 416 |
| SqlAot.App, System.Data 4.9.1 | 10 108 928 | +8.42 MB | sni.dll 160 040 |

Publish time: one run each. Timed from `dotnet publish --no-restore` to exit, after `obj/.../native` was deleted so
that ILC does a full compile. Other builds were running on the machine at the time.

| Variant | Strict (to the failure) | Lax (full) |
|---|---|---|
| Baseline | 6.7 s | 6.6 s |
| MDS 7.1.2 | 15.1 s | 15.7 s |
| MDS 7.1.2, managed SNI | 14.9 s | 15.3 s |
| MDS 8.0.0-preview1 | 13.9 s | 14.3 s |
| System.Data 4.9.1 | 0.7 s (CS0618) | 12.2 s |

Startup to first query (`first-query` mode). Each process opens a new connection: TCP, TLS and SQL login, then
`SELECT 1`. A second open from the pool plus `SELECT 1` is timed inside the same process. "Cold" is the first
launch of that exe in the run script; the exe was already in the OS file cache from publishing, so this is not a
cold-disk start. "Warm" is the median of runs 2–6. Time is in ms; "since start" is measured from the OS process
start time.

| Exe | Wall, cold | Wall, warm median | First query since start, cold | First query since start, warm median | Pooled open + query, median |
|---|---|---|---|---|---|
| Baseline AOT (no SQL; prints and exits) | 50.9 | 51.6 | (Main at 39.7) | (Main at 41.5) | — |
| **AOT MDS 7.1.2, native SNI** | 109.7 | **101.6** | 99.8 | **92.0** | 0.77 |
| AOT MDS 7.1.2, managed SNI | 132.7 | 131.9 | 121.4 | 121.7 | 1.22 |
| JIT MDS 7.1.2 (same code and feature switches) | 335.6 | 336.6 | 310.3 | 312.3 | 5.15 |

About 40 ms of every row is process launch, including the baseline; the timings are taken from PowerShell. AOT
reaches the first query about 3.4× sooner than JIT (92 ms vs 312 ms). Managed SNI costs about 30 ms more per new
physical connection than native SNI on this machine.

## Observed, skipped, not tested

**Observed (this run):**

- Strict publishes of every variant in the table above, all failing except the baseline.
- The full diagnostic lists.
- Lax publications.
- Every runtime feature row against a real SQL Server 2022 container, for AOT 7.1.2 (native and managed SNI),
  AOT 8.0.0-preview1, JIT 7.1.2 and AOT System.Data 4.9.1.
- The invariant-mode failure (AOT, and JIT during setup).
- The missing-SNI failure.
- The TLS matrix.
- Sizes, publish times and the 6-run startup timings.
- The container was started with `--pull never` and removed afterwards; no image was pulled. No files outside
  `spikes/sqlclient-aot/` were changed.

**Skipped:**

- System.Data.SqlClient strict beyond the compile step: getting past CS0618 needs a suppression.
- A JIT run of the System.Data.SqlClient variant.
- Strict-mode TLS against a server with a trusted certificate.

**Not tested:**

- Code paths that the remaining warnings point at:
  - SQL CLR UDT columns (group F).
  - App.config-driven authentication providers and retry logic (D, E, H).
  - Entra ID authentication with Microsoft.Data.SqlClient.Extensions.Azure (D).
  - Always Encrypted and enclaves (C).
  - The `vector` and `json` types (C; SQL Server 2025).
  - `DiagnosticListener` subscribers (A).
  - `GetSchemaTable` (G).
- Other features:
  - MARS, `SqlBulkCopy`, `SqlBatch`, `DataSet`/`SqlDataAdapter`, `DbProviderFactories`.
  - Windows integrated authentication.
  - Named pipes and shared memory.
  - ARM64.
- Note on the JIT comparison: because `PublishAot=true`, the JIT build also runs with the AOT feature switches
  (`IsDynamicCodeSupported=false`, `EventSource.IsSupported=false`,
  `JsonSerializer.IsReflectionEnabledByDefault=false`). The JIT comparison therefore checks code generation, not
  those switches. The last switch means that SqlClient's reflection-based JSON paths (vector) would also fail in
  this configuration under JIT; this is inferred, not tested.
- True cold-disk startup.

## Reproduce

```powershell
./publish.ps1          # strict + lax publishes, artifacts/results/publish-summary.txt and warnings-*.txt
./run.ps1              # container, features-*.txt, tls-*.txt, timing.txt; removes the container
./run.ps1 -Steps timing
```
