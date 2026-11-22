using Spar.Generator;
using Spar.Sdk.Tasks.Client;

namespace Spar.Sdk.Tasks.Tests;

/// <summary>
/// <c>theme.json</c> in the generated package (plan section 3d): the window colours Program.cs declares, read by the
/// client step without running the app, for the Vite plugin's theme and splash styles; the defaults without them; and a
/// colour the build cannot read stopping the step with SPAR106 at its place.
/// </summary>
public sealed class ThemeFileTests
{
    [Fact]
    public void The_colours_are_written_for_the_Vite_plugin_in_lower_case_and_six_digits()
    {
        var read = ClientHarness.ReadApp("""
            using Spar;

            const string Dark = "#1D1F24";
            var app = SparApplication.Create(args, spar =>
            {
                spar.Window(w => w.Size(1100, 720).Background(light: "#fff", dark: Dark).MinSize(640, 420));
            });
            return app.Run();
            """);

        Assert.Empty(read.Problems);
        Assert.Equal(
            """
            {
              "light": "#ffffff",
              "dark": "#1d1f24"
            }

            """.ReplaceLineEndings("\n"),
            ClientPackage.ThemeJson(read.WindowColors!));
    }

    [Fact]
    public void The_last_Background_wins_and_named_arguments_may_come_in_any_order()
    {
        var read = ClientHarness.ReadApp("""
            using Spar;

            var app = SparApplication.Create(args, spar =>
            {
                spar.Window(w => w.Background("#000000", "#111111"));
                spar.Window(w =>
                {
                    w.Size(800, 600);
                    w.Background(dark: "#222222", light: "#eeeeee");
                });
            });
            return app.Run();
            """);

        Assert.Empty(read.Problems);
        Assert.Equal(new("#eeeeee", "#222222"), read.WindowColors);
    }

    [Fact]
    public void Without_a_Background_call_the_file_has_the_hosts_defaults()
    {
        var read = ClientHarness.ReadApp("""
            using Spar;

            var app = SparApplication.Create(args, spar => spar.Window(w => w.Size(args.Length * 100 + 400, 300)));
            return app.Run();
            """);

        Assert.Empty(read.Problems);
        Assert.Equal(WindowColorsModel.Default, read.WindowColors);
        Assert.Contains("\"dark\": \"#1f1f1f\"", ClientPackage.ThemeJson(WindowColorsModel.Default), StringComparison.Ordinal);
    }

    [Fact]
    public void A_colour_the_build_cannot_read_stops_the_step_with_its_place()
    {
        var read = ClientHarness.ReadApp("""
            using Spar;

            var dark = args[0];
            var app = SparApplication.Create(args, spar =>
            {
                spar.Window(w => w.Background(light: "white", dark: dark));
                if (args.Length > 1)
                {
                    spar.Window(w => w.Background("#ffffff", "#000000"));
                }
            });
            return app.Run();
            """);

        Assert.Equal(["SPAR106", "SPAR106", "SPAR106"], read.Problems.Select(p => p.Id));
        Assert.All(read.Problems, problem => Assert.True(problem.Location.IsInSource));
    }
}
