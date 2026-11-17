using System.Diagnostics;
using System.Text.Json;

namespace SandboxRunner;

/// <summary>One call of the <c>wsb</c> command line, with its output and duration.</summary>
internal sealed record WsbResult(int ProcessExitCode, string Output, string Error, TimeSpan Elapsed, bool TimedOut)
{
    /// <summary>The guest command's exit code from <c>wsb exec --raw</c>'s JSON (<c>{"ExitCode": n}</c>), if any.</summary>
    public int? GuestExitCode
    {
        get
        {
            var start = Output.IndexOf('{', StringComparison.Ordinal);
            if (start < 0)
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(Output[start..]);
                return document.RootElement.TryGetProperty("ExitCode", out var code) && code.TryGetInt32(out var value) ? value : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    public string OneLine => $"exit={ProcessExitCode} out={Flatten(Output)} err={Flatten(Error)}";

    private static string Flatten(string text) => text.Replace("\r", "", StringComparison.Ordinal).Replace('\n', ' ').Trim();
}

internal static class Wsb
{
    public static async Task<WsbResult> RunAsync(TimeSpan timeout, params string[] arguments)
    {
        var info = new ProcessStartInfo("wsb")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        var clock = Stopwatch.StartNew();
        using var process = Process.Start(info) ?? throw new InvalidOperationException("wsb did not start");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException)
        {
            // The deadline stops waiting for wsb; it does not prove the guest command stopped (the caller handles that).
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            return new WsbResult(-1, await output, await error, clock.Elapsed, TimedOut: true);
        }

        return new WsbResult(process.ExitCode, await output, await error, clock.Elapsed, TimedOut: false);
    }

    /// <summary>Starts <c>wsb connect</c>, which does not return while the session stays open; the caller kills it later.</summary>
    public static Process StartConnect(string id)
    {
        var info = new ProcessStartInfo("wsb")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        info.ArgumentList.Add("connect");
        info.ArgumentList.Add("--id");
        info.ArgumentList.Add(id);
        info.ArgumentList.Add("--raw");
        return Process.Start(info) ?? throw new InvalidOperationException("wsb connect did not start");
    }

    public static Task<WsbResult> ExecAsync(string id, string runAs, string command, TimeSpan timeout) =>
        RunAsync(timeout, "exec", "--id", id, "-r", runAs, "-c", command, "--raw");

    /// <summary>The ids of running sandboxes, from <c>wsb list --raw</c>.</summary>
    public static async Task<(IReadOnlyList<string> Ids, WsbResult Raw)> ListAsync()
    {
        var result = await RunAsync(TimeSpan.FromSeconds(60), "list", "--raw");
        var ids = new List<string>();
        var start = result.Output.IndexOfAny(['{', '[']);
        if (start >= 0)
        {
            try
            {
                using var document = JsonDocument.Parse(result.Output[start..]);
                Collect(document.RootElement, ids);
            }
            catch (JsonException)
            {
            }
        }

        return (ids, result);
    }

    private static void Collect(JsonElement element, List<string> ids)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String when Guid.TryParse(element.GetString(), out _):
                ids.Add(element.GetString()!);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Collect(item, ids);
                }

                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Collect(property.Value, ids);
                }

                break;
        }
    }
}
