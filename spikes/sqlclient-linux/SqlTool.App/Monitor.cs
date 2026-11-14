using System.Diagnostics;
using Microsoft.Data.SqlClient;

namespace SqlTool;

internal static class DmvMonitor
{
    private const string PollBatch =
        """
        SELECT object_name, counter_name, instance_name, cntr_value, cntr_type
        FROM sys.dm_os_performance_counters;

        SELECT r.session_id, r.status, r.command, r.wait_type, r.wait_time, r.cpu_time, r.total_elapsed_time,
               r.logical_reads, r.blocking_session_id, DB_NAME(r.database_id), SUBSTRING(t.text, 1, 400)
        FROM sys.dm_exec_requests r
        OUTER APPLY sys.dm_exec_sql_text(r.sql_handle) t
        WHERE r.session_id <> @@SPID;

        SELECT wait_type, waiting_tasks_count, wait_time_ms, signal_wait_time_ms
        FROM sys.dm_os_wait_stats
        WHERE waiting_tasks_count > 0;
        """;

    public static async Task RunAsync(int seconds)
    {
        await PollAsync("idle-persistent", seconds, openPerPoll: false);
        await PollAsync("idle-open-per-poll", seconds, openPerPoll: true);

        // Load: four CPU-bound queries on A while polling A.
        using var stopLoad = new CancellationTokenSource();
        var load = Enumerable.Range(0, 4).Select(_ => LoadAsync(stopLoad.Token)).ToArray();
        await Task.Delay(1000);
        await PollAsync("load-persistent", seconds, openPerPoll: false);
        await stopLoad.CancelAsync();
        try
        {
            await Task.WhenAll(load);
        }
        catch (Exception exception) when (exception is OperationCanceledException or SqlException)
        {
        }
    }

    private static async Task LoadAsync(CancellationToken token)
    {
        await using var connection = await Db.OpenAsync(Db.With(Db.A, Db.Source, "spar-load"));
        await using var command = new SqlCommand(
            "SELECT COUNT_BIG(*) FROM dbo.Orders a CROSS JOIN dbo.Orders b WHERE a.Total + b.Total = -1 " +
            "OPTION (MAXDOP 1)", connection) { CommandTimeout = 0 };
        await command.ExecuteScalarAsync(token);
    }

    private static async Task PollAsync(string name, int seconds, bool openPerPoll)
    {
        var connectionString = Db.With(Db.A, "master", "spar-monitor");
        SqlConnection? persistent = openPerPoll ? null : await Db.OpenAsync(connectionString);
        var spid = persistent is null ? (short)0 : await Db.SpidAsync(persistent);
        var cpuBefore = persistent is null ? 0 : await SessionCpuAsync(persistent);
        var latencies = new List<double>();
        var allocations = new List<long>();
        int counters = 0, requests = 0, waits = 0;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        for (var poll = 0; poll < seconds; poll++)
        {
            await timer.WaitForNextTickAsync();
            var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var watch = Stopwatch.StartNew();
            var connection = persistent ?? await Db.OpenAsync(connectionString);
            try
            {
                await using var command = new SqlCommand(PollBatch, connection);
                await using var reader = await command.ExecuteReaderAsync();
                counters = 0;
                requests = 0;
                waits = 0;
                while (await reader.ReadAsync())
                {
                    counters += reader.GetString(1).Length > 0 ? 1 : 0;
                    _ = reader.GetInt64(3);
                }

                await reader.NextResultAsync();
                while (await reader.ReadAsync())
                {
                    requests++;
                    _ = reader.IsDBNull(10) ? null : reader.GetString(10);
                }

                await reader.NextResultAsync();
                while (await reader.ReadAsync())
                {
                    waits++;
                    _ = reader.GetInt64(2);
                }
            }
            finally
            {
                if (persistent is null)
                {
                    await connection.DisposeAsync();
                }
            }

            latencies.Add(watch.Elapsed.TotalMilliseconds);
            allocations.Add(GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore);
        }

        var serverCpu = persistent is null ? double.NaN : (await SessionCpuAsync(persistent) - cpuBefore) /
            (double)seconds;
        if (persistent is not null)
        {
            await persistent.DisposeAsync();
        }

        var sorted = latencies.Order().ToList();
        // The first poll includes first-use costs; report it separately.
        Console.WriteLine($"RESULT monitor phase={name} polls={seconds} spid={spid} first={latencies[0]:F2}ms " +
            $"median={sorted[sorted.Count / 2]:F2}ms p95={sorted[(int)(sorted.Count * 0.95)]:F2}ms " +
            $"max={sorted[^1]:F2}ms serverCpuPerPollMs={serverCpu:F2} " +
            $"allocKBPerPollMedian={allocations.Order().ElementAt(allocations.Count / 2) / 1024.0:F1} " +
            $"rows: counters={counters} requests={requests} waits={waits}");
    }

    private static async Task<int> SessionCpuAsync(SqlConnection connection) =>
        await Db.ScalarAsync<int>(connection, "SELECT cpu_time FROM sys.dm_exec_sessions WHERE session_id = @@SPID");
}
