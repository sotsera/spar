using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;

namespace WebView2Flags;

/// <summary>
/// One child run: a WebView2 environment with the job's browser arguments (through the environment options object, as
/// Spar's host passes them), a controller in a plain window, pages served on <c>https://app.localhost</c> through
/// <c>WebResourceRequested</c> with a Spar-like CSP, and one scenario.
/// </summary>
internal sealed class HostRun(JsonObject job, nint hwnd)
{
    private const string Origin = "https://app.localhost";
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly JsonObject _result = new();
    private readonly JsonArray _requests = [];
    private readonly Dictionary<string, TaskCompletionSource<(double At, JsonObject Message)>> _waiting = [];
    private readonly string _www = Path.Combine(AppContext.BaseDirectory, "www");
    private CoreWebView2Environment _env = null!;
    private CoreWebView2 _view = null!;

    private double Now => _clock.Elapsed.TotalMilliseconds;

    public async Task<JsonObject> RunAsync()
    {
        _result["job"] = job.DeepClone();
        try
        {
            await RunCoreAsync();
        }
        catch (Exception e)
        {
            _result["error"] = e.ToString();
        }

        return _result;
    }

    private async Task RunCoreAsync()
    {
        foreach (var name in new[] { "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", "WEBVIEW2_BROWSER_EXECUTABLE_FOLDER", "WEBVIEW2_USER_DATA_FOLDER", "WEBVIEW2_RELEASE_CHANNEL_PREFERENCE" })
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        var arguments = (string?)job["args"];
        var options = new CoreWebView2EnvironmentOptions(additionalBrowserArguments: arguments);
        _result["optionsArguments"] = options.AdditionalBrowserArguments;
        var t0 = Now;
        _env = await CoreWebView2Environment.CreateAsync((string?)job["browserFolder"], (string)job["userDataFolder"]!, options);
        _result["envMs"] = Now - t0;
        _result["runtime"] = _env.BrowserVersionString;
        var exited = new TaskCompletionSource<double>();
        _env.BrowserProcessExited += (_, _) => exited.TrySetResult(Now);

        var t1 = Now;
        var controller = await _env.CreateCoreWebView2ControllerAsync(hwnd);
        _result["controllerMs"] = Now - t1;
        controller.Bounds = new System.Drawing.Rectangle(0, 0, 1000, 680);
        controller.IsVisible = true;
        _view = controller.CoreWebView2;
        _view.Settings.IsReputationCheckingRequired = (bool?)job["reputation"] ?? false;
        _view.Settings.AreDefaultScriptDialogsEnabled = false;
        _view.AddWebResourceRequestedFilter(Origin + "/*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
        _view.WebResourceRequested += Serve;
        _view.WebMessageReceived += OnMessage;
        _result["browserPid"] = _view.BrowserProcessId;
        _result["browserCommandLine"] = Native.CommandLine(_view.BrowserProcessId);

        switch ((string)job["scenario"]!)
        {
            case "load":
                await LoadAsync();
                break;
            case "wasm":
                await WasmAsync();
                break;
            case "smart":
                await SmartAsync();
                break;
            case "find":
                await FindScenario.RunAsync(_env, _view, hwnd, _result, (string)job["outDir"]!, NavigateAsync);
                break;
            default:
                throw new ArgumentException("unknown scenario");
        }

        var processes = new JsonArray();
        foreach (var p in _env.GetProcessInfos())
        {
            processes.Add(new JsonObject { ["pid"] = p.ProcessId, ["kind"] = p.Kind.ToString(), ["commandLine"] = Native.CommandLine((uint)p.ProcessId) });
        }

        _result["processes"] = processes;
        _result["requests"] = _requests;
        var tc = Now;
        controller.Close();
        var done = await Task.WhenAny(exited.Task, Task.Delay(20000));
        _result["browserExitMs"] = done == exited.Task ? exited.Task.Result - tc : null;
    }

    /// <summary>Navigates and waits for the page's message of kind <paramref name="kind"/>; returns ms from Navigate to it.</summary>
    private async Task<(double Ms, JsonObject Message, double BootMs)> NavigateAsync(string path, string kind)
    {
        var boot = Expect("boot");
        var wanted = Expect(kind);
        var t = Now;
        (_result["navigateAt"] ??= new JsonArray()).AsArray().Add(Math.Round(t, 1));
        _view.Navigate(Origin + path);
        var finished = await Task.WhenAny(wanted, Task.Delay(30000));
        if (finished != wanted)
        {
            throw new TimeoutException($"no '{kind}' message for {path}");
        }

        var (at, message) = wanted.Result;
        var bootMs = boot.IsCompleted ? boot.Result.At - t : double.NaN;
        return (at - t, message, bootMs);
    }

    private Task<(double At, JsonObject Message)> Expect(string kind)
    {
        var tcs = new TaskCompletionSource<(double, JsonObject)>();
        _waiting[kind] = tcs;
        return tcs.Task;
    }

    private void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var at = Now;
        if (JsonNode.Parse(e.TryGetWebMessageAsString()) is JsonObject message && (string?)message["kind"] is { } kind && _waiting.Remove(kind, out var tcs))
        {
            tcs.TrySetResult((at, message));
        }
    }

    private async Task LoadAsync()
    {
        var navigations = new JsonArray();
        var count = (int?)job["navigations"] ?? 3;
        for (var i = 0; i < count; i++)
        {
            var (ms, message, bootMs) = await NavigateAsync($"/load.html?n={i}", "ready");
            navigations.Add(new JsonObject { ["readyMs"] = ms, ["bootMs"] = bootMs, ["page"] = message.DeepClone() });
        }

        _result["navigations"] = navigations;
        await Task.Delay((int?)job["stayMs"] ?? 0);
    }

    private async Task WasmAsync()
    {
        var (ms, message, _) = await NavigateAsync("/wasm.html", "wasm");
        _result["wasmMs"] = ms;
        _result["wasm"] = message.DeepClone();
        await Task.Delay((int?)job["stayMs"] ?? 0);
    }

    private async Task SmartAsync()
    {
        var first = await NavigateAsync("/smart.html", "ready");
        await Task.Delay(1000);
        var second = await NavigateAsync("/smart.html?second", "ready");
        _result["navigations"] = new JsonArray(first.Ms, second.Ms);
        _result["page"] = second.Message.DeepClone();
        await Task.Delay((int?)job["stayMs"] ?? 8000);
    }

    private void Serve(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var uri = new Uri(e.Request.Uri);
        var path = uri.AbsolutePath;
        _requests.Add(new JsonObject { ["at"] = Math.Round(Now, 1), ["path"] = uri.PathAndQuery, ["context"] = e.ResourceContext.ToString(), ["source"] = e.RequestedSourceKind.ToString() });
        if (path == "/")
        {
            path = "/load.html";
        }

        var file = Path.GetFullPath(Path.Combine(_www, path.TrimStart('/')));
        var found = uri.Host == "app.localhost" && file.StartsWith(_www, StringComparison.OrdinalIgnoreCase) && File.Exists(file);
        var body = found ? File.ReadAllBytes(file) : Encoding.UTF8.GetBytes("not found");
        var headers = new StringBuilder()
            .Append("Content-Type: ").Append(found ? ContentType(file) : "text/plain; charset=utf-8").Append("\r\n")
            .Append("Content-Security-Policy: ").Append(Csp()).Append("\r\n")
            .Append("X-Content-Type-Options: nosniff\r\n")
            .Append("Cache-Control: no-store\r\n")
            .ToString();
        e.Response = _env.CreateWebResourceResponse(new MemoryStream(body), found ? 200 : 404, found ? "OK" : "Not Found", headers);
    }

    private string Csp()
    {
        var wasm = (bool?)job["wasmEval"] == true ? " 'wasm-unsafe-eval'" : "";
        return $"default-src 'none'; script-src 'self'{wasm}; style-src 'self'; img-src 'self'; connect-src {Origin}; worker-src 'self'; frame-src 'self'; media-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'self'";
    }

    private static string ContentType(string file) => Path.GetExtension(file) switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" => "application/json",
        ".svg" => "image/svg+xml",
        _ => "application/octet-stream",
    };

    /// <summary>QueryInterface on a COM pointer for an IID; the HRESULT.</summary>
    public static int Probe(nint unknown, Guid iid)
    {
        var hr = Marshal.QueryInterface(unknown, in iid, out var p);
        if (p != 0)
        {
            Marshal.Release(p);
        }

        return hr;
    }
}
