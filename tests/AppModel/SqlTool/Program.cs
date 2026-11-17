using Microsoft.Extensions.DependencyInjection;
using Spar;
using SqlTool.Connections;
using SqlTool.CopyJobs;
using SqlTool.Monitoring;
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

    // The browser keeps its strict defaults; only the support build the team sends to one user adds devtools (3.17). Decided.
    if (SupportBuild.IsSupportBuild)
    {
        spar.Browser(b => b.AllowDevTools());
    }

    // Services: Microsoft DI, one scope per command call.
    spar.Services.AddSingleton<IConnectionStore, FileConnectionStore>();
    spar.Services.AddScoped<IConnections, ConnectionsApi>();
    spar.Services.AddSingleton<IMonitor, InstanceMonitor>();
    spar.Services.AddSingleton<RunningCopies>();
    spar.Services.AddSingleton<RecipeQueue>();
    spar.Services.AddSingleton<HangLog>();
});

return app.Run();
