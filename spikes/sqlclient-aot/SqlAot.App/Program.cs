using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

#if SYSTEM_SQLCLIENT
using System.Data.SqlClient;
#else
using Microsoft.Data.SqlClient;
#endif

namespace SqlAot;

// Modes:
//   features     every ADO.NET check, one PASS/FAIL line each; exit code = number of failures
//   first-query  time from process start to the first completed query, then a pooled second query
//   tls          connection outcomes for several Encrypt / TrustServerCertificate combinations
// The connection string comes from SPAR_SQL_CS (without database); it is never printed.
internal static class Program
{
    private static int s_failures;

    private static async Task<int> Main(string[] args)
    {
        var mainAt = SinceProcessStart();
        var mode = args.Length > 0 ? args[0] : "features";
        var baseCs = Environment.GetEnvironmentVariable("SPAR_SQL_CS");
        if (string.IsNullOrEmpty(baseCs))
        {
            Console.Error.WriteLine("SPAR_SQL_CS is not set.");
            return 100;
        }

        switch (mode)
        {
            case "first-query":
                await FirstQueryAsync(baseCs, mainAt);
                return 0;
            case "tls":
                await TlsAsync(baseCs);
                return 0;
            default:
                await FeaturesAsync(baseCs);
                Console.WriteLine($"FAILURES {s_failures}");
                return s_failures;
        }
    }

    private static double SinceProcessStart()
    {
        using var self = Process.GetCurrentProcess();
        return (DateTime.Now - self.StartTime).TotalMilliseconds;
    }

    private static async Task FirstQueryAsync(string baseCs, double mainAt)
    {
        await using (var connection = new SqlConnection(baseCs))
        {
            await connection.OpenAsync();
            await using var command = new SqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync();
        }

        var firstAt = SinceProcessStart();

        var pooled = Stopwatch.StartNew();
        await using (var connection = new SqlConnection(baseCs))
        {
            await connection.OpenAsync();
            await using var command = new SqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync();
        }

        pooled.Stop();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"TIMING main={mainAt:F1} firstQuery={firstAt:F1} pooledOpenQuery={pooled.Elapsed.TotalMilliseconds:F2} " +
            $"dynamicCode={RuntimeFeature.IsDynamicCodeSupported}"));
    }

    private static async Task TlsAsync(string baseCs)
    {
        var variants = new (string Name, Action<SqlConnectionStringBuilder> Apply)[]
        {
            ("default (no Encrypt, no Trust)", static b => { b.Remove("Encrypt"); b.Remove("TrustServerCertificate"); }),
            ("Encrypt=true Trust=false", static b => { b.Encrypt = true; b.TrustServerCertificate = false; }),
            ("Encrypt=true Trust=true", static b => { b.Encrypt = true; b.TrustServerCertificate = true; }),
            ("Encrypt=false Trust=false", static b => { b.Encrypt = false; b.TrustServerCertificate = false; }),
#if !SYSTEM_SQLCLIENT
            ("Encrypt=Strict Trust=true", static b => { b.Encrypt = SqlConnectionEncryptOption.Strict; b.TrustServerCertificate = true; }),
#endif
        };

        foreach (var (name, apply) in variants)
        {
            var builder = new SqlConnectionStringBuilder(baseCs);
            apply(builder);
            try
            {
                await using var connection = new SqlConnection(builder.ConnectionString);
                await connection.OpenAsync();
                await using var command = new SqlCommand(
                    "SELECT encrypt_option FROM sys.dm_exec_connections WHERE session_id = @@SPID", connection);
                var encrypted = await command.ExecuteScalarAsync();
                Console.WriteLine($"TLS [{name}] OK encrypt_option={encrypted}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"TLS [{name}] FAIL {ex.GetType().Name}: {OneLine(ex.Message)}");
            }
        }
    }

    private static async Task FeaturesAsync(string baseCs)
    {
        Console.WriteLine($"INFO dynamicCode={RuntimeFeature.IsDynamicCodeSupported} " +
            $"provider={typeof(SqlConnection).Assembly.GetName().Name} " +
            $"version={typeof(SqlConnection).Assembly.GetName().Version}");

        await CheckAsync("open master", async () =>
        {
            await using var connection = new SqlConnection(baseCs);
            await connection.OpenAsync();
            return $"server {connection.ServerVersion}";
        });

        await CheckAsync("create database", async () =>
        {
            await using var connection = new SqlConnection(baseCs);
            await connection.OpenAsync();
            await using var command = new SqlCommand(
                "IF DB_ID('SparAot') IS NULL CREATE DATABASE SparAot; " +
                "SELECT CAST(SERVERPROPERTY('Collation') AS nvarchar(128))", connection);
            return $"server collation {await command.ExecuteScalarAsync()}";
        });

        var cs = new SqlConnectionStringBuilder(baseCs) { InitialCatalog = "SparAot" }.ConnectionString;
        await using var db = new SqlConnection(cs);
        await db.OpenAsync();

        var table = $"dbo.Items_{Environment.ProcessId}_{Environment.TickCount64}";
        await CheckAsync("create table", async () =>
        {
            await ExecAsync(db, $"""
                CREATE TABLE {table} (
                    Id int IDENTITY PRIMARY KEY,
                    Name nvarchar(100) NOT NULL UNIQUE,
                    Count int NULL,
                    Big bigint NULL,
                    Price decimal(18, 4) NULL,
                    Created datetime2(7) NULL,
                    Body nvarchar(max) NULL,
                    Blob varbinary(max) NULL,
                    Latin varchar(50) COLLATE Latin1_General_CI_AS NULL,
                    Japanese varchar(50) COLLATE Japanese_CI_AS NULL,
                    Tag uniqueidentifier NULL)
                """);
            return table;
        });

        var created = new DateTime(2026, 11, 13, 12, 34, 56, DateTimeKind.Unspecified).AddTicks(1_234_567);
        var body = new string('é', 10_000) + "\U0001F600";
        var blob = new byte[100_000];
        new Random(7).NextBytes(blob);
        var tag = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
        const string Latin = "café Größe €";
        const string Japanese = "日本語";

        await CheckAsync("parameterized insert (values) + output parameter", async () =>
        {
            var id = await InsertAsync(db, null, table, "alpha", 42, 9_007_199_254_740_993L, 12345.6789m, created,
                body, blob, Latin, Japanese, tag);
            return $"id {id}";
        });

        await CheckAsync("parameterized insert (all NULLs)", async () =>
        {
            var id = await InsertAsync(db, null, table, "beta", null, null, null, null, null, null, null, null, null);
            return $"id {id}";
        });

        await CheckAsync("select typed getters round trip", async () =>
        {
            await using var command = new SqlCommand(
                $"SELECT Count, Big, Price, Created, Body, Blob, Latin, Japanese, Tag FROM {table} WHERE Name = @name",
                db);
            command.Parameters.Add("@name", SqlDbType.NVarChar, 100).Value = "alpha";
            await using var reader = await command.ExecuteReaderAsync();
            Require(await reader.ReadAsync(), "no row");
            Require(reader.GetInt32(0) == 42, "int");
            Require(reader.GetInt64(1) == 9_007_199_254_740_993L, "bigint");
            Require(reader.GetDecimal(2) == 12345.6789m, "decimal");
            Require(reader.GetDateTime(3) == created, $"datetime2 {reader.GetDateTime(3):O}");
            Require(await reader.GetFieldValueAsync<string>(4) == body, "nvarchar(max)");
            Require(reader.GetFieldValue<byte[]>(5).AsSpan().SequenceEqual(blob), "varbinary(max)");
            Require(reader.GetString(6) == Latin, $"varchar Latin1 '{reader.GetString(6)}'");
            Require(reader.GetString(7) == Japanese, $"varchar Japanese '{reader.GetString(7)}'");
            Require(reader.GetGuid(8) == tag, "uniqueidentifier");
            return "9 columns equal";
        });

        await CheckAsync("select NULLs (IsDBNull, GetValue boxed)", async () =>
        {
            await using var command = new SqlCommand(
                $"SELECT Count, Big, Price, Created, Body, Blob, Latin, Japanese, Tag FROM {table} WHERE Name = @name",
                db);
            command.Parameters.Add("@name", SqlDbType.NVarChar, 100).Value = "beta";
            await using var reader = await command.ExecuteReaderAsync();
            Require(await reader.ReadAsync(), "no row");
            for (var i = 0; i < reader.FieldCount; i++)
            {
                Require(await reader.IsDBNullAsync(i), $"column {i} not null");
                Require(reader.GetValue(i) is DBNull, $"column {i} GetValue not DBNull");
            }

            return $"{reader.FieldCount} columns NULL";
        });

        await CheckAsync("GetValue boxed types + schema", async () =>
        {
            await using var command = new SqlCommand(
                $"SELECT Count, Big, Price, Created, Body, Blob, Tag FROM {table} WHERE Name = N'alpha'", db);
            await using var reader = await command.ExecuteReaderAsync();
            Require(await reader.ReadAsync(), "no row");
            var types = new StringBuilder();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                types.Append(reader.GetName(i)).Append(':').Append(reader.GetDataTypeName(i)).Append('=')
                    .Append(reader.GetValue(i).GetType().Name).Append(' ');
            }

            return types.ToString().TrimEnd();
        });

        await CheckAsync("transaction rollback", async () =>
        {
            var before = await CountAsync(db, table);
            await using (var transaction = (SqlTransaction)await db.BeginTransactionAsync())
            {
                await InsertAsync(db, transaction, table, "rolled-back", 1, null, null, null, null, null, null, null,
                    null);
                Require(await CountAsync(db, table, transaction) == before + 1, "insert not visible in transaction");
                await transaction.RollbackAsync();
            }

            var after = await CountAsync(db, table);
            Require(after == before, $"count {after} after rollback, expected {before}");
            return $"count {before} -> {before + 1} -> {after}";
        });

        await CheckAsync("transaction commit", async () =>
        {
            var before = await CountAsync(db, table);
            await using (var transaction = db.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                await InsertAsync(db, transaction, table, "committed", 2, null, null, null, null, null, null, null,
                    null);
                await transaction.CommitAsync();
            }

            var after = await CountAsync(db, table);
            Require(after == before + 1, $"count {after}");
            return $"count {before} -> {after}";
        });

        await CheckAsync("unique violation -> SqlException 2627", async () =>
        {
            try
            {
                await InsertAsync(db, null, table, "alpha", null, null, null, null, null, null, null, null, null);
            }
            catch (SqlException ex)
            {
                Require(ex.Number == 2627, $"number {ex.Number}");
                return $"Number={ex.Number} Class={ex.Class} State={ex.State} Errors={ex.Errors.Count} " +
                    $"msg='{OneLine(ex.Message)[..Math.Min(80, ex.Message.Length)]}'";
            }

            throw new InvalidOperationException("no exception");
        });

        await CheckAsync("NOT NULL violation -> SqlException 515", async () =>
        {
            try
            {
                await ExecAsync(db, $"INSERT INTO {table} (Name) VALUES (NULL)");
            }
            catch (SqlException ex)
            {
                Require(ex.Number == 515, $"number {ex.Number}");
                return $"Number={ex.Number}";
            }

            throw new InvalidOperationException("no exception");
        });

        await CheckAsync("connection usable after errors", async () =>
        {
            Require(db.State == ConnectionState.Open, $"state {db.State}");
            return $"count {await CountAsync(db, table)}";
        });

        await CheckAsync("async: pre-cancelled token", async () =>
        {
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();
            try
            {
                await using var command = new SqlCommand("SELECT 1", db);
                await command.ExecuteScalarAsync(cts.Token);
            }
            catch (OperationCanceledException ex)
            {
                return ex.GetType().Name;
            }

            throw new InvalidOperationException("no exception");
        });

        await CheckAsync("async: cancel WAITFOR mid-flight; batch side effect absent", async () =>
        {
            var before = await CountAsync(db, table);
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            var watch = Stopwatch.StartNew();
            string outcome;
            try
            {
                await using var command = new SqlCommand(
                    $"WAITFOR DELAY '00:00:02'; INSERT INTO {table} (Name) VALUES (N'after-cancel')", db);
                await command.ExecuteNonQueryAsync(cts.Token);
                outcome = "completed (not cancelled)";
            }
            catch (Exception ex) when (ex is OperationCanceledException or SqlException)
            {
                outcome = $"{ex.GetType().Name} '{OneLine(ex.Message)}'";
            }

            watch.Stop();
            Require(watch.ElapsedMilliseconds < 1_500, $"took {watch.ElapsedMilliseconds} ms");

            // The batch would insert at ~2 s; wait past that before checking it never ran.
            await Task.Delay(2_500);
            var after = await CountAsync(db, table);
            Require(after == before, $"count {after}, expected {before}: batch kept running");
            return $"{outcome} after {watch.ElapsedMilliseconds} ms; state {db.State}; count unchanged";
        });

        await CheckAsync("CommandTimeout -> SqlException -2", async () =>
        {
            try
            {
                await using var command = new SqlCommand("WAITFOR DELAY '00:00:05'", db) { CommandTimeout = 1 };
                await command.ExecuteNonQueryAsync();
            }
            catch (SqlException ex)
            {
                Require(ex.Number == -2, $"number {ex.Number}");
                return $"Number={ex.Number} state {db.State}";
            }

            throw new InvalidOperationException("no exception");
        });

        await CheckAsync("sync API (ExecuteReader/Read)", () =>
        {
            using var command = new SqlCommand($"SELECT Name FROM {table} ORDER BY Id", db);
            using var reader = command.ExecuteReader();
            var names = new List<string>();
            while (reader.Read())
            {
                names.Add(reader.GetString(0));
            }

            return Task.FromResult(string.Join(",", names));
        });

        await CheckAsync("drop table", async () =>
        {
            await ExecAsync(db, $"DROP TABLE {table}");
            return "dropped";
        });
    }

    private static async Task<int> InsertAsync(SqlConnection db, SqlTransaction? transaction, string table,
        string name, int? count, long? big, decimal? price, DateTime? created, string? body, byte[]? blob,
        string? latin, string? japanese, Guid? tag)
    {
        await using var command = new SqlCommand($"""
            INSERT INTO {table} (Name, Count, Big, Price, Created, Body, Blob, Latin, Japanese, Tag)
            VALUES (@name, @count, @big, @price, @created, @body, @blob, @latin, @japanese, @tag);
            SET @id = CAST(SCOPE_IDENTITY() AS int);
            """, db, transaction);
        var parameters = command.Parameters;
        parameters.Add("@name", SqlDbType.NVarChar, 100).Value = name;
        parameters.Add("@count", SqlDbType.Int).Value = (object?)count ?? DBNull.Value;
        parameters.Add("@big", SqlDbType.BigInt).Value = (object?)big ?? DBNull.Value;
        var priceParameter = parameters.Add("@price", SqlDbType.Decimal);
        priceParameter.Precision = 18;
        priceParameter.Scale = 4;
        priceParameter.Value = (object?)price ?? DBNull.Value;
        parameters.Add("@created", SqlDbType.DateTime2).Value = (object?)created ?? DBNull.Value;
        parameters.Add("@body", SqlDbType.NVarChar, -1).Value = (object?)body ?? DBNull.Value;
        parameters.Add("@blob", SqlDbType.VarBinary, -1).Value = (object?)blob ?? DBNull.Value;
        parameters.Add("@latin", SqlDbType.VarChar, 50).Value = (object?)latin ?? DBNull.Value;
        parameters.Add("@japanese", SqlDbType.NVarChar, 50).Value = (object?)japanese ?? DBNull.Value;
        parameters.Add("@tag", SqlDbType.UniqueIdentifier).Value = (object?)tag ?? DBNull.Value;
        var id = parameters.Add("@id", SqlDbType.Int);
        id.Direction = ParameterDirection.Output;
        await command.ExecuteNonQueryAsync();
        return (int)id.Value;
    }

    private static async Task<int> CountAsync(SqlConnection db, string table, SqlTransaction? transaction = null)
    {
        await using var command = new SqlCommand($"SELECT COUNT(*) FROM {table}", db, transaction);
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private static async Task ExecAsync(SqlConnection db, string sql)
    {
        await using var command = new SqlCommand(sql, db);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CheckAsync(string name, Func<Task<string>> check)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            var detail = await check();
            Console.WriteLine($"PASS [{name}] {detail} ({watch.Elapsed.TotalMilliseconds:F1} ms)");
        }
        catch (Exception ex)
        {
            s_failures++;
            Console.WriteLine($"FAIL [{name}] {ex.GetType().FullName}: {OneLine(ex.Message)}");
            Console.WriteLine($"     at {OneLine(ex.StackTrace ?? "").Split(" at ", 4).Skip(1).FirstOrDefault()}");
        }
    }

    private static void Require(bool condition, string what)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"mismatch: {what}");
        }
    }

    private static string OneLine(string text) => text.ReplaceLineEndings(" ");
}
