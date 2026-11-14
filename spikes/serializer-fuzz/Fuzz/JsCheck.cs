using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace Fuzz;

/// <summary>
/// Feeds A's outputs through one <c>node</c> process (JSON.parse, as the TypeScript client reads responses) and
/// compares every string and number with what .NET reads from the same bytes.
/// </summary>
internal static class JsCheck
{
    private static readonly BigInteger SafeInteger = BigInteger.Pow(2, 53);

    public static void Run(List<(long Id, byte[] Bytes)> docs, Stats stats)
    {
        if (docs.Count == 0)
        {
            return;
        }

        List<string> results;
        try
        {
            results = Node(docs);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            foreach (var (id, bytes) in docs)
            {
                stats.Add("JS", CaseStream.Value, "SKIPPED", "node unavailable: " + Text.Normalize(ex.Message), id, bytes.Length);
            }

            return;
        }

        for (var i = 0; i < docs.Count; i++)
        {
            var (id, bytes) = docs[i];
            var labels = i < results.Count ? Compare(bytes, results[i]) : ["no result from node"];
            stats.Add("JS", CaseStream.Value, labels.Count == 0 ? "PASS" : "FAIL", labels.Count == 0 ? "-" : string.Join("; ", labels), id, bytes.Length);
        }
    }

    private static List<string> Node(List<(long Id, byte[] Bytes)> docs)
    {
        var psi = new ProcessStartInfo("node")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = new UTF8Encoding(false),
        };
        psi.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "js-check.mjs"));
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("node did not start");
        var errors = process.StandardError.ReadToEndAsync();
        var writer = Task.Run(() =>
        {
            foreach (var (_, bytes) in docs)
            {
                process.StandardInput.Write(Convert.ToBase64String(bytes));
                process.StandardInput.Write('\n');
            }

            process.StandardInput.Close();
        });
        var results = new List<string>(docs.Count);
        while (process.StandardOutput.ReadLine() is { } line)
        {
            results.Add(line);
        }

        writer.GetAwaiter().GetResult();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("node exited with " + process.ExitCode + ": " + errors.GetAwaiter().GetResult());
        }

        return results;
    }

    private static List<string> Compare(byte[] aOutput, string nodeLine)
    {
        var labels = new SortedSet<string>(StringComparer.Ordinal);
        var options = new JsonDocumentOptions { MaxDepth = 2000 };
        using var expected = JsonDocument.Parse(aOutput, options);
        using var actual = JsonDocument.Parse(nodeLine, options);
        if (actual.RootElement.ValueKind == JsonValueKind.String && actual.RootElement.GetString()!.StartsWith("E:", StringComparison.Ordinal))
        {
            return ["JSON.parse threw: " + Text.Normalize(actual.RootElement.GetString()!)];
        }

        Walk(expected.RootElement, actual.RootElement, labels);
        return [.. labels];
    }

    private static void Walk(JsonElement a, JsonElement js, SortedSet<string> labels)
    {
        switch (a.ValueKind)
        {
            case JsonValueKind.Object:
                if (js.ValueKind != JsonValueKind.Object)
                {
                    labels.Add("structure differs");
                    return;
                }

                using (var left = a.EnumerateObject())
                using (var right = js.EnumerateObject())
                {
                    while (true)
                    {
                        var hasLeft = left.MoveNext();
                        if (hasLeft != right.MoveNext())
                        {
                            labels.Add("property count differs");
                            return;
                        }

                        if (!hasLeft)
                        {
                            return;
                        }

                        if (left.Current.Name != right.Current.Name)
                        {
                            labels.Add("property name or order differs");
                        }

                        Walk(left.Current.Value, right.Current.Value, labels);
                    }
                }

            case JsonValueKind.Array:
                if (js.ValueKind != JsonValueKind.Array || js.GetArrayLength() != a.GetArrayLength())
                {
                    labels.Add("array differs");
                    return;
                }

                for (var i = 0; i < a.GetArrayLength(); i++)
                {
                    Walk(a[i], js[i], labels);
                }

                return;
            case JsonValueKind.String:
                if (js.ValueKind != JsonValueKind.String || js.GetString() != "S:" + a.GetString())
                {
                    labels.Add("string differs");
                }

                return;
            case JsonValueKind.Number:
                var tagged = js.ValueKind == JsonValueKind.String ? js.GetString()! : string.Empty;
                if (!tagged.StartsWith("N:", StringComparison.Ordinal))
                {
                    labels.Add("number became non-number");
                    return;
                }

                var jsValue = double.Parse(tagged.AsSpan(2), CultureInfo.InvariantCulture);
                var raw = a.GetRawText();
                if (raw.AsSpan().IndexOfAny(".eE") < 0)
                {
                    var exact = BigInteger.Parse(raw, CultureInfo.InvariantCulture);
                    if (exact != new BigInteger(jsValue))
                    {
                        labels.Add(BigInteger.Abs(exact) > SafeInteger ? "integer above 2^53 rounded by JSON.parse" : "integer differs");
                    }
                }
                else if (BitConverter.DoubleToInt64Bits(double.Parse(raw, CultureInfo.InvariantCulture)) != BitConverter.DoubleToInt64Bits(jsValue))
                {
                    labels.Add("double differs");
                }

                return;
            default:
                if (js.ValueKind != a.ValueKind)
                {
                    labels.Add("literal differs");
                }

                return;
        }
    }
}
