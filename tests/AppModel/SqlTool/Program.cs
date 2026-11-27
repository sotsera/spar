using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spar;
using SqlTool.Connections;
using SqlTool.CopyJobs;
using SqlTool.Monitoring;
using SqlTool.Reports;
using SqlTool.Settings;
using SqlTool.Support;

var app = SparApplication.Create(args, spar =>
{
    // Window colours before the page exists, in both themes (plan 3d), and a title bar the page draws (6.9). Decided.
    spar.Window(w => w
        .Background(light: "#ffffff", dark: "#1f1f1f")
        .TitleBar(TitleBar.Custom)
        .Size(1200, 800)
        .MinSize(800, 600)
        .RememberPlacement()
        .CloseToTray());

    // A tray icon for the monitoring the tool keeps running (3.5). Decided.
    spar.Tray(t => t
        .Icon("assets/tray.png")
        .Tooltip("SQL Tool")
        .OnActivate(TrayAction.ShowWindow)
        .Menu(m => m
            .Item("Open", TrayAction.ShowWindow)
            .Item<IMonitor>("pause", "Pause monitoring", monitor => monitor.TogglePause())
            .Separator()
            .Item("Quit", TrayAction.Quit)));

    // The strict CSP is the default; each addition is explicit (plan 3b). A directive method takes several sources and
    // may be called again: Spar sends each directive once, its sources together. Violations are reported in every
    // build by default; here report-uri, the backstop, reports only in Development and Test builds. Decided.
    spar.ContentSecurity(csp => csp
        .FontSrc("https://fonts.gstatic.com", "https://fonts.example-cdn.com")
        .FontSrc("https://fonts.gstatic.com")
        .ImgSrc("data:")
        .Reporting(listener: CspReports.Always, endpoint: CspReports.DevelopmentOnly));

    // Powerful features are denied unless declared (plan 3b). Decided.
    spar.Permissions(p => p.Allow(Permission.ClipboardRead));

    // File types the app may open with their default program; nothing opens by default (6.6). Decided.
    spar.Shell(s => s.AllowOpening(".csv", ".pdf"));

    // Where data, logs and crash records go; each has a per-user default (3.13). Decided.
    // Here logs sit next to the executable, as in a portable install.
    spar.Folders(f => f.Logs(Path.Combine(AppContext.BaseDirectory, "logs")));

    // Deployment choices beside the executable, in SqlTool.spar.json: an installation may move the folders and the
    // settings file, or keep the browser's data in memory, without a new build; never security (3.19). Built in M3b.
    spar.InstallationFile();

    // The folders the services will get, resolved now for what Program.cs builds itself: the hang record goes in the
    // logs folder chosen above without repeating its path (3.13). spar.Folders(…) throws after this call. Built; the
    // shape is for review (decision "Configuration stays the app's choice").
    var folders = spar.ResolveFolders();

    // Window kinds the page opens by name (6.7). Decided.
    spar.Window("settings", w => w
        .Route("/settings")
        .Size(640, 520)
        .Modal()
        .SingleInstance()
        .ProtectFromCapture()
        .Commands(c => c.Allow<IConnections>()));
    spar.Window("queryEditor", w => w.Route("/query").Size(1000, 700).Zoom(min: 0.5, max: 3).FindInPage());

    // Closing asks first: the page's guard, then this one, which asks while a copy runs (6.11). Decided.
    spar.OnCloseRequested((request, services) => services.GetRequiredService<RunningCopies>().Any
        ? CloseDecision.Ask("A copy is still running. Close anyway?")
        : CloseDecision.Close);

    // A second launch shows and focuses the main window, then hands its arguments here (3.14). Decided.
    spar.OnRelaunch((relaunch, services) =>
    {
        if (relaunch.Arguments is ["--recipe", var file])
        {
            services.GetRequiredService<RecipeQueue>().TryAdd(relaunch.ResolvePath(file));
        }
    });

    // A missing prerequisite opens the company's install guide in place of Spar's message (3.15). Decided.
    spar.OnStartupProblem(SupportPage.TryOpen);

    // A page that stops responding is recorded for support; Spar's own dialog still asks the user (3.16). Decided.
    spar.OnPageUnresponsive((hang, services, cancellationToken) =>
        services.GetRequiredService<HangLog>().RecordAsync(hang, cancellationToken));

    // The UI-thread stall probe stays in this Production build, logging only, for users' machines (3.13). Decided.
    spar.Diagnostics(d => d.KeepStallProbe());

    // The log file: Information and above, and Debug for the copy jobs; a logging.json in the per-user folder overrides
    // these on a user's machine without a new build (3.13). Decided.
    spar.Logging(l => l.Level(LogLevel.Information).Level("SqlTool.CopyJobs", LogLevel.Debug));

    // What the tool remembers between runs, in settings.json in the data folder (3.19). Built in M3b.
    spar.Settings(new ToolSettings(BatchSize: 50_000, PreviewRows: 200, LastRecipe: null));

    // Traces, metrics and logs over OTLP, the page's spans with them, when OTEL_EXPORTER_OTLP_ENDPOINT is set; nothing
    // otherwise (3.18). Built in M2d.
    spar.Telemetry();

    // The browser keeps its strict defaults; only the support build the team sends to one user adds devtools (3.17).
    // Decided. On Linux it also draws without the GPU, which rules a broken graphics driver out (3.17). Built; this use
    // of the option is for review.
    if (SupportBuild.IsSupportBuild)
    {
        spar.Browser(b => b
            .AllowDevTools()
            .HardwareAcceleration(HardwareAcceleration.Never));
    }

    // Services: Microsoft DI, one scope per command call.
    spar.Services.AddSingleton<IConnectionStore, FileConnectionStore>();
    spar.Services.AddScoped<IConnections, ConnectionsApi>();
    spar.Services.AddSingleton<IMonitor, InstanceMonitor>();
    spar.Services.AddSingleton<RunningCopies>();
    spar.Services.AddSingleton<RecipeQueue>();
    spar.Services.AddSingleton(new HangLog(folders.Logs));
    spar.Services.AddSingleton<ICopyHistory, CopyHistory>();
    spar.Services.AddSingleton<IActivityCharts, ActivityCharts>();
});

return app.Run();
