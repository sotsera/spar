using Spar.Hosting;
using Spar.Runtime;

namespace Spar.Tests;

/// <summary>
/// The policy every host sends (plan section 3b): one valid policy, each directive once with all its values grouped, in
/// a stable order and without duplicates, for every combination of what the app declares (decision "Page security's
/// public shape").
/// </summary>
public sealed class PagePolicyTests
{
    private const string Origin = "https://app.localhost";

    private const string Base =
        "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self'; font-src 'self'; " +
        "connect-src https://app.localhost; media-src 'self'; worker-src 'self'; manifest-src 'self'; base-uri 'none'; " +
        "form-action 'none'; frame-ancestors 'none'; object-src 'none'; require-trusted-types-for 'script'; " +
        "report-uri /_spar/csp-report";

    [Fact]
    public void Nothing_declared_is_the_strict_base_policy()
    {
        var policy = new PagePolicy(SparPageSecurity.Default, Origin, development: false);

        Assert.Equal(Base, policy.ContentSecurityPolicy);
        Assert.True(policy.ListenerReports);
        Assert.True(policy.EndpointReports);
        Assert.False(policy.StyleNonce);
    }

    [Fact]
    public void Several_sources_over_several_calls_go_into_one_directive_in_order_without_duplicates()
    {
        var csp = Builder();
        csp.FontSrc("https://fonts.gstatic.com", "https://cdn.example.com")
            .FontSrc("https://FONTS.gstatic.com")
            .ImgSrc("data:", "https://images.example.com")
            .FontSrc("https://fonts.example.org", "https://cdn.example.com");

        var policy = Policy(csp);

        Assert.Equal(["https://fonts.gstatic.com", "https://cdn.example.com", "https://fonts.example.org"], csp.Build([]).FontSrc);
        Assert.Contains("; font-src 'self' https://fonts.gstatic.com https://cdn.example.com https://fonts.example.org; ", policy);
        Assert.Contains("; img-src 'self' data: https://images.example.com; ", policy);
        AssertOneOfEach(policy);
    }

    [Fact]
    public void Every_directive_appears_once_whatever_is_declared()
    {
        var csp = Builder();
        csp.FontSrc("https://f.example").ImgSrc("https://i.example").ConnectSrc("wss://w.example").MediaSrc("https://m.example")
            .ScriptSrc("https://s.example").StyleSrc("https://c.example").FrameSrc("'self'", "https://frames.example")
            .AllowEval().AllowWebAssembly().StyleNonce().WithoutIntegrity()
            .FontSrc("https://f.example", "https://f2.example");

        var policy = new PagePolicy(csp.Build([]), Origin, development: false, ["'sha256-map'", "'sha256-map'"], ["'sha256-theme'"]);

        AssertOneOfEach(policy.ContentSecurityPolicy);
        Assert.Equal(
            "default-src 'none'; script-src 'self' 'unsafe-eval' 'wasm-unsafe-eval' https://s.example 'sha256-map'; " +
            "style-src 'self' https://c.example 'sha256-theme'; img-src 'self' https://i.example; " +
            "font-src 'self' https://f.example https://f2.example; connect-src https://app.localhost wss://w.example; " +
            "media-src 'self' https://m.example; frame-src 'self' https://frames.example; worker-src 'self'; " +
            "manifest-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'; object-src 'none'; " +
            "require-trusted-types-for 'script'; report-uri /_spar/csp-report",
            policy.ContentSecurityPolicy);
    }

    [Fact]
    public void The_style_nonce_goes_into_style_src_after_its_other_values()
    {
        var csp = Builder();
        csp.StyleNonce().StyleSrc("https://c.example");

        var policy = new PagePolicy(csp.Build([]), Origin, development: false, styleHashes: ["'sha256-theme'"]);

        Assert.True(policy.StyleNonce);
        Assert.Contains("; style-src 'self' https://c.example 'sha256-theme'; ", policy.ContentSecurityPolicy);
        var withNonce = policy.ContentSecurityPolicyWithNonce("abc+/def");
        Assert.Contains("; style-src 'self' https://c.example 'sha256-theme' 'nonce-abc+/def'; img-src", withNonce);
        AssertOneOfEach(withNonce);
        Assert.Equal(withNonce.Replace(" 'nonce-abc+/def'", "", StringComparison.Ordinal), policy.ContentSecurityPolicy);
    }

    [Fact]
    public void Inline_styles_leave_out_every_nonce_and_hash_of_style_src_which_browsers_would_ignore_them_for()
    {
        var csp = Builder();
        csp.StyleNonce().AllowInlineStyles();

        var policy = new PagePolicy(csp.Build([]), Origin, development: false, styleHashes: ["'sha256-theme'"]);

        Assert.False(policy.StyleNonce);
        Assert.Contains("; style-src 'self' 'unsafe-inline'; ", policy.ContentSecurityPolicy);
        Assert.Equal(policy.ContentSecurityPolicy, policy.ContentSecurityPolicyWithNonce("abc"));
    }

    [Fact]
    public void Without_trusted_types_the_directive_is_gone()
    {
        var csp = Builder();
        csp.WithoutTrustedTypes();

        Assert.Equal(Base.Replace("require-trusted-types-for 'script'; ", "", StringComparison.Ordinal), Policy(csp));
    }

    [Theory]
    [InlineData(CspReports.Always, false, true)]
    [InlineData(CspReports.Always, true, true)]
    [InlineData(CspReports.DevelopmentOnly, false, false)]
    [InlineData(CspReports.DevelopmentOnly, true, true)]
    [InlineData(CspReports.Off, true, false)]
    public void Reporting_decides_each_channel_and_report_uri_follows_the_endpoint(CspReports setting, bool development, bool reports)
    {
        var listener = Builder();
        listener.Reporting(listener: setting);
        var endpoint = Builder();
        endpoint.Reporting(endpoint: setting);

        var byListener = new PagePolicy(listener.Build([]), Origin, development);
        var byEndpoint = new PagePolicy(endpoint.Build([]), Origin, development);

        Assert.Equal((reports, true), (byListener.ListenerReports, byListener.EndpointReports));
        Assert.Equal((true, reports), (byEndpoint.ListenerReports, byEndpoint.EndpointReports));
        Assert.Equal(reports, byEndpoint.ContentSecurityPolicy.Contains("report-uri", StringComparison.Ordinal));
        Assert.EndsWith(reports ? "report-uri /_spar/csp-report" : "require-trusted-types-for 'script'", byEndpoint.ContentSecurityPolicy, StringComparison.Ordinal);
    }

    [Fact]
    public void Frames_stay_blocked_by_default_src_until_declared()
    {
        Assert.DoesNotContain("frame-src", Base, StringComparison.Ordinal);

        var csp = Builder();
        csp.FrameSrc("'self'");

        Assert.Contains("; media-src 'self'; frame-src 'self'; worker-src 'self'; ", Policy(csp));
    }

    [Fact]
    public void Permissions_policy_denies_everything_but_the_declared_features()
    {
        var none = new PagePolicy(SparPageSecurity.Default, Origin, development: false);
        var declared = new PagePolicy(new SparPageSecurity { Permissions = [Permission.ClipboardRead, Permission.Sensors] }, Origin, development: false);

        Assert.Equal(
            "camera=(), microphone=(), geolocation=(), clipboard-read=(), accelerometer=(), gyroscope=(), magnetometer=(), " +
            "local-fonts=(), midi=(), window-management=(), autoplay=(), usb=(), serial=(), hid=(), bluetooth=(), " +
            "payment=(), display-capture=()",
            none.PermissionsPolicy);
        Assert.Contains("clipboard-read=(self), accelerometer=(self), gyroscope=(self), magnetometer=(self), local-fonts=()", declared.PermissionsPolicy);
        Assert.True(declared.Allows(Permission.ClipboardRead));
        Assert.False(declared.Allows(Permission.Camera));
    }

    [Fact]
    public void Every_permission_has_a_feature_in_the_header()
    {
        foreach (var permission in Enum.GetValues<Permission>())
        {
            var policy = new PagePolicy(new SparPageSecurity { Permissions = [permission] }, Origin, development: false);
            Assert.Contains("=(self)", policy.PermissionsPolicy, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_headers_are_the_fixed_set_with_the_documents_own_policy()
    {
        var csp = Builder();
        csp.StyleNonce();
        var policy = new PagePolicy(csp.Build([]), Origin, development: false);

        var headers = policy.Headers("n0nce");

        Assert.Equal(
            ["Content-Security-Policy", "X-Content-Type-Options", "Referrer-Policy", "Cross-Origin-Opener-Policy",
             "Cross-Origin-Resource-Policy", "Permissions-Policy", "Cache-Control"],
            headers.Select(header => header.Key));
        Assert.Contains("'nonce-n0nce'", headers[0].Value, StringComparison.Ordinal);
        Assert.Equal("no-store", headers[^1].Value);
        Assert.DoesNotContain("nonce", policy.Headers()[0].Value, StringComparison.Ordinal);
    }

    [Fact]
    public void The_connect_src_names_the_hosts_own_origin()
    {
        var policy = new PagePolicy(SparPageSecurity.Default, "http://127.0.0.1:5123", development: true);

        Assert.Contains("; connect-src http://127.0.0.1:5123; ", policy.ContentSecurityPolicy);
    }

    /// <summary>A fresh builder, as <c>spar.ContentSecurity</c> hands it to the app's lambda.</summary>
    private static CspBuilder Builder() => new SparBuilder([]).ContentSecurityOptions;

    private static string Policy(CspBuilder csp) => new PagePolicy(csp.Build([]), Origin, development: false).ContentSecurityPolicy;

    /// <summary>Each directive once, none empty: the property the user asked for.</summary>
    private static void AssertOneOfEach(string policy)
    {
        var names = policy.Split("; ").Select(directive => directive.Split(' ')).ToList();
        Assert.All(names, parts => Assert.True(parts.Length > 1, $"'{parts[0]}' has no value"));
        Assert.Equal(names.Count, names.Select(parts => parts[0]).Distinct(StringComparer.Ordinal).Count());
        Assert.All(names, parts => Assert.Equal(parts.Length - 1, parts.Skip(1).Distinct(StringComparer.Ordinal).Count()));
    }
}
