using System.Data;
using System.Diagnostics;
using Microsoft.Data.SqlClient;

namespace SqlTool;

internal sealed record BulkVariant(
    int BatchSize,
    SqlBulkCopyOptions Options,
    bool External,
    bool Streaming,
    int NotifyAfter);

internal static class Bulk
{
    public static BulkVariant Variant(string name) => name switch
    {
        "default" => new(0, SqlBulkCopyOptions.Default, false, false, 0),
        "b10k" => new(10_000, SqlBulkCopyOptions.Default, false, false, 100_000),
        "b100k" => new(100_000, SqlBulkCopyOptions.Default, false, false, 100_000),
        "b100k-stream" => new(100_000, SqlBulkCopyOptions.Default, false, true, 100_000),
        "b100k-tablock" => new(100_000, SqlBulkCopyOptions.TableLock, false, false, 100_000),
        "b100k-internaltx" => new(100_000, SqlBulkCopyOptions.UseInternalTransaction, false, false, 100_000),
        "b100k-externaltx" => new(100_000, SqlBulkCopyOptions.Default, true, false, 100_000),
        "default-tablock" => new(0, SqlBulkCopyOptions.TableLock, false, false, 100_000),
        _ => throw new ArgumentException($"unknown bulk variant '{name}'"),
    };

    public static SqlBulkCopy Create(SqlConnection target, BulkVariant variant, SqlTransaction? transaction)
    {
        var bulk = new SqlBulkCopy(target, variant.Options, transaction)
        {
            DestinationTableName = "dbo.Orders",
            BatchSize = variant.BatchSize,
            BulkCopyTimeout = 0,
            EnableStreaming = variant.Streaming,
            NotifyAfter = variant.NotifyAfter,
        };
        foreach (var column in Db.Columns.Split(", "))
        {
            bulk.ColumnMappings.Add(column, column);
        }

        return bulk;
    }

    public static async Task RunAsync(string name)
    {
        var variant = Variant(name);
        await using var source = await Db.OpenAsync(Db.With(Db.A, Db.Source));
        await using var target = await Db.OpenAsync(Db.With(Db.B, Db.Target));
        await Db.ExecAsync(target, "TRUNCATE TABLE dbo.Orders; CHECKPOINT;");
        var payloadBytes = await Db.ScalarAsync<long>(source, "SELECT PayloadBytes FROM dbo.SourceStats");
        var expected = await Db.ScalarAsync<string>(source, Db.Fingerprint);
        Mem.Print("before");

        var progress = new List<(long Rows, double Ms)>();
        var watch = Stopwatch.StartNew();
        await using var command = new SqlCommand($"SELECT {Db.Columns} FROM dbo.Orders ORDER BY OrderId", source)
        {
            CommandTimeout = 0,
        };
        await using var reader = await command.ExecuteReaderAsync(
            variant.Streaming ? CommandBehavior.SequentialAccess : CommandBehavior.Default);
        var transaction = variant.External ? target.BeginTransaction() : null;
        using (var bulk = Create(target, variant, transaction))
        {
            bulk.SqlRowsCopied += (_, e) => progress.Add((e.RowsCopied, watch.Elapsed.TotalMilliseconds));
            await bulk.WriteToServerAsync(reader);
            var copyMs = watch.Elapsed.TotalMilliseconds;
            transaction?.Commit();
            var totalMs = watch.Elapsed.TotalMilliseconds;
            Mem.Print("after-copy");

            await reader.DisposeAsync();
            var actual = await Db.ScalarAsync<string>(target, Db.Fingerprint);
            var rows = bulk.RowsCopied64;
            var gaps = progress.Select((p, i) => i == 0 ? p.Ms : p.Ms - progress[i - 1].Ms).ToList();
            Console.WriteLine(
                $"RESULT bulk variant={name} rows={rows} copyMs={copyMs:F0} totalMs={totalMs:F0} " +
                $"rowsPerSec={rows / (totalMs / 1000):F0} payloadMBps={payloadBytes / 1048576.0 / (totalMs / 1000):F1} " +
                $"progressEvents={progress.Count} maxProgressGapMs={(gaps.Count > 0 ? gaps.Max() : 0):F0} " +
                $"identical={expected == actual}");
            if (expected != actual)
            {
                Console.WriteLine($"MISMATCH source={expected} target={actual}");
            }
        }

        await using var log = new SqlCommand(
            "SELECT CAST(log_space_in_bytes_since_last_backup / 1048576.0 AS decimal(10,1)), " +
            "CAST(used_log_space_in_bytes / 1048576.0 AS decimal(10,1)) FROM sys.dm_db_log_space_usage", target);
        await using var logReader = await log.ExecuteReaderAsync();
        await logReader.ReadAsync();
        Console.WriteLine($"TARGET log usedMB={logReader.GetDecimal(1)}");
    }
}

internal static class BulkCancel
{
    // Mid-batch for the 50 000-row variants: 300 000 rows are in six complete batches, 10 000 more are in flight.
    private const long CancelAtRows = 310_000;

    public static async Task RunAsync(string name)
    {
        // Every variant uses 50 000-row batches unless it says otherwise.
        var (variant, trigger, drain) = name switch
        {
            "none" => (new BulkVariant(50_000, SqlBulkCopyOptions.Default, false, false, 10_000), "rows", false),
            "none-timed" => (new BulkVariant(50_000, SqlBulkCopyOptions.Default, false, false, 10_000), "timed", false),
            "none-drain" => (new BulkVariant(50_000, SqlBulkCopyOptions.Default, false, false, 10_000), "rows", true),
            "internal" => (new BulkVariant(50_000, SqlBulkCopyOptions.UseInternalTransaction, false, false, 10_000),
                "rows", false),
            "external" => (new BulkVariant(50_000, SqlBulkCopyOptions.Default, true, false, 10_000), "rows", false),
            "batch0" => (new BulkVariant(0, SqlBulkCopyOptions.Default, false, false, 10_000), "rows", false),
            "batch0-internal" => (new BulkVariant(0, SqlBulkCopyOptions.UseInternalTransaction, false, false, 10_000),
                "rows", false),
            "batch0-abort" => (new BulkVariant(0, SqlBulkCopyOptions.Default, false, false, 10_000), "abort", false),
            "batch0-external" => (new BulkVariant(0, SqlBulkCopyOptions.Default, true, false, 10_000), "rows", false),
            "batch0-tablock" => (new BulkVariant(0, SqlBulkCopyOptions.TableLock, false, false, 10_000), "rows",
                false),
            "abort" => (new BulkVariant(50_000, SqlBulkCopyOptions.Default, false, false, 10_000), "abort", false),
            _ => throw new ArgumentException($"unknown bulk-cancel variant '{name}'"),
        };

        await using var source = await Db.OpenAsync(Db.With(Db.A, Db.Source, "spar-bulk-source"));
        await using var target = await Db.OpenAsync(Db.With(Db.B, Db.Target, "spar-bulk-target"));
        await using var probeB = await Db.OpenAsync(Db.With(Db.B, Db.Target, "spar-probe"));
        await using var probeA = await Db.OpenAsync(Db.With(Db.A, Db.Source, "spar-probe"));
        await Db.ExecAsync(target, "TRUNCATE TABLE dbo.Orders;");
        var sourceSpid = await Db.SpidAsync(source);
        var targetSpid = await Db.SpidAsync(target);
        var onB = new Probe(probeB);
        var onA = new Probe(probeA);

        using var cancellation = new CancellationTokenSource();
        long cancelAt = 0;
        long rowsAtCancel = 0;
        var triggered = 0;
        long lastNotified = 0;
        var watch = Stopwatch.StartNew();
        await using var command = new SqlCommand($"SELECT {Db.Columns} FROM dbo.Orders ORDER BY OrderId", source)
        {
            CommandTimeout = 0,
        };
        var reader = await command.ExecuteReaderAsync();
        var transaction = variant.External ? target.BeginTransaction() : null;
        var bulk = Bulk.Create(target, variant, transaction);
        bulk.SqlRowsCopied += (_, e) =>
        {
            Volatile.Write(ref lastNotified, e.RowsCopied);
            if (e.RowsCopied < CancelAtRows || Interlocked.Exchange(ref triggered, 1) == 1 || trigger == "timed")
            {
                return;
            }

            rowsAtCancel = e.RowsCopied;
            if (trigger == "abort")
            {
                cancelAt = Stopwatch.GetTimestamp();
                e.Abort = true;
                return;
            }

            // Cancel from another thread, as a UI would.
            _ = Task.Run(() =>
            {
                cancelAt = Stopwatch.GetTimestamp();
                cancellation.Cancel();
            });
        };
        if (trigger == "timed")
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(1500);
                rowsAtCancel = Volatile.Read(ref lastNotified);
                cancelAt = Stopwatch.GetTimestamp();
                cancellation.Cancel();
            });
        }

        string outcome;
        try
        {
            await bulk.WriteToServerAsync(reader, cancellation.Token);
            outcome = "completed";
        }
        catch (Exception exception)
        {
            outcome = Db.Describe(exception);
        }

        var thrownAt = Stopwatch.GetTimestamp();
        var latency = cancelAt == 0 ? double.NaN : Db.Ms(cancelAt, thrownAt);
        Console.WriteLine($"CANCEL variant={name} trigger={trigger} rowsAtCancel={rowsAtCancel} " +
            $"lastNotified={Volatile.Read(ref lastNotified)} " +
            $"rowsCopied64={bulk.RowsCopied64} elapsedMs={watch.Elapsed.TotalMilliseconds:F0} " +
            $"cancelToExceptionMs={latency:F1}");
        Console.WriteLine($"CANCEL outcome={outcome}");
        Console.WriteLine($"CANCEL targetConnection={target.State} transactionConnection=" +
            $"{(transaction is null ? "n/a" : transaction.Connection is null ? "null (zombied)" : "set")}");

        var gone = await onB.WaitRequestGoneAsync(targetSpid, thrownAt, 10_000);
        Console.WriteLine($"SERVER-B target spid request gone after {gone.Ms:F1} ms (last seen: {gone.LastSeen}); " +
            $"now {await onB.StateAsync(targetSpid)}");
        Console.WriteLine($"SERVER-B any bulk request: " +
            $"{await Db.ScalarAsync<int>(probeB, "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE command LIKE '%BULK%'")}");
        Console.WriteLine($"SERVER-A source spid before reader dispose: {await onA.StateAsync(sourceSpid)}");

        if (transaction is not null)
        {
            try
            {
                transaction.Rollback();
                Console.WriteLine("CANCEL external transaction Rollback(): ok");
            }
            catch (Exception exception)
            {
                Console.WriteLine($"CANCEL external transaction Rollback(): {Db.Describe(exception)}");
            }

            Console.WriteLine($"SERVER-B after rollback {await onB.StateAsync(targetSpid)}");
        }

        try
        {
            var one = await Db.ScalarAsync<int>(target, "SELECT 1");
            Console.WriteLine($"CANCEL target connection reusable: SELECT 1 -> {one}");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"CANCEL target connection reusable: no, {Db.Describe(exception)}");
        }

        await Db.ExecAsync(probeB, "SET LOCK_TIMEOUT 5000;");
        try
        {
            var rows = await Db.ScalarAsync<int>(probeB, "SELECT COUNT(*) FROM dbo.Orders");
            Console.WriteLine($"RESULT bulk-cancel variant={name} committedRowsOnB={rows} rowsAtCancel={rowsAtCancel} " +
                $"cancelToExceptionMs={latency:F1} requestGoneMs={gone.Ms:F1}");
        }
        catch (SqlException exception)
        {
            var dirty = await Db.ScalarAsync<int>(probeB, "SELECT COUNT(*) FROM dbo.Orders WITH (NOLOCK)");
            Console.WriteLine($"RESULT bulk-cancel variant={name} committedRowsOnB=blocked({Db.Describe(exception)}) " +
                $"dirtyRows={dirty} rowsAtCancel={rowsAtCancel} cancelToExceptionMs={latency:F1}");
        }

        // The source reader: cancel the command first, or Dispose drains every remaining row.
        var disposeWatch = Stopwatch.StartNew();
        if (!drain)
        {
            command.Cancel();
        }

        await reader.DisposeAsync();
        Console.WriteLine($"SOURCE reader dispose ({(drain ? "no Cancel, drains" : "after SqlCommand.Cancel")}) " +
            $"took {disposeWatch.Elapsed.TotalMilliseconds:F1} ms; source spid now {await onA.StateAsync(sourceSpid)}");
        ((IDisposable)bulk).Dispose();
        await target.DisposeAsync();
        await Task.Delay(200);
        var after = await Db.ScalarAsync<int>(probeB, "SELECT COUNT(*) FROM dbo.Orders");
        Console.WriteLine($"SERVER-B rows after target connection closed: {after}");
    }
}
