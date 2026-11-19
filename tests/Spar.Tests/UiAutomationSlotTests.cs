namespace Spar.Tests;

/// <summary>
/// The slot-order test of the black-box fixture's hand-declared UI Automation interfaces (<c>Spar.Testing</c>,
/// <c>BlackBox/Interop/UiAutomation.cs</c>) against the Windows SDK's <c>UIAutomationClient.h</c> they were written from.
/// The header comes with the Windows SDK, not with a package: where no SDK is installed the test is an explicit skip.
/// </summary>
public sealed class UiAutomationSlotTests
{
    [Fact]
    public void Every_declared_UI_Automation_method_sits_in_its_header_slot()
    {
        var header = Header();
        Assert.SkipWhen(header is null, "No Windows SDK with UIAutomationClient.h is installed (Windows Kits/10/Include/<version>/um).");

        var problems = ComSlots.Problems(header, "Spar.Testing", out var declared);

        Assert.Equal(7, declared);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>UIAutomationClient.h of the newest Windows SDK installed, or null.</summary>
    private static string? Header()
    {
        var kits = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
        var include = kits is null ? null : Path.Combine(kits, "Windows Kits", "10", "Include");
        if (include is null || !Directory.Exists(include))
        {
            return null;
        }

        return Directory.GetDirectories(include)
            .Select(version => Path.Combine(version, "um", "UIAutomationClient.h"))
            .Where(File.Exists)
            .OrderByDescending(path => new Version(Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path)))!), Comparer<Version>.Default)
            .FirstOrDefault();
    }
}
