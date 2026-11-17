namespace Spar.Hosting;

/// <summary>
/// What a Development log line says about a violation: the C# in <c>spar.ContentSecurity(…)</c> that would allow it,
/// or why nothing should (plan section 3b, "Violations become diagnostics … with the exact directive to add").
/// </summary>
internal static class CspAdvice
{
    public static string For(CspViolation violation)
    {
        var directive = violation.Directive;
        var blocked = violation.Blocked;
        var source = SourceOf(blocked);
        return BaseDirective(directive) switch
        {
            "script-src" when blocked == "eval" => "Allow it with csp.AllowEval(), after reading its risk; better, avoid eval.",
            "script-src" when blocked == "wasm-eval" => "Allow it with csp.AllowWebAssembly(), after reading its risk.",
            "script-src" when blocked == "inline" =>
                "Inline scripts and event handler attributes stay blocked: move the code into the app's bundle.",
            "script-src" when source is not null =>
                $"Allow it with csp.ScriptSrc(\"{source}\"), and give the <script> an integrity hash; better, bundle it.",
            "style-src" when blocked == "inline" =>
                "Use build-time CSS or constructed stylesheets; csp.StyleNonce() serves a library that takes a nonce, and " +
                "csp.AllowInlineStyles() allows every inline style.",
            "style-src" when source is not null =>
                $"Allow it with csp.StyleSrc(\"{source}\"), and give the stylesheet link an integrity hash.",
            "img-src" when source is not null => $"Allow it with csp.ImgSrc(\"{source}\").",
            "font-src" when source is not null => $"Allow it with csp.FontSrc(\"{source}\").",
            "connect-src" when source is not null => $"Allow it with csp.ConnectSrc(\"{source}\").",
            "media-src" when source is not null => $"Allow it with csp.MediaSrc(\"{source}\").",
            "frame-src" or "child-src" when source is not null =>
                $"Allow it with csp.FrameSrc(\"{source}\"), after reading its risk; for untrusted content use a sandbox iframe.",
            "require-trusted-types-for" =>
                "Pass the value through a Trusted Types policy (trustedTypes.createPolicy); csp.WithoutTrustedTypes() " +
                "turns the check off.",
            "worker-src" =>
                "Serve the worker as a file of the app: new Worker(new URL(\"./worker.js\", import.meta.url), { type: \"module\" }).",
            _ => $"Spar keeps {directive} closed; there is no option to widen it.",
        };
    }

    /// <summary><c>script-src-elem</c> and <c>script-src-attr</c> are <c>script-src</c>'s parts; the same for styles.</summary>
    private static string BaseDirective(string directive) => directive switch
    {
        "script-src-elem" or "script-src-attr" => "script-src",
        "style-src-elem" or "style-src-attr" => "style-src",
        _ => directive,
    };

    /// <summary>
    /// The source to name for a blocked address: its scheme for <c>data</c> and <c>blob</c> (which engines report without
    /// the address), and <c>scheme://host[:port]</c> for an address; null for keywords such as <c>inline</c>.
    /// </summary>
    private static string? SourceOf(string blocked)
    {
        if (blocked is "data" or "blob")
        {
            return blocked + ":";
        }

        var separator = blocked.IndexOf("://", StringComparison.Ordinal);
        if (separator <= 0)
        {
            return blocked.StartsWith("data:", StringComparison.Ordinal) ? "data:"
                : blocked.StartsWith("blob:", StringComparison.Ordinal) ? "blob:"
                : null;
        }

        var end = blocked.IndexOfAny(['/', '?', '#'], separator + 3);
        var origin = end < 0 ? blocked : blocked[..end];
        var credentials = origin.IndexOf('@', separator + 3);
        return credentials < 0 ? origin : string.Concat(origin.AsSpan(0, separator + 3), origin.AsSpan(credentials + 1));
    }
}
