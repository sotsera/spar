using System.Text;

namespace Spar.BuildTests;

/// <summary>
/// What a Production executable must not carry (plan section 5): the test host, the loopback host, the test
/// libraries, the Windows host's Development-only triggers, the Development-only parts of page security (the check
/// that the run's policy equals the build's, and the advice in violation lines), the Window surface of window tests (the
/// host's test windows, the shared test browser and the page engine), and the UI-thread stall probe unless the app keeps it
/// (<see cref="StallProbe"/>). Type names are UTF-8 in a native image's metadata; string literals are UTF-16. Each marker is unique to
/// Spar, since generic names give false positives (pitfalls: <c>LoopbackHost</c> is also a <c>System.Uri</c> flag).
/// </summary>
internal static class ProductionBoundary
{
    private static readonly string[] Types =
    [
        "SparTestSession", "SparTestApp", "ISparEntryPoint", "LoopbackHost", "LoopbackChannel", "LoopbackAssets",
        "Spar.Loopback", "Spar.Testing", "Microsoft.AspNetCore.Server.Kestrel", "DevelopmentTriggers",
        "UiStallProbe", "PageSecurityMismatch", "CspAdvice", "DevelopmentInput", "DevelopmentConsole",

        // The Window surface (M1b step 8a): the host's test-only part, the test host's interfaces to it, and the page
        // engine and its isolated world, which only Spar.Testing carries.
        "TestWindowProcess", "ISparTestWindow", "SparTestWindowRequest", "FrameProcessesCompleted",
        "SparDevToolsException", "__sparAutomation",

        // Closing from a window test (decision "Window tests: closing, session-end budget, test theme and any-character
        // keys"): the test host's close request and its report. The close request itself, the page's guard and the
        // answers seam (ICloseQuestions) are production code.
        "SparTestClose", "SparCloseOptions", "SparCloseResult",

        // The black-box fixture (M1b step 8b): the launcher, its handle, its UI Automation client and the tested-app
        // marker the generated Projects class names, which only Spar.Testing carries.
        "SparBlackBox", "SparBlackBoxApp", "SparBlackBoxOptions", "SparNativeDialog", "SparProject", "BlackBoxQuery",
        "IUIAutomationElementArray",

        // The Windows Sandbox runner and real input (M1b step 8c): Spar.Testing only.
        "SandboxRunner", "SandboxSession", "SandboxGuest", "RealInput", "ScreenCapture", "SandboxEvidence",

        // The Window surface on Linux (M1c step 7): the test views' sessions and the test process's environment.
        "TestEnvironment", "TestViews",

        // The black box on Linux (M1c step 7): its AT-SPI client, its launched processes and its private desktop.
        "AtSpiAutomation", "AtSpiBus", "LinuxAppProcess", "LinuxDesktop",

        // A Debug build's development server (M1d step 1): the session, its launcher, the output it reads, the record it
        // finds, the proxy and the error page, and the WebView2 request headers only the proxy reads.
        "DevServerSession", "DevServerLauncher", "DevServerOutput", "DevServerState", "DevServerProxy", "DevErrorPage",
        "DevServerOutcome", "ICoreWebView2HttpRequestHeaders",

        // Loopback in the development loop (M1d step 2): the host Spar.Sdk adds to Development builds only, its run, its
        // dev session and the browser opener.
        "SparLoopbackHost", "LoopbackApp", "LoopbackSession", "LoopbackAddress", "BrowserOpener",

        // The Hot Reload signal (M1d step 3): the handler the runtime calls after an applied update, and the generated
        // enum whose member restarts the app for a contract edit (a Development build's generator output only).
        "BackendUpdates", "SparContractShape",
    ];

    private static readonly string[] Literals =
    [
        "/_spar/channel", "/_spar/send", "Spar.TestHost.IsSupported", "SPAR_DEV_PAGE_HANG", "SPAR_DEV_HEARTBEAT_MS",
        "SPAR_DEV_UI_STALL_MS", "SPAR_DEV_SLOW_DISPOSE_MS", "The UI thread stalled",
        "differs from the one the build read", "Allow it with csp.", "SPAR_DEV_INPUT", "remote-debugging-port",
        "spar-automation", "Spar test anchor", "Spar UI (tests)", "spartest", "app.Window.CloseAsync",
        "spar-black-box-", "spar-apps",
        "SPAR_SANDBOX_RUN", "SPAR_SANDBOX_DESKTOP", "WDAGUtilityAccount", "SparSandboxOtherWindow", @"C:\spar\runner",

        // The Linux host's own Development trigger (M1c step 3), and the descriptions and read-back lines only a
        // Development build of either host writes (M1c step 6): a Production image carries the values it set.
        "SPAR_DEV_PROMPT_ANSWERS", "Browser settings read back (", "Context menu (",

        // The Window surface on Linux (M1c step 7): the private Xvfb's setting and screen, and its runtime folders.
        "SPAR_TEST_DISPLAY", "6400x4200x24", "spar-test-",

        // The black box on Linux (M1c step 7): the launch's tag and runtime folders.
        "SPAR_BLACK_BOX_LAUNCH", "spar-bb-",

        // A Debug build's development server (M1d step 1): its variables, its record and output files, the build's
        // settings, the plugin's nonce placeholder and identity path, the proxy's route and the error page's text.
        "SPAR_DEV_SERVER_URL", "SPAR_DEV_SERVER_FILE", "spar-dev-server.json", "spar-dev-server.log",
        "Spar.DevServer.Frontend", "spar-dev-nonce", "/__spar/dev-server", "/_spar/dev-server/",
        "The page cannot come from the development server", "npm run dev",

        // Loopback in the development loop (M1d step 2): its variables, its record and runtime configuration value, the
        // dev session's probe and header, and the Linux no-display hint of Development builds.
        "SPAR_HOST", "SPAR_LOOPBACK_PORT", "SPAR_LOOPBACK_TOKEN", "SPAR_LOOPBACK_BROWSER", "spar-loopback.json",
        "Spar.Loopback.SessionFile", "/_spar/session", "X-Spar-Run", "-lp Loopback",

        // The Hot Reload signal (M1d step 3): the event's name and the hosts' line for each applied update.
        "backend-updated", "Hot Reload applied revision",
    ];

    /// <summary>
    /// The stall probe as a kept image carries it (<c>spar.Diagnostics(d => d.KeepStallProbe())</c>): its type name
    /// (UTF-8) and its warning (UTF-16). An app without the call carries neither.
    /// </summary>
    public static readonly (string Text, Encoding Encoding)[] StallProbe =
    [
        ("UiStallProbe", Encoding.UTF8),
        ("The UI thread stalled", Encoding.Unicode),
    ];

    /// <summary>
    /// What a Production page bundle must not carry: the fake backend of <c>@spar/app/testing</c> (its messages start
    /// with "fakeSpar") and the generated tables and error writers only that module uses; and the Hot Reload signal's
    /// page side (M1d step 3), which <c>onBackendUpdate</c> and the development overlay's toast reach only behind
    /// <c>import.meta.env.DEV</c>: the event's name, the overlay's element and the toast's text.
    /// </summary>
    public static readonly string[] PageMarkers =
        ["fakeSpar", "__sparTesting", "createFakeSpar", "backend-updated", "data-spar-dev-overlay", "Backend updated"];

    /// <summary>The page markers a folder's JavaScript files contain; empty for a clean Production bundle.</summary>
    public static IReadOnlyList<string> FindInPage(string directory, IEnumerable<string> markers)
    {
        var scripts = Directory.GetFiles(directory, "*.js", SearchOption.AllDirectories).Select(File.ReadAllText).ToList();
        return markers.Where(m => scripts.Any(s => s.Contains(m, StringComparison.Ordinal))).ToList();
    }

    /// <summary>
    /// The DevTools Protocol methods a Production executable may name (decision "Window-test profiles, trusted input,
    /// the page's pickers and the Sandbox runner"): the two in-process calls that keep the page's file picker closed,
    /// unless the app called <c>AllowPagePicker()</c>.
    /// </summary>
    public static readonly string[] AllowedDevToolsMethods = ["Page.enable", "Page.setInterceptFileChooserDialog"];

    /// <summary>
    /// The DevTools Protocol domains whose methods are searched for. The automation surface of window tests (the
    /// <c>Input</c>, <c>Runtime</c> and <c>Target</c> methods) and every other domain are refused, so a Production image
    /// that names any method beyond <see cref="AllowedDevToolsMethods"/> fails, whatever its marker list says.
    /// </summary>
    private static readonly string[] DevToolsDomains =
    [
        "Accessibility", "Browser", "CSS", "Debugger", "DOM", "DOMDebugger", "DOMSnapshot", "Emulation", "Fetch",
        "HeapProfiler", "Input", "IO", "Log", "Memory", "Network", "Overlay", "Page", "Profiler", "Runtime", "Security",
        "ServiceWorker", "Storage", "SystemInfo", "Target", "Tracing",
    ];

    /// <summary>
    /// Every DevTools Protocol method name (<c>Domain.method</c>, UTF-16 like the literals that hold them) in
    /// <paramref name="image"/>, by the domains above.
    /// </summary>
    public static IReadOnlyList<string> DevToolsMethods(byte[] image)
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var domain in DevToolsDomains)
        {
            var prefix = Encoding.Unicode.GetBytes(domain + ".");
            var span = image.AsSpan();
            var offset = 0;
            while (span[offset..].IndexOf(prefix) is >= 0 and var at)
            {
                var start = offset + at;
                offset = start + prefix.Length;
                var method = ReadIdentifier(image, offset);
                var preceded = start >= 2 && IsIdentifierChar(image[start - 2], image[start - 1]);
                if (preceded || method.Length == 0 || !char.IsAsciiLetterLower(method[0]))
                {
                    continue;
                }

                // A native image stores literals back to back with no terminator ("Page.enabletext"), so an allowed
                // name is matched as a prefix; anything else is reported with whatever follows it.
                var name = domain + "." + method;
                found.Add(AllowedDevToolsMethods.FirstOrDefault(allowed => name.StartsWith(allowed, StringComparison.Ordinal)) ?? name);
            }
        }

        return [.. found];
    }

    /// <summary>The markers <paramref name="image"/> contains; empty for a clean Production executable.</summary>
    public static IReadOnlyList<string> Find(byte[] image) =>
        Types.Where(t => Contains(image, t, Encoding.UTF8))
            .Concat(Literals.Where(l => Contains(image, l, Encoding.Unicode)))
            .ToList();

    public static bool Contains(byte[] image, string text, Encoding encoding) =>
        image.AsSpan().IndexOf(encoding.GetBytes(text)) >= 0;

    private static string ReadIdentifier(byte[] image, int offset)
    {
        var builder = new StringBuilder();
        for (var at = offset; at + 1 < image.Length && IsIdentifierChar(image[at], image[at + 1]); at += 2)
        {
            builder.Append((char)image[at]);
        }

        return builder.ToString();
    }

    /// <summary>An ASCII letter or digit as UTF-16 little-endian.</summary>
    private static bool IsIdentifierChar(byte low, byte high) => high == 0 && char.IsAsciiLetterOrDigit((char)low);
}
