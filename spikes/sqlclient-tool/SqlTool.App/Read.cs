using System.Data;
using System.Diagnostics;
using Microsoft.Data.SqlClient;

namespace SqlTool;

internal static class Read
{
    public static async Task RunAsync(string name)
    {
        var payloadBytes = 0L;
        await using (var stats = await Db.OpenAsync(Db.With(Db.A, Db.Source)))
        {
            payloadBytes = await Db.ScalarAsync<long>(stats, "SELECT PayloadBytes FROM dbo.SourceStats");
        }

        var timings = new List<double>();
        long rows = 0;
        long checksum = 0;
        for (var run = 0; run < 3; run++)
        {
            await using var connection = await Db.OpenAsync(Db.With(Db.A, Db.Source));
            await using var command = new SqlCommand($"SELECT {Db.Columns} FROM dbo.Orders", connection)
            {
                CommandTimeout = 0,
            };
            var allocatedBefore = GC.GetTotalAllocatedBytes();
            var watch = Stopwatch.StartNew();
            (rows, checksum) = name switch
            {
                "typed-async" => await TypedAsync(command, CommandBehavior.Default),
                "typed-sync" => TypedSync(command, CommandBehavior.Default),
                "seq-async" => await TypedAsync(command, CommandBehavior.SequentialAccess),
                "seq-sync" => TypedSync(command, CommandBehavior.SequentialAccess),
                "seq-stream-async" => await StreamAsync(command),
                "values-sync" => ValuesSync(command),
                _ => throw new ArgumentException($"unknown read variant '{name}'"),
            };
            var ms = watch.Elapsed.TotalMilliseconds;
            timings.Add(ms);
            Console.WriteLine($"RUN {run} rows={rows} ms={ms:F0} rowsPerSec={rows / (ms / 1000):F0} " +
                $"allocatedMB={(GC.GetTotalAllocatedBytes() - allocatedBefore) / 1048576.0:F1} checksum={checksum}");
        }

        var sorted = timings.Order().ToList();
        var median = sorted[1];
        Console.WriteLine($"RESULT read variant={name} rows={rows} medianMs={median:F0} " +
            $"rowsPerSec={rows / (median / 1000):F0} payloadMBps={payloadBytes / 1048576.0 / (median / 1000):F1} " +
            $"runsMs=[{string.Join(", ", timings.Select(t => t.ToString("F0")))}] checksum={checksum}");
    }

    private static async Task<(long, long)> TypedAsync(SqlCommand command, CommandBehavior behavior)
    {
        await using var reader = await command.ExecuteReaderAsync(behavior);
        long rows = 0;
        long sum = 0;
        while (await reader.ReadAsync())
        {
            rows++;
            sum += Row(reader);
        }

        return (rows, sum);
    }

    private static (long, long) TypedSync(SqlCommand command, CommandBehavior behavior)
    {
        using var reader = command.ExecuteReader(behavior);
        long rows = 0;
        long sum = 0;
        while (reader.Read())
        {
            rows++;
            sum += Row(reader);
        }

        return (rows, sum);
    }

    // Typed getters in ordinal order, so the same code works with SequentialAccess.
    private static long Row(SqlDataReader r)
    {
        long sum = r.GetInt32(0);
        sum += r.GetInt32(1);
        sum += r.GetGuid(2).GetHashCode() & 0xFF;
        sum += r.GetInt16(3);
        sum += (long)r.GetDecimal(4);
        sum += r.IsDBNull(5) ? 0 : (long)(r.GetDecimal(5) * 100);
        sum += (long)r.GetDecimal(6);
        sum += r.GetByte(7);
        sum += r.GetBoolean(8) ? 1 : 0;
        sum += r.GetString(9).Length;
        sum += r.IsDBNull(10) ? 0 : r.GetString(10).Length;
        sum += r.IsDBNull(11) ? 0 : r.GetString(11).Length;
        sum += r.GetDateTime(12).Ticks % 1000;
        sum += r.IsDBNull(13) ? 0 : r.GetDateTime(13).Millisecond;
        sum += r.IsDBNull(14) ? 0 : r.GetFieldValue<byte[]>(14).Length;
        sum += r.IsDBNull(15) ? 0 : r.GetFieldValue<byte[]>(15).Length;
        sum += r.GetInt64(16) % 1000;
        sum += r.IsDBNull(17) ? 0 : (long)r.GetDouble(17);
        return sum;
    }

    // SequentialAccess with the large columns read as streams, as a copier or exporter would.
    private static async Task<(long, long)> StreamAsync(SqlCommand command)
    {
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
        long rows = 0;
        long sum = 0;
        var chars = new char[4096];
        var bytes = new byte[8192];
        while (await reader.ReadAsync())
        {
            rows++;
            for (var i = 0; i < 11; i++)
            {
                sum += await reader.IsDBNullAsync(i) ? 0 : 1;
            }

            if (!await reader.IsDBNullAsync(11))
            {
                using var text = reader.GetTextReader(11);
                int read;
                while ((read = await text.ReadAsync(chars.AsMemory())) > 0)
                {
                    sum += read;
                }
            }

            sum += reader.GetDateTime(12).Ticks % 1000;
            sum += await reader.IsDBNullAsync(13) ? 0 : 1;
            sum += await reader.IsDBNullAsync(14) ? 0 : reader.GetFieldValue<byte[]>(14).Length;
            if (!await reader.IsDBNullAsync(15))
            {
                await using var stream = reader.GetStream(15);
                int read;
                while ((read = await stream.ReadAsync(bytes.AsMemory())) > 0)
                {
                    sum += read;
                }
            }

            sum += reader.GetInt64(16) % 1000;
        }

        return (rows, sum);
    }

    // Boxed values for every column, the generic "copy any table" shape.
    private static (long, long) ValuesSync(SqlCommand command)
    {
        using var reader = command.ExecuteReader();
        var values = new object[reader.FieldCount];
        long rows = 0;
        long sum = 0;
        while (reader.Read())
        {
            rows++;
            reader.GetValues(values);
            foreach (var value in values)
            {
                sum += value is DBNull ? 0 : 1;
            }
        }

        return (rows, sum);
    }
}
