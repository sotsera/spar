using Spar.Sdk.Tasks.Client;

namespace Spar.Sdk.Tasks.Tests;

/// <summary>
/// <c>windows.d.ts</c> in the generated package (decision "Secondary and modal windows"): the window kinds Program.cs
/// declares, read by the client step without running the app, as the page's <c>WindowKind</c> union in declaration
/// order; <c>never</c> without any; and a name the build cannot read stopping the step with SPAR107 at its place.
/// </summary>
public sealed class WindowKindsFileTests
{
    [Fact]
    public void The_kinds_become_the_WindowKind_union_in_the_order_Program_declares_them()
    {
        var read = ClientHarness.ReadApp("""
            using Spar;

            const string Editor = "queryEditor";
            var app = SparApplication.Create(args, spar =>
            {
                spar.Window(w => w.Size(1100, 720));
                spar.Window("settings", w => w.Route("/settings").SingleInstance());
                spar.Window(Editor, w => w.Route("/query"));
            });
            return app.Run();
            """);

        Assert.Empty(read.Problems);
        Assert.Equal(["settings", "queryEditor"], read.WindowKinds);
        var declaration = DeclarationEmitter.Windows(read.WindowKinds);
        Assert.Contains("export type WindowKind = \"settings\" | \"queryEditor\";\n", declaration, StringComparison.Ordinal);
        Assert.Contains("export declare const windows: WindowsApi<WindowKind>;\n", declaration, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_kinds_the_union_is_never()
    {
        var read = ClientHarness.ReadApp("""
            using Spar;

            var app = SparApplication.Create(args, spar => spar.Window(w => w.Size(800, 600)));
            return app.Run();
            """);

        Assert.Empty(read.WindowKinds!);
        Assert.Contains("export type WindowKind = never;\n", DeclarationEmitter.Windows(read.WindowKinds), StringComparison.Ordinal);
    }

    [Fact]
    public void A_kind_name_the_build_cannot_read_stops_the_step_with_SPAR107()
    {
        var read = ClientHarness.ReadApp("""
            using Spar;

            var app = SparApplication.Create(args, spar =>
            {
                spar.Window(args[0], w => w.Route("/a"));
            });
            return app.Run();
            """);

        Assert.Equal(["SPAR107"], read.Problems.Select(p => p.Id));
        Assert.True(read.Problems[0].Location.IsInSource);
    }
}
