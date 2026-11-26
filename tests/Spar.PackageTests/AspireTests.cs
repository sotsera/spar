using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;

namespace Spar.PackageTests;

/// <summary>
/// The telemetry guide's Aspire recipe, from Spar's packages (plan, M2d; decision "M2d's decide-first questions", item
/// 5): a new app with the guide's package and <c>Program.cs</c> line, an app host with the guide's <c>AppHost.cs</c>,
/// started headless, and the app's and the page's telemetry read back from the dashboard's own API. On Linux it runs
/// under a private home and Xvfb, on a machine with WebKitGTK 6.0, Xvfb and D-Bus (not the hosted CI runner, which has
/// none of them); on Windows only in CI, because Aspire's orchestrator keeps its state in the user's
/// profile (<c>%USERPROFILE%\.dcp</c>), which a developer's run must not touch.
/// </summary>
public sealed class AspireTests(Consumer consumer) : IDisposable
{
    /// <summary>The Aspire version the guide's recipe is checked with.</summary>
    private const string AspireVersion = "13.6.5";

    private const string App = "Ledger";
    private const string Service = "ledger";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    public async Task The_guides_app_host_shows_the_app_and_its_page_in_the_dashboard()
    {
        var inCi = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";
        Assert.SkipUnless(OperatingSystem.IsLinux() || (OperatingSystem.IsWindows() && inCi),
            "Aspire's orchestrator writes its state to %USERPROFILE%\\.dcp: on Windows this check runs in CI only.");
        TemplateTests.SkipWithoutLinuxDesktop();

        var root = await MakeAppAsync();
        var (built, buildOutput) = await consumer.DotnetInAsync(root, "", "build", $"{App}.AppHost", "-nologo");
        Assert.True(built == 0, buildOutput);

        await using var host = await AppHostRun.StartAsync(root, consumer.Environment, Token);
        var resources = await host.WaitForAsync(
            "/api/telemetry/resources",
            json => HasResource(json, Service, "hasLogs") && HasResource(json, $"{Service}-page", "hasTraces"),
            TimeSpan.FromMinutes(3));
        Assert.True(HasResource(resources, Service, "hasLogs"), $"The app's logs never reached the dashboard:\n{resources}\n{host.Output}");
        Assert.True(HasResource(resources, $"{Service}-page", "hasTraces"), $"The page's spans never reached the dashboard:\n{resources}\n{host.Output}");

        var spans = await host.GetAsync("/api/telemetry/spans");
        Assert.Contains(Spans(spans), span => span.Service == $"{Service}-page" && span.Name == "documentLoad");
    }

    public void Dispose() => consumer.Report(TestContext.Current.TestState?.Result == TestResult.Passed);

    /// <summary>The new app with the guide's blocks, and its app host beside it.</summary>
    private async Task<string> MakeAppAsync()
    {
        var root = Path.Combine(consumer.RunRoot, "Aspire Äpp");
        var (created, createOutput) = await consumer.NewAppAsync(root, "-n", App);
        Assert.True(created == 0, createOutput);
        ConsumerFiles.WriteNugetConfig(root, consumer.Feed);

        var blocks = GuideSnippets.ReadPage(GuideSnippets.TelemetryPage(consumer.Repository));
        var project = Path.Combine(root, App, $"{App}.csproj");
        File.WriteAllText(project, GuideSnippets.AddTo(File.ReadAllText(project), blocks, GuideSnippets.TelemetryPackage));

        // The guide's line inside the Create lambda, after the template's own settings.
        var program = Path.Combine(root, App, "Program.cs");
        var text = File.ReadAllText(program);
        var end = text.LastIndexOf("\n});", StringComparison.Ordinal);
        Assert.True(end > 0, "The template's Program.cs has no Create lambda.");
        var line = blocks[GuideSnippets.TelemetryProgram].Trim();
        File.WriteAllText(program, $"{text[..end]}\n\n    {line}{text[end..]}");

        var appHost = Directory.CreateDirectory(Path.Combine(root, $"{App}.AppHost")).FullName;
        File.WriteAllText(Path.Combine(appHost, $"{App}.AppHost.csproj"), AppHostProject());
        var appHostCode = blocks[GuideSnippets.AspireAppHost].Replace("MyApp", App, StringComparison.Ordinal).Replace("\"myapp\"", $"\"{Service}\"", StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(appHost, "AppHost.cs"), appHostCode);
        return root;
    }

    /// <summary>
    /// Aspire's own app host template's project (<c>dotnet new aspire-apphost</c>), with the reference the guide adds, on
    /// the app's .NET, and without the Aspire CLI's bundle: DCP and the dashboard come from their NuGet packages, since
    /// a CI runner has no Aspire CLI.
    /// </summary>
    private static string AppHostProject() => $"""
        <Project Sdk="Aspire.AppHost.Sdk/{AspireVersion}">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net11.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
            <AspireUseCliBundle>false</AspireUseCliBundle>
            <NoWarn>$(NoWarn);ASPIRE010</NoWarn>
          </PropertyGroup>

          <ItemGroup>
            <ProjectReference Include="..\{App}\{App}.csproj" />
          </ItemGroup>
        </Project>

        """;

    private static bool HasResource(string json, string name, string signal)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.ValueKind == JsonValueKind.Array && document.RootElement.EnumerateArray().Any(resource =>
            resource.GetProperty("name").GetString() == name && resource.TryGetProperty(signal, out var has) && has.GetBoolean());
    }

    /// <summary>The spans of the dashboard's OTLP JSON (<c>resourceSpans</c>), with their resource's service name.</summary>
    private static List<(string Service, string Name)> Spans(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement.TryGetProperty("data", out var data) ? data : document.RootElement;
        var spans = new List<(string, string)>();
        foreach (var resourceSpans in root.GetProperty("resourceSpans").EnumerateArray())
        {
            var service = resourceSpans.GetProperty("resource").GetProperty("attributes").EnumerateArray()
                .Where(a => a.GetProperty("key").GetString() == "service.name")
                .Select(a => a.GetProperty("value").GetProperty("stringValue").GetString())
                .FirstOrDefault() ?? "";
            foreach (var scope in resourceSpans.GetProperty("scopeSpans").EnumerateArray())
            {
                spans.AddRange(scope.GetProperty("spans").EnumerateArray().Select(span => (service, span.GetProperty("name").GetString() ?? "")));
            }
        }

        return spans;
    }
}

/// <summary>
/// One run of the app host: the dashboard unsecured on free ports of this machine, its API open, nothing on the
/// developer's display (Linux: an Xvfb of its own, a private D-Bus session and home), ended with its process tree.
/// </summary>
[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
internal sealed class AppHostRun : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Process? _display;
    private readonly StringBuilder _output = new();
    private readonly HttpClient _http;

    private AppHostRun(Process process, Process? display, Uri dashboard)
    {
        _process = process;
        _display = display;
        _http = new HttpClient { BaseAddress = dashboard, Timeout = TimeSpan.FromSeconds(10) };
        process.OutputDataReceived += (_, e) => Append(e.Data);
        process.ErrorDataReceived += (_, e) => Append(e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    public string Output
    {
        get
        {
            lock (_output)
            {
                return _output.ToString();
            }
        }
    }

    public static async Task<AppHostRun> StartAsync(string root, IReadOnlyDictionary<string, string> environment, CancellationToken cancellationToken)
    {
        var executable = Path.Combine(root, "Ledger.AppHost", "bin", "Debug", "net11.0", OperatingSystem.IsWindows() ? "Ledger.AppHost.exe" : "Ledger.AppHost");
        var dashboard = new Uri($"http://localhost:{FreePort()}");
        var start = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsLinux() ? "dbus-run-session" : executable,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            KillOnParentExit = true,
        };
        if (OperatingSystem.IsLinux())
        {
            start.ArgumentList.Add("--");
            start.ArgumentList.Add(executable);
        }

        foreach (var (name, value) in environment)
        {
            start.Environment[name] = value;
        }

        start.Environment["DOTNET_ENVIRONMENT"] = "Development";
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        start.Environment["ASPNETCORE_URLS"] = dashboard.ToString().TrimEnd('/');
        start.Environment["ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL"] = $"http://localhost:{FreePort()}";
        start.Environment["ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL"] = $"http://localhost:{FreePort()}";
        start.Environment["ASPIRE_ALLOW_UNSECURED_TRANSPORT"] = "true";
        start.Environment["ASPIRE_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS"] = "true";

        Process? display = null;
        if (OperatingSystem.IsLinux())
        {
            (display, var number) = await StartXvfbAsync(cancellationToken);
            var home = Directory.CreateDirectory(Path.Combine(root, "aspire-home")).FullName;
            start.Environment["DISPLAY"] = $":{number}";
            start.Environment.Remove("WAYLAND_DISPLAY");
            start.Environment["HOME"] = home;
            foreach (var (name, folder) in new[] { ("XDG_DATA_HOME", "data"), ("XDG_CONFIG_HOME", "config"), ("XDG_CACHE_HOME", "cache"), ("XDG_STATE_HOME", "state") })
            {
                start.Environment[name] = Directory.CreateDirectory(Path.Combine(home, folder)).FullName;
            }

            var runtime = Directory.CreateDirectory(Path.Combine("/dev/shm", $"spar-aspire-{Environment.ProcessId}")).FullName;
            File.SetUnixFileMode(runtime, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            start.Environment["XDG_RUNTIME_DIR"] = runtime;
        }

        return new AppHostRun(Process.Start(start)!, display, dashboard);
    }

    public async Task<string> GetAsync(string path)
    {
        using var response = await _http.GetAsync(path);
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>The path's answer once <paramref name="done"/> holds, or the last one when the time is up or the host exited.</summary>
    public async Task<string> WaitForAsync(string path, Func<string, bool> done, TimeSpan limit)
    {
        var deadline = Stopwatch.GetTimestamp();
        var last = "";
        while (Stopwatch.GetElapsedTime(deadline) < limit && !_process.HasExited)
        {
            try
            {
                last = await GetAsync(path);
                if (last.StartsWith('[') && done(last))
                {
                    return last;
                }
            }
            catch (HttpRequestException)
            {
                // The dashboard is not listening yet.
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        return last;
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
        }

        await _process.WaitForExitAsync();
        _process.Dispose();
        if (_display is not null)
        {
            _display.Kill(entireProcessTree: true);
            await _display.WaitForExitAsync();
            _display.Dispose();
        }
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <summary>
    /// An Xvfb on the first free display from :230, as the black box starts its own: <c>-displayfd 1</c> prints the
    /// number once the server accepts clients (a number another server holds fails), <c>-noreset</c> keeps it up when its
    /// last client leaves, and only an abstract socket.
    /// </summary>
    private static async Task<(Process, string)> StartXvfbAsync(CancellationToken cancellationToken)
    {
        for (var number = 230; number < 260; number++)
        {
            var start = new ProcessStartInfo("Xvfb") { RedirectStandardOutput = true, RedirectStandardError = true, KillOnParentExit = true };
            foreach (var argument in new[] { $":{number}", "-displayfd", "1", "-noreset", "-screen", "0", "1280x800x24", "-nolisten", "tcp", "-nolisten", "unix" })
            {
                start.ArgumentList.Add(argument);
            }

            var process = Process.Start(start)!;
            process.ErrorDataReceived += (_, _) => { };
            process.BeginErrorReadLine();
            var ready = await process.StandardOutput.ReadLineAsync(cancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            if (ready?.Trim() == number.ToString(System.Globalization.CultureInfo.InvariantCulture))
            {
                return (process, ready.Trim());
            }

            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(cancellationToken);
            process.Dispose();
        }

        throw new InvalidOperationException("No Xvfb started on a display from :230 to :259.");
    }

    private void Append(string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (_output)
        {
            _output.Append(line).Append('\n');
        }
    }
}
