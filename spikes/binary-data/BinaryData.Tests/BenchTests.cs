using System.Runtime.InteropServices;
using BinaryData;
using Microsoft.Extensions.DependencyInjection;
using Spar.Testing;

namespace BinaryData.Tests;

/// <summary>Runs the page's measurements in the real window (offscreen on Windows, a private Xvfb on Linux) and saves them.</summary>
[Trait("Surface", "Window")]
public sealed class BenchTests
{
    [Fact]
    public async Task Measure()
    {
        var token = TestContext.Current.CancellationToken;
        await using var app = await SparTestApp.StartAsync<Program>(o => o.Surface = SparTestSurface.Window, token);

        var json = await app.Services.GetRequiredService<BenchResults>().WaitAsync(TimeSpan.FromMinutes(10));

        var folder = Path.Combine(SpikeFolder(), "results");
        Directory.CreateDirectory(folder);
        var host = OperatingSystem.IsWindows() ? "windows" : "linux";
        await File.WriteAllTextAsync(Path.Combine(folder, $"{host}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json"), json, token);
        Assert.DoesNotContain("\"error\"", json, StringComparison.Ordinal);
        TestContext.Current.TestOutputHelper?.WriteLine($"{RuntimeInformation.OSDescription}: {json.Length} characters saved.");
    }

    private static string SpikeFolder()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "BinaryData.slnx")))
            {
                return folder.FullName;
            }
        }

        throw new InvalidOperationException("Run from the spike's folder.");
    }
}
