#if SPAR_LOOPBACK
using System.Diagnostics;
using System.Text.Json;

namespace React.App;

/// <summary>
/// Spike: the development build owns the Vite dev server. One server per project is reused across backend restarts
/// (dotnet watch restarts only the backend). Vite records itself in obj/spar-dev-server.json and exits on its own once
/// the backend named by the dev session has been gone for a while, so nothing outlives the session for long.
/// </summary>
internal static class DevServer
{
    /// <summary>The desktop host is an owner too: Vite stays while this process lives.</summary>
    public static Task<string?> EnsureForDesktopAsync(string projectDirectory)
    {
        var owner = Path.Combine(projectDirectory, "obj", "spar-dev-owner.json");
        File.WriteAllText(owner, $"{{\"pid\":{Environment.ProcessId}}}");
        return EnsureAsync(projectDirectory);
    }

    public static async Task<string?> EnsureAsync(string projectDirectory)
    {
        // An orchestrator (Aspire) that runs Vite itself passes its URL: use it, start nothing.
        if (Environment.GetEnvironmentVariable("SPAR_DEV_SERVER_URL") is { Length: > 0 } assigned)
        {
            Console.WriteLine($"SPAR_DEV_SERVER url={assigned} assigned");
            return assigned;
        }

        var timer = Stopwatch.StartNew();
        var stateFile = Path.Combine(projectDirectory, "obj", "spar-dev-server.json");
        if (await TryReadLiveAsync(stateFile) is { } running)
        {
            Console.WriteLine($"SPAR_DEV_SERVER url={running} reused at={timer.ElapsedMilliseconds}ms");
            return running;
        }

        File.Delete(stateFile);
        var frontend = Path.Combine(projectDirectory, "frontend");
        var log = Path.Combine(projectDirectory, "obj", "spar-dev-server.log");

        // Started through "cmd start" so the server is not our child: dotnet watch kills the backend's process tree
        // on every restart, and the dev server must survive that. /s keeps the quoted command line literal.
        var start = new ProcessStartInfo("cmd.exe", $"/d /s /c \"start \"\" /b node node_modules/vite/bin/vite.js >\"{log}\" 2>&1\"")
        {
            WorkingDirectory = frontend,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        start.Environment["SPAR_DEV_SERVER_OWNED"] = "1";
        using (Process.Start(start))
        {
        }

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
            if (await TryReadLiveAsync(stateFile) is { } url)
            {
                Console.WriteLine($"SPAR_DEV_SERVER url={url} started at={timer.ElapsedMilliseconds}ms");
                return url;
            }
        }

        Console.WriteLine($"SPAR_DEV_SERVER failed; see {log}");
        return null;
    }

    private static async Task<string?> TryReadLiveAsync(string stateFile)
    {
        try
        {
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(stateFile));
            var url = document.RootElement.GetProperty("url").GetString()!;
            using (Process.GetProcessById(document.RootElement.GetProperty("pid").GetInt32()))
            {
            }

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            using var response = await client.GetAsync(url);
            return response.IsSuccessStatusCode ? url : null;
        }
        catch (Exception exception) when (exception is IOException or JsonException or ArgumentException or KeyNotFoundException
            or InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }
}
#endif
