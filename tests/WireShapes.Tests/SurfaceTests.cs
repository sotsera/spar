using Spar.Testing;

namespace WireShapes.Tests;

/// <summary>
/// A surface fails at its start, naming what is missing: the package this project does not reference, or the trait a
/// Window or Browser test must carry (decision "M1d step 4's shape: tests in the template"). The trait check runs before
/// anything starts, so a window test without its trait fails here too, with no window.
/// </summary>
public sealed class SurfaceTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Surface", "Browser")]
    public async Task The_browser_surface_without_its_package_names_the_package()
    {
        var start = SparTestApp.StartAsync<Program>(o => o.Surface = SparTestSurface.Browser, Token);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => start);

        Assert.Contains("Spar.Testing.Browser", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_window_test_without_its_trait_fails_at_its_start_naming_the_trait()
    {
        var start = SparTestApp.StartAsync<Program>(o => o.Surface = SparTestSurface.Window, Token);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => start);

        Assert.StartsWith("This test starts the Window surface but has no [Trait(\"Surface\", \"Window\")].", error.Message, StringComparison.Ordinal);
        Assert.Contains("--filter-trait \"Surface=Window\"", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Surface", "Page")]
    public async Task A_browser_test_with_another_surface_value_fails_naming_the_browser_trait()
    {
        var start = SparTestApp.StartAsync<Program>(o => o.Surface = SparTestSurface.Browser, Token);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => start);

        Assert.StartsWith("This test starts the Browser surface but has no [Trait(\"Surface\", \"Browser\")].", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_test_without_a_surface_needs_no_trait()
    {
        await using var app = await SparTestApp.StartAsync<Program>(cancellationToken: Token);

        Assert.NotEmpty(app.Fingerprint);
    }
}

/// <summary>The trait on the class counts for each of its tests, as xUnit merges it.</summary>
[Trait("Surface", "Browser")]
public sealed class SurfaceTraitOnClassTests
{
    [Fact]
    public async Task A_browser_test_whose_class_has_the_trait_starts()
    {
        var start = SparTestApp.StartAsync<Program>(o => o.Surface = SparTestSurface.Browser, TestContext.Current.CancellationToken);

        // Past the trait check: what stops this start is the missing package.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => start);

        Assert.Contains("Spar.Testing.Browser", error.Message, StringComparison.Ordinal);
    }
}
