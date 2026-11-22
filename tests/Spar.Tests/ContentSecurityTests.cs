using System.Security.Cryptography;
using System.Text;
using Spar.Hosting;
using Spar.Runtime;

namespace Spar.Tests;

/// <summary>
/// The C# side of page security that runs without a window (plan section 3b): the source rule the build and the
/// builder share, the builders, the Development comparison, the HTML the hosts read, the external-link rule and the
/// permission kinds.
/// </summary>
public sealed class ContentSecurityTests
{
    [Theory]
    [InlineData("https://fonts.gstatic.com", "https://fonts.gstatic.com")]
    [InlineData("HTTPS://Fonts.GStatic.com/Path/", "https://fonts.gstatic.com/Path/")]
    [InlineData("*.example.com", "*.example.com")]
    [InlineData("https://*.example.com:8443", "https://*.example.com:8443")]
    [InlineData("example.com:*", "example.com:*")]
    [InlineData("data:", "data:")]
    [InlineData("WSS:", "wss:")]
    [InlineData("192.168.1.10:9000", "192.168.1.10:9000")]
    public void Hosts_and_schemes_are_accepted_and_normalized(string source, string normalized)
    {
        var check = CspSourceRule.Check(source, CspDirective.Font);

        Assert.Equal((normalized, (string?)null), (check.Normalized, check.Problem));
    }

    [Theory]
    [InlineData("'unsafe-inline'", CspDirective.Style, "use AllowInlineStyles()")]
    [InlineData("'unsafe-inline'", CspDirective.Script, "Inline scripts are blocked")]
    [InlineData("'unsafe-eval'", CspDirective.Script, "use AllowEval()")]
    [InlineData("'wasm-unsafe-eval'", CspDirective.Script, "use AllowWebAssembly()")]
    [InlineData("'nonce-abc'", CspDirective.Style, "use StyleNonce()")]
    [InlineData("'nonce-abc'", CspDirective.Script, "Spar adds the nonces")]
    [InlineData("'sha256-abc='", CspDirective.Script, "integrity attribute")]
    [InlineData("'self'", CspDirective.Image, "already in every directive")]
    [InlineData("'none'", CspDirective.Image, "would block everything")]
    [InlineData("'strict-dynamic'", CspDirective.Script, "is a keyword")]
    [InlineData("*", CspDirective.Image, "allows every host")]
    [InlineData("data:", CspDirective.Script, "as eval would")]
    [InlineData("blob:", CspDirective.Script, "as eval would")]
    [InlineData("javascript:", CspDirective.Image, "inline code")]
    [InlineData("https://a.example https://b.example", CspDirective.Font, "not one source")]
    [InlineData("https://a.example;script-src *", CspDirective.Font, "not one source")]
    [InlineData("https://user:pass@a.example", CspDirective.Font, "credentials")]
    [InlineData("https://", CspDirective.Font, "not a host or scheme")]
    [InlineData("-bad.example", CspDirective.Font, "not a host or scheme")]
    [InlineData("a.example:99999999", CspDirective.Font, "not a host or scheme")]
    [InlineData("", CspDirective.Font, "empty source")]
    public void Keywords_and_malformed_sources_are_refused_with_the_method_to_use(string source, object directiveValue, string message)
    {
        var directive = (CspDirective)directiveValue;
        var check = CspSourceRule.Check(source, directive);

        Assert.Null(check.Normalized);
        Assert.Contains(message, check.Problem, StringComparison.Ordinal);
        Assert.StartsWith(CspSourceRule.MethodName(directive), check.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Self_is_a_source_for_frames_only()
    {
        Assert.Equal("'self'", CspSourceRule.Check("'SELF'", CspDirective.Frame).Normalized);
        Assert.Null(CspSourceRule.Check("'self'", CspDirective.Media).Normalized);
    }

    [Fact]
    public void The_builder_refuses_what_the_build_refuses_and_records_each_option()
    {
        var csp = new SparBuilder([]).ContentSecurityOptions;

        var refused = Assert.Throws<ArgumentException>(() => csp.StyleSrc("https://ok.example", "'unsafe-inline'"));
        csp.AllowInlineStyles().AllowEval().AllowWebAssembly().StyleNonce().WithoutTrustedTypes().WithoutIntegrity()
            .Reporting(listener: CspReports.DevelopmentOnly, endpoint: CspReports.Off);
        var built = csp.Build([Permission.Camera]);

        Assert.Contains("AllowInlineStyles()", refused.Message, StringComparison.Ordinal);
        Assert.Equal(["https://ok.example"], built.StyleSrc);
        Assert.Equal(
            (true, true, true, true, false, false, CspReports.DevelopmentOnly, CspReports.Off),
            (built.AllowInlineStyles, built.AllowEval, built.AllowWebAssembly, built.StyleNonce, built.TrustedTypes, built.Integrity, built.Listener, built.Endpoint));
        Assert.Equal([Permission.Camera], built.Permissions);
        Assert.Throws<ArgumentOutOfRangeException>(() => csp.Reporting(listener: (CspReports)7));
    }

    [Fact]
    public void Permissions_are_each_declared_once_in_the_enums_order()
    {
        var builder = new SparBuilder([]);
        builder.Permissions(p => p.Allow(Permission.ClipboardRead).Allow(Permission.Camera))
            .Permissions(p => p.Allow(Permission.ClipboardRead));

        Assert.Equal([Permission.Camera, Permission.ClipboardRead], builder.DeclaredPageSecurity.Permissions);
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.PermissionOptions.Allow((Permission)99));
    }

    [Fact]
    public void Content_security_adds_up_over_several_calls()
    {
        var builder = new SparBuilder([]);
        builder.ContentSecurity(csp => csp.FontSrc("https://a.example"))
            .ContentSecurity(csp => csp.FontSrc("https://b.example").FontSrc("https://a.example"));

        Assert.Equal(["https://a.example", "https://b.example"], builder.DeclaredPageSecurity.FontSrc);
    }

    [Fact]
    public void The_description_names_every_value_so_two_declarations_compare_by_it()
    {
        var one = new SparPageSecurity { FontSrc = ["https://a.example"], Permissions = [Permission.Midi] };
        var same = new SparPageSecurity { FontSrc = ["https://a.example"], Permissions = [Permission.Midi] };
        var other = new SparPageSecurity { FontSrc = ["https://a.example"], Permissions = [Permission.Midi], StyleNonce = true };

        Assert.Equal(one.Describe(), same.Describe());
        Assert.NotEqual(one.Describe(), other.Describe());
        Assert.Equal(
            "inline-styles=no; eval=no; webassembly=no; style-nonce=no; trusted-types=yes; integrity=yes; listener=Always; endpoint=Always; permissions=",
            SparPageSecurity.Default.Describe());
    }

    [Fact]
    public void The_Development_check_logs_an_error_when_the_run_differs_from_the_build()
    {
        var log = new CapturedLog();
        var built = new SparPageSecurity { FontSrc = ["https://a.example"] };

        PageSecurityMismatch.Check(built, new SparPageSecurity { FontSrc = ["https://a.example"] }, log);
        PageSecurityMismatch.Check(built, new SparPageSecurity { FontSrc = ["https://b.example"] }, log);

        var entry = Assert.Single(log.Entries);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Error, entry.Level);
        Assert.Contains("differs from the one the build read", entry.Message, StringComparison.Ordinal);
        Assert.Contains("font-src https://b.example", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_overrides_the_host_logs_at_start_name_the_csharp()
    {
        var security = new SparPageSecurity
        {
            FontSrc = ["https://fonts.gstatic.com"],
            AllowEval = true,
            TrustedTypes = false,
            Endpoint = CspReports.Off,
            Permissions = [Permission.ClipboardRead],
        };

        Assert.Equal("", PageSecuritySetup.Describe(SparPageSecurity.Default));
        Assert.Equal(
            "FontSrc(\"https://fonts.gstatic.com\"), AllowEval(), WithoutTrustedTypes(), Reporting(listener: Always, endpoint: Off), Allow(Permission.ClipboardRead)",
            PageSecuritySetup.Describe(security));
    }

    [Fact]
    public void The_import_map_hash_is_the_sha256_of_its_text_exactly_as_served()
    {
        const string Map = "{\"imports\":{},\"integrity\":{\"/assets/lazy.js\":\"sha384-x\"}}";
        var html = Encoding.UTF8.GetBytes($"<head><script type=\"importmap\">{Map}</script><script type=\"module\">x</script></head>");

        var hashes = PageHtml.ImportMapHashes(html);

        Assert.Equal([$"'sha256-{Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(Map)))}'"], hashes);
        Assert.Empty(PageHtml.ImportMapHashes("<script>inline()</script><script type=importmap>{}</script>"u8));
    }

    [Fact]
    public void The_theme_and_splash_style_hashes_are_of_the_plugins_own_styles_only_exactly_as_served()
    {
        const string Theme = ":where(html){color-scheme:light dark;background-color:#ffffff}";
        const string Splash = "#spar-splash{position:fixed}";
        var html = Encoding.UTF8.GetBytes(
            $"<head><style id=\"spar-theme\">{Theme}</style><style id=\"spar-splash-style\">{Splash}</style>" +
            "<style>body{}</style><style id=\"app\">p{}</style><style id=spar-theme>x{}</style></head>");

        var hashes = PageHtml.StyleHashes(html);

        Assert.Equal([Hash(Theme), Hash(Splash)], hashes);
        Assert.Empty(PageHtml.StyleHashes("<style>body{}</style><style nonce=\"n\" id=\"spar-theme\">x{}</style>"u8));

        static string Hash(string text) => $"'sha256-{Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(text)))}'";
    }

    [Theory]
    [InlineData("<!doctype html><html><head><meta charset=\"utf-8\"></head>", 27)]
    [InlineData("<html><HEAD lang=\"en\">x", 22)]
    [InlineData("<html><header></header><head>", 29)]
    [InlineData("<p>no head</p>", 0)]
    public void The_nonce_goes_just_after_the_head_tag(string html, int at)
    {
        Assert.Equal(at, PageHtml.NonceInsertionPoint(Encoding.UTF8.GetBytes(html)));
    }

    [Fact]
    public void A_nonce_is_fresh_base64_that_needs_no_escaping()
    {
        var first = PageHtml.NewNonce();

        Assert.NotEqual(first, PageHtml.NewNonce());
        Assert.Equal(24, first.Length);
        Assert.Equal("<meta property=\"csp-nonce\" nonce=\"abc\">", Encoding.UTF8.GetString(PageHtml.NonceMeta("abc")));
    }

    [Fact]
    public void The_report_endpoint_lets_a_burst_through_then_a_few_a_second()
    {
        var limiter = new ReportLimiter();
        var second = System.Diagnostics.Stopwatch.Frequency;
        var start = 1_000L;

        var burst = Enumerable.Range(0, 100).Count(_ => limiter.TryTake(start));
        var oneSecondLater = Enumerable.Range(0, 100).Count(_ => limiter.TryTake(start + second));
        var muchLater = Enumerable.Range(0, 100).Count(_ => limiter.TryTake(start + (100 * second)));

        Assert.Equal((ReportLimiter.Burst, ReportLimiter.PerSecond, ReportLimiter.Burst), (burst, oneSecondLater, muchLater));
    }
}
