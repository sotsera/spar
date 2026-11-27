// Spike (M3d): what a second window costs on WebView2 with an app's own profile (one environment, the default profile,
// not InPrivate), how many renderers 1, 2, 4 and 8 windows on https://app.localhost get, what ending one window's
// renderer does to the others, and whether zoom is per window. Every window is shown offscreen without activation, so
// nothing reaches the monitor.
//   dotnet run --project spikes/windows/Windows -c Release -- <run>
// Writes spikes/windows/results/windows-<run>.json and prints it.
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace WindowsSpike;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        var run = args.Length > 0 ? args[0] : DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var probe = new Probe(FindRoot(), run);
        var context = new ApplicationContext();
        var timer = new System.Windows.Forms.Timer { Interval = 1 };
        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            Environment.ExitCode = await probe.RunAsync();
            context.ExitThread();
        };
        timer.Start();
        Application.Run(context);
    }

    private static string FindRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "global.json")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }
}

internal sealed class ProbeWindow : Form
{
    private readonly Stopwatch _clock;

    public ProbeWindow(string name, int index, Stopwatch clock)
    {
        Name = name;
        _clock = clock;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(-8000 + index * 40, -8000 + index * 40);
        ClientSize = new Size(800, 600);
        ShowInTaskbar = false;
        Text = $"windows spike {name}";
        Controls.Add(Web);
    }

    public new string Name { get; }
    public WebView2 Web { get; } = new() { Dock = DockStyle.Fill };
    public TaskCompletionSource<double> Frame { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public JsonArray Failures { get; } = [];
    public JsonArray ZoomChanges { get; } = [];

    protected override bool ShowWithoutActivation => true;

    public void Attach(CoreWebView2 core)
    {
        core.WebMessageReceived += (_, e) =>
        {
            var message = JsonNode.Parse(e.TryGetWebMessageAsString())!;
            if ((string?)message["type"] == "frame")
            {
                Frame.TrySetResult(_clock.Elapsed.TotalMilliseconds);
            }
        };
        core.ProcessFailed += (_, e) => Failures.Add(new JsonObject
        {
            ["at"] = _clock.Elapsed.TotalMilliseconds,
            ["kind"] = e.ProcessFailedKind.ToString(),
            ["reason"] = e.Reason.ToString(),
        });
        Web.ZoomFactorChanged += (_, _) => ZoomChanges.Add(Web.ZoomFactor);
    }

    public void ExpectFrame() => Frame = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed class Probe(string root, string run)
{
    private const int Windows = 8;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<ProbeWindow> _windows = [];
    private readonly JsonObject _results = [];
    private CoreWebView2Environment _environment = null!;
    private string _page = "";

    public async Task<int> RunAsync()
    {
        var code = 1;
        try
        {
            await ProbeAsync();
            code = 0;
        }
        catch (Exception error)
        {
            _results["error"] = error.ToString();
        }

        foreach (var window in _windows)
        {
            window.Dispose();
        }

        var folder = Path.Combine(root, "spikes", "windows", "results");
        Directory.CreateDirectory(folder);
        var text = _results.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(folder, $"windows-{run}.json"), text);
        Console.WriteLine(text);
        return code;
    }

    private async Task ProbeAsync()
    {
        _page = Path.Combine(AppContext.BaseDirectory, "page");
        var profile = Path.Combine(root, "artifacts", "webview2-profiles", $"windows-spike-{run}");
        if (Directory.Exists(profile))
        {
            Directory.Delete(profile, recursive: true);
        }

        var started = _clock.Elapsed.TotalMilliseconds;
        _environment = await CoreWebView2Environment.CreateAsync(null, profile);
        _results["runtime"] = _environment.BrowserVersionString;
        _results["environmentMs"] = _clock.Elapsed.TotalMilliseconds - started;

        var opened = new JsonArray();
        _results["opened"] = opened;
        var renderers = new JsonObject();
        _results["renderers"] = renderers;
        var memory = new JsonArray();
        _results["memory"] = memory;
        // No browser process runs before the first controller, so there is no zero-window sample.
        for (var i = 0; i < Windows; i++)
        {
            opened.Add(await OpenAsync(i.ToString()));
            await Task.Delay(500);
            memory.Add(new JsonObject { ["windows"] = i + 1, ["bytes"] = await PrivateBytesAsync() });
            if (i + 1 is 1 or 2 or 4 or 8)
            {
                renderers[(i + 1).ToString()] = await RenderersAsync();
            }
        }

        _results["zoom"] = await ZoomAsync();
        _results["kill"] = await KillAsync(3);
        _results["afterKill"] = await RenderersAsync();
    }

    /// <summary>Opens one window: the form, the controller on the shared environment, then the page's first frame.</summary>
    private async Task<JsonObject> OpenAsync(string name)
    {
        var start = _clock.Elapsed.TotalMilliseconds;
        var window = new ProbeWindow(name, _windows.Count, _clock);
        _windows.Add(window);
        window.Show();
        var shown = _clock.Elapsed.TotalMilliseconds;

        await window.Web.EnsureCoreWebView2Async(_environment);
        var controller = _clock.Elapsed.TotalMilliseconds;
        var core = window.Web.CoreWebView2;
        window.Attach(core);
        core.SetVirtualHostNameToFolderMapping("app.localhost", _page, CoreWebView2HostResourceAccessKind.Deny);
        core.Navigate($"https://app.localhost/index.html?w={name}");
        var frame = await window.Frame.Task.WaitAsync(TimeSpan.FromSeconds(30));

        return new JsonObject
        {
            ["window"] = name,
            ["showMs"] = shown - start,
            ["controllerMs"] = controller - start,
            ["frameMs"] = frame - start,
            ["browserPid"] = core.BrowserProcessId,
        };
    }

    /// <summary>The renderers and the windows whose main frame each one holds.</summary>
    private async Task<JsonArray> RenderersAsync()
    {
        var list = new JsonArray();
        foreach (var info in await _environment.GetProcessExtendedInfosAsync())
        {
            if (info.ProcessInfo.Kind != CoreWebView2ProcessKind.Renderer)
            {
                continue;
            }

            var frames = new JsonArray();
            foreach (var frame in info.AssociatedFrameInfos)
            {
                frames.Add($"{frame.FrameKind} {frame.Source}");
            }

            list.Add(new JsonObject { ["pid"] = info.ProcessInfo.ProcessId, ["frames"] = frames });
        }

        return list;
    }

    private async Task<JsonArray> PrivateBytesAsync()
    {
        var list = new JsonArray();
        long total = 0;
        foreach (var info in await _environment.GetProcessExtendedInfosAsync())
        {
            try
            {
                using var process = Process.GetProcessById(info.ProcessInfo.ProcessId);
                total += process.PrivateMemorySize64;
                list.Add($"{info.ProcessInfo.Kind} {info.ProcessInfo.ProcessId} {process.PrivateMemorySize64}");
            }
            catch (ArgumentException)
            {
            }
        }

        list.Insert(0, $"total {total}");
        return list;
    }

    /// <summary>Zoom set on one window: the others, a reload, a new window, and Ctrl+wheel through the DevTools protocol.</summary>
    private async Task<JsonObject> ZoomAsync()
    {
        var result = new JsonObject();
        var one = _windows[1];
        result["defaultEnabled"] = one.Web.CoreWebView2.Settings.IsZoomControlEnabled;
        one.Web.ZoomFactor = 1.5;
        await Task.Delay(500);
        result["afterSet"] = Factors();

        one.ExpectFrame();
        one.Web.CoreWebView2.Reload();
        await one.Frame.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await Task.Delay(300);
        result["afterReload"] = one.Web.ZoomFactor;

        var extra = await OpenAsync("zoom-new");
        await Task.Delay(300);
        result["newWindow"] = _windows[^1].Web.ZoomFactor;
        result["newWindowOpen"] = extra;

        var two = _windows[2];
        var wheel = """{"type":"mouseWheel","x":200,"y":200,"deltaX":0,"deltaY":-120,"modifiers":2}""";
        await two.Web.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", wheel);
        await Task.Delay(700);
        result["afterCtrlWheel"] = two.Web.ZoomFactor;
        result["changes"] = new JsonObject(_windows.Select(w => KeyValuePair.Create(w.Name, (JsonNode?)w.ZoomChanges.DeepClone())));
        return result;
    }

    private JsonObject Factors() => new(_windows.Select(w => KeyValuePair.Create(w.Name, (JsonNode?)w.Web.ZoomFactor)));

    /// <summary>Ends window <paramref name="target"/>'s renderer, then asks every window's page to answer.</summary>
    private async Task<JsonObject> KillAsync(int target)
    {
        var result = new JsonObject();
        var source = $"w={target}";
        var victim = (await _environment.GetProcessExtendedInfosAsync()).FirstOrDefault(info =>
            info.ProcessInfo.Kind == CoreWebView2ProcessKind.Renderer
            && info.AssociatedFrameInfos.Any(frame => frame.Source.EndsWith(source, StringComparison.Ordinal)));
        if (victim is null)
        {
            result["error"] = "no renderer found for the window";
            return result;
        }

        var pid = victim.ProcessInfo.ProcessId;
        result["pid"] = pid;
        result["framesInIt"] = new JsonArray(victim.AssociatedFrameInfos.Select(frame => (JsonNode?)frame.Source).ToArray());
        var killedAt = _clock.Elapsed.TotalMilliseconds;
        using (var process = Process.GetProcessById(pid))
        {
            process.Kill();
        }

        await Task.Delay(2000);
        var answers = new JsonObject();
        foreach (var window in _windows)
        {
            var asked = _clock.Elapsed.TotalMilliseconds;
            var answer = window.Web.CoreWebView2.ExecuteScriptAsync("document.getElementById('name').textContent");
            var done = await Task.WhenAny(answer, Task.Delay(3000));
            answers[window.Name] = new JsonObject
            {
                ["answer"] = done == answer ? answer.Result : "no answer in 3 s",
                ["ms"] = _clock.Elapsed.TotalMilliseconds - asked,
                ["failures"] = new JsonArray(window.Failures.Select(f => f!.DeepClone()).Select(f =>
                {
                    f["at"] = (double)f["at"]! - killedAt;
                    return f;
                }).ToArray()),
            };
        }

        result["windows"] = answers;
        return result;
    }
}
