# SqlClient features for the SQL tool: spike results

> A spike. Its code was deleted with `spikes/` at the start of M2; it is in git history at [`spikes/sqlclient-tool/`](https://github.com/sotsera/spar/tree/24f5db0942833047c81fa692908410596f3b8960/spikes/sqlclient-tool) (commit `24f5db0`), and this record and its `results/` moved here. Paths under `spikes/` below name that history.

**Question.** The first Spar consumer is a Windows tool that monitors SQL Server instances, compares databases and
copies data between instances. Do the SqlClient features it needs work when the app publishes NativeAOT and accepts
its own AOT warnings (the user's per-app decision)? How do they compare with CoreCLR (JIT)?

**Answer: yes. Every feature tested works under NativeAOT with warnings accepted, and the results match JIT.**
No check behaved differently under AOT. Every checksum, schema difference, row count and exception type was the same.
The issues found are SqlClient behaviours that the tool must design around under both runtimes, not AOT problems:

- **Cancelling a `SqlBulkCopy` with no transaction commits the rows already sent, including part of the current
  batch.** With 50 000-row batches cancelled at 310 000 rows, B kept 310 001 to 310 027 rows. With `BatchSize = 0`
  (one batch) it still kept about 310 000. Only `UseInternalTransaction` (complete batches only) or an external
  transaction (all or nothing) gives a defined result.
- **Disposing a `SqlDataReader` early drains the rest of the result set.** Stopping after 1 000 of 1M rows took
  946 ms, against 19 ms after `SqlCommand.Cancel()`. The same goes for the source reader of a cancelled bulk copy.
- **`ReadAsync(token)` cancellation is client-side only.** It throws in 0.1–1.8 ms, but the server request keeps
  running (`ASYNC_NETWORK_IO`) until the reader is disposed.
- A cancelled command surfaces as `SqlException` (Number 0, Class 11, "A severe error occurred on the current
  command"), not as `OperationCanceledException`. Check the token to tell a cancellation from a failure.
- Async reads with the default `CommandBehavior` are 1.5–1.7× slower and allocate about twice as much as
  `SequentialAccess` or sync reads.
- `EnableStreaming` on the bulk copy, and `GetStream`/`GetTextReader` reads, allocate 6–17 GB per million rows.
  Use them only for real BLOB columns.

On performance, AOT uses about half the memory (peak working set 40–44 MB against 74–81 MB) and less than half the
process CPU. On first passes it is 20–30% faster. Steady-state throughput is within about ±10% of JIT for reads.
**For bulk copy, JIT was 3–20% faster** in every variant (default variant, median of 3: 11.7 s against 14.1 s for 1M rows).

Environment:

- Windows 11 Pro 10.0.26320, Intel i9-13900K (32 logical CPUs), 96 GB of RAM.
- .NET SDK 11.0.100 (ILCompiler 11.0.0), win-x64.
- Docker 29.9 with Linux containers.
- Two containers from the local image `mcr.microsoft.com/mssql/server:2022-latest`, started with `--pull never`,
  `MSSQL_MEMORY_LIMIT_MB=4096` and a host port on 127.0.0.1. The server reports 16.0.4295.3, Developer Edition,
  32 CPUs and a committed target of about 3.2 GB.
- Both containers and the client ran on the same machine, so the network is loopback. All numbers were observed on
  2026-11-13 in this folder, with other agents building on the machine at the same time.
- Raw output is in `artifacts/results/`: one file per runtime, mode and variant, plus `run-console.txt`.

## Versions and build

| Item | Value |
|---|---|
| Microsoft.Data.SqlClient | 7.1.2 (informational version `7.1.2+b1dd9db7…`, assembly version 7.0.0.0) |
| Microsoft.Data.SqlClient.SNI.runtime | 7.1.2 (native `Microsoft.Data.SqlClient.SNI.dll`, 555 416 bytes) |
| `InvariantGlobalization` | `false` (required by SqlClient) |
| AOT publish | `dotnet publish -c Release -p:SqlToolRuntime=aot -p:TreatWarningsAsErrors=false -p:IlcTreatWarningsAsErrors=false`: the app-level warning acceptance, on the command line only, with no suppressions in project files |
| JIT build | `-p:SqlToolRuntime=jit`: `PublishAot=false`, framework-dependent. This is a true CoreCLR comparison, so unlike `spikes/sqlclient-aot` the AOT feature switches are not on. |
| AOT exe | 17 115 136 bytes plus the 555 416-byte SNI DLL. Managed SNI variant: a single exe of 16 932 352 bytes. |
| JIT output | 7.1 MB in 38 files (needs the .NET 11 runtime) |
| AOT publish time | 12.4 s (native SNI), 11.9 s (managed SNI); full ILC run, one run each |

**ILC diagnostics: 53 unique lines, the same for both AOT variants** (`warnings-aot.txt`).

- 51 lines come from SqlClient and its dependencies. This is the list from `spikes/sqlclient-aot` plus **one new
  line**: IL2072 in `SqlMetaDataFactory.<ExecuteCommandAsync>` (`DataColumnCollection.Add(string, Type)`), reached
  through `SqlConnection.GetSchema`. The earlier spike reported 49 because it counted the two IL2067 lines in
  `CreateAzureAuthenticationProvider` (`optionsType` and `providerType`) as one.
- **`SqlBulkCopy`, cancellation, sequential access and the catalog queries add no diagnostics.**
- 2 lines are the app's own IL2026 for `DataTable.Load(IDataReader)`, once from the build analyzer and once from ILC.
  The getschema probe uses it. The JIT build also needs `TreatWarningsAsErrors=false` for that analyzer warning,
  because the app has `IsAotCompatible=true`.

## What was built

| Path | Role |
|---|---|
| `SqlTool.App/` | Console harness (`net11.0`, `win-x64`, plain ADO.NET). Each scenario runs in its own process, so its peak memory is its own. The connection strings come from `SPAR_SQL_A`/`SPAR_SQL_B` and are never printed. |
| `Directory.Build.props` | Imports the root props. `SqlToolRuntime=aot\|aot-managedsni\|jit` selects the runtime, and each runtime gets its own `bin/<runtime>/` and `obj/<runtime>/`. |
| `Directory.Packages.props` | Imports the root file, then pins SqlClient 7.1.2. |
| `run.ps1` | Builds all three variants, records the unique warnings and runs SSPI against the local Windows instance. It then starts `spar-sqltool-a` and `spar-sqltool-b` (`--pull never`), runs every scenario under AOT and JIT (interleaved: AOT then JIT for each variant), plus a managed-SNI subset, and always removes the containers. The SA password is generated in-process and passed with `-e MSSQL_SA_PASSWORD` (no value on the command line). |

Data: `SparToolSrc.dbo.Orders` on A, 1 000 000 rows and 18 columns.

- Column types: int, bigint, smallint, tinyint, bit, uniqueidentifier, two decimals, float, non-ASCII nvarchar
  (`København`, `東京`, `€`), datetime2(7) with 100 ns ticks, datetime2(3), varbinary(32) and NULLs in 9 columns.
- Every 1 000th row has a 4 320-byte nvarchar(max), and every 500th a 16 KiB varbinary(max).
- 195.5 MB of column payload, 230 MB on disk.

The destination `SparToolDst.dbo.Orders` on B has the same definition, with a clustered primary key. Both databases
use simple recovery. Every copy was verified with an aggregate fingerprint (count, `BINARY_CHECKSUM`, sums, BLOB
lengths), compared between A and B.

## 1. SqlBulkCopy A → B, streaming from a SqlDataReader

All copies use `WriteToServerAsync(SqlDataReader)` and name-based `ColumnMappings` for all 18 columns. The source is
`SELECT … ORDER BY OrderId`. Each row in the table is one process run, except `default` and `b100k`, which are
medians of 3 runs. Notify is every 100 000 rows (10 progress events), except for `default`.

| Variant | AOT ms | AOT rows/s | JIT ms | JIT rows/s | Identical on B |
|---|---:|---:|---:|---:|---|
| `default`: BatchSize 0, no options. Runs: AOT 14 789 / 13 693 / 14 067, JIT 11 673 / 9 121 / 12 400 | **14 067** | 71 091 | **11 673** | 85 668 | yes ×6 |
| `default-tablock`: BatchSize 0, `TableLock` | 11 543 | 86 634 | 10 303 | 97 058 | yes |
| `b10k`: BatchSize 10 000 | 13 603 | 73 511 | 10 887 | 91 854 | yes |
| `b100k`: BatchSize 100 000. Runs: AOT 12 390 / 17 330 / 14 509, JIT 11 770 / 11 484 / 12 358 | **14 509** | 68 922 | **11 770** | 84 965 | yes ×6 |
| `b100k-stream`: `EnableStreaming`, source reader `SequentialAccess` | 17 265 | 57 921 | 16 408 | 60 946 | yes |
| `b100k-tablock` | 11 921 | 83 889 | 11 535 | 86 695 | yes |
| `b100k-internaltx`: `UseInternalTransaction` | 12 795 | 78 155 | 10 710 | 93 371 | yes |
| `b100k-externaltx`: `SqlTransaction` on B, commit at the end | 13 310 | 75 130 | 11 406 | 87 671 | yes |
| `b100k`, AOT with managed SNI (1 run) | 17 167 | 58 251 | — | — | yes |

Throughput: 11–19 MB/s of column payload, or 58 000–109 000 rows/s across all runs.

Memory and CPU per process, for the non-streaming variants:

| | AOT | JIT |
|---|---:|---:|
| Peak working set | 42–44 MB | 75 MB |
| Peak private bytes | 29–31 MB | 37–38 MB |
| Managed allocations for 1M rows | 1 834 MB (≈1.9 KB/row) | 1 829–1 839 MB |
| Gen0/1/2 GCs | ≈103/2/1 | ≈104/2/1 |
| Process CPU for the copy | 12.0–14.4 s | 26–32 s |

- With `EnableStreaming`, allocations jump to **17.1 GB** in both runtimes (1 025–1 176 gen0 GCs), and the copy is
  20–40% slower.
- Memory stays flat with every option: the bulk copy streams, so peak memory does not depend on the table size.
- `NotifyAfter` progress events arrive on the copying thread, every 1.1–2.6 s at 100 000 rows. The maximum gap
  includes the first batch.
- `TableLock` was the fastest option for an empty destination.
- Observation: JIT was faster in every bulk variant, by 3–22%, while using twice the CPU. Tiered PGO is the likely
  reason (inferred, not profiled). AOT was not faster for the copy itself.

## 2. Cancelling a bulk copy mid-way

Every variant uses 50 000-row batches (except `batch0*`), with NotifyAfter 10 000. The trigger is
`CancellationTokenSource.Cancel()` from a thread-pool thread once the progress callback reports 310 000 rows. At that
point six batches are complete and the seventh is in flight. The exceptions are `abort` (sets
`SqlRowsCopiedEventArgs.Abort = true` in the callback) and `none-timed` (`Cancel()` after 1.5 s).

After the exception, a separate connection inspects B: `sys.dm_exec_requests`, `dm_exec_sessions` and
`dm_tran_locks` for the destination session, and any request whose command is `%BULK%`. Rows on B are counted after
any rollback and again after the connection closes.

| Variant | Rows on B, AOT / JIT | Cancel → exception, AOT / JIT (ms) | Exception |
|---|---|---|---|
| `none`: no transaction | **310 002 / 310 012** | 51 / 37 | `TaskCanceledException` |
| `none-timed`: no transaction, cancelled after 1.5 s | **103 462 / 45 319** (last progress event 100 000 / 40 000) | 34 / 168 | `TaskCanceledException` |
| `none-drain`: as `none`, source reader disposed without `Cancel` | 310 001 / 310 027 | 38 / 48 | `TaskCanceledException` |
| `internal`: `UseInternalTransaction` | **300 000 / 300 000** | 39 / 36 | `TaskCanceledException` |
| `external`: `SqlTransaction`, then `Rollback()` | **0 / 0** | 35 / 42 | `TaskCanceledException` |
| `abort`: `e.Abort = true` | 310 000 / 310 000 | 34 / 39 | `OperationAbortedException` |
| `batch0`: BatchSize 0, no transaction | **310 001 / 310 023** | 1 005 / 884 | `TaskCanceledException` |
| `batch0-internal`: BatchSize 0, `UseInternalTransaction` | **0 / 0** | 1 045 / 989 | `TaskCanceledException` |
| `batch0-external`: BatchSize 0, external transaction, then `Rollback()` | 0 / 0 | 809 / 806 | `TaskCanceledException` |
| `batch0-abort`: BatchSize 0, `e.Abort` | 310 000 / 310 000 | 830 / 793 | `OperationAbortedException` |
| `batch0-tablock`: BatchSize 0, `TableLock` | 310 000 / 310 025 | 388 / 279 | `TaskCanceledException` |
| `none`, AOT with managed SNI | 310 001 | 35 | `TaskCanceledException` |

What is committed:

- **Without a transaction, what was sent is kept, and the partial batch counts.** The token cancel and `Abort` both
  end the bulk insert cleanly instead of rolling it back. This is true even for `BatchSize = 0`, which looks like
  "one atomic batch" but is not.
- `UseInternalTransaction` keeps only complete batches. With BatchSize 0 that means nothing.
- With an external transaction, B showed the session `sleeping`, `open_transaction_count = 1` and 9 880 (BatchSize
  50 000) or 1 713 (BatchSize 0) locks held until `Rollback()`. After the rollback: 0 rows, no locks.
  `SqlTransaction.Connection` was still set (not zombied), and `Rollback()` succeeded.

Did server work stop?

- **Destination.** In every run, the first probe after the exception (0.7–11.6 ms after it) found no request for the
  destination session and no `BULK` request on B. The session was `sleeping`. The destination connection stayed
  `Open` and ran `SELECT 1` afterwards.
- **Source.** In every run but one, the source `SELECT` on A was still running (`ASYNC_NETWORK_IO`) after the bulk
  copy threw, because `SqlBulkCopy` does not cancel its source reader. `SqlCommand.Cancel()` followed by reader
  dispose took 18–98 ms and left the source session idle.
- `none-drain`: disposing the source reader without `Cancel` also took only 43–51 ms in this run, but 694 ms in a
  development run with the same code. The difference is not explained. The query-cancel results below show the
  drain clearly.

Cancellation latency:

- 34–51 ms with 50 000-row batches.
- 0.8–1.0 s with BatchSize 0 (0.3–0.4 s with `TableLock`).
- A 1 M-row bulk copy that must be cancellable quickly should use bounded batches.

## 3. Cancelling long-running queries

Each case runs on A. A probe connection reads the session's request just before cancelling, then polls
`sys.dm_exec_requests` every 5 ms after the exception.

| Case | Server state before cancel | Cancel → exception, AOT / JIT (ms) | Request gone (first probe), AOT / JIT (ms after cancel) | Connection afterwards |
|---|---|---|---|---|
| `WAITFOR DELAY '00:01:00'`, `ExecuteNonQueryAsync(token)`, cancel at 1 s | `WAITFOR/suspended` | 10.4 / 6.7 | 12.3 / 9.6 | Open, `SELECT 1` 0.8 / 2.0 ms |
| The same, sync `ExecuteNonQuery` with `SqlCommand.Cancel()` from another thread | `WAITFOR/suspended` | 4.0 / 4.5 | 8.2 / 7.4 | Open, reusable |
| CPU-bound cross join, `MAXDOP 1`, cancel at 2 s | `SELECT/running`, cpu ≈2 s | 3.0 / 7.3 | 3.8 / 9.6 | Open, reusable |
| The same, `MAXDOP 4` (parallel) | `SELECT/suspended/CXCONSUMER`, cpu ≈8 s | 7.0 / 15.1 | 7.7 / 18.0 | Open, reusable |
| SELECT blocked on a row lock (`LCK_M_S`), cancel at 1 s | `SELECT/suspended/LCK_M_S` | 1.1 / 3.7 | 1.9 / 6.2 | Open, reusable |
| Streaming 1M rows with `ReadAsync(token)`, cancel after 100 000 rows | `SELECT/suspended/ASYNC_NETWORK_IO` | **0.1 / 1.8** | **still running 500 ms later**; gone only after reader dispose (55 / 60 ms), so 579 / 590 ms including the deliberate 500 ms wait | Open, reusable |
| `WAITFOR`, async, AOT with managed SNI | `WAITFOR/suspended` | 8.9 | 22.3 | Open, reusable (16.4 ms) |

- The exception for a cancelled command is `SqlException` with Number 0, Class 11, "A severe error occurred on the
  current command. The results, if any, should be discarded.", in both runtimes, both SNI modes and both the token
  and `SqlCommand.Cancel()` paths. `ReadAsync` with a cancelled token throws `TaskCanceledException` instead.
- The parallel query's workers also stopped: the request was gone by the first probe.
- Early exit without cancelling: reading 1 000 rows of `SELECT *` and then disposing the reader took **946 / 1 000 ms**,
  because it drains the other 999 000 rows. With `SqlCommand.Cancel()` before dispose it took **19 / 20 ms**. The
  connection was reusable in both cases.

## 4. Reading 1M rows

`SELECT` of all 18 columns. Each process runs 3 passes, each on a new pooled connection. The table shows the median
pass. The checksum (computed from every value read) was identical across the runtimes for each access pattern.

| Variant | AOT median ms (rows/s, payload MB/s) | JIT median ms (rows/s, payload MB/s) | First pass, AOT / JIT (ms) | Allocated per pass |
|---|---|---|---|---:|
| `typed-async`: `ReadAsync`, default behaviour, typed getters | 2 247 (445 128, 87.0) | 2 046 (488 824, 95.6) | 2 247 / 2 808 | 629 MB |
| `typed-sync`: `Read`, typed getters | 1 379 (725 296, 141.8) | 1 453 (688 057, 134.5) | 1 354 / 1 735 | 327 MB |
| `seq-async`: `ReadAsync` + `SequentialAccess` | **1 295 (771 987, 150.9)** | 1 421 (703 483, 137.5) | 1 295 / 1 809 | 331 MB |
| `seq-sync`: `Read` + `SequentialAccess` | 1 504 (664 897, 130.0) | 1 290 (774 957, 151.5) | 1 504 / 1 743 | 327 MB |
| `seq-stream-async`: `SequentialAccess`, `IsDBNullAsync`, `GetTextReader`/`GetStream` for the max columns | 1 956 (511 237, 99.9) | 2 144 (466 434, 91.2) | 1 963 / 2 757 | **5 995 MB** |
| `values-sync`: `GetValues(object[])`, boxed | 1 339 (747 050, 146.0) | **1 173 (852 522, 166.7)** | 1 339 / 1 623 | 619 MB |
| `typed-async`, AOT with managed SNI | 2 788 (358 621, 70.1) | — | 3 688 | 636 MB |

- Peak working set: AOT 40–44 MB, JIT 74–81 MB.
- Process CPU for 3 passes: AOT 3.4–6.9 s, JIT 3.7–11.8 s.
- Steady state is within ±10% either way. AOT's first pass is 17–29% faster.
- Default-behaviour `ReadAsync` costs about 1.7× the time and 1.9× the allocations of `SequentialAccess` `ReadAsync`.
- Managed SNI was about 24% slower than native SNI for this read.
- The 6 GB of `seq-stream-async` (about 6 KB per row) was not broken down. It is the per-row async-null-check and
  stream path, not the payload: 195 MB.

## 5. Schema comparison through catalog views

The comparison reads `SparSchemaL` on A and `SparSchemaR` on B with one batch of 8 result sets:

- `sys.tables`, `sys.columns` (joined to `sys.types` twice, `sys.computed_columns` and `sys.default_constraints`);
- `sys.indexes`, `sys.index_columns`, `sys.foreign_keys`, `sys.foreign_key_columns`, `sys.check_constraints`;
- user types from `sys.types`.

Each object becomes a keyed description, and the two sides are diffed. Both databases hold 300 generated tables with
16 columns, a primary key and an index each (every 50th differs), plus hand-written tables with deliberate
differences.

- **Result: 5 730 / 5 727 objects. 28 differences, all of them intended, identical under AOT and JIT** (lines in
  `*-schema.txt`). The detected differences were:
  - a user type's length (`dbo.Code` varchar(10) → varchar(12));
  - an `IDENTITY` property;
  - a column collation (`Latin1_General_CI_AS` → `SQL_Latin1_General_CP1_CI_AS`);
  - an nvarchar length;
  - nullability;
  - a missing column;
  - a check constraint's definition;
  - a default's definition;
  - decimal precision and scale;
  - a computed column's definition, and its derived type;
  - a missing foreign key;
  - index key order plus `UNIQUE`;
  - a missing filtered index;
  - a `DESC` key;
  - tables present on one side only (with their columns and system-named primary keys);
  - six generated tables with a changed column length.
- **Time to extract both databases:** AOT 344.8 ms on the first run (includes two connection opens), then 99.5–107.3 ms.
  JIT 263.9 ms, then 100.9–149.1 ms. The in-memory diff took 5–9 ms.
- System-named constraints (`PK__OnlyLeft__3214EC06…`) differ by name between databases. A real comparer should key
  primary keys by table, not by name.

**`GetSchemaTable` and the other metadata APIs work under AOT** (`getschema` mode, identical output under AOT and JIT):

| API | AOT | Notes |
|---|---|---|
| `SqlDataReader.GetSchemaTable()` (`CommandBehavior.KeyInfo`) | pass, 18.1 ms | 18 rows × 31 columns; `DataType` = `Int32`, `IsKey` = true, `IsLong` for nvarchar(max). Group G warning (IL2111). |
| `DbDataReader.GetColumnSchema()` | pass, 3.0 ms | |
| `SqlConnection.GetSchema()`, `GetSchema("Tables")`, `GetSchema("Columns", …)` | pass, 4.0 / 6.8 / 13.4 ms | The new IL2072 warning is on this path. |
| `DataTable.Load(SqlDataReader)`, 10 000 rows | pass, 66.9 ms | The app's own IL2026. |
| `GetFieldType`/`GetProviderSpecificFieldType` | pass | `Int32/SqlInt32`, `Guid/SqlGuid`, … |

These passing results cover the column types used here. The warnings concern reflection over `Type` members, which
were not exercised beyond this.

## 6. Monitoring queries every second

One batch, polled with a `PeriodicTimer` of 1 s, 30 polls per phase, has three result sets:

- `sys.dm_os_performance_counters` (all 1 986 rows);
- `sys.dm_exec_requests` with `OUTER APPLY sys.dm_exec_sql_text` (45–50 rows);
- `sys.dm_os_wait_stats` where `waiting_tasks_count > 0` (126 rows).

The load phase ran four `MAXDOP 1` CPU-bound queries on the monitored server. Server CPU is the change in
`sys.dm_exec_sessions.cpu_time` for the polling session, divided by the number of polls.

| Phase | AOT median / p95 / max (ms) | JIT median / p95 / max (ms) | Server CPU per poll, AOT / JIT | Allocated per poll |
|---|---|---|---|---:|
| Idle, persistent connection | 9.42 / 21.38 / 46.38 (first poll) | 9.62 / 17.92 / 20.00 | 1.37 / 1.43 ms | 3.4 MB |
| Idle, pooled open per poll | 8.60 / 19.79 / 21.88 | 8.98 / 31.50 / 33.83 | — | 3.4 MB |
| Under load, persistent | 9.30 / 13.29 / 142.93 | 10.17 / 12.51 / 12.63 | 1.10 / 2.07 ms | 3.4 MB |

- A full poll costs about 9–10 ms of wall time and 1–2 ms of server CPU, so a 1 s cadence is cheap for the server.
- The client allocations come almost entirely from the 1 986 performance-counter rows (nchar columns). A real monitor
  should filter the counters it needs.
- Whole process for the three phases: AOT 766 ms of CPU and 43 MB peak working set; JIT 2 203 ms and 76 MB.
- The single 143 ms AOT outlier under load was not repeated and was not investigated.

## 7. Windows integrated authentication (SSPI)

A Windows instance exists locally: service `MSSQLSERVER`, SQL Server 17.0.1155.3, Standard Developer Edition.
`SqlLocalDB.exe` is also installed, but the automatic `MSSQLLocalDB` instance is not created. It was not used,
because connecting would create it.

Nothing was installed or reconfigured. The checks were read-only queries on `sys.dm_exec_connections` and
`SERVERPROPERTY`, with `Integrated Security=true`, `Pooling=false` and a 5 s connect timeout. The login name is not
recorded. The instance has TCP disabled: the only TCP listener is `[::1]:1434`, the dedicated admin connection.

| Data source | AOT (native SNI) | JIT | AOT (managed SNI) |
|---|---|---|---|
| `.` | pass: shared memory, **NTLM**, 28.7 ms (cold) | pass: shared memory, NTLM | fail: TCP timeout (error 35) |
| `localhost` | pass: shared memory, NTLM | pass | fail: TCP timeout |
| `(local)` | pass: shared memory, NTLM | pass | fail: TCP timeout |
| `np:.` | pass: **named pipe**, NTLM, 4.9 ms | pass | **pass: named pipe, NTLM** |
| `lpc:.` | pass: shared memory, NTLM | pass | fail: "No such host is known" (11001) |
| `tcp:localhost`, `tcp:127.0.0.1` | fail: timeout (TCP disabled on the instance) | fail, same | fail, same |

- **SSPI works under NativeAOT**, identically to JIT, over shared memory and named pipes. It used NTLM in every case.
- **Managed SNI has no shared-memory provider.** It maps `.`, `localhost` and `(local)` to TCP, but integrated
  authentication over named pipes works with it.
- Not testable here: Kerberos, SSPI over TCP (TCP is disabled on the only Windows instance, and enabling it would
  change the user's server configuration) and a remote domain server.

## 8. Per-feature outcome

| Feature | AOT, warnings accepted | Difference from JIT |
|---|---|---|
| Open, login, TLS with `TrustServerCertificate` (SQL auth) | works | none |
| `SqlBulkCopy` from `SqlDataReader`: BatchSize, NotifyAfter, `TableLock`, `UseInternalTransaction`, external transaction, `EnableStreaming`, column mappings | works, data identical | JIT 3–22% faster; AOT half the memory and CPU |
| Bulk copy cancellation (token, `Abort`), transaction semantics, server-side stop | works | same committed-row semantics and same server state |
| Command cancellation (async token, `SqlCommand.Cancel`), CPU-bound, parallel, blocked, `WAITFOR` | works, 1–15 ms | none of note |
| `ReadAsync(token)` cancellation and reader drain behaviour | works (client-side only, as in JIT) | none |
| 1M-row reads: typed, sequential, streams, `GetValues` | works, checksums identical | ±10% steady state; AOT faster on the first pass; AOT half the memory |
| Catalog-view schema comparison | works, identical differences | none |
| `GetSchemaTable`, `GetColumnSchema`, `GetSchema`, `DataTable.Load` | works for these types | none |
| DMV polling every second | works | none of note |
| Integrated Security (SSPI) | works (shared memory, named pipes; NTLM) | none |
| Managed SNI (single exe) | works for SQL auth over TCP and SSPI over named pipes | about 24% slower reads and 18% slower bulk copy than native SNI; no shared memory |

**Failures and their causes.** No scenario failed in the final run. Three failures came up during development, all
in the harness, and were fixed:

- The setup script used `REPLICATE` on varbinary, which is implicitly varchar. The setup failed with SQL error 257.
- The harness ran the fingerprint query while the bulk copy's source reader was still open, so `SqlCommand` threw
  "There is already an open DataReader".
- `Directory.Build.props` placed each runtime's `obj/` folder under the project. A sibling runtime's generated
  `AssemblyInfo.cs` was then compiled (CS0579). The fix excludes `bin/**` and `obj/**` from the default items.

## Observed, skipped, not tested

**Observed (final run, `run-console.txt`):**

- The JIT build and both AOT publishes with warnings accepted, and the warning lists.
- SSPI against the local Windows instance under all three variants.
- Setup of 1M rows.
- Every bulk, bulk-cancel, query-cancel, read, schema, getschema and monitor scenario under AOT and JIT, and the
  managed-SNI subset (bulk, read, cancel, bulk-cancel).
- Containers `spar-sqltool-a`/`-b` were started with `--pull never` and removed at the end (`containers removed:
  True`). No image was pulled, and no other container was touched.
- The bulk-cancel semantics (partial batch committed without a transaction) were also seen in an earlier development
  run, with the same numbers within ±30 rows.

**Skipped:**

- Strict publish (the repository rule). It is known to fail; see `spikes/sqlclient-aot`.
- Repeats: only `default` and `b100k` were repeated (3 runs each). The other bulk variants ran once, so a ±15% spread
  should be assumed (the b100k AOT runs ranged 12.4–17.3 s).
- Breaking down the 6 GB and 17 GB allocation figures.
- Log usage on B (`dm_db_log_space_usage`). It was recorded but is unusable, because checkpoint timing under simple
  recovery made it range from 28 to 139 MB.

**Not tested:**

- A real network between client and servers (everything was loopback on one machine), and servers other than SQL
  Server 2022 on Linux (bulk, cancellation, reads, schema, DMVs) and 2025 on Windows (SSPI only).
- Kerberos and SSPI over TCP; LocalDB; Entra ID authentication.
- MARS; `SqlBatch`; `SqlDataAdapter`/`DataSet`; `KeepIdentity`, `FireTriggers` and `CheckConstraints` bulk options;
  bulk copy into tables with triggers, foreign keys or identity columns; a `DataTable` or `IDataReader` source other
  than `SqlDataReader`.
- Whether the rows committed by a cancelled bulk copy are a contiguous prefix by `OrderId` (only counts were checked).
- Cancelling during the server-side commit of a batch, connection loss mid-copy, and `BulkCopyTimeout`.
- Code paths behind the remaining warnings: SQL CLR UDT columns, app.config providers and retry logic,
  `DiagnosticListener` subscribers, vector/json types, Always Encrypted.
- ARM64; a cold disk; long-running monitoring (hours) and the memory behaviour that comes with it.

## Reproduce

```powershell
./run.ps1                                  # build, SSPI, containers, every scenario, cleanup (≈17 min)
./run.ps1 -Steps build,sspi                # no containers
./run.ps1 -Steps @() -Start -StateFile <file outside the repo>   # keep containers for iteration
./run.ps1 -Steps bulk-cancel -StateFile <file>
./run.ps1 -Steps @() -Stop -StateFile <file>
```
