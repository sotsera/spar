// What SqlClient hands the backend for wire-relevant SQL types. Read-only SELECTs against the local default instance
// over shared memory with integrated security; nothing is created or changed on the server.
// Usage (from spikes/wire-details): dotnet run --project SqlProbe -- [data source, default "."]

using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;

var dataSource = args.Length > 0 ? args[0] : ".";
var connectionString = new SqlConnectionStringBuilder
{
    DataSource = dataSource,
    IntegratedSecurity = true,
    Pooling = false,
    ConnectTimeout = 5,
    TrustServerCertificate = true,
    ApplicationName = "spar-wire-details",
}.ConnectionString;

using var connection = new SqlConnection(connectionString);
connection.Open();
Console.WriteLine($"Server {connection.ServerVersion}");

static string Units(string s)
{
    var builder = new StringBuilder();
    foreach (var c in s)
    {
        builder.Append(CultureInfo.InvariantCulture, $"{(int)c:X4} ");
    }

    return builder.ToString().TrimEnd();
}

void Query(string label, string sql, Action<SqlDataReader> show, Action<SqlCommand>? parameters = null)
{
    try
    {
        using var command = new SqlCommand(sql, connection) { CommandTimeout = 10 };
        parameters?.Invoke(command);
        using var reader = command.ExecuteReader();
        reader.Read();
        show(reader);
    }
    catch (Exception e)
    {
        Console.WriteLine($"- {label}: THROWS {e.GetType().Name}: {e.Message.ReplaceLineEndings(" ")}");
    }
}

string Try(Func<object?> f)
{
    try
    {
        var v = f();
        return v switch
        {
            null => "null",
            IFormattable x => $"{x.ToString(null, CultureInfo.InvariantCulture)} ({v.GetType().Name})",
            byte[] b => $"0x{Convert.ToHexString(b)} (byte[])",
            _ => $"{v} ({v.GetType().Name})",
        };
    }
    catch (Exception e)
    {
        return $"THROWS {e.GetType().Name}: {e.Message.ReplaceLineEndings(" ")}";
    }
}

Console.WriteLine("## nvarchar with lone surrogates");
Query("NCHAR(0xD800)", "SELECT N'a' + NCHAR(0xD800) + N'b' AS s, CAST(N'a' + NCHAR(0xD800) + N'b' AS varbinary(16)) AS b, " +
    "CONVERT(varchar(8000), SERVERPROPERTY('Collation')) AS c", r =>
    Console.WriteLine($"- SELECT N'a' + NCHAR(0xD800) + N'b': units [{Units(r.GetString(0))}], stored bytes 0x{Convert.ToHexString((byte[])r[1])}, " +
                      $"server collation {r.GetString(2)}"));
Query("parameter round trip", "SELECT @p AS s, DATALENGTH(@p) AS n, CAST(@p AS varbinary(16)) AS b", r =>
    Console.WriteLine($"- SELECT @p with @p = \"a\\uDC00b\" (nvarchar): units [{Units(r.GetString(0))}], DATALENGTH {r.GetInt32(1)}, " +
                      $"bytes 0x{Convert.ToHexString((byte[])r[2])}"),
    c => c.Parameters.Add(new SqlParameter("@p", System.Data.SqlDbType.NVarChar, 10) { Value = "a\uDC00b" }));

Console.WriteLine("## decimal and money");
Query("decimal(38,0)", "SELECT CAST(99999999999999999999999999999999999999 AS decimal(38,0))", r =>
{
    Console.WriteLine($"- decimal(38,0) 38 nines: GetDecimal {Try(() => r.GetDecimal(0))}");
    Console.WriteLine($"  GetSqlDecimal {Try(() => r.GetSqlDecimal(0).ToString())}; GetValue {Try(() => r.GetValue(0))}");
});
Query("decimal(10,4)", "SELECT CAST(1.5 AS decimal(10,4)), CAST(922337203685477.5807 AS money), CAST(-214748.3648 AS smallmoney)", r =>
    Console.WriteLine($"- decimal(10,4) 1.5: {Try(() => r.GetDecimal(0))}; money max: {Try(() => r.GetDecimal(1))}; " +
                      $"smallmoney min: {Try(() => r.GetDecimal(2))}"));

Console.WriteLine("## dates and times");
Query("dates", "SELECT CAST('2026-11-14T10:20:30.1234567' AS datetime2(7)), CAST('2026-11-14T10:20:30.997' AS datetime), " +
               "CAST('2026-11-14T10:20:30.1234567+01:00' AS datetimeoffset(7)), CAST('2026-11-14' AS date), " +
               "CAST('10:20:30.1234567' AS time(7)), SYSUTCDATETIME()", r =>
{
    Console.WriteLine($"- datetime2(7): {Try(() => r.GetDateTime(0).ToString("O", CultureInfo.InvariantCulture))}, Kind {r.GetDateTime(0).Kind}");
    Console.WriteLine($"- datetime: {Try(() => r.GetDateTime(1).ToString("O", CultureInfo.InvariantCulture))}");
    Console.WriteLine($"- datetimeoffset(7): {Try(() => r.GetDateTimeOffset(2).ToString("O", CultureInfo.InvariantCulture))}");
    Console.WriteLine($"- date: GetValue {Try(() => r.GetValue(3))}; GetFieldValue<DateOnly> {Try(() => r.GetFieldValue<DateOnly>(3))}");
    Console.WriteLine($"- time(7): GetValue {Try(() => r.GetValue(4))}; GetFieldValue<TimeOnly> {Try(() => r.GetFieldValue<TimeOnly>(4).ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture))}");
    Console.WriteLine($"- SYSUTCDATETIME(): Kind {r.GetDateTime(5).Kind}");
});

Console.WriteLine("## uniqueidentifier, rowversion, float");
Query("guid", "SELECT CAST('3F2504E0-4F89-11D3-9A0C-0305E82C3301' AS uniqueidentifier) AS g, " +
              "CAST(CAST('3F2504E0-4F89-11D3-9A0C-0305E82C3301' AS uniqueidentifier) AS varchar(36)) AS t, @@DBTS AS dbts", r =>
    Console.WriteLine($"- uniqueidentifier: GetGuid {r.GetGuid(0)}; CAST AS varchar {r.GetString(1)}; @@DBTS {Try(() => r.GetValue(2))}"));
Query("float overflow", "SELECT CAST(1e308 AS float) * 10", r => Console.WriteLine($"- {r.GetDouble(0)}"));
Query("float NaN parameter", "SELECT @f", r => Console.WriteLine($"- SELECT @f with NaN: {r.GetDouble(0)}"),
    c => c.Parameters.Add(new SqlParameter("@f", System.Data.SqlDbType.Float) { Value = double.NaN }));

Console.WriteLine("## bigint values a monitor reads");
Query("dm_os_sys_info", "SELECT cpu_ticks, ms_ticks, CAST(DATEDIFF(SECOND, sqlserver_start_time, SYSDATETIME()) AS bigint) " +
                        "FROM sys.dm_os_sys_info", r =>
{
    var cpuTicks = r.GetInt64(0);
    Console.WriteLine($"- sys.dm_os_sys_info.cpu_ticks = {cpuTicks} ({(cpuTicks > 9007199254740991 ? "ABOVE" : "below")} 2^53-1; " +
                      $"{(double)cpuTicks / 9007199254740991:P2} of it); ms_ticks = {r.GetInt64(1)}; uptime {r.GetInt64(2)} s");
});
Query("query_hash", "SELECT TOP (1) CAST(query_hash AS bigint) FROM sys.dm_exec_query_stats WHERE query_hash IS NOT NULL " +
                    "ORDER BY ABS(CAST(query_hash AS bigint)) DESC", r =>
    Console.WriteLine($"- largest CAST(query_hash AS bigint) in sys.dm_exec_query_stats: {r.GetInt64(0)}"));
