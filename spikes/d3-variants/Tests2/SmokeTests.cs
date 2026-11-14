namespace D3.Tests2;

// A second consumer of the same variant, for the parallel-build check.
public sealed class SmokeTests
{
    [Fact]
    public void Runs() => Assert.True(true);
}
