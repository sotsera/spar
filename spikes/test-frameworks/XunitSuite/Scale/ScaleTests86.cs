#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.XunitSuite.Scale;

public sealed class ScaleTests86
{
    [Fact]
    public void Case_8601() => Assert.Equal(17203, Calc.Add(8601, 8602));

    [Fact]
    public void Case_8602() => Assert.Equal(17205, Calc.Add(8602, 8603));

    [Fact]
    public void Case_8603() => Assert.Equal(17207, Calc.Add(8603, 8604));

    [Fact]
    public void Case_8604() => Assert.Equal(17209, Calc.Add(8604, 8605));

    [Fact]
    public void Case_8605() => Assert.Equal(17211, Calc.Add(8605, 8606));

    [Fact]
    public void Case_8606() => Assert.Equal(17213, Calc.Add(8606, 8607));

    [Fact]
    public void Case_8607() => Assert.Equal(17215, Calc.Add(8607, 8608));

    [Fact]
    public void Case_8608() => Assert.Equal(17217, Calc.Add(8608, 8609));

    [Fact]
    public void Case_8609() => Assert.Equal(17219, Calc.Add(8609, 8610));

    [Fact]
    public void Case_8610() => Assert.Equal(17221, Calc.Add(8610, 8611));

    [Fact]
    public void Case_8611() => Assert.Equal(17223, Calc.Add(8611, 8612));

    [Fact]
    public void Case_8612() => Assert.Equal(17225, Calc.Add(8612, 8613));

    [Fact]
    public void Case_8613() => Assert.Equal(17227, Calc.Add(8613, 8614));

    [Fact]
    public void Case_8614() => Assert.Equal(17229, Calc.Add(8614, 8615));

    [Fact]
    public void Case_8615() => Assert.Equal(17231, Calc.Add(8615, 8616));

    [Fact]
    public void Case_8616() => Assert.Equal(17233, Calc.Add(8616, 8617));

    [Fact]
    public void Case_8617() => Assert.Equal(17235, Calc.Add(8617, 8618));

    [Fact]
    public void Case_8618() => Assert.Equal(17237, Calc.Add(8618, 8619));

    [Fact]
    public void Case_8619() => Assert.Equal(17239, Calc.Add(8619, 8620));

    [Fact]
    public void Case_8620() => Assert.Equal(17241, Calc.Add(8620, 8621));
}
#endif
