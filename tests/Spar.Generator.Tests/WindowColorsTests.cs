namespace Spar.Generator.Tests;

/// <summary>
/// The window colours <c>Program.cs</c> declares, as the build reads them for the page's theme style (plan section 3d):
/// SPAR106 at its location for a colour that is not a constant or not a colour, and for a <c>Background</c> call the
/// build cannot read where it sits; nothing for the readable shapes, or for a computed size.
/// </summary>
public sealed class WindowColorsTests
{
    private const string Usings = """
        using System;
        using Spar;

        """;

    [Fact]
    public async Task Constant_colours_directly_in_Create_and_a_computed_size_are_fine()
    {
        const string source = Usings + """
            const string Light = "#ffffff";
            var width = args.Length * 100 + 800;
            var app = SparApplication.Create(args, spar =>
            {
                spar.Window(w => w.Background(Light, "#1d1f24").Size(width, 600));
                spar.Window(w =>
                {
                    w.MinSize(640, 420);
                    w.Background(dark: "#000", light: "#FFF");
                });
            });
            return app.Run();
            """;

        Assert.Empty(await GeneratorHarness.AnalyzeAsync(source));
    }

    [Fact]
    public async Task A_colour_that_is_not_a_constant_or_not_a_colour_is_SPAR106_at_the_argument()
    {
        const string source = Usings + """
            var dark = args[0];
            var app = SparApplication.Create(args, spar =>
            {
                spar.Window(w => w.Background(light: "white", dark: dark));
            });
            return app.Run();
            """;

        Assert.Equal(["SPAR106: light: \"white\"", "SPAR106: dark: dark"], await GeneratorHarness.AnalyzeAsync(source));
    }

    [Fact]
    public async Task A_Background_call_the_build_cannot_read_is_SPAR106_at_its_name()
    {
        const string source = Usings + """
            var app = SparApplication.Create(args, spar =>
            {
                if (args.Length > 0)
                {
                    spar.Window(w => w.Background("#ffffff", "#000000"));
                }

                spar.Window(w => Paint(w));
            });
            return app.Run();

            static void Paint(WindowOptions window) => window.Background("#ffffff", "#000000");
            """;

        Assert.Equal(["SPAR106: Background", "SPAR106: Background"], await GeneratorHarness.AnalyzeAsync(source));
    }

    [Fact]
    public async Task A_library_may_set_colours_freely()
    {
        const string source = Usings + """
            public static class Look
            {
                public static void Apply(SparBuilder spar, string dark) => spar.Window(w => w.Background("#ffffff", dark));
            }
            """;

        Assert.Empty(await GeneratorHarness.AnalyzeAsync(source));
    }
}
