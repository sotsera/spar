using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace Spar.Testing;

/// <summary>One call of Windows Sandbox's command line, <c>wsb</c>: its exit code, its output and how long it took.</summary>
internal sealed record WsbResult(int ExitCode, string Output, string Error, TimeSpan Elapsed, bool TimedOut)
{
    /// <summary>
    /// The guest command's exit code from <c>wsb exec --raw</c>'s JSON (<c>{"ExitCode": n}</c>), or null when the call
    /// failed before the command ran (the output then names the reason).
    /// </summary>
    public int? GuestExitCode => Json(Output) is { ValueKind: JsonValueKind.Object } root
        && root.TryGetProperty("ExitCode", out var code) && code.TryGetInt32(out var value) ? value : null;

    /// <summary>The output and the error on one line, for a message or the timeline.</summary>
    public string OneLine => Flatten(Output + " " + Error);

    /// <summary>The first JSON value in <paramref name="text"/>, or null: <c>wsb</c> prints it with no other text in raw mode.</summary>
    internal static JsonElement? Json(string text)
    {
        var start = text.IndexOfAny(['{', '[']);
        if (start < 0)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text[start..]);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Flatten(string text) =>
        string.Join(' ', text.Split(['\r', '\n', ' '], StringSplitOptions.RemoveEmptyEntries));
}

/// <summary>
/// Windows Sandbox's command line (<c>wsb</c>, Windows 11 24H2 and later): start, list, exec, connect and stop. Every
/// call runs with a deadline; a deadline ends the wait for <c>wsb</c>, never proves the guest command stopped (the
/// runner ends that in the guest). The output is read as the JSON <c>--raw</c> asks for.
/// </summary>
internal static class Wsb
{
    /// <summary>The command, found on <c>PATH</c> (an app execution alias of the Windows Sandbox package).</summary>
    public const string Command = "wsb";

    public static async Task<WsbResult> RunAsync(TimeSpan timeout, CancellationToken cancellationToken, params string[] arguments)
    {
        var clock = Stopwatch.StartNew();
        using var process = new Process { StartInfo = Start(arguments) };
        try
        {
            process.Start();
        }
        catch (Win32Exception exception)
        {
            // Not installed, or the feature is off: no alias on PATH.
            return new WsbResult(-1, "", exception.Message, clock.Elapsed, TimedOut: false);
        }

        var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var error = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException)
        {
            // The deadline ends the wait for wsb itself (a process this runner started); the guest is handled by the caller.
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            return new WsbResult(-1, await output, await error, clock.Elapsed, TimedOut: true);
        }

        return new WsbResult(process.ExitCode, await output, await error, clock.Elapsed, TimedOut: false);
    }

    /// <summary>Starts <c>wsb connect</c>, which may stay while the session is open; the runner ends it at the stop.</summary>
    public static Process StartConnect(string id)
    {
        var process = new Process { StartInfo = Start(["connect", "--id", id, "--raw"]) };
        process.Start();
        _ = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        _ = process.StandardError.ReadToEndAsync(CancellationToken.None);
        return process;
    }

    public static Task<WsbResult> ExecAsync(string id, SandboxLane lane, string command, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var runAs = lane == SandboxLane.System ? "System" : "ExistingLogin";
        return RunAsync(timeout, cancellationToken, "exec", "--id", id, "-r", runAs, "-c", command, "--raw");
    }

    /// <summary>The ids of the running sandboxes (<c>wsb list --raw</c>), or null when <c>wsb</c> could not answer.</summary>
    public static async Task<IReadOnlyList<string>?> ListAsync(CancellationToken cancellationToken)
    {
        var result = await RunAsync(TimeSpan.FromSeconds(60), cancellationToken, "list", "--raw");
        return result.ExitCode == 0 ? ParseList(result.Output) : null;
    }

    /// <summary>
    /// The ids in <c>wsb list --raw</c>'s output, <c>{"WindowsSandboxEnvironments": [{"Id": "…"}]}</c>; null when the
    /// output is not that.
    /// </summary>
    internal static IReadOnlyList<string>? ParseList(string output)
    {
        if (WsbResult.Json(output) is not { ValueKind: JsonValueKind.Object } root
            || !root.TryGetProperty("WindowsSandboxEnvironments", out var environments)
            || environments.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var ids = new List<string>();
        foreach (var environment in environments.EnumerateArray())
        {
            if (environment.ValueKind == JsonValueKind.Object && environment.TryGetProperty("Id", out var id) && id.GetString() is { } text)
            {
                ids.Add(text);
            }
        }

        return ids;
    }

    private static ProcessStartInfo Start(IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(Command)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        return start;
    }
}
