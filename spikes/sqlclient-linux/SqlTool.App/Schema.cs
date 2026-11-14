using System.Diagnostics;
using System.Text;
using Microsoft.Data.SqlClient;

namespace SqlTool;

// Two databases with deliberate differences. "right" is the changed side.
internal static class SchemaDdl
{
    public static string Script(bool right) =>
        $"""
        CREATE TYPE dbo.Code FROM varchar({(right ? 12 : 10)}) NOT NULL;
        EXEC('CREATE SCHEMA sales');
        EXEC('
        CREATE TABLE dbo.Customers
        (
            CustomerId int {(right ? "" : "IDENTITY(1, 1)")} NOT NULL CONSTRAINT PK_Customers PRIMARY KEY,
            Name nvarchar(100) COLLATE {(right ? "SQL_Latin1_General_CP1_CI_AS" : "Latin1_General_CI_AS")} NOT NULL,
            Email nvarchar({(right ? 320 : 200)}) NULL,
            Phone varchar(30) {(right ? "NOT NULL" : "NULL")},
            {(right ? "" : "Notes nvarchar(max) NULL,")}
            Age int NULL CONSTRAINT CK_Customers_Age CHECK (Age >= {(right ? 18 : 0)}),
            CustomerCode dbo.Code,
            CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_Customers_CreatedAt DEFAULT ({(right ? "getutcdate()" : "sysutcdatetime()")})
        );
        CREATE TABLE sales.Orders
        (
            OrderId bigint NOT NULL CONSTRAINT PK_Orders PRIMARY KEY,
            CustomerId int NOT NULL,
            OrderDate date NOT NULL,
            Quantity int NOT NULL,
            Price decimal({(right ? "19, 4" : "18, 2")}) NOT NULL,
            Amount AS ({(right ? "Quantity * Price * 1.22" : "Quantity * Price")}),
            Status tinyint NOT NULL CONSTRAINT DF_Orders_Status DEFAULT (0),
            {(right ? "" : "CONSTRAINT FK_Orders_Customers FOREIGN KEY (CustomerId) REFERENCES dbo.Customers (CustomerId),")}
            CONSTRAINT CK_Orders_Quantity CHECK (Quantity > 0)
        );
        CREATE {(right ? "UNIQUE " : "")}INDEX IX_Orders_Customer ON sales.Orders ({(right ? "OrderDate, CustomerId" : "CustomerId, OrderDate")}) INCLUDE (Status);
        {(right ? "" : "CREATE INDEX IX_Orders_Status ON sales.Orders (Status) WHERE Status <> 0;")}
        CREATE INDEX IX_Customers_Email ON dbo.Customers (Email {(right ? "DESC" : "ASC")});
        CREATE TABLE dbo.{(right ? "OnlyRight" : "OnlyLeft")} (Id int NOT NULL PRIMARY KEY NONCLUSTERED, Value sql_variant NULL);
        ');
        """;

    // 300 tables x 16 columns with a primary key and one index each; every 50th table differs on the right.
    public static string GeneratedTables(bool right) =>
        $"""
        DECLARE @i int = 1, @n nvarchar(10), @sql nvarchar(max);
        WHILE @i <= 300
        BEGIN
            SET @n = RIGHT('00' + CAST(@i AS nvarchar(10)), 3);
            SET @sql = N'CREATE TABLE dbo.T' + @n + N' (Id int NOT NULL CONSTRAINT PK_T' + @n + N' PRIMARY KEY,
              C1 nvarchar(50) NULL, C2 int NOT NULL CONSTRAINT DF_T' + @n + N'_C2 DEFAULT (0),
              C3 nvarchar(' + CASE WHEN {(right ? 1 : 0)} = 1 AND @i % 50 = 0 THEN N'60' ELSE N'50' END + N') NULL,
              C4 decimal(18, 2) NULL, C5 datetime2(3) NULL, C6 bit NOT NULL, C7 varbinary(100) NULL,
              C8 bigint NULL, C9 float NULL, C10 uniqueidentifier NULL, C11 date NULL, C12 nvarchar(max) NULL,
              C13 varchar(20) NULL, C14 smallint NULL, C15 tinyint NULL);
              CREATE INDEX IX_T' + @n + N'_C1 ON dbo.T' + @n + N' (C1, C2);';
            EXEC (@sql);
            SET @i += 1;
        END;
        """;
}

internal static class SchemaCompare
{
    private const string CatalogQuery =
        """
        SELECT t.object_id, s.name, t.name
        FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
        WHERE t.is_ms_shipped = 0;

        SELECT c.object_id, c.column_id, c.name, ty.name, bt.name, ty.is_user_defined, c.max_length, c.precision,
               c.scale, c.is_nullable, c.is_identity, c.collation_name, cc.definition, dc.definition
        FROM sys.columns c
        JOIN sys.tables t ON t.object_id = c.object_id
        JOIN sys.types ty ON ty.user_type_id = c.user_type_id
        JOIN sys.types bt ON bt.user_type_id = c.system_type_id
        LEFT JOIN sys.computed_columns cc ON cc.object_id = c.object_id AND cc.column_id = c.column_id
        LEFT JOIN sys.default_constraints dc ON dc.object_id = c.default_object_id
        WHERE t.is_ms_shipped = 0;

        SELECT i.object_id, i.index_id, i.name, i.type_desc, i.is_unique, i.is_primary_key, i.is_unique_constraint,
               i.filter_definition
        FROM sys.indexes i JOIN sys.tables t ON t.object_id = i.object_id
        WHERE t.is_ms_shipped = 0 AND i.type > 0;

        SELECT ic.object_id, ic.index_id, c.name, ic.is_descending_key, ic.is_included_column
        FROM sys.index_columns ic
        JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        JOIN sys.tables t ON t.object_id = ic.object_id
        WHERE t.is_ms_shipped = 0
        ORDER BY ic.object_id, ic.index_id, ic.is_included_column, ic.key_ordinal, c.name;

        SELECT fk.object_id, fk.name, fk.parent_object_id,
               CONCAT(OBJECT_SCHEMA_NAME(fk.referenced_object_id), '.', OBJECT_NAME(fk.referenced_object_id)),
               fk.delete_referential_action_desc, fk.update_referential_action_desc, fk.is_disabled
        FROM sys.foreign_keys fk;

        SELECT fkc.constraint_object_id, pc.name, rc.name
        FROM sys.foreign_key_columns fkc
        JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
        JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
        ORDER BY fkc.constraint_object_id, fkc.constraint_column_id;

        SELECT cc.parent_object_id, cc.name, cc.definition, cc.is_disabled
        FROM sys.check_constraints cc;

        SELECT s.name, t.name, bt.name, t.max_length, t.precision, t.scale, t.is_nullable
        FROM sys.types t
        JOIN sys.schemas s ON s.schema_id = t.schema_id
        JOIN sys.types bt ON bt.user_type_id = t.system_type_id
        WHERE t.is_user_defined = 1;
        """;

    public static async Task<int> RunAsync()
    {
        var timings = new List<double>();
        SortedDictionary<string, string>? left = null;
        SortedDictionary<string, string>? right = null;
        for (var run = 0; run < 5; run++)
        {
            var watch = Stopwatch.StartNew();
            left = await ExtractAsync(Db.With(Db.A, "SparSchemaL"));
            right = await ExtractAsync(Db.With(Db.B, "SparSchemaR"));
            timings.Add(watch.Elapsed.TotalMilliseconds);
        }

        var compare = Stopwatch.StartNew();
        var differences = new List<string>();
        foreach (var (key, value) in left!)
        {
            if (!right!.TryGetValue(key, out var other))
            {
                differences.Add($"only-left  {key}: {value}");
            }
            else if (value != other)
            {
                differences.Add($"changed    {key}: {value}  =>  {other}");
            }
        }

        foreach (var (key, value) in right!)
        {
            if (!left.ContainsKey(key))
            {
                differences.Add($"only-right {key}: {value}");
            }
        }

        compare.Stop();
        foreach (var line in differences)
        {
            Console.WriteLine($"DIFF {line}");
        }

        Console.WriteLine($"RESULT schema objectsLeft={left.Count} objectsRight={right.Count} " +
            $"differences={differences.Count} " +
            $"extractBothMs=[{string.Join(", ", timings.Select(t => t.ToString("F1")))}] " +
            $"compareMs={compare.Elapsed.TotalMilliseconds:F2}");
        return 0;
    }

    private static async Task<SortedDictionary<string, string>> ExtractAsync(string connectionString)
    {
        var model = new SortedDictionary<string, string>(StringComparer.Ordinal);
        await using var connection = await Db.OpenAsync(connectionString);
        await using var command = new SqlCommand(CatalogQuery, connection);
        await using var reader = await command.ExecuteReaderAsync();

        var tables = new Dictionary<int, string>();
        while (await reader.ReadAsync())
        {
            var name = $"{reader.GetString(1)}.{reader.GetString(2)}";
            tables[reader.GetInt32(0)] = name;
            model[$"table  {name}"] = "exists";
        }

        await reader.NextResultAsync();
        while (await reader.ReadAsync())
        {
            var table = tables[reader.GetInt32(0)];
            var typeName = reader.GetString(3);
            var baseType = reader.GetString(4);
            var userDefined = reader.GetBoolean(5);
            var type = userDefined ? typeName : FormatType(baseType, reader.GetInt16(6), reader.GetByte(7),
                reader.GetByte(8));
            var text = new StringBuilder(type);
            text.Append(reader.GetBoolean(9) ? " NULL" : " NOT NULL");
            if (reader.GetBoolean(10))
            {
                text.Append(" IDENTITY");
            }

            if (!reader.IsDBNull(11))
            {
                text.Append(" COLLATE ").Append(reader.GetString(11));
            }

            if (!reader.IsDBNull(12))
            {
                text.Append(" AS ").Append(reader.GetString(12));
            }

            if (!reader.IsDBNull(13))
            {
                text.Append(" DEFAULT ").Append(reader.GetString(13));
            }

            model[$"column {table}.{reader.GetString(2)}"] = text.ToString();
        }

        await reader.NextResultAsync();
        var indexes = new Dictionary<(int, int), (string Key, string Text)>();
        while (await reader.ReadAsync())
        {
            var key = $"index  {tables[reader.GetInt32(0)]}.{(reader.IsDBNull(2) ? "?" : reader.GetString(2))}";
            var text = $"{reader.GetString(3)}{(reader.GetBoolean(4) ? " UNIQUE" : "")}" +
                $"{(reader.GetBoolean(5) ? " PK" : "")}{(reader.GetBoolean(6) ? " UQ" : "")}" +
                $"{(reader.IsDBNull(7) ? "" : $" WHERE {reader.GetString(7)}")}";
            indexes[(reader.GetInt32(0), reader.GetInt32(1))] = (key, text);
        }

        await reader.NextResultAsync();
        var keyColumns = new Dictionary<(int, int), (List<string> Keys, List<string> Included)>();
        while (await reader.ReadAsync())
        {
            var id = (reader.GetInt32(0), reader.GetInt32(1));
            if (!keyColumns.TryGetValue(id, out var lists))
            {
                lists = ([], []);
                keyColumns[id] = lists;
            }

            if (reader.GetBoolean(4))
            {
                lists.Included.Add(reader.GetString(2));
            }
            else
            {
                lists.Keys.Add(reader.GetString(2) + (reader.GetBoolean(3) ? " DESC" : ""));
            }
        }

        foreach (var (id, (key, text)) in indexes)
        {
            var lists = keyColumns.GetValueOrDefault(id, ([], []));
            model[key] = $"{text} ({string.Join(", ", lists.Keys)})" +
                (lists.Included.Count > 0 ? $" INCLUDE ({string.Join(", ", lists.Included)})" : "");
        }

        await reader.NextResultAsync();
        var foreignKeys = new Dictionary<int, (string Key, string Text)>();
        while (await reader.ReadAsync())
        {
            foreignKeys[reader.GetInt32(0)] = ($"fk     {tables[reader.GetInt32(2)]}.{reader.GetString(1)}",
                $"-> {reader.GetString(3)} ON DELETE {reader.GetString(4)} ON UPDATE {reader.GetString(5)}" +
                (reader.GetBoolean(6) ? " DISABLED" : ""));
        }

        await reader.NextResultAsync();
        var foreignKeyColumns = new Dictionary<int, List<string>>();
        while (await reader.ReadAsync())
        {
            var id = reader.GetInt32(0);
            if (!foreignKeyColumns.TryGetValue(id, out var list))
            {
                list = [];
                foreignKeyColumns[id] = list;
            }

            list.Add($"{reader.GetString(1)}={reader.GetString(2)}");
        }

        foreach (var (id, (key, text)) in foreignKeys)
        {
            model[key] = $"{text} ({string.Join(", ", foreignKeyColumns.GetValueOrDefault(id, []))})";
        }

        await reader.NextResultAsync();
        while (await reader.ReadAsync())
        {
            model[$"check  {tables[reader.GetInt32(0)]}.{reader.GetString(1)}"] =
                reader.GetString(2) + (reader.GetBoolean(3) ? " DISABLED" : "");
        }

        await reader.NextResultAsync();
        while (await reader.ReadAsync())
        {
            model[$"type   {reader.GetString(0)}.{reader.GetString(1)}"] =
                FormatType(reader.GetString(2), reader.GetInt16(3), reader.GetByte(4), reader.GetByte(5)) +
                (reader.GetBoolean(6) ? " NULL" : " NOT NULL");
        }

        return model;
    }

    private static string FormatType(string type, short maxLength, byte precision, byte scale) => type switch
    {
        "nvarchar" or "nchar" => $"{type}({(maxLength < 0 ? "max" : (maxLength / 2).ToString())})",
        "varchar" or "char" or "varbinary" or "binary" => $"{type}({(maxLength < 0 ? "max" : maxLength.ToString())})",
        "decimal" or "numeric" => $"{type}({precision}, {scale})",
        "datetime2" or "time" or "datetimeoffset" => $"{type}({scale})",
        _ => type,
    };
}

internal static class GetSchemaChecks
{
    public static async Task<int> RunAsync()
    {
        var failures = 0;
        await using var connection = await Db.OpenAsync(Db.With(Db.A, Db.Source));

        failures += await CheckAsync("SqlDataReader.GetSchemaTable", async () =>
        {
            await using var command = new SqlCommand("SELECT TOP (1) * FROM dbo.Orders", connection);
            await using var reader = await command.ExecuteReaderAsync(System.Data.CommandBehavior.KeyInfo);
            var table = reader.GetSchemaTable();
            var first = table.Rows[0];
            return $"rows={table.Rows.Count} columns={table.Columns.Count} " +
                $"first={first["ColumnName"]}:{((Type)first["DataType"]).Name}:{first["ColumnSize"]} " +
                $"key={first["IsKey"]} " +
                $"notes={table.Rows[11]["ColumnName"]}:{((Type)table.Rows[11]["DataType"]).Name}:" +
                $"{table.Rows[11]["ColumnSize"]}:long={table.Rows[11]["IsLong"]}";
        });

        failures += await CheckAsync("DbDataReader.GetColumnSchema", async () =>
        {
            await using var command = new SqlCommand("SELECT TOP (1) * FROM dbo.Orders", connection);
            await using var reader = await command.ExecuteReaderAsync();
            var columns = reader.GetColumnSchema();
            return $"columns={columns.Count} " +
                string.Join(", ", columns.Take(3).Select(c => $"{c.ColumnName}:{c.DataType?.Name}:{c.DataTypeName}"));
        });

        failures += await CheckAsync("SqlConnection.GetSchema()", () =>
            Task.FromResult($"collections={connection.GetSchema().Rows.Count}"));

        failures += await CheckAsync("SqlConnection.GetSchema(Tables)", () =>
            Task.FromResult($"rows={connection.GetSchema("Tables").Rows.Count}"));

        failures += await CheckAsync("SqlConnection.GetSchema(Columns, Orders)", () =>
        {
            var table = connection.GetSchema("Columns", [null, null, "Orders", null]);
            return Task.FromResult($"rows={table.Rows.Count} first={table.Rows[0]["COLUMN_NAME"]}:" +
                $"{table.Rows[0]["DATA_TYPE"]}");
        });

        failures += await CheckAsync("DataTable.Load(SqlDataReader) 10k rows", async () =>
        {
            await using var command = new SqlCommand($"SELECT TOP (10000) {Db.Columns} FROM dbo.Orders", connection);
            await using var reader = await command.ExecuteReaderAsync();
            var table = new System.Data.DataTable();
            table.Load(reader);
            return $"rows={table.Rows.Count} columns={table.Columns.Count} " +
                $"notesType={table.Columns["Notes"]!.DataType.Name}";
        });

        failures += await CheckAsync("SqlDataReader.GetFieldType/GetProviderSpecificFieldType", async () =>
        {
            await using var command = new SqlCommand("SELECT TOP (1) * FROM dbo.Orders", connection);
            await using var reader = await command.ExecuteReaderAsync();
            return string.Join(", ", Enumerable.Range(0, 5).Select(i =>
                $"{reader.GetFieldType(i).Name}/{reader.GetProviderSpecificFieldType(i).Name}"));
        });

        Console.WriteLine($"RESULT getschema failures={failures}");
        return failures;
    }

    private static async Task<int> CheckAsync(string name, Func<Task<string>> check)
    {
        try
        {
            var watch = Stopwatch.StartNew();
            var detail = await check();
            Console.WriteLine($"PASS {name} ({watch.Elapsed.TotalMilliseconds:F1} ms): {detail}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.WriteLine($"FAIL {name}: {Db.Describe(exception)}");
            Console.WriteLine(exception.StackTrace);
            return 1;
        }
    }
}
