using Spar.Testing;

namespace WireShapes.Tests;

/// <summary>Surfaces this project does not reference fail at start, naming what is missing.</summary>
public sealed class SurfaceTests
{
    [Fact]
    public async Task The_browser_surface_without_its_package_names_the_package()
    {
        var start = SparTestApp.StartAsync<Program>(o => o.Surface = SparTestSurface.Browser, TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => start);

        Assert.Contains("Spar.Testing.Browser", error.Message, StringComparison.Ordinal);
    }
}
