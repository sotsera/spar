using System.Diagnostics;
using System.Reflection;

namespace D3.Tests;

public sealed class VariantTests
{
    private const string Marker = "D3MARKER-DESKTOPTEST";

    [Fact]
    public void VariantExecutablesContainAndRunTheVariantCode()
    {
        var found = 0;
        foreach (var key in new[] { "D3VariantBuildExe", "D3VariantPublishExe" })
        {
            var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, Metadata(key)));
            if (!File.Exists(path))
            {
                continue;
            }

            found++;
            Assert.Contains("variant: DesktopTest", Run(path), StringComparison.Ordinal);
            Assert.Contains(Marker, Run(path), StringComparison.Ordinal);
        }

        Assert.True(found > 0, "No variant executable was built.");
    }

    private static string Metadata(string key) =>
        typeof(VariantTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == key).Value
        ?? throw new InvalidOperationException(key);

    private static string Run(string path)
    {
        using var process = Process.Start(new ProcessStartInfo(path) { RedirectStandardOutput = true })
            ?? throw new InvalidOperationException(path);
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return output;
    }
}
