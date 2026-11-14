using System.Data.SqlTypes;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WireProbe;

public enum Severity
{
    Info,
    Warning,
    Error,
}

[Flags]
public enum Access
{
    None = 0,
    Read = 1,
    Write = 2,
}

[JsonSerializable(typeof(decimal))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(DateTimeOffset))]
[JsonSerializable(typeof(DateOnly))]
[JsonSerializable(typeof(TimeOnly))]
[JsonSerializable(typeof(TimeSpan))]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(byte[]))]
[JsonSerializable(typeof(char))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(float))]
[JsonSerializable(typeof(Severity))]
[JsonSerializable(typeof(Access))]
[JsonSerializable(typeof(string))]
internal sealed partial class DefaultContext : JsonSerializerContext;

[JsonSourceGenerationOptions(
    UseStringEnumConverter = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals)]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(Severity))]
[JsonSerializable(typeof(Access))]
internal sealed partial class NamedContext : JsonSerializerContext;

/// <summary>Item 2: the types the SQL tool needs. STJ's default JSON shape, what reads back, and what changes.</summary>
internal static class TypesProbe
{
    public static void Run()
    {
        var file = new ResultFile("types.json");
        Console.WriteLine($"Local time zone: {TimeZoneInfo.Local.Id}, offset now {TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow)}");

        Decimals(file);
        DateTimes(file);
        Others(file);
        Floats(file);
        Enums(file);
        file.Save();
        Console.WriteLine("- wrote results/types.json");
    }

    private static void Decimals(ResultFile file)
    {
        Console.WriteLine("### decimal");
        decimal[] values = [1.50m, 0.1m, -0.0001m, 922337203685477.5807m, 12345678901234567890.123456789m,
            decimal.MaxValue, 0.0000000000000000000000000001m];
        for (var i = 0; i < values.Length; i++)
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(values[i], DefaultContext.Default.Decimal);
            var back = Probe.Try(() => JsonSerializer.Deserialize(json, DefaultContext.Default.Decimal));
            Probe.Line(values[i].ToString(CultureInfo.InvariantCulture), $"{Probe.Show(json)} -> read back {back}");
            file.AddRaw($"decimal{i}", json);
            file.AddString($"decimal{i}Text", values[i].ToString(CultureInfo.InvariantCulture));
        }

        Probe.Line("read number 1e2 as decimal", Probe.Read("1e2", (ref r) => r.GetDecimal()));
        Probe.Line("read number 1.500 as decimal (scale kept?)", Probe.Read("1.500", (ref r) => r.GetDecimal()));
        Probe.Line("read 79228162514264337593543950336 (decimal.MaxValue + 1)",
            Probe.Read("79228162514264337593543950336", (ref r) => r.TryGetDecimal(out var d) ? d.ToString(CultureInfo.InvariantCulture) : "false"));
        Probe.Line("read 0.12345678901234567890123456789 (30 digits)",
            Probe.Read("0.12345678901234567890123456789", (ref r) => r.GetDecimal()));

        // SQL decimal(38, s) is wider than System.Decimal (28-29 significant digits).
        var sql38 = SqlDecimal.Parse("99999999999999999999999999999999999999");
        Probe.Line("SqlDecimal 38 nines .ToString()", sql38.ToString());
        Probe.Line("SqlDecimal 38 nines .Value (as System.Decimal)", Probe.Try(() => sql38.Value));
        var sql38s = SqlDecimal.Parse("1234567890123456789012345678.9012345678");
        Probe.Line("SqlDecimal(38,10) .Value", Probe.Try(() => sql38s.Value));
    }

    private static void DateTimes(ResultFile file)
    {
        Console.WriteLine("### DateTime / DateTimeOffset / DateOnly / TimeOnly / TimeSpan");
        var baseTicks = new DateTime(2026, 11, 14, 10, 20, 30).AddTicks(1234567).Ticks;
        foreach (var kind in new[] { DateTimeKind.Utc, DateTimeKind.Local, DateTimeKind.Unspecified })
        {
            var value = new DateTime(baseTicks, kind);
            var json = JsonSerializer.SerializeToUtf8Bytes(value, DefaultContext.Default.DateTime);
            var back = JsonSerializer.Deserialize(json, DefaultContext.Default.DateTime);
            Probe.Line($"DateTime Kind={kind}",
                $"{Probe.Show(json)} -> Kind={back.Kind}, ticks equal {back.Ticks == value.Ticks}, == {back == value}");
            file.AddRaw($"dateTime{kind}", json);
        }

        var whole = new DateTime(2026, 11, 14, 10, 20, 30, DateTimeKind.Utc);
        Probe.Line("DateTime Utc, whole seconds", Probe.Show(JsonSerializer.SerializeToUtf8Bytes(whole, DefaultContext.Default.DateTime)));
        Probe.Line("read \"2026-11-14T10:20:30+01:00\" as DateTime",
            Probe.Read("\"2026-11-14T10:20:30+01:00\"", (ref r) => r.GetDateTime()));
        Probe.Line("read \"2026-11-14T10:20:30Z\" as DateTime", Probe.Read("\"2026-11-14T10:20:30Z\"", (ref r) => r.GetDateTime()));
        Probe.Line("read \"2026-11-14\" as DateTime", Probe.Read("\"2026-11-14\"", (ref r) => r.GetDateTime()));
        Probe.Line("read \"2026-11-14T10:20:30.123Z\" (JS toISOString) as DateTime",
            Probe.Read("\"2026-11-14T10:20:30.123Z\"", (ref r) => r.GetDateTime()));
        Probe.Line("read \"2026-11-14T10:20:30.12345678Z\" (8 fraction digits)",
            Probe.Read("\"2026-11-14T10:20:30.12345678Z\"", (ref r) => r.GetDateTime()));
        Probe.Line("read \"2026-11-14 10:20:30\" (space)", Probe.Read("\"2026-11-14 10:20:30\"", (ref r) => r.GetDateTime()));

        var offset = new DateTimeOffset(new DateTime(baseTicks), TimeSpan.FromHours(1));
        var offsetZero = new DateTimeOffset(new DateTime(baseTicks), TimeSpan.Zero);
        var offsetJson = JsonSerializer.SerializeToUtf8Bytes(offset, DefaultContext.Default.DateTimeOffset);
        var offsetBack = JsonSerializer.Deserialize(offsetJson, DefaultContext.Default.DateTimeOffset);
        Probe.Line("DateTimeOffset +01:00, 100 ns",
            $"{Probe.Show(offsetJson)} -> offset {offsetBack.Offset}, ticks equal {offsetBack.Ticks == offset.Ticks}");
        Probe.Line("DateTimeOffset +00:00", Probe.Show(JsonSerializer.SerializeToUtf8Bytes(offsetZero, DefaultContext.Default.DateTimeOffset)));
        Probe.Line("read \"2026-11-14T10:20:30Z\" as DateTimeOffset",
            Probe.Read("\"2026-11-14T10:20:30Z\"", (ref r) => r.GetDateTimeOffset()));
        Probe.Line("read \"2026-11-14T10:20:30\" (no offset) as DateTimeOffset",
            Probe.Read("\"2026-11-14T10:20:30\"", (ref r) => r.GetDateTimeOffset()));
        file.AddRaw("dateTimeOffset", offsetJson);

        var date = new DateOnly(2026, 11, 14);
        var dateJson = JsonSerializer.SerializeToUtf8Bytes(date, DefaultContext.Default.DateOnly);
        Probe.Line("DateOnly", Probe.Show(dateJson));
        file.AddRaw("dateOnly", dateJson);
        Probe.Line("read \"2026-11-14T00:00:00\" as DateOnly",
            Probe.Try(() => JsonSerializer.Deserialize("\"2026-11-14T00:00:00\""u8, DefaultContext.Default.DateOnly)));

        TimeOnly[] times = [new TimeOnly(10, 20, 30).Add(TimeSpan.FromTicks(1234567)), new TimeOnly(10, 20), TimeOnly.MaxValue];
        foreach (var time in times)
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(time, DefaultContext.Default.TimeOnly);
            Probe.Line($"TimeOnly {time.Ticks} ticks", Probe.Show(json));
        }

        file.AddRaw("timeOnly", JsonSerializer.SerializeToUtf8Bytes(times[0], DefaultContext.Default.TimeOnly));

        TimeSpan[] spans = [new TimeSpan(1, 2, 3, 4, 500), TimeSpan.FromSeconds(-1), TimeSpan.FromTicks(1), TimeSpan.MaxValue,
            TimeSpan.FromHours(36)];
        foreach (var span in spans)
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(span, DefaultContext.Default.TimeSpan);
            Probe.Line($"TimeSpan {span.Ticks} ticks", Probe.Show(json));
        }

        file.AddRaw("timeSpan", JsonSerializer.SerializeToUtf8Bytes(spans[0], DefaultContext.Default.TimeSpan));
        Probe.Line("read \"PT1H\" (ISO 8601 duration) as TimeSpan",
            Probe.Try(() => JsonSerializer.Deserialize("\"PT1H\""u8, DefaultContext.Default.TimeSpan)));
    }

    private static void Others(ResultFile file)
    {
        Console.WriteLine("### Guid, byte[], char");
        var guid = Guid.Parse("3F2504E0-4F89-11D3-9A0C-0305E82C3301");
        var guidJson = Probe.WriteBytes(w => w.WriteStringValue(guid));
        Probe.Line("Guid", Probe.Show(guidJson));
        file.AddRaw("guid", guidJson);
        foreach (var text in new[] { "3F2504E0-4F89-11D3-9A0C-0305E82C3301", "{3f2504e0-4f89-11d3-9a0c-0305e82c3301}",
                     "3f2504e04f8911d39a0c0305e82c3301", " 3f2504e0-4f89-11d3-9a0c-0305e82c3301" })
        {
            Probe.Line($"TryGetGuid \"{text}\"", Probe.Read($"\"{text}\"", (ref r) => r.TryGetGuid(out var g) ? g.ToString() : "false"));
        }

        byte[] rowversion = [0, 0, 0, 0, 0, 0, 0x07, 0xD1];
        var rowversionJson = Probe.WriteBytes(w => w.WriteBase64StringValue(rowversion));
        Probe.Line("byte[] rowversion 0x00000000000007D1", Probe.Show(rowversionJson));
        file.AddRaw("rowversion", rowversionJson);
        Probe.Line("byte[0]", Probe.Write(w => w.WriteBase64StringValue(ReadOnlySpan<byte>.Empty)));
        foreach (var text in new[] { "AQID", "AQI", "AQI=", "AQ\\u003d\\u003d", "-_8=", "+/8=", "AQ ID", "AQ\\nID" })
        {
            Probe.Line($"TryGetBytesFromBase64 \"{text}\"",
                Probe.Read($"\"{text}\"", (ref r) => r.TryGetBytesFromBase64(out var b) ? "bytes[" + Convert.ToHexString(b) + "]" : "false"));
        }

        foreach (var c in new[] { 'a', '"', '\uD800' })
        {
            Probe.Line($"char U+{(int)c:X4}", Probe.Try(() => Probe.Show(JsonSerializer.SerializeToUtf8Bytes(c, DefaultContext.Default.Char))));
        }

        Probe.Line("read \"ab\" as char", Probe.Try(() => JsonSerializer.Deserialize("\"ab\""u8, DefaultContext.Default.Char)));
    }

    private static void Floats(ResultFile file)
    {
        Console.WriteLine("### double / float");
        double[] doubles = [double.NaN, double.PositiveInfinity, double.NegativeInfinity, -0.0, 0.1, 1e21, double.Epsilon,
            double.MaxValue];
        foreach (var d in doubles)
        {
            var label = d.ToString("R", CultureInfo.InvariantCulture);
            Probe.Line($"double {label} WriteNumberValue", Probe.Write(w => w.WriteNumberValue(d)));
            Probe.Line($"double {label} STJ named literals",
                Probe.Try(() => Probe.Show(JsonSerializer.SerializeToUtf8Bytes(d, NamedContext.Default.Double))));
        }

        Probe.Line("read \"NaN\" with named literals", Probe.Try(() => JsonSerializer.Deserialize("\"NaN\""u8, NamedContext.Default.Double)));
        Probe.Line("read NaN token", Probe.Read("NaN", (ref r) => r.GetDouble()));

        float[] floats = [0.1f, float.MaxValue, 16777217f, 1.0000001f];
        foreach (var f in floats)
        {
            var json = Probe.WriteBytes(w => w.WriteNumberValue(f));
            Probe.Line($"float {f.ToString("R", CultureInfo.InvariantCulture)}",
                $"{Probe.Show(json)}; as double {((double)f).ToString("R", CultureInfo.InvariantCulture)}");
        }

        file.AddRaw("float01", Probe.WriteBytes(w => w.WriteNumberValue(0.1f)));
        Probe.Line("read 3.5e38 as float (above float.MaxValue)", Probe.Read("3.5e38", (ref r) => r.TryGetSingle(out var f) ? f.ToString("R", CultureInfo.InvariantCulture) : "false"));
    }

    private static void Enums(ResultFile file)
    {
        Console.WriteLine("### enums");
        foreach (var severity in new[] { Severity.Warning, (Severity)42 })
        {
            Probe.Line($"Severity {(int)severity} default",
                Probe.Show(JsonSerializer.SerializeToUtf8Bytes(severity, DefaultContext.Default.Severity)));
            Probe.Line($"Severity {(int)severity} string converter",
                Probe.Try(() => Probe.Show(JsonSerializer.SerializeToUtf8Bytes(severity, NamedContext.Default.Severity))));
        }

        foreach (var access in new[] { Access.Read | Access.Write, (Access)8, Access.None })
        {
            Probe.Line($"Access {(int)access} string converter",
                Probe.Try(() => Probe.Show(JsonSerializer.SerializeToUtf8Bytes(access, NamedContext.Default.Access))));
        }

        Probe.Line("read \"warning\" (camelCase)", Probe.Try(() => JsonSerializer.Deserialize("\"warning\""u8, NamedContext.Default.Severity)));
        Probe.Line("read \"Warning\"", Probe.Try(() => JsonSerializer.Deserialize("\"Warning\""u8, NamedContext.Default.Severity)));
        Probe.Line("read 42 with string converter", Probe.Try(() => JsonSerializer.Deserialize("42"u8, NamedContext.Default.Severity)));
        Probe.Line("read \"Bogus\"", Probe.Try(() => JsonSerializer.Deserialize("\"Bogus\""u8, NamedContext.Default.Severity)));
        file.AddRaw("enumName", JsonSerializer.SerializeToUtf8Bytes(Severity.Warning, NamedContext.Default.Severity));
    }
}
