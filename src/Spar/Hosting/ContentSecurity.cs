using Spar.Hosting;
using Spar.Runtime;

namespace Spar;

/// <summary>
/// The page's Content Security Policy, from <c>spar.ContentSecurity(csp => …)</c> in <c>Program.cs</c> (plan section
/// 3b). The default is strict: everything comes from the app's own origin, inline code and <c>eval</c> are blocked,
/// Trusted Types and Subresource Integrity are on, and violations are reported. Each method widens it on purpose, so
/// the widening shows in the code and its diffs. The build reads these calls without running the app, so the values
/// are constants and the call sits directly in the <c>SparApplication.Create</c> configuration (SPAR101–SPAR104).
/// </summary>
/// <remarks>
/// The directive methods take hosts and schemes only (<c>https://fonts.gstatic.com</c>, <c>*.example.com</c>,
/// <c>https:</c>, <c>data:</c>): keywords such as <c>'unsafe-inline'</c> come only through the named methods below,
/// whose documentation explains the risk. Each method takes several sources and may be called many times; the policy
/// Spar sends lists each directive once, with all of its values, in a stable order and without duplicates.
/// </remarks>
public sealed class CspBuilder
{
    private readonly List<string> _fontSrc = [];
    private readonly List<string> _imgSrc = [];
    private readonly List<string> _connectSrc = [];
    private readonly List<string> _mediaSrc = [];
    private readonly List<string> _scriptSrc = [];
    private readonly List<string> _styleSrc = [];
    private readonly List<string> _frameSrc = [];
    private bool _allowInlineStyles;
    private bool _allowEval;
    private bool _allowWebAssembly;
    private bool _styleNonce;
    private bool _withoutTrustedTypes;
    private bool _withoutIntegrity;
    private CspReports _listener = CspReports.Always;
    private CspReports _endpoint = CspReports.Always;

    internal CspBuilder()
    {
    }

    /// <summary>Fonts from these hosts or schemes (<c>font-src</c>), such as <c>https://fonts.gstatic.com</c>.</summary>
    public CspBuilder FontSrc(params string[] sources) => Add(_fontSrc, sources, CspDirective.Font);

    /// <summary>Images from these hosts or schemes (<c>img-src</c>). <c>data:</c> and <c>blob:</c> are schemes too.</summary>
    public CspBuilder ImgSrc(params string[] sources) => Add(_imgSrc, sources, CspDirective.Image);

    /// <summary>
    /// <c>fetch</c>, WebSockets and event streams to these hosts or schemes (<c>connect-src</c>). The app's own origin is
    /// always there; the backend is reached through Spar's bridge, not through this.
    /// </summary>
    public CspBuilder ConnectSrc(params string[] sources) => Add(_connectSrc, sources, CspDirective.Connect);

    /// <summary>Audio and video from these hosts or schemes (<c>media-src</c>); the app's own files are always allowed.</summary>
    public CspBuilder MediaSrc(params string[] sources) => Add(_mediaSrc, sources, CspDirective.Media);

    /// <summary>
    /// Scripts from these hosts (<c>script-src</c>). A script from another host runs with the page's full access to the
    /// app, so the build requires an <c>integrity</c> hash on each such <c>&lt;script&gt;</c> (unless
    /// <see cref="WithoutIntegrity"/>). Prefer bundling the library into the app.
    /// </summary>
    public CspBuilder ScriptSrc(params string[] sources) => Add(_scriptSrc, sources, CspDirective.Script);

    /// <summary>
    /// Stylesheets from these hosts (<c>style-src</c>). The build requires an <c>integrity</c> hash on each such
    /// stylesheet link (unless <see cref="WithoutIntegrity"/>). Prefer bundling the stylesheet into the app.
    /// </summary>
    public CspBuilder StyleSrc(params string[] sources) => Add(_styleSrc, sources, CspDirective.Style);

    /// <summary>
    /// Frames (<c>&lt;iframe&gt;</c>) from these hosts, and from the app's own origin with <c>'self'</c>
    /// (<c>frame-src</c>); every frame is blocked by default.
    /// <para>
    /// <b>Risk:</b> a same-origin frame (<c>'self'</c>) runs with the app's own access, the bridge to the backend
    /// included, and on Linux the host cannot tell a frame's message from the window's. A frame from another host is
    /// kept on its own origin, but it is still content the app does not control.
    /// <b>Safer:</b> for untrusted content use a <c>sandbox</c> iframe without <c>allow-same-origin</c>.
    /// </para>
    /// </summary>
    public CspBuilder FrameSrc(params string[] sources) => Add(_frameSrc, sources, CspDirective.Frame);

    /// <summary>
    /// Allows inline styles (<c>'unsafe-inline'</c> in <c>style-src</c>): <c>&lt;style&gt;</c> elements and
    /// <c>style</c> attributes from markup and from script.
    /// <para>
    /// <b>Risk:</b> injected markup can then restyle the page, for example to hide or overlay content and trick the user
    /// into an action, or read data out through attribute selectors. Browsers ignore <c>'unsafe-inline'</c> next to a
    /// nonce or a hash, so with this option Spar puts neither in <c>style-src</c>, and <see cref="StyleNonce"/> has no
    /// effect (SPAR105).
    /// <b>Reasonable</b> for a CSS-in-JS library that inserts <c>&lt;style&gt;</c> elements and cannot take a nonce.
    /// <b>Safer:</b> build-time CSS, constructed stylesheets (<c>new CSSStyleSheet()</c>, <c>adoptedStyleSheets</c>) or
    /// <c>element.style.setProperty</c>, which the default allows; or <see cref="StyleNonce"/> for a library that takes a
    /// nonce.
    /// </para>
    /// </summary>
    public CspBuilder AllowInlineStyles()
    {
        _allowInlineStyles = true;
        return this;
    }

    /// <summary>
    /// Allows <c>eval</c>, <c>new Function</c> and string timers (<c>'unsafe-eval'</c> in <c>script-src</c>).
    /// <para>
    /// <b>Risk:</b> any string that reaches one of them runs as the app's code, with the app's access to the backend;
    /// it is the usual way a cross-site scripting flaw becomes code execution.
    /// <b>Reasonable</b> only for a library that cannot work without it, such as a template engine compiling at run time.
    /// <b>Safer:</b> a build that compiles templates ahead of time (for example Vue's runtime-only build), or a library
    /// that does not evaluate strings.
    /// </para>
    /// </summary>
    public CspBuilder AllowEval()
    {
        _allowEval = true;
        return this;
    }

    /// <summary>
    /// Allows WebAssembly (<c>'wasm-unsafe-eval'</c> in <c>script-src</c>); on Linux it also turns the engine's
    /// WebAssembly support on.
    /// <para>
    /// Without it every module is refused, in the page and in its workers, but the global differs: <c>typeof
    /// WebAssembly</c> is <c>"object"</c> on Windows and <c>"undefined"</c> on Linux. Code that adapts to WebAssembly
    /// being unavailable should try to compile a module rather than test for the global.
    /// </para>
    /// <para>
    /// <b>Risk:</b> compiled code the page loads runs with the page's access, and the engine's WebAssembly compiler is
    /// more attack surface. It cannot evaluate JavaScript strings.
    /// <b>Reasonable</b> for a library shipped as WebAssembly, such as a parser or an image codec, bundled with the app.
    /// <b>Safer:</b> do the work in the backend, in C#, through a command.
    /// </para>
    /// </summary>
    public CspBuilder AllowWebAssembly()
    {
        _allowWebAssembly = true;
        return this;
    }

    /// <summary>
    /// Gives each document a fresh style nonce: the host writes it into the served HTML as
    /// <c>&lt;meta property="csp-nonce" nonce="…"&gt;</c> and into <c>style-src</c>, so a CSS-in-JS library that takes
    /// a nonce can add <c>&lt;style nonce="…"&gt;</c> elements. Read it as <c>meta.nonce</c>: the attribute reads empty.
    /// <para>
    /// <b>Risk:</b> script that can read the page's DOM can read the nonce too, so it stops injected markup, not injected
    /// script. <b>Safer:</b> build-time CSS or constructed stylesheets, which need no nonce.
    /// </para>
    /// </summary>
    public CspBuilder StyleNonce()
    {
        _styleNonce = true;
        return this;
    }

    /// <summary>
    /// Leaves out <c>require-trusted-types-for 'script'</c>, which is on by default.
    /// <para>
    /// <b>Risk:</b> strings then reach the DOM's script sinks (<c>innerHTML</c>, <c>script.src</c>,
    /// <c>insertAdjacentHTML</c>) without passing a policy, so an injection flaw in the page or a dependency can run
    /// script.
    /// <b>Reasonable</b> for a library that writes HTML strings and cannot be given a Trusted Types policy.
    /// <b>Safer:</b> create a Trusted Types policy (<c>trustedTypes.createPolicy</c>) that sanitizes what the library
    /// writes, and keep the default.
    /// </para>
    /// </summary>
    public CspBuilder WithoutTrustedTypes()
    {
        _withoutTrustedTypes = true;
        return this;
    }

    /// <summary>
    /// Turns Subresource Integrity off: the Release bundle's scripts, stylesheets and lazy chunks get no
    /// <c>integrity</c> hashes, and the build no longer requires one on external scripts and stylesheets.
    /// <para>
    /// <b>Risk:</b> a script or stylesheet from another host can then change under the app, with the page's full access;
    /// for the app's own files a serving or packing fault goes unnoticed.
    /// <b>Reasonable</b> for an external script whose content changes by design and cannot be pinned.
    /// <b>Safer:</b> bundle or self-host the resource, or pin its version and give its hash.
    /// </para>
    /// </summary>
    public CspBuilder WithoutIntegrity()
    {
        _withoutIntegrity = true;
        return this;
    }

    /// <summary>
    /// Where violations are reported. Both channels are on in every build by default: the
    /// <paramref name="listener"/> (a script the host adds to each document, which groups and batches them) and the
    /// <paramref name="endpoint"/> (<c>report-uri</c>, the backstop for what the listener cannot see, such as workers).
    /// The host removes duplicates and logs grouped summaries, and in Development each new group with the directive to
    /// add. <see cref="CspReports.DevelopmentOnly"/> keeps a channel in Development and Test builds only.
    /// </summary>
    public CspBuilder Reporting(CspReports listener = CspReports.Always, CspReports endpoint = CspReports.Always)
    {
        _listener = CheckReports(listener, nameof(listener));
        _endpoint = CheckReports(endpoint, nameof(endpoint));
        return this;
    }

    /// <summary>What this builder declares, in the form the generated catalog carries.</summary>
    internal SparPageSecurity Build(Permission[] permissions) => new()
    {
        FontSrc = [.. _fontSrc],
        ImgSrc = [.. _imgSrc],
        ConnectSrc = [.. _connectSrc],
        MediaSrc = [.. _mediaSrc],
        ScriptSrc = [.. _scriptSrc],
        StyleSrc = [.. _styleSrc],
        FrameSrc = [.. _frameSrc],
        AllowInlineStyles = _allowInlineStyles,
        AllowEval = _allowEval,
        AllowWebAssembly = _allowWebAssembly,
        StyleNonce = _styleNonce,
        TrustedTypes = !_withoutTrustedTypes,
        Integrity = !_withoutIntegrity,
        Listener = _listener,
        Endpoint = _endpoint,
        Permissions = permissions,
    };

    private static CspReports CheckReports(CspReports value, string name) =>
        value is CspReports.Always or CspReports.DevelopmentOnly or CspReports.Off
            ? value
            : throw new ArgumentOutOfRangeException(name, (int)value, "Use a CspReports member.");

    /// <summary>Checks and normalizes each source as the build does, and keeps the first of each.</summary>
    private CspBuilder Add(List<string> into, string[] sources, CspDirective directive)
    {
        ArgumentNullException.ThrowIfNull(sources);
        foreach (var source in sources)
        {
            var checkedSource = CspSourceRule.Check(source, directive);
            if (checkedSource.Problem is { } problem)
            {
                throw new ArgumentException(problem, nameof(sources));
            }

            if (!into.Contains(checkedSource.Normalized!, StringComparer.Ordinal))
            {
                into.Add(checkedSource.Normalized!);
            }
        }

        return this;
    }
}

/// <summary>When a channel reports Content Security Policy violations (<see cref="CspBuilder.Reporting"/>).</summary>
public enum CspReports
{
    /// <summary>In every build, Production included: a violation there is a bug or an injection attempt.</summary>
    Always,

    /// <summary>In Development and Test builds only.</summary>
    DevelopmentOnly,

    /// <summary>Never.</summary>
    Off,
}
