#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.XunitSuite.Scale;

public sealed class ScaleTests36
{
    [Fact]
    public void Case_3601() => Assert.Equal(7203, Calc.Add(3601, 3602));

    [Fact]
    public void Case_3602() => Assert.Equal(7205, Calc.Add(3602, 3603));

    [Fact]
    public void Case_3603() => Assert.Equal(7207, Calc.Add(3603, 3604));

    [Fact]
    public void Case_3604() => Assert.Equal(7209, Calc.Add(3604, 3605));

    [Fact]
    public void Case_3605() => Assert.Equal(7211, Calc.Add(3605, 3606));

    [Fact]
    public void Case_3606() => Assert.Equal(7213, Calc.Add(3606, 3607));

    [Fact]
    public void Case_3607() => Assert.Equal(7215, Calc.Add(3607, 3608));

    [Fact]
    public void Case_3608() => Assert.Equal(7217, Calc.Add(3608, 3609));

    [Fact]
    public void Case_3609() => Assert.Equal(7219, Calc.Add(3609, 3610));

    [Fact]
    public void Case_3610() => Assert.Equal(7221, Calc.Add(3610, 3611));

    [Fact]
    public void Case_3611() => Assert.Equal(7223, Calc.Add(3611, 3612));

    [Fact]
    public void Case_3612() => Assert.Equal(7225, Calc.Add(3612, 3613));

    [Fact]
    public void Case_3613() => Assert.Equal(7227, Calc.Add(3613, 3614));

    [Fact]
    public void Case_3614() => Assert.Equal(7229, Calc.Add(3614, 3615));

    [Fact]
    public void Case_3615() => Assert.Equal(7231, Calc.Add(3615, 3616));

    [Fact]
    public void Case_3616() => Assert.Equal(7233, Calc.Add(3616, 3617));

    [Fact]
    public void Case_3617() => Assert.Equal(7235, Calc.Add(3617, 3618));

    [Fact]
    public void Case_3618() => Assert.Equal(7237, Calc.Add(3618, 3619));

    [Fact]
    public void Case_3619() => Assert.Equal(7239, Calc.Add(3619, 3620));

    [Fact]
    public void Case_3620() => Assert.Equal(7241, Calc.Add(3620, 3621));
}
#endif
