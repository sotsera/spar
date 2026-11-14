using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Data.SqlClient;

namespace SqlTool;

// One scenario per process, so peak memory belongs to that scenario.
// Connection strings come from SPAR_SQL_A / SPAR_SQL_B (no database); they are never printed.
//   setup [rows]            source table on A, destination table on B, schema-compare databases on A and B
//   bulk <variant>          SqlBulkCopy A -> B, streamed from a SqlDataReader
//   bulk-cancel <variant>   cancel a bulk copy mid-way and inspect B
//   query-cancel <case>     cancel a long-running command and inspect the server
//   read <variant>          read the 1M-row table
//   schema                  catalog-view schema comparison (A.SparSchemaL vs B.SparSchemaR)
//   getschema               GetSchemaTable / GetColumnSchema / GetSchema / DataTable.Load
//   monitor <seconds>       DMV polling every second, idle then under load
//   probe <what>            Linux checks: env, transport, tls, auth, globalization, secret (Linux.cs)
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        var mode = args.Length > 0 ? args[0] : "";
        var arg = args.Length > 1 ? args[1] : "";
        var runtime = RuntimeFeature.IsDynamicCodeSupported ? "jit" : "aot";
        Console.WriteLine($"RUN runtime={runtime} mode={mode} arg={arg} " +
            $"sqlclient={typeof(SqlConnection).Assembly.GetName().Version} " +
            $"sqlclientInfo={typeof(SqlConnection).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion} " +
            $"framework={RuntimeInformation.FrameworkDescription}");
        var exit = 0;
        try
        {
            switch (mode)
            {
                case "ping":
                    await using (var a = await Db.OpenAsync(Db.A))
                    await using (var b = await Db.OpenAsync(Db.B))
                    {
                        Console.WriteLine($"PING {await Db.ScalarAsync<int>(a, "SELECT 1")}" +
                            $"{await Db.ScalarAsync<int>(b, "SELECT 1")}");
                    }

                    break;
                case "setup":
                    await Setup.RunAsync(args.Length > 1 ? int.Parse(arg, CultureInfo.InvariantCulture) : 1_000_000);
                    break;
                case "bulk":
                    await Bulk.RunAsync(arg);
                    break;
                case "bulk-cancel":
                    await BulkCancel.RunAsync(arg);
                    break;
                case "query-cancel":
                    await QueryCancel.RunAsync(arg);
                    break;
                case "read":
                    await Read.RunAsync(arg);
                    break;
                case "schema":
                    exit = await SchemaCompare.RunAsync();
                    break;
                case "getschema":
                    exit = await GetSchemaChecks.RunAsync();
                    break;
                case "monitor":
                    await DmvMonitor.RunAsync(args.Length > 1 ? int.Parse(arg, CultureInfo.InvariantCulture) : 30);
                    break;
                case "probe":
                    exit = await LinuxProbe.RunAsync(arg);
                    break;
                default:
                    Console.Error.WriteLine($"unknown mode '{mode}'");
                    return 100;
            }
        }
        catch (Exception exception)
        {
            Console.WriteLine($"FAIL unhandled {Db.Describe(exception)}");
            Console.WriteLine(exception.StackTrace);
            exit = 1;
        }

        Mem.Print("end");
        return exit;
    }
}

internal static class Db
{
    public const string Source = "SparToolSrc";
    public const string Target = "SparToolDst";

    public const string Columns =
        "OrderId, CustomerId, ExternalRef, Quantity, UnitPrice, Discount, Total, Status, IsGift, CustomerName, " +
        "ShipCity, Notes, CreatedAt, ShippedAt, RowHash, Attachment, BigCounter, Score";

    public static string A => Env("SPAR_SQL_A");

    public static string B => Env("SPAR_SQL_B");

    public static string With(string server, string database, string? applicationName = null)
    {
        var builder = new SqlConnectionStringBuilder(server) { InitialCatalog = database };
        if (applicationName is not null)
        {
            builder.ApplicationName = applicationName;
        }

        return builder.ConnectionString;
    }

    public static async Task<SqlConnection> OpenAsync(string connectionString)
    {
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }

    public static async Task<int> ExecAsync(SqlConnection connection, string sql, SqlTransaction? transaction = null)
    {
        await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 0 };
        return await command.ExecuteNonQueryAsync();
    }

    public static async Task<T> ScalarAsync<T>(SqlConnection connection, string sql, SqlTransaction? transaction = null)
    {
        await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 0 };
        return (T)(await command.ExecuteScalarAsync())!;
    }

    public static async Task<short> SpidAsync(SqlConnection connection) =>
        await ScalarAsync<short>(connection, "SELECT @@SPID");

    // Aggregate fingerprint of dbo.Orders, used to prove that a copy is complete and identical.
    public const string Fingerprint =
        "SELECT CONCAT(COUNT_BIG(*), '|', SUM(CAST(BINARY_CHECKSUM(*) AS bigint)), '|', SUM(Total), '|', " +
        "SUM(CAST(DATALENGTH(Attachment) AS bigint)), '|', SUM(CAST(DATALENGTH(Notes) AS bigint)), '|', " +
        "SUM(CAST(DATALENGTH(CustomerName) AS bigint)), '|', CHECKSUM_AGG(CHECKSUM(ExternalRef, CreatedAt, Score))) " +
        "FROM dbo.Orders";

    public static string Describe(Exception exception)
    {
        var text = exception is SqlException sql
            ? $"{exception.GetType().Name}(Number={sql.Number}, Class={sql.Class}): {FirstLine(exception.Message)}"
            : $"{exception.GetType().Name}: {FirstLine(exception.Message)}";
        if (exception.InnerException is { } inner)
        {
            text += $" <- {inner.GetType().Name}: {FirstLine(inner.Message)}";
        }

        return text;
    }

    public static double Ms(long fromTimestamp, long toTimestamp) =>
        Stopwatch.GetElapsedTime(fromTimestamp, toTimestamp).TotalMilliseconds;

    private static string FirstLine(string text)
    {
        var index = text.IndexOfAny(['\r', '\n']);
        return index < 0 ? text : text[..index];
    }

    private static string Env(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"{name} is not set.");
}

internal static class Mem
{
    // Linux: peak working set is VmHWM. There is no peak private-bytes counter, so RssAnon (current anonymous
    // resident memory) is reported instead. Both come from /proc/self/status.
    public static void Print(string label)
    {
        using var process = Process.GetCurrentProcess();
        Console.WriteLine(
            $"MEM {label} peakWorkingSetMB={Status("VmHWM") / 1024.0:F1} " +
            $"workingSetMB={Status("VmRSS") / 1024.0:F1} " +
            $"rssAnonMB={Status("RssAnon") / 1024.0:F1} " +
            $"allocatedMB={GC.GetTotalAllocatedBytes() / 1048576.0:F1} " +
            $"gc0={GC.CollectionCount(0)} gc1={GC.CollectionCount(1)} gc2={GC.CollectionCount(2)} " +
            $"cpuMs={process.TotalProcessorTime.TotalMilliseconds:F0}");
    }

    // Value in kB of one /proc/self/status line, or -1.
    private static long Status(string key)
    {
        foreach (var line in File.ReadLines("/proc/self/status"))
        {
            if (line.StartsWith(key + ":", StringComparison.Ordinal))
            {
                var value = line[(key.Length + 1)..].Trim();
                return long.Parse(value[..value.IndexOf(' ')], CultureInfo.InvariantCulture);
            }
        }

        return -1;
    }
}

// Server-side view of one session, from a separate connection.
internal sealed class Probe(SqlConnection connection)
{
    public async Task<string> StateAsync(short spid)
    {
        await using var command = new SqlCommand(
            """
            SELECT
              (SELECT TOP 1 CONCAT(r.command, '/', r.status, '/', ISNULL(r.wait_type, '-'), '/cpu=', r.cpu_time)
                 FROM sys.dm_exec_requests r WHERE r.session_id = @s),
              (SELECT TOP 1 CONCAT(s.status, '/opentran=', s.open_transaction_count)
                 FROM sys.dm_exec_sessions s WHERE s.session_id = @s),
              (SELECT COUNT(*) FROM sys.dm_tran_locks l
                 WHERE l.request_session_id = @s AND l.resource_type <> 'DATABASE')
            """, connection);
        command.Parameters.Add(new SqlParameter("@s", System.Data.SqlDbType.SmallInt) { Value = spid });
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        var request = reader.IsDBNull(0) ? "none" : reader.GetString(0);
        var session = reader.IsDBNull(1) ? "none" : reader.GetString(1);
        return $"request={request} session={session} locks={reader.GetInt32(2)}";
    }

    // Milliseconds until the session has no row in sys.dm_exec_requests, or -1 after the timeout.
    public async Task<(double Ms, string LastSeen)> WaitRequestGoneAsync(short spid, long sinceTimestamp, int timeoutMs)
    {
        var last = "";
        await using var command = new SqlCommand(
            "SELECT TOP 1 CONCAT(command, '/', status, '/', ISNULL(wait_type, '-')) " +
            "FROM sys.dm_exec_requests WHERE session_id = @s", connection);
        command.Parameters.Add(new SqlParameter("@s", System.Data.SqlDbType.SmallInt) { Value = spid });
        while (true)
        {
            var seen = await command.ExecuteScalarAsync() as string;
            var now = Stopwatch.GetTimestamp();
            if (seen is null)
            {
                return (Db.Ms(sinceTimestamp, now), last);
            }

            last = seen;
            if (Db.Ms(sinceTimestamp, now) > timeoutMs)
            {
                return (-1, last);
            }

            await Task.Delay(5);
        }
    }
}
