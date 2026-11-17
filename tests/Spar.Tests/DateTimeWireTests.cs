using System.Text;
using System.Text.Json;
using Spar.Runtime;

namespace Spar.Tests;

/// <summary>
/// <c>DateTime</c> on the wire (plan section 2, the wire-rules table): ISO 8601 as System.Text.Json writes it, with
/// <c>Z</c>, an offset or nothing by the value's Kind. Tested on the runtime's reader and the writer call the generated
/// serializer makes, because a contract that uses <c>DateTime</c> gets warning SPAR024, which fails this repository's
/// builds.
/// </summary>
public sealed class DateTimeWireTests
{
    [Theory]
    [InlineData(DateTimeKind.Utc, "\"2026-11-16T08:30:00.25Z\"")]
    [InlineData(DateTimeKind.Unspecified, "\"2026-11-16T08:30:00.25\"")]
    public void A_DateTime_is_written_by_its_Kind_and_read_back_with_it(DateTimeKind kind, string json)
    {
        var value = new DateTime(2026, 11, 16, 8, 30, 0, 250, kind);

        var written = Encoding.UTF8.GetString(SparJson.Write(value, (writer, v) => writer.WriteStringValue(v)).Span);
        var read = SparJson.Read<DateTime>(Encoding.UTF8.GetBytes(json), SparJson.ReadDateTime);

        Assert.Equal(json, written);
        Assert.Equal((value, kind), (read, read.Kind));
    }

    [Fact]
    public void A_DateTime_with_an_offset_is_read_as_local_time()
    {
        var read = SparJson.Read<DateTime>("\"2026-11-16T08:30:00+05:00\""u8.ToArray(), SparJson.ReadDateTime);

        Assert.Equal(DateTimeKind.Local, read.Kind);
        Assert.Equal(new DateTime(2026, 11, 16, 3, 30, 0, DateTimeKind.Utc), read.ToUniversalTime());
    }

    [Theory]
    [InlineData("\"2026-11-16 08:30\"")]
    [InlineData("\"16/11/2026\"")]
    [InlineData("\"2026-11-16T08:30:00\\ud800\"")]
    [InlineData("1763281800")]
    public void A_malformed_DateTime_is_a_JsonException(string json)
    {
        Assert.Throws<JsonException>(() => SparJson.Read<DateTime>(Encoding.UTF8.GetBytes(json), SparJson.ReadDateTime));
    }
}
