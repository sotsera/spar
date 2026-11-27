// Spike (M3c): how files dropped on a WebView2 page reach the host with their paths. The page's drop handler posts
// the DataTransfer's files with chrome.webview.postMessageWithAdditionalObjects; the host reads the paths from
// AdditionalObjects. Drops are made through the in-process DevTools protocol (Input.dispatchDragEvent with files), so
// nothing reaches the monitor: the window is shown offscreen without activation.
//   dotnet run --project spikes/file-drop/Windows
// Writes spikes/file-drop/results/windows-<time>.json and prints it.
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace FileDrop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        using var form = new ProbeForm(FindRoot());
        Application.Run(form);
        Environment.ExitCode = form.ExitCode;
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

internal sealed class Scenario(string name)
{
    public string Name { get; } = name;
    public JsonArray Messages { get; } = [];
    public JsonArray Navigations { get; } = [];
    public JsonArray NewWindows { get; } = [];
    public TaskCompletionSource<JsonNode?> Report { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public double DropDispatchedAt { get; set; }
    public double? FilesReceivedAt { get; set; }
}

internal sealed class ProbeForm : Form
{
    private const double HandledX = 150;
    private const double UnhandledX = 550;
    private const double AreaY = 250;

    private readonly string _root;
    private readonly string _fixtures;
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly JsonObject _results = [];
    private Scenario? _current;

    public ProbeForm(string root)
    {
        _root = root;
        _fixtures = Path.Combine(root, "artifacts", "file-drop-spike", "fixtures");
        StartPosition = FormStartPosition.Manual;
        Location = new Point(-6000, -6000);
        ClientSize = new Size(800, 500);
        Text = "file-drop spike";
        Controls.Add(_web);
    }

    public int ExitCode { get; private set; } = 1;

    protected override bool ShowWithoutActivation => true;

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        try
        {
            await RunAsync();
            ExitCode = 0;
        }
        catch (Exception error)
        {
            _results["error"] = error.ToString();
        }

        WriteResults();
        Close();
    }

    private async Task RunAsync()
    {
        MakeFixtures();

        var profile = Path.Combine(_root, "artifacts", "webview2-profiles", "file-drop-spike");
        var environment = await CoreWebView2Environment.CreateAsync(null, profile);
        _results["runtime"] = environment.BrowserVersionString;
        await _web.EnsureCoreWebView2Async(environment);

        var core = _web.CoreWebView2;
        var page = Path.Combine(AppContext.BaseDirectory, "page");
        core.SetVirtualHostNameToFolderMapping("app.localhost", page, CoreWebView2HostResourceAccessKind.Deny);
        core.WebMessageReceived += OnMessage;
        core.NavigationStarting += OnNavigationStarting;
        core.NewWindowRequested += OnNewWindowRequested;
        core.Navigate("https://app.localhost/index.html");
        await _ready.Task.WaitAsync(TimeSpan.FromSeconds(20));

        var scenarios = new JsonArray();
        _results["scenarios"] = scenarios;
        var a = Fixture("a.txt");

        scenarios.Add(await DropAsync("one file, handled area", HandledX, [a]));
        scenarios.Add(await DropAsync("unicode name", HandledX, [Fixture("ünïcödé 文件.txt")]));
        scenarios.Add(await DropAsync("a file and a folder", HandledX, [a, Fixture("folder")]));
        scenarios.Add(await DropAsync("a folder alone", HandledX, [Fixture("folder")]));
        scenarios.Add(await DropAsync("a 50 MB file", HandledX, [Fixture("big.bin")]));

        var many = Directory.GetFiles(Fixture("many")).Order(StringComparer.Ordinal).ToArray();
        scenarios.Add(await DropAsync("1,000 files", HandledX, many, waitMs: 15000));

        scenarios.Add(await DropAsync("one file, area without a handler", UnhandledX, [a], waitMs: 2500));
        scenarios.Add(await ForgeAsync());

        var timings = new JsonArray();
        for (var i = 0; i < 11; i++)
        {
            var timed = await DropAsync("timed", HandledX, [a]);
            if (i > 0)
            {
                timings.Add(timed["dropToFilesMs"]?.DeepClone());
            }
        }

        _results["oneFileDropToHostMs"] = timings;

        _web.AllowExternalDrop = false;
        scenarios.Add(await DropAsync("AllowExternalDrop false, handled area", HandledX, [a], waitMs: 2500));
        _web.AllowExternalDrop = true;
    }

    private async Task<JsonObject> DropAsync(string name, double x, string[] paths, int waitMs = 5000)
    {
        var scenario = new Scenario(name);
        _current = scenario;

        var result = new JsonObject { ["name"] = name, ["paths"] = paths.Length };
        try
        {
            await DragAsync("dragEnter", x, paths);
            await DragAsync("dragOver", x, paths);
            scenario.DropDispatchedAt = _clock.Elapsed.TotalMilliseconds;
            await DragAsync("drop", x, paths);
            result["dropReturnedMs"] = Round(_clock.Elapsed.TotalMilliseconds - scenario.DropDispatchedAt);
        }
        catch (Exception error)
        {
            result["cdpError"] = error.Message;
        }

        var finished = await Task.WhenAny(scenario.Report.Task, Task.Delay(waitMs));
        result["pageReport"] = finished == scenario.Report.Task ? scenario.Report.Task.Result?.DeepClone() : null;
        if (scenario.FilesReceivedAt is { } received)
        {
            result["dropToFilesMs"] = Round(received - scenario.DropDispatchedAt);
        }

        result["hostMessages"] = scenario.Messages.DeepClone();
        result["navigations"] = scenario.Navigations.DeepClone();
        result["newWindows"] = scenario.NewWindows.DeepClone();
        _current = null;
        return result;
    }

    private async Task<JsonObject> ForgeAsync()
    {
        var scenario = new Scenario("a File made by the page");
        _current = scenario;
        _web.CoreWebView2.PostWebMessageAsString("forge");

        var finished = await Task.WhenAny(scenario.Report.Task, Task.Delay(3000));
        await Task.Delay(300);
        var result = new JsonObject
        {
            ["name"] = scenario.Name,
            ["pageReport"] = finished == scenario.Report.Task ? scenario.Report.Task.Result?.DeepClone() : null,
            ["hostMessages"] = scenario.Messages.DeepClone(),
        };
        _current = null;
        return result;
    }

    private Task<string> DragAsync(string type, double x, string[] paths)
    {
        var files = new JsonArray();
        foreach (var path in paths)
        {
            files.Add(path);
        }

        var parameters = new JsonObject
        {
            ["type"] = type,
            ["x"] = x,
            ["y"] = AreaY,
            ["data"] = new JsonObject { ["items"] = new JsonArray(), ["files"] = files, ["dragOperationsMask"] = 1 },
        };
        return _web.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchDragEvent", parameters.ToJsonString());
    }

    private void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var at = _clock.Elapsed.TotalMilliseconds;
        string? text;
        try
        {
            text = e.TryGetWebMessageAsString();
        }
        catch (ArgumentException)
        {
            text = null;
        }

        if (text is not null && text.StartsWith('{'))
        {
            var report = JsonNode.Parse(text);
            if (report?["kind"]?.GetValue<string>() == "ready")
            {
                _ready.TrySetResult();
                return;
            }

            _current?.Report.TrySetResult(report);
            return;
        }

        if (_current is null)
        {
            return;
        }

        if (text == "files")
        {
            _current.FilesReceivedAt ??= at;
        }

        _current.Messages.Add(DescribeMessage(text, e.AdditionalObjects, at));
    }

    private JsonObject DescribeMessage(string? text, System.Collections.IEnumerable? objects, double at)
    {
        var message = new JsonObject { ["message"] = text, ["atMs"] = Round(at) };
        if (objects is null)
        {
            message["objects"] = null;
            return message;
        }

        message["type"] = objects.GetType().FullName;
        var listed = new JsonArray();
        var missing = 0;
        var index = 0;
        foreach (var item in objects)
        {
            var described = Describe(item);
            var path = described["path"]?.GetValue<string>();
            if (path is not null && !File.Exists(path) && !Directory.Exists(path))
            {
                missing++;
            }

            if (index < 5)
            {
                listed.Add(described);
            }

            index++;
        }

        message["count"] = index;
        message["first"] = listed;
        message["pathsMissingOnDisk"] = missing;
        return message;
    }

    private static JsonObject Describe(object? item)
    {
        return item switch
        {
            CoreWebView2File file => new JsonObject { ["type"] = "CoreWebView2File", ["path"] = file.Path },
            CoreWebView2FileSystemHandle handle => new JsonObject
            {
                ["type"] = "CoreWebView2FileSystemHandle",
                ["kind"] = handle.Kind.ToString(),
                ["path"] = handle.Path,
                ["permission"] = handle.Permission.ToString(),
            },
            null => new JsonObject { ["type"] = "null" },
            _ => new JsonObject { ["type"] = item.GetType().FullName },
        };
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (e.Uri.StartsWith("https://app.localhost/", StringComparison.Ordinal))
        {
            return;
        }

        _current?.Navigations.Add(new JsonObject { ["uri"] = e.Uri, ["userInitiated"] = e.IsUserInitiated });
        e.Cancel = true;
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        _current?.NewWindows.Add(new JsonObject { ["uri"] = e.Uri, ["userInitiated"] = e.IsUserInitiated });
        e.Handled = true;
    }

    private void MakeFixtures()
    {
        Directory.CreateDirectory(_fixtures);
        File.WriteAllText(Fixture("a.txt"), "alpha\n");
        File.WriteAllText(Fixture("ünïcödé 文件.txt"), "unicode\n");

        var folder = Directory.CreateDirectory(Fixture("folder"));
        File.WriteAllText(Path.Combine(folder.FullName, "one.md"), "# one\n");
        File.WriteAllText(Path.Combine(folder.FullName, "two.md"), "# two\n");
        Directory.CreateDirectory(Path.Combine(folder.FullName, "inner"));
        File.WriteAllText(Path.Combine(folder.FullName, "inner", "three.md"), "# three\n");

        using (var big = File.Create(Fixture("big.bin")))
        {
            big.SetLength(50L * 1024 * 1024);
        }

        var many = Directory.CreateDirectory(Fixture("many"));
        for (var i = 0; i < 1000; i++)
        {
            var path = Path.Combine(many.FullName, $"f{i:0000}.txt");
            if (!File.Exists(path))
            {
                File.WriteAllText(path, "x");
            }
        }
    }

    private string Fixture(string name) => Path.Combine(_fixtures, name);

    private void WriteResults()
    {
        _results["os"] = Environment.OSVersion.VersionString;
        var text = _results.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var folder = Path.Combine(_root, "spikes", "file-drop", "results");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, $"windows-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        File.WriteAllText(file, text);
        Console.WriteLine(file);
    }

    private static double Round(double value) => Math.Round(value, 2);
}
