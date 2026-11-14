using System.Diagnostics;
using Microsoft.Data.SqlClient;

namespace SqlTool;

internal static class QueryCancel
{
    private const string CpuQuery =
        "SELECT COUNT_BIG(*) FROM dbo.Orders a CROSS JOIN dbo.Orders b WHERE a.Total + b.Total = -1";

    public static async Task RunAsync(string name)
    {
        switch (name)
        {
            case "waitfor-async":
                await CommandCaseAsync(name, "WAITFOR DELAY '00:01:00'", 1000, sync: false);
                break;
            case "waitfor-sync":
                await CommandCaseAsync(name, "WAITFOR DELAY '00:01:00'", 1000, sync: true);
                break;
            case "cpu-async":
                await CommandCaseAsync(name, CpuQuery + " OPTION (MAXDOP 1)", 2000, sync: false);
                break;
            case "cpu-parallel-async":
                await CommandCaseAsync(name, CpuQuery + " OPTION (MAXDOP 4)", 2000, sync: false);
                break;
            case "blocked-async":
                await BlockedCaseAsync(name);
                break;
            case "stream-async":
                await StreamCaseAsync(name, cancelCommand: false);
                break;
            case "dispose-drain":
                await DisposeCaseAsync(name, cancelFirst: false);
                break;
            case "dispose-cancel":
                await DisposeCaseAsync(name, cancelFirst: true);
                break;
            default:
                throw new ArgumentException($"unknown query-cancel case '{name}'");
        }
    }

    private static async Task CommandCaseAsync(string name, string sql, int cancelAfterMs, bool sync)
    {
        await using var connection = await Db.OpenAsync(Db.With(Db.A, Db.Source, "spar-cancel"));
        await using var probeConnection = await Db.OpenAsync(Db.With(Db.A, Db.Source, "spar-probe"));
        var probe = new Probe(probeConnection);
        var spid = await Db.SpidAsync(connection);
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 0 };
        using var cancellation = new CancellationTokenSource();
        long cancelAt = 0;
        string serverBefore = "";
        _ = Task.Run(async () =>
        {
            await Task.Delay(cancelAfterMs);
            serverBefore = await probe.StateAsync(spid);
            cancelAt = Stopwatch.GetTimestamp();
            if (sync)
            {
                command.Cancel();
            }
            else
            {
                cancellation.Cancel();
            }
        });

        string outcome;
        try
        {
            if (sync)
            {
                await Task.Run(() => command.ExecuteNonQuery());
            }
            else
            {
                await command.ExecuteNonQueryAsync(cancellation.Token);
            }

            outcome = "completed";
        }
        catch (Exception exception)
        {
            outcome = Db.Describe(exception);
        }

        await ReportAsync(name, connection, probe, spid, cancelAt, Stopwatch.GetTimestamp(), outcome, serverBefore);
    }

    // A SELECT waiting on a row lock held by another open transaction.
    private static async Task BlockedCaseAsync(string name)
    {
        await using var blocker = await Db.OpenAsync(Db.With(Db.A, Db.Source, "spar-blocker"));
        await using var blockerTransaction = blocker.BeginTransaction();
        await Db.ExecAsync(blocker, "UPDATE dbo.Orders SET Status = Status WHERE OrderId = 42", blockerTransaction);
        try
        {
            await CommandCaseAsync(name, "SELECT * FROM dbo.Orders WHERE OrderId = 42", 1000, sync: false);
        }
        finally
        {
            blockerTransaction.Rollback();
        }
    }

    // Cancel while streaming a large result with ReadAsync(token).
    private static async Task StreamCaseAsync(string name, bool cancelCommand)
    {
        await using var connection = await Db.OpenAsync(Db.With(Db.A, Db.Source, "spar-cancel"));
        await using var probeConnection = await Db.OpenAsync(Db.With(Db.A, Db.Source, "spar-probe"));
        var probe = new Probe(probeConnection);
        var spid = await Db.SpidAsync(connection);
        await using var command = new SqlCommand($"SELECT {Db.Columns} FROM dbo.Orders", connection);
        using var cancellation = new CancellationTokenSource();
        long cancelAt = 0;
        long rows = 0;
        long rowsAtCancel = 0;
        string outcome;
        var reader = await command.ExecuteReaderAsync(cancellation.Token);
        var serverBefore = "";
        try
        {
            while (await reader.ReadAsync(cancellation.Token))
            {
                rows++;
                if (rows == 100_000)
                {
                    serverBefore = await probe.StateAsync(spid);
                    rowsAtCancel = rows;
                    cancelAt = Stopwatch.GetTimestamp();
                    if (cancelCommand)
                    {
                        command.Cancel();
                    }
                    else
                    {
                        cancellation.Cancel();
                    }
                }
            }

            outcome = "completed";
        }
        catch (Exception exception)
        {
            outcome = Db.Describe(exception);
        }

        var thrownAt = Stopwatch.GetTimestamp();
        Console.WriteLine($"STREAM rowsAtCancel={rowsAtCancel} rowsReadTotal={rows} " +
            $"rowsAfterCancel={rows - rowsAtCancel}");
        Console.WriteLine($"STREAM server after the exception, before reader dispose " +
            $"({Db.Ms(cancelAt, Stopwatch.GetTimestamp()):F1} ms after cancel): {await probe.StateAsync(spid)}");
        await Task.Delay(500);
        Console.WriteLine($"STREAM server 500 ms later, reader still open: {await probe.StateAsync(spid)}");
        var dispose = Stopwatch.StartNew();
        await reader.DisposeAsync();
        Console.WriteLine($"STREAM reader dispose after cancel took {dispose.Elapsed.TotalMilliseconds:F1} ms");
        await ReportAsync(name, connection, probe, spid, cancelAt, thrownAt, outcome, serverBefore);
    }

    // Stopping early: Dispose without cancelling drains the rest of the result set.
    private static async Task DisposeCaseAsync(string name, bool cancelFirst)
    {
        await using var connection = await Db.OpenAsync(Db.With(Db.A, Db.Source, "spar-cancel"));
        await using var command = new SqlCommand($"SELECT {Db.Columns} FROM dbo.Orders", connection);
        var reader = await command.ExecuteReaderAsync();
        for (var i = 0; i < 1000 && await reader.ReadAsync(); i++)
        {
        }

        var watch = Stopwatch.StartNew();
        if (cancelFirst)
        {
            command.Cancel();
        }

        await reader.DisposeAsync();
        var disposeMs = watch.Elapsed.TotalMilliseconds;
        var one = await Db.ScalarAsync<int>(connection, "SELECT 1");
        Console.WriteLine($"RESULT query-cancel case={name} readerDisposeMs={disposeMs:F1} connectionReusable={one == 1}");
    }

    private static async Task ReportAsync(
        string name, SqlConnection connection, Probe probe, short spid, long cancelAt, long thrownAt, string outcome,
        string serverBefore)
    {
        var latency = cancelAt == 0 ? double.NaN : Db.Ms(cancelAt, thrownAt);
        var gone = await probe.WaitRequestGoneAsync(spid, cancelAt == 0 ? thrownAt : cancelAt, 10_000);
        var state = connection.State;
        string reusable;
        try
        {
            var watch = Stopwatch.StartNew();
            var one = await Db.ScalarAsync<int>(connection, "SELECT 1");
            reusable = $"yes ({watch.Elapsed.TotalMilliseconds:F1} ms, SELECT 1 -> {one})";
        }
        catch (Exception exception)
        {
            reusable = $"no, {Db.Describe(exception)}";
        }

        Console.WriteLine($"SERVER before cancel: {serverBefore}");
        Console.WriteLine($"SERVER after: request gone {gone.Ms:F1} ms after cancel (last seen: {gone.LastSeen}); " +
            $"now {await probe.StateAsync(spid)}");
        Console.WriteLine($"OUTCOME {outcome}");
        Console.WriteLine($"RESULT query-cancel case={name} cancelToExceptionMs={latency:F1} " +
            $"requestGoneAfterCancelMs={gone.Ms:F1} connectionState={state} reusable={reusable}");
    }
}
