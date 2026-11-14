using System.Diagnostics;
using System.Globalization;

namespace Baseline;

// Same entry shape as SqlAot.App's first-query mode, without any data access.
internal static class Program
{
    private static async Task<int> Main()
    {
        await Task.Yield();
        using var self = Process.GetCurrentProcess();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"TIMING main={(DateTime.Now - self.StartTime).TotalMilliseconds:F1}"));
        return 0;
    }
}
