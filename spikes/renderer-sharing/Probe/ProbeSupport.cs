using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Spar.Testing;
using WindowHost;
using static Spar.Testing.SparAssertions;

namespace RendererSharing;

/// <summary>One WindowHost window on the Window surface, with its phases and its app log.</summary>
internal sealed class ProbeWindow : IAsyncDisposable
{
    private ProbeWindow(SparTestApp app, ConcurrentQueue<string> phases)
    {
        App = app;
        Phases = phases;
    }

    public SparTestApp App { get; }

    public ConcurrentQueue<string> Phases { get; }

    public WindowHost.Activity Activity => App.Services.GetRequiredService<WindowHost.Activity>();

    public string LogFile => Path.Combine(App.Folders.Logs, "app.log");

    public static async Task<ProbeWindow> OpenAsync()
    {
        var phases = new ConcurrentQueue<string>();
        var app = await SparTestApp.StartAsync<Program>(options =>
        {
            options.Surface = SparTestSurface.Window;
            options.WindowPhase = phases.Enqueue;
        }, TestContext.Current.CancellationToken);
        await Expect(app.Page.GetByRole(SparRole.Status)).ToHaveTextAsync("Ready");
        return new ProbeWindow(app, phases);
    }

    public Task GuardAsync(string mode) => App.Page.GetByLabel("Close guard").SelectOptionAsync(mode);

    /// <summary>Whether the page answers an evaluation within <paramref name="limit"/>, and how long it took.</summary>
    public async Task<(bool Answered, double Milliseconds, string Detail)> PingAsync(TimeSpan limit)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var value = await App.Page.EvaluateAsync("1 + 1").WaitAsync(limit, TestContext.Current.CancellationToken);
            return (true, Stopwatch.GetElapsedTime(started).TotalMilliseconds, value.ToString());
        }
        catch (Exception exception) when (exception is TimeoutException or InvalidOperationException or JsonException)
        {
            return (false, Stopwatch.GetElapsedTime(started).TotalMilliseconds, exception.GetType().Name + ": " + exception.Message);
        }
    }

    public string ReadLog()
    {
        if (!File.Exists(LogFile))
        {
            return "";
        }

        using var stream = new FileStream(LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public ValueTask DisposeAsync() => App.DisposeAsync();
}

/// <summary>A browser process of this test process's WebView2 root, from its command line.</summary>
internal sealed record BrowserProcess(int Id, int Parent, string Type, string? RendererClientId, string Extra);

internal static class Os
{
    /// <summary>
    /// Every msedgewebview2 process whose command line names this test process's profile root (<c>…-&lt;pid&gt;\b0</c>),
    /// with its <c>--type</c> and, for a renderer, its <c>--renderer-client-id</c>.
    /// </summary>
    public static async Task<List<BrowserProcess>> BrowserProcessesAsync()
    {
        var start = new ProcessStartInfo("pwsh", ["-NoProfile", "-Command",
            "Get-CimInstance Win32_Process -Filter \"Name='msedgewebview2.exe'\" | Select-Object ProcessId,ParentProcessId,CommandLine | ConvertTo-Json -Compress"])
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(start)!;
        var json = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        var marker = $"-{Environment.ProcessId}\\b";
        var found = new List<BrowserProcess>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return found;
        }

        using var document = JsonDocument.Parse(json);
        var items = document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement.EnumerateArray().ToList() : [document.RootElement];
        foreach (var item in items)
        {
            var line = item.GetProperty("CommandLine").GetString() ?? "";
            if (!line.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var type = Argument(line, "--type=") ?? "browser";
            var extra = string.Join(' ', new[] { "--renderer-process-limit=", "--site-per-process", "--process-per-site", "--disable-features=", "--enable-features=" }
                .Select(name => Argument(line, name) is { } value ? name + value : null)
                .Where(text => text is not null));
            found.Add(new BrowserProcess(
                item.GetProperty("ProcessId").GetInt32(),
                item.GetProperty("ParentProcessId").GetInt32(),
                type,
                Argument(line, "--renderer-client-id="),
                extra));
        }

        return found;
    }

    private static string? Argument(string line, string name)
    {
        var index = line.IndexOf(name, StringComparison.Ordinal);
        if (index < 0)
        {
            return null;
        }

        var start = index + name.Length;
        var end = line.IndexOf(' ', start);
        var value = end < 0 ? line[start..] : line[start..end];
        return value.Trim('"');
    }
}

internal static class Evidence
{
    private static readonly Lock Gate = new();

    public static string Folder
    {
        get
        {
            var spike = new DirectoryInfo(AppContext.BaseDirectory);
            while (spike.Name != "renderer-sharing")
            {
                spike = spike.Parent!;
            }

            var folder = Path.Combine(spike.FullName, "results");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    public static void Append(string file, string text)
    {
        lock (Gate)
        {
            File.AppendAllText(Path.Combine(Folder, file), $"{DateTime.UtcNow:O} {text}{Environment.NewLine}");
        }
    }
}
