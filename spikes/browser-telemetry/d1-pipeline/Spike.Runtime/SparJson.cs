using System.Buffers;
using System.Text.Json;

namespace Spike;

/// <summary>Reads one JSON value at the reader's current token.</summary>
public delegate T JsonValueReader<out T>(ref Utf8JsonReader reader);

/// <summary>Shared helpers called by generated serializers.</summary>
public static class SparJson
{
    public static T[] ReadArray<T>(ref Utf8JsonReader reader, JsonValueReader<T> readElement)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException($"Expected an array but found {reader.TokenType}.");
        }

        var items = new List<T>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            items.Add(readElement(ref reader));
        }

        return [.. items];
    }

    public static string ReadString(ref Utf8JsonReader reader) =>
        reader.TokenType == JsonTokenType.String
            ? reader.GetString()!
            : throw new JsonException($"Expected a string but found {reader.TokenType}.");

    public static byte[] Write<T>(T value, Action<Utf8JsonWriter, T> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            write(writer, value);
        }

        return buffer.WrittenSpan.ToArray();
    }

    public static Utf8JsonReader Begin(ReadOnlySpan<byte> payload)
    {
        var reader = new Utf8JsonReader(payload);
        if (!reader.Read())
        {
            throw new JsonException("The payload is empty.");
        }

        return reader;
    }
}
