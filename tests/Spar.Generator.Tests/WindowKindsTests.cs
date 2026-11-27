namespace Spar.Generator.Tests;

/// <summary>
/// The window kinds <c>Program.cs</c> declares, as the build reads them for the page's <c>WindowKind</c> union
/// (decision "Secondary and modal windows"): SPAR107 at its location for a name that is not a constant or not a kind's
/// name, and for a <c>Window(kind, …)</c> call the build cannot read where it sits; SPAR106 for a kind's own colours,
/// which the start refuses; nothing for the readable shapes or a kind's computed size.
/// </summary>
public sealed class WindowKindsTests
{
    private const string Usings = """
        using System;
        using Spar;

        """;

    [Fact]
    public async Task Constant_kind_names_directly_in_Create_are_fine()
    {
        const string source = Usings + """
            const string Editor = "queryEditor";
            var width = args.Length * 100 + 800;
            var app = SparApplication.Create(args, spar =>
            {
                spar.Window(w => w.Background("#ffffff", "#1d1f24"));
                spar.Window("settings", w => w.Route("/settings").Size(640, 520).SingleInstance());
                spar.Window(Editor, w => w.Route("/query").Size(width, 700));
            });
            return app.Run();
            """;

        Assert.Empty(await GeneratorHarness.AnalyzeAsync(source));
    }

    [Fact]
    public async Task A_name_that_is_not_a_constant_or_not_a_kinds_name_is_SPAR107_at_the_argument()
    {
        const string source = Usings + """
            var kind = args[0];
            var app = SparApplication.Create(args, spar =>
            {
                spar.Window(kind, w => w.Route("/a"));
                spar.Window("Settings", w => w.Route("/b"));
                spar.Window("main", w => w.Route("/c"));
            });
            return app.Run();
            """;

        Assert.Equal(["SPAR107: kind", "SPAR107: \"Settings\"", "SPAR107: \"main\""], await GeneratorHarness.AnalyzeAsync(source));
    }

    [Fact]
    public async Task A_kind_the_build_cannot_read_is_SPAR107_and_a_kinds_colours_SPAR106()
    {
        const string source = Usings + """
            var app = SparApplication.Create(args, spar =>
            {
                if (args.Length > 0)
                {
                    spar.Window("report", w => w.Route("/report"));
                }

                spar.Window("settings", w => w.Route("/settings").Background("#ffffff", "#000000"));
            });
            return app.Run();
            """;

        Assert.Equal(["SPAR107: Window", "SPAR106: Background"], await GeneratorHarness.AnalyzeAsync(source));
    }
}
