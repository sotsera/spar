// M1d step 4's probe (docs/evidence/m1d-template-tests.md): a trait on the assembly, CaptureConsole, and xUnit1051, in
// both lanes. Copy this folder to artifacts/scratch/TraitProbe.Tests/ (the project names WireShapes from there), then
// dotnet test it, and publish it with -p:SparTestLane=Aot and run the executable with -result-trx. For xUnit1051, drop
// the token from Task.Delay and build with -p:TreatWarningsAsErrors and -p:MSBuildTreatWarningsAsErrors set each way.
using Spar.Testing;

[assembly: Trait("Surface", "Browser")]
[assembly: CaptureConsole]

namespace TraitProbe.Tests;

public sealed class ProbeTests
{
    [Fact]
    public async Task Assembly_trait_counts()
    {
        // CaptureConsole: this line is in the test's output in the TRX report, in both lanes.
        Console.WriteLine("captured-console-line-42");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SparTestApp.StartAsync<Program>(o => o.Surface = SparTestSurface.Browser, TestContext.Current.CancellationToken));

        // Past the trait check, which the assembly's trait satisfies: what stops it is the missing Browser package.
        Assert.Contains("Spar.Testing.Browser", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Window_without_trait_fails()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SparTestApp.StartAsync<Program>(o => o.Surface = SparTestSurface.Window, TestContext.Current.CancellationToken));
        Assert.Contains("[Trait(\"Surface\", \"Window\")]", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Uses_the_test_token()
    {
        await Task.Delay(1, TestContext.Current.CancellationToken);
    }
}
