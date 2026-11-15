using System.Runtime.CompilerServices;

namespace SampleApp.Tests;

/// <summary>
/// Calls the app's own top-level statements directly. The compiler names that method <c>&lt;Main&gt;$</c> on the
/// internal <c>Program</c> class; an unsafe accessor reaches it without reflection, so this also works under NativeAOT.
/// Spar would generate this (the build knows the entry point's metadata name), not the test author.
/// </summary>
internal static class AppEntry
{
    public static int Run(string[] args) => CallMain(null, args);

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "<Main>$")]
    private static extern int CallMain([UnsafeAccessorType("Program, SampleApp")] object? program, string[] args);
}
