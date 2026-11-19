using Spar.Sdk.Tasks.Client;

namespace Spar.Sdk.Tasks.Tests;

/// <summary>
/// <c>csp.json</c> in the generated package (plan section 3b): the page security Program.cs declares, read by the client
/// step with the generator's analysis, for the Vite plugin; and its problems stopping the step like a contract's.
/// </summary>
public sealed class CspFileTests
{
    [Fact]
    public void The_declaration_is_written_for_the_Vite_plugin()
    {
        var read = ClientHarness.ReadApp("""
            using Spar;

            var app = SparApplication.Create(args, spar =>
            {
                spar.ContentSecurity(csp => csp.FontSrc("https://fonts.gstatic.com").WithoutIntegrity().Reporting(endpoint: CspReports.Off));
                spar.Permissions(p => p.Allow(Permission.ClipboardRead));
            });
            return app.Run();
            """);

        Assert.Empty(read.Problems);
        Assert.Equal(
            """
            {
              "fontSrc": [
                "https://fonts.gstatic.com"
              ],
              "imgSrc": [],
              "connectSrc": [],
              "mediaSrc": [],
              "scriptSrc": [],
              "styleSrc": [],
              "frameSrc": [],
              "allowInlineStyles": false,
              "allowEval": false,
              "allowWebAssembly": false,
              "styleNonce": false,
              "trustedTypes": true,
              "integrity": false,
              "reporting": {
                "listener": "always",
                "endpoint": "off"
              },
              "permissions": [
                "clipboardRead"
              ]
            }

            """.ReplaceLineEndings("\n"),
            ClientPackage.CspJson(read.PageSecurity));
    }

    [Fact]
    public void Without_a_Create_call_the_file_has_the_strict_defaults()
    {
        var text = ClientPackage.CspJson(null);

        Assert.Contains("\"integrity\": true", text, StringComparison.Ordinal);
        Assert.Contains("\"trustedTypes\": true", text, StringComparison.Ordinal);
        Assert.Contains("\"listener\": \"always\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_page_security_problem_stops_the_step_with_its_place()
    {
        var read = ClientHarness.ReadApp("""
            using Spar;

            var host = args[0];
            var app = SparApplication.Create(args, spar =>
            {
                spar.ContentSecurity(csp => csp.FontSrc(host).StyleSrc("'unsafe-inline'"));
                if (args.Length > 1)
                {
                    spar.Permissions(p => p.Allow(Permission.Camera));
                }
            });
            return app.Run();
            """);

        Assert.Equal(["SPAR101", "SPAR102", "SPAR104"], read.Problems.Select(p => p.Id).Order());
        Assert.All(read.Problems, problem => Assert.True(problem.Location.IsInSource));
    }
}
