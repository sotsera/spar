using Spar.Sdk.Tasks.Client;

namespace Spar.Sdk.Tasks.Tests;

/// <summary>
/// <c>windows.d.ts</c> in the generated package (decision "Secondary and modal windows"): the window kinds Program.cs
/// declares, read by the client step without running the app, as the page's <c>WindowKind</c> and <c>ModalKind</c>
/// unions in declaration order; <c>never</c> without any; and a name the build cannot read stopping the step with
/// SPAR107 at its place.
/// </summary>
public sealed class WindowKindsFileTests
{
    [Fact]
    public void The_kinds_become_the_WindowKind_and_ModalKind_unions_in_the_order_Program_declares_them()
    {
        var read = ClientHarness.ReadApp("""
            using Spar;

            const string Editor = "queryEditor";
            var app = SparApplication.Create(args, spar =>
            {
                spar.Window(w => w.Size(1100, 720));
                spar.Window("settings", w => w.Route("/settings").Modal().SingleInstance());
                spar.Window(Editor, w => w.Route("/query"));
                spar.Window("confirm", w =>
                {
                    w.Route("/confirm");
                    w.Modal();
                });
                spar.Window("report", Report);
            });
            return app.Run();

            static void Report(WindowOptions w) => w.Route("/report");
            """);

        Assert.Empty(read.Problems);
        Assert.Equal(
            [("settings", true), ("queryEditor", false), ("confirm", true), ("report", (bool?)null)],
            read.WindowKinds!.Select(kind => (kind.Name, kind.Modal)));
        var declaration = DeclarationEmitter.Windows(read.WindowKinds);

        // A kind whose options the build cannot read (a method group here) is in both: the start checks it.
        Assert.Contains("export type WindowKind = \"queryEditor\" | \"report\";\n", declaration, StringComparison.Ordinal);
        Assert.Contains("export type ModalKind = \"settings\" | \"confirm\" | \"report\";\n", declaration, StringComparison.Ordinal);
        Assert.Contains("export declare const windows: WindowsApi<WindowKind, ModalKind>;\n", declaration, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_kinds_the_unions_are_never()
    {
        var read = ClientHarness.ReadApp("""
            using Spar;

            var app = SparApplication.Create(args, spar => spar.Window(w => w.Size(800, 600)));
            return app.Run();
            """);

        Assert.Empty(read.WindowKinds!);
        var declaration = DeclarationEmitter.Windows(read.WindowKinds);
        Assert.Contains("export type WindowKind = never;\n", declaration, StringComparison.Ordinal);
        Assert.Contains("export type ModalKind = never;\n", declaration, StringComparison.Ordinal);
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
