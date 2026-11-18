using System.Text;

namespace Spar.BuildTests;

/// <summary>
/// What a Production executable must not carry (plan section 5): the test host, the loopback host, the test
/// libraries, the Windows host's Development-only triggers, the Development-only parts of page security (the check
/// that the run's policy equals the build's, and the advice in violation lines), and the UI-thread stall probe unless the app keeps it
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
    ];

    private static readonly string[] Literals =
    [
        "/_spar/channel", "/_spar/send", "Spar.TestHost.IsSupported", "SPAR_DEV_PAGE_HANG", "SPAR_DEV_HEARTBEAT_MS",
        "SPAR_DEV_UI_STALL_MS", "SPAR_DEV_SLOW_DISPOSE_MS", "The UI thread stalled",
        "differs from the one the build read", "Allow it with csp.", "SPAR_DEV_INPUT", "remote-debugging-port",
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
    /// with "fakeSpar") and the generated tables and error writers only that module uses.
    /// </summary>
    public static readonly string[] PageMarkers = ["fakeSpar", "__sparTesting", "createFakeSpar"];

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
