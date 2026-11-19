namespace Spar.Generator.Tests;

/// <summary>
/// The page security <c>Program.cs</c> declares, as the build reads it without running code (plan section 3b): the
/// generated catalog, and SPAR101–SPAR105 at their locations.
/// </summary>
public sealed class PageSecurityTests
{
    private const string Usings = """
        using System;
        using Spar;

        """;

    [Fact]
    public async Task Constants_directly_in_Create_are_read_into_the_catalog()
    {
        const string source = Usings + """
            const string Fonts = "https://fonts.gstatic.com";

            var app = SparApplication.Create(args, spar =>
            {
                spar.ContentSecurity(csp => csp
                    .FontSrc(Fonts, "https://cdn.example.com")
                    .FontSrc("https://FONTS.gstatic.com", "https://fonts.example.org")
                    .ImgSrc("data:")
                    .FrameSrc("'self'")
                    .AllowEval()
                    .StyleNonce()
                    .WithoutTrustedTypes()
                    .Reporting(endpoint: CspReports.DevelopmentOnly));
                spar.ContentSecurity(csp =>
                {
                    csp.ConnectSrc("wss://live.example.com");
                    csp.Reporting(CspReports.Off);
                });
                spar.Permissions(p => p.Allow(Permission.ClipboardRead).Allow(Permission.Camera).Allow(Permission.ClipboardRead));
            });
            return app.Run();
            """;

        Assert.Empty(await GeneratorHarness.AnalyzeAsync(source));
        var run = GeneratorHarness.Generate(source);

        Assert.Empty(run.Errors);
        Assert.Contains("""
            PageSecurity = new global::Spar.Runtime.SparPageSecurity
            {
            FontSrc = ["https://fonts.gstatic.com", "https://cdn.example.com", "https://fonts.example.org"],
            ImgSrc = ["data:"],
            ConnectSrc = ["wss://live.example.com"],
            FrameSrc = ["'self'"],
            AllowEval = true,
            StyleNonce = true,
            TrustedTypes = false,
            Listener = global::Spar.CspReports.Off,
            Endpoint = global::Spar.CspReports.DevelopmentOnly,
            Permissions = [global::Spar.Permission.Camera, global::Spar.Permission.ClipboardRead],
            },
            """, Unindented(run.Files["Spar.Contracts.g.cs"]), StringComparison.Ordinal);
    }

    [Fact]
    public void An_app_that_widens_nothing_gets_the_strict_default()
    {
        var run = GeneratorHarness.Generate(Usings + "var app = SparApplication.Create(args, _ => { });\nreturn app.Run();\n");

        Assert.Empty(run.Errors);
        Assert.Contains("\nPageSecurity = new global::Spar.Runtime.SparPageSecurity(),\n", Unindented(run.Files["Spar.Contracts.g.cs"]), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Calls_the_build_cannot_read_where_they_sit_are_SPAR101()
    {
        const string source = Usings + """
            var app = SparApplication.Create(args, spar =>
            {
                if (args.Length > 0)
                {
                    spar.ContentSecurity(csp => csp.FontSrc("https://a.example"));
                }

                Configure(spar);
                spar.Window(w => w.Size(800, 600)).Permissions(p => p.Allow(Permission.Midi));
            });
            return app.Run();

            static void Configure(SparBuilder builder) => builder.Permissions(p => p.Allow(Permission.Camera));
            """;

        Assert.Equal(["SPAR101: ContentSecurity", "SPAR101: Permissions"], await GeneratorHarness.AnalyzeAsync(source));
    }

    [Fact]
    public async Task A_delegate_variable_or_a_method_group_is_SPAR103()
    {
        const string source = Usings + """
            Action<CspBuilder> policy = csp => csp.FontSrc("https://a.example");
            var app = SparApplication.Create(args, spar =>
            {
                spar.ContentSecurity(policy);
                spar.Permissions(Allow);
            });
            return app.Run();

            static void Allow(PermissionsBuilder permissions) => permissions.Allow(Permission.Camera);
            """;

        Assert.Equal(["SPAR103: policy", "SPAR103: Allow"], await GeneratorHarness.AnalyzeAsync(source));
    }

    [Fact]
    public async Task Other_statements_and_calls_in_the_lambda_are_SPAR103()
    {
        const string source = Usings + """
            var app = SparApplication.Create(args, spar =>
            {
                spar.ContentSecurity(csp =>
                {
                    var host = "https://a.example";
                    csp.FontSrc("https://b.example").ToString();
                    if (host.Length > 0) csp.AllowEval();
                });
            });
            return app.Run();
            """;

        Assert.Equal(
            [
                "SPAR103: var host = \"https://a.example\";",
                "SPAR103: csp.FontSrc(\"https://b.example\").ToString()",
                "SPAR103: if (host.Length > 0) csp.AllowEval();",
            ],
            await GeneratorHarness.AnalyzeAsync(source));
    }

    [Fact]
    public async Task Values_that_are_not_constants_are_SPAR102()
    {
        const string source = Usings + """
            var host = Environment.GetEnvironmentVariable("FONTS") ?? "";
            var reports = CspReports.Off;
            var permission = Permission.Camera;
            var app = SparApplication.Create(args, spar =>
            {
                spar.ContentSecurity(csp => csp.FontSrc(host, $"https://{host}").FontSrc(["https://a.example"]).Reporting(reports));
                spar.Permissions(p => p.Allow(permission));
            });
            return app.Run();
            """;

        Assert.Equal(
            [
                "SPAR102: host",
                "SPAR102: $\"https://{host}\"",
                "SPAR102: [\"https://a.example\"]",
                "SPAR102: reports",
                "SPAR102: permission",
            ],
            await GeneratorHarness.AnalyzeAsync(source));
    }

    [Fact]
    public async Task Keywords_nonces_and_malformed_sources_are_SPAR104_naming_the_method_to_use()
    {
        const string source = Usings + """
            var app = SparApplication.Create(args, spar =>
            {
                spar.ContentSecurity(csp => csp
                    .StyleSrc("'unsafe-inline'", "'nonce-abc'")
                    .ScriptSrc("'unsafe-eval'", "'wasm-unsafe-eval'", "data:")
                    .ImgSrc("'self'", "*", "https://a.example b")
                    .FrameSrc("'self'"));
                spar.Permissions(p => p.Allow((Permission)42));
            });
            return app.Run();
            """;

        var messages = await GeneratorHarness.MessagesAsync(source);

        Assert.Equal(
            [
                "SPAR104: StyleSrc: use AllowInlineStyles() instead of ''unsafe-inline''; its documentation explains the risk.",
                "SPAR104: StyleSrc: use StyleNonce() instead of a fixed nonce; Spar gives each document a fresh one.",
                "SPAR104: ScriptSrc: use AllowEval() instead of ''unsafe-eval''; its documentation explains the risk.",
                "SPAR104: ScriptSrc: use AllowWebAssembly() instead of ''wasm-unsafe-eval''; its documentation explains the risk.",
                "SPAR104: ScriptSrc: 'data:' lets the page run script it builds itself, as eval would. Bundle the code instead.",
                "SPAR104: ImgSrc: the app's own origin is already in every directive Spar sends; remove ''self''.",
                "SPAR104: ImgSrc: '*' allows every host. Name the hosts the page needs.",
                "SPAR104: ImgSrc: 'https://a.example b' is not one source. Pass each host or scheme as its own argument, without spaces, quotes, ';' or ','.",
                "SPAR104: '(Permission)42' is not a Permission member: write one by name, such as Permission.ClipboardRead.",
            ],
            messages);
        Assert.Equal(
            ["SPAR104: \"'unsafe-inline'\"", "SPAR104: \"'nonce-abc'\"", "SPAR104: \"'unsafe-eval'\"", "SPAR104: \"'wasm-unsafe-eval'\"",
             "SPAR104: \"data:\"", "SPAR104: \"'self'\"", "SPAR104: \"*\"", "SPAR104: \"https://a.example b\"", "SPAR104: (Permission)42"],
            await GeneratorHarness.AnalyzeAsync(source));
    }

    [Fact]
    public async Task A_nonce_with_inline_styles_is_the_SPAR105_warning_on_StyleNonce()
    {
        const string source = Usings + """
            var app = SparApplication.Create(args, spar => spar.ContentSecurity(csp => csp.AllowInlineStyles().StyleNonce()));
            return app.Run();
            """;

        Assert.Equal(["SPAR105: StyleNonce"], await GeneratorHarness.AnalyzeAsync(source));
        Assert.Empty(GeneratorHarness.Generate(source).Errors);
    }

    [Fact]
    public async Task An_expression_bodied_configuration_and_a_parenthesized_parameter_are_read()
    {
        const string source = Usings + """
            var app = SparApplication.Create(args, (spar) => spar.ContentSecurity((csp) => csp.MediaSrc("https://media.example")));
            return app.Run();
            """;

        Assert.Empty(await GeneratorHarness.AnalyzeAsync(source));
        Assert.Contains("MediaSrc = [\"https://media.example\"],", GeneratorHarness.Generate(source).Files["Spar.Contracts.g.cs"], StringComparison.Ordinal);
    }

    /// <summary>The generated text with each line's indentation removed, so a test reads its content alone.</summary>
    private static string Unindented(string code) => string.Join('\n', code.Split('\n').Select(line => line.TrimStart()));
}
