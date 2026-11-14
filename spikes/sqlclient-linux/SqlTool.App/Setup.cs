using System.Diagnostics;

namespace SqlTool;

internal static class Setup
{
    public const string OrdersTable =
        """
        CREATE TABLE dbo.Orders
        (
            OrderId      int              NOT NULL CONSTRAINT PK_Orders PRIMARY KEY CLUSTERED,
            CustomerId   int              NOT NULL,
            ExternalRef  uniqueidentifier NOT NULL,
            Quantity     smallint         NOT NULL,
            UnitPrice    decimal(19, 4)   NOT NULL,
            Discount     decimal(5, 4)    NULL,
            Total        decimal(19, 4)   NOT NULL,
            Status       tinyint          NOT NULL,
            IsGift       bit              NOT NULL,
            CustomerName nvarchar(100)    NOT NULL,
            ShipCity     nvarchar(60)     NULL,
            Notes        nvarchar(max)    NULL,
            CreatedAt    datetime2(7)     NOT NULL,
            ShippedAt    datetime2(3)     NULL,
            RowHash      varbinary(32)    NULL,
            Attachment   varbinary(max)   NULL,
            BigCounter   bigint           NOT NULL,
            Score        float            NULL
        );
        """;

    public static async Task RunAsync(int rows)
    {
        var watch = Stopwatch.StartNew();
        await using (var a = await Db.OpenAsync(Db.With(Db.A, "master")))
        {
            await RecreateDatabaseAsync(a, Db.Source);
            await RecreateDatabaseAsync(a, "SparSchemaL");
            Console.WriteLine($"SERVER A {await ServerInfoAsync(a)}");
        }

        await using (var b = await Db.OpenAsync(Db.With(Db.B, "master")))
        {
            await RecreateDatabaseAsync(b, Db.Target);
            await RecreateDatabaseAsync(b, "SparSchemaR");
            Console.WriteLine($"SERVER B {await ServerInfoAsync(b)}");
        }

        await using (var source = await Db.OpenAsync(Db.With(Db.A, Db.Source)))
        {
            await Db.ExecAsync(source, OrdersTable);
            var generate = Stopwatch.StartNew();
            await Db.ExecAsync(source, Generate(rows));
            Console.WriteLine($"SETUP generated {rows} rows on A in {generate.Elapsed.TotalSeconds:F1} s");
            await Db.ExecAsync(source,
                """
                CREATE TABLE dbo.SourceStats (Rows bigint NOT NULL, PayloadBytes bigint NOT NULL);
                INSERT dbo.SourceStats
                SELECT COUNT_BIG(*),
                  SUM(4 + 4 + 16 + 2 + 9 + ISNULL(5 + 0 * Discount, 0) + 9 + 1 + 1
                    + CAST(DATALENGTH(CustomerName) AS bigint) + ISNULL(DATALENGTH(ShipCity), 0)
                    + ISNULL(CAST(DATALENGTH(Notes) AS bigint), 0) + 8 + ISNULL(7 + 0 * DATEPART(year, ShippedAt), 0)
                    + ISNULL(DATALENGTH(RowHash), 0) + ISNULL(CAST(DATALENGTH(Attachment) AS bigint), 0) + 8
                    + ISNULL(8 + 0 * Score, 0))
                FROM dbo.Orders;
                """);
            await using var command = new Microsoft.Data.SqlClient.SqlCommand(
                "SELECT Rows, PayloadBytes FROM dbo.SourceStats; EXEC sp_spaceused 'dbo.Orders';", source);
            await using var reader = await command.ExecuteReaderAsync();
            await reader.ReadAsync();
            Console.WriteLine($"SOURCE rows={reader.GetInt64(0)} payloadMB={reader.GetInt64(1) / 1048576.0:F1}");
            await reader.NextResultAsync();
            await reader.ReadAsync();
            Console.WriteLine($"SOURCE sp_spaceused reserved={reader.GetString(2)} data={reader.GetString(3)} " +
                $"index={reader.GetString(4)}");
            Console.WriteLine($"SOURCE fingerprint {await FingerprintAfterAsync(reader, source)}");
        }

        await using (var target = await Db.OpenAsync(Db.With(Db.B, Db.Target)))
        {
            await Db.ExecAsync(target, OrdersTable);
        }

        await using (var left = await Db.OpenAsync(Db.With(Db.A, "SparSchemaL")))
        {
            await Db.ExecAsync(left, SchemaDdl.Script(right: false));
            await Db.ExecAsync(left, SchemaDdl.GeneratedTables(right: false));
        }

        await using (var right = await Db.OpenAsync(Db.With(Db.B, "SparSchemaR")))
        {
            await Db.ExecAsync(right, SchemaDdl.Script(right: true));
            await Db.ExecAsync(right, SchemaDdl.GeneratedTables(right: true));
        }

        Console.WriteLine($"SETUP done in {watch.Elapsed.TotalSeconds:F1} s");
    }

    private static async Task<string> FingerprintAfterAsync(
        Microsoft.Data.SqlClient.SqlDataReader reader, Microsoft.Data.SqlClient.SqlConnection connection)
    {
        await reader.DisposeAsync();
        return await Db.ScalarAsync<string>(connection, Db.Fingerprint);
    }

    private static async Task RecreateDatabaseAsync(Microsoft.Data.SqlClient.SqlConnection master, string name)
    {
        await Db.ExecAsync(master,
            $"""
            IF DB_ID('{name}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{name}];
            END;
            CREATE DATABASE [{name}];
            ALTER DATABASE [{name}] SET RECOVERY SIMPLE;
            ALTER DATABASE [{name}] MODIFY FILE (NAME = '{name}', SIZE = 512MB, FILEGROWTH = 256MB);
            ALTER DATABASE [{name}] MODIFY FILE (NAME = '{name}_log', SIZE = 1024MB, FILEGROWTH = 512MB);
            """);
    }

    private static async Task<string> ServerInfoAsync(Microsoft.Data.SqlClient.SqlConnection connection) =>
        await Db.ScalarAsync<string>(connection,
            """
            SELECT CONCAT('version=', CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(40)),
              ' edition=', CAST(SERVERPROPERTY('Edition') AS nvarchar(80)),
              ' cpus=', (SELECT cpu_count FROM sys.dm_os_sys_info),
              ' committedTargetMB=', (SELECT committed_target_kb / 1024 FROM sys.dm_os_sys_info),
              ' collation=', CAST(SERVERPROPERTY('Collation') AS nvarchar(80)))
            """);

    // Deterministic rows: ints, decimals, non-ASCII nvarchar, datetime2(7) with 100 ns ticks, varbinary, NULLs,
    // some nvarchar(max) of 4 320 bytes and some varbinary(max) of 16 KiB.
    private static string Generate(int rows) =>
        $"""
        WITH n AS
        (
            SELECT TOP ({rows}) CAST(ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS int) AS i
            FROM sys.all_columns a CROSS JOIN sys.all_columns b CROSS JOIN sys.all_columns c
        )
        INSERT INTO dbo.Orders WITH (TABLOCK) ({Db.Columns})
        SELECT
            i,
            1 + i % 50000,
            CAST(HASHBYTES('MD5', CAST(i AS varchar(12))) AS uniqueidentifier),
            CAST(1 + i % 20 AS smallint),
            CAST(1 + (i % 100000) / 100.0 AS decimal(19, 4)),
            CASE WHEN i % 5 = 0 THEN NULL ELSE CAST((i % 30) / 100.0 AS decimal(5, 4)) END,
            CAST((1 + i % 20) * (1 + (i % 100000) / 100.0) AS decimal(19, 4)),
            CAST(i % 6 AS tinyint),
            CAST(CASE WHEN i % 11 = 0 THEN 1 ELSE 0 END AS bit),
            CONCAT(N'Customer ', 1 + i % 50000, N' ', NCHAR(0x00E0 + i % 20), N'ñ €'),
            CASE WHEN i % 4 = 0 THEN NULL
                 ELSE CHOOSE(1 + i % 5, N'Milano', N'Torino', N'København', N'Zürich', N'東京') END,
            CASE WHEN i % 1000 = 0 THEN REPLICATE(CAST(N'Lorem ipsum dolor sit amet ' AS nvarchar(max)), 80)
                 WHEN i % 3 = 0 THEN NULL
                 ELSE CONCAT(N'Note ', i) END,
            DATEADD(NANOSECOND, (i % 9999999) * 100, DATEADD(SECOND, i, CAST('2020-01-01T00:00:00' AS datetime2(7)))),
            CASE WHEN i % 6 = 0 THEN NULL
                 ELSE DATEADD(MILLISECOND, i % 86400000, CAST('2021-01-01' AS datetime2(3))) END,
            CASE WHEN i % 9 = 0 THEN NULL ELSE HASHBYTES('SHA2_256', CAST(i AS varchar(12))) END,
            CASE WHEN i % 500 = 0 THEN CAST(REPLICATE(CAST(CAST(0x0102030405060708 AS varchar(8)) AS varchar(max)), 2048)
                 AS varbinary(max)) END,
            CAST(CAST(i AS bigint) * 2654435761 AS bigint),
            CASE WHEN i % 7 = 0 THEN NULL ELSE SQRT(CAST(i AS float)) END
        FROM n
        OPTION (MAXDOP 4);
        """;
}
