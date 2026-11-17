using Microsoft.Extensions.Logging;
using Spar.Runtime;

namespace Spar.Hosting;

/// <summary>
/// Which page security a host applies, and what it logs about it at start (plan section 3c: "Spar logs every active
/// override at startup"). The host uses the build's copy, read from <c>Program.cs</c> without running it and carried
/// by the generated catalog; a catalog without one (no generator) falls back to the run's own configuration. In a
/// Development build the run's configuration is compared with the build's, and a difference is logged as an error.
/// </summary>
internal static class PageSecuritySetup
{
    private static readonly Action<ILogger, string, Exception?> Overrides = LoggerMessage.Define<string>(
        LogLevel.Information, new EventId(10, "PageSecurityOverrides"), "Page security overrides in this app: {Overrides}");

    public static SparPageSecurity Resolve(SparApp app, ILogger logger)
    {
        var built = app.Catalog.PageSecurity;
        var security = built ?? app.Builder.DeclaredPageSecurity;
        if (SparDevelopment.IsSupported && built is not null)
        {
            PageSecurityMismatch.Check(built, app.Builder.DeclaredPageSecurity, logger);
        }

        if (Describe(security) is { Length: > 0 } overrides)
        {
            Overrides(logger, overrides, null);
        }

        return security;
    }

    /// <summary>The opt-ins away from the strict default, by the C# that declared them; empty when there are none.</summary>
    internal static string Describe(SparPageSecurity security)
    {
        var parts = new List<string>();
        AddSources(parts, "FontSrc", security.FontSrc);
        AddSources(parts, "ImgSrc", security.ImgSrc);
        AddSources(parts, "ConnectSrc", security.ConnectSrc);
        AddSources(parts, "MediaSrc", security.MediaSrc);
        AddSources(parts, "ScriptSrc", security.ScriptSrc);
        AddSources(parts, "StyleSrc", security.StyleSrc);
        AddSources(parts, "FrameSrc", security.FrameSrc);
        AddIf(parts, security.AllowInlineStyles, "AllowInlineStyles()");
        AddIf(parts, security.AllowEval, "AllowEval()");
        AddIf(parts, security.AllowWebAssembly, "AllowWebAssembly()");
        AddIf(parts, security.StyleNonce, "StyleNonce()");
        AddIf(parts, !security.TrustedTypes, "WithoutTrustedTypes()");
        AddIf(parts, !security.Integrity, "WithoutIntegrity()");
        AddIf(parts, security.Listener != CspReports.Always || security.Endpoint != CspReports.Always,
            $"Reporting(listener: {PageSecurityNames.Of(security.Listener)}, endpoint: {PageSecurityNames.Of(security.Endpoint)})");
        foreach (var permission in security.Permissions)
        {
            parts.Add($"Allow(Permission.{PageSecurityNames.Of(permission)})");
        }

        return string.Join(", ", parts);
    }

    private static void AddSources(List<string> parts, string method, IReadOnlyList<string> sources)
    {
        if (sources.Count > 0)
        {
            parts.Add($"{method}({string.Join(", ", sources.Select(source => $"\"{source}\""))})");
        }
    }

    private static void AddIf(List<string> parts, bool condition, string text)
    {
        if (condition)
        {
            parts.Add(text);
        }
    }
}

/// <summary>
/// The Development check that the configuration the app ran equals the one the build read (plan section 3b). Called
/// only behind <see cref="SparDevelopment.IsSupported"/>, so a Production build carries none of it.
/// </summary>
internal static class PageSecurityMismatch
{
    private static readonly Action<ILogger, string, string, Exception?> Differs = LoggerMessage.Define<string, string>(
        LogLevel.Error, new EventId(11, "PageSecurityMismatch"),
        "The page security Program.cs configured at run time differs from the one the build read, which the host uses. " +
        "Keep spar.ContentSecurity(…) and spar.Permissions(…) directly in SparApplication.Create with constant values, and " +
        "build again. Build: {Built}. Run: {Ran}");

    public static void Check(SparPageSecurity built, SparPageSecurity ran, ILogger logger)
    {
        var builtText = built.Describe();
        var ranText = ran.Describe();
        if (!string.Equals(builtText, ranText, StringComparison.Ordinal))
        {
            Differs(logger, builtText, ranText, null);
        }
    }
}
