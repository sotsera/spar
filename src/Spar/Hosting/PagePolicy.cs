using System.Text;
using Spar.Runtime;

namespace Spar.Hosting;

/// <summary>
/// The policy a host sends with every response (plan section 3b): the Content Security Policy composed from the base
/// policy, the app's declaration (<see cref="SparPageSecurity"/>), the hashes the build needs and, per document, the
/// style nonce; and the <c>Permissions-Policy</c> from the declared permissions. The policy is one valid policy: each
/// directive appears once, with every value for it grouped together (the base value first, then the opt-in keywords,
/// the app's sources, and the nonce and hashes), duplicates removed, in a fixed directive order. Browsers keep only the
/// first copy of a repeated directive, so a repeat would silently drop values. Composed once per host; only the nonce
/// changes per document. A Debug build that serves its page from the Vite development server (plan section 4) composes
/// it with the server's HMR WebSocket, which joins <c>connect-src</c>, and every document's nonce then goes in
/// <c>script-src</c> and <c>style-src</c>, for Vite's inline preamble and the styles its client injects.
/// </summary>
internal sealed class PagePolicy
{
    /// <summary>Where the policy's <c>report-uri</c> sends violation reports.</summary>
    public const string ReportPath = "/_spar/csp-report";

    /// <summary>The features both <c>Permissions-Policy</c> and the WebView know, in the order the header lists them.</summary>
    private static readonly (string Feature, Permission Permission)[] Declarable =
    [
        ("camera", Permission.Camera),
        ("microphone", Permission.Microphone),
        ("geolocation", Permission.Geolocation),
        ("clipboard-read", Permission.ClipboardRead),
        ("accelerometer", Permission.Sensors),
        ("gyroscope", Permission.Sensors),
        ("magnetometer", Permission.Sensors),
        ("local-fonts", Permission.LocalFonts),
        ("midi", Permission.Midi),
        ("window-management", Permission.WindowManagement),
        ("autoplay", Permission.Autoplay),
    ];

    /// <summary>Powerful features the WebView has no permission request for: always denied.</summary>
    private static readonly string[] AlwaysDenied = ["usb", "serial", "hid", "bluetooth", "payment", "display-capture"];

    /// <summary>Where a document's nonce goes in the marked policy; never a character of a real one.</summary>
    private const string NonceMarker = "\0nonce\0";

    private readonly string _policy;
    private readonly string _marked;

    /// <param name="declared">The app's declaration, from the generated catalog.</param>
    /// <param name="origin">The page's origin with its scheme: <c>connect-src</c> names it, since <c>'self'</c> would also allow WebSockets.</param>
    /// <param name="development">A Development or Test build, where <see cref="CspReports.DevelopmentOnly"/> reports.</param>
    /// <param name="scriptHashes">The build's inline-script hashes, such as the SRI import map's (<c>'sha256-…'</c>).</param>
    /// <param name="styleHashes">The build's inline-style hashes.</param>
    /// <param name="devServerSocket">
    /// The Vite development server's HMR WebSocket (<c>ws://127.0.0.1:&lt;port&gt;</c>) when a Debug build serves the
    /// page from it; null otherwise.
    /// </param>
    public PagePolicy(
        SparPageSecurity declared,
        string origin,
        bool development,
        IReadOnlyList<string>? scriptHashes = null,
        IReadOnlyList<string>? styleHashes = null,
        string? devServerSocket = null)
    {
        Declared = declared;
        ListenerReports = Reports(declared.Listener, development);
        EndpointReports = Reports(declared.Endpoint, development);
        StyleNonce = declared.StyleNonce && !declared.AllowInlineStyles;
        DevServer = devServerSocket is not null;
        PermissionsPolicy = ComposePermissions(declared.Permissions);

        var directives = Compose(declared, origin, scriptHashes ?? [], styleHashes ?? [], EndpointReports, devServerSocket);
        _policy = Render(directives, nonce: null);
        _marked = StyleNonce || DevServer ? Render(directives, NonceMarker) : _policy;
    }

    /// <summary>The app's declaration this policy was composed from.</summary>
    public SparPageSecurity Declared { get; }

    /// <summary>Whether the host-injected listener reports violations in this build.</summary>
    public bool ListenerReports { get; }

    /// <summary>Whether the policy carries <c>report-uri</c>, and the host answers it, in this build.</summary>
    public bool EndpointReports { get; }

    /// <summary>Whether each document served gets a fresh style nonce (<c>StyleNonce()</c>, unless inline styles are allowed).</summary>
    public bool StyleNonce { get; }

    /// <summary>
    /// Whether the policy is a Debug build's for the Vite development server: every document's nonce is in
    /// <c>script-src</c> and <c>style-src</c>, and <c>connect-src</c> names the server's HMR WebSocket.
    /// </summary>
    public bool DevServer { get; }

    /// <summary>The <c>Permissions-Policy</c> header: declared features <c>=(self)</c>, everything else <c>=()</c>.</summary>
    public string PermissionsPolicy { get; }

    /// <summary>The Content Security Policy for every response that is not a document with a style nonce.</summary>
    public string ContentSecurityPolicy => _policy;

    /// <summary>Whether the app declared <paramref name="permission"/>, so the host grants the page's request for it.</summary>
    public bool Allows(Permission permission) => PageSecurityNames.Contains(Declared.Permissions, permission);

    /// <summary>
    /// The Content Security Policy for a document whose nonce is <paramref name="nonce"/> (base64): in <c>style-src</c>
    /// with <see cref="StyleNonce"/>, and in <c>script-src</c> and <c>style-src</c> for the development server.
    /// </summary>
    public string ContentSecurityPolicyWithNonce(string nonce) =>
        ReferenceEquals(_marked, _policy) ? _policy : _marked.Replace(NonceMarker, $"'nonce-{nonce}'", StringComparison.Ordinal);

    /// <summary>
    /// The headers every host puts on every response it serves (plan section 3b), in the order hosts send them; hosts
    /// add an exact <c>Content-Type</c> (<see cref="SparContentTypes"/>). <paramref name="nonce"/> is the document's
    /// style nonce, for an HTML document when <see cref="StyleNonce"/> is on.
    /// </summary>
    public KeyValuePair<string, string>[] Headers(string? nonce = null) =>
    [
        new("Content-Security-Policy", nonce is null ? _policy : ContentSecurityPolicyWithNonce(nonce)),
        new("X-Content-Type-Options", "nosniff"),
        new("Referrer-Policy", "no-referrer"),
        new("Cross-Origin-Opener-Policy", "same-origin"),
        new("Cross-Origin-Resource-Policy", "same-origin"),
        new("Permissions-Policy", PermissionsPolicy),
        new("Cache-Control", "no-store"),
    ];

    /// <summary>Whether a channel set to <paramref name="setting"/> reports in this build.</summary>
    public static bool Reports(CspReports setting, bool development) => setting switch
    {
        CspReports.Always => true,
        CspReports.DevelopmentOnly => development,
        _ => false,
    };

    /// <summary>Every directive once, in the fixed order, each with its values in order and without duplicates.</summary>
    private static List<Directive> Compose(
        SparPageSecurity declared,
        string origin,
        IReadOnlyList<string> scriptHashes,
        IReadOnlyList<string> styleHashes,
        bool reportUri,
        string? devServerSocket)
    {
        var script = new Directive("script-src", "'self'");
        script.AddIf(declared.AllowEval, "'unsafe-eval'");
        script.AddIf(declared.AllowWebAssembly, "'wasm-unsafe-eval'");
        script.AddAll(declared.ScriptSrc);
        script.AddAll(scriptHashes);

        // Vite's development page has inline module scripts (its client's preamble), which only its nonce allows.
        script.NonceGoesHere = devServerSocket is not null;

        // Browsers ignore 'unsafe-inline' next to a nonce or a hash, so with inline styles allowed neither is added.
        var style = new Directive("style-src", "'self'");
        style.AddIf(declared.AllowInlineStyles, "'unsafe-inline'");
        style.AddAll(declared.StyleSrc);
        if (!declared.AllowInlineStyles)
        {
            style.NonceGoesHere = declared.StyleNonce || devServerSocket is not null;
            style.AddAll(styleHashes);
        }

        var directives = new List<Directive>
        {
            new("default-src", "'none'"),
            script,
            style,
            new Directive("img-src", "'self'").AddAll(declared.ImgSrc),
            new Directive("font-src", "'self'").AddAll(declared.FontSrc),
            new Directive("connect-src", origin).AddAll(declared.ConnectSrc).AddAll(devServerSocket is null ? [] : [devServerSocket]),
            new Directive("media-src", "'self'").AddAll(declared.MediaSrc),
        };

        // Frames are blocked by default-src 'none' until the app names their sources ('self' included).
        if (declared.FrameSrc.Count > 0)
        {
            directives.Add(new Directive("frame-src").AddAll(declared.FrameSrc));
        }

        directives.Add(new("worker-src", "'self'"));
        directives.Add(new("manifest-src", "'self'"));
        directives.Add(new("base-uri", "'none'"));
        directives.Add(new("form-action", "'none'"));
        directives.Add(new("frame-ancestors", "'none'"));
        directives.Add(new("object-src", "'none'"));
        if (declared.TrustedTypes)
        {
            directives.Add(new("require-trusted-types-for", "'script'"));
        }

        if (reportUri)
        {
            directives.Add(new("report-uri", ReportPath));
        }

        return directives;
    }

    private static string Render(List<Directive> directives, string? nonce)
    {
        var text = new StringBuilder(512);
        foreach (var directive in directives)
        {
            if (text.Length > 0)
            {
                text.Append("; ");
            }

            text.Append(directive.Name);
            foreach (var value in directive.Values)
            {
                text.Append(' ').Append(value);
            }

            if (directive.NonceGoesHere && nonce is not null)
            {
                text.Append(' ').Append(nonce);
            }
        }

        return text.ToString();
    }

    private static string ComposePermissions(Permission[] declared)
    {
        var text = new StringBuilder(256);
        foreach (var (feature, permission) in Declarable)
        {
            Separate(text).Append(feature).Append(PageSecurityNames.Contains(declared, permission) ? "=(self)" : "=()");
        }

        foreach (var feature in AlwaysDenied)
        {
            Separate(text).Append(feature).Append("=()");
        }

        return text.ToString();
    }

    private static StringBuilder Separate(StringBuilder text) => text.Length > 0 ? text.Append(", ") : text;

    /// <summary>One directive and its values, each once, in the order added.</summary>
    private sealed class Directive
    {
        private readonly List<string> _values = [];

        public Directive(string name, params string[] values)
        {
            Name = name;
            AddAll(values);
        }

        public string Name { get; }

        public IReadOnlyList<string> Values => _values;

        /// <summary>Whether the document's style nonce is added after the values (the nonce is unique, never a duplicate).</summary>
        public bool NonceGoesHere { get; set; }

        public Directive AddAll(IReadOnlyList<string> values)
        {
            foreach (var value in values)
            {
                if (!_values.Contains(value, StringComparer.Ordinal))
                {
                    _values.Add(value);
                }
            }

            return this;
        }

        public void AddIf(bool condition, string value)
        {
            if (condition)
            {
                AddAll([value]);
            }
        }
    }
}
