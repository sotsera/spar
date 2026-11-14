#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.XunitSuite.Scale;

public sealed class ScaleTests96
{
    [Fact]
    public void Case_9601() => Assert.Equal(19203, Calc.Add(9601, 9602));

    [Fact]
    public void Case_9602() => Assert.Equal(19205, Calc.Add(9602, 9603));

    [Fact]
    public void Case_9603() => Assert.Equal(19207, Calc.Add(9603, 9604));

    [Fact]
    public void Case_9604() => Assert.Equal(19209, Calc.Add(9604, 9605));

    [Fact]
    public void Case_9605() => Assert.Equal(19211, Calc.Add(9605, 9606));

    [Fact]
    public void Case_9606() => Assert.Equal(19213, Calc.Add(9606, 9607));

    [Fact]
    public void Case_9607() => Assert.Equal(19215, Calc.Add(9607, 9608));

    [Fact]
    public void Case_9608() => Assert.Equal(19217, Calc.Add(9608, 9609));

    [Fact]
    public void Case_9609() => Assert.Equal(19219, Calc.Add(9609, 9610));

    [Fact]
    public void Case_9610() => Assert.Equal(19221, Calc.Add(9610, 9611));

    [Fact]
    public void Case_9611() => Assert.Equal(19223, Calc.Add(9611, 9612));

    [Fact]
    public void Case_9612() => Assert.Equal(19225, Calc.Add(9612, 9613));

    [Fact]
    public void Case_9613() => Assert.Equal(19227, Calc.Add(9613, 9614));

    [Fact]
    public void Case_9614() => Assert.Equal(19229, Calc.Add(9614, 9615));

    [Fact]
    public void Case_9615() => Assert.Equal(19231, Calc.Add(9615, 9616));

    [Fact]
    public void Case_9616() => Assert.Equal(19233, Calc.Add(9616, 9617));

    [Fact]
    public void Case_9617() => Assert.Equal(19235, Calc.Add(9617, 9618));

    [Fact]
    public void Case_9618() => Assert.Equal(19237, Calc.Add(9618, 9619));

    [Fact]
    public void Case_9619() => Assert.Equal(19239, Calc.Add(9619, 9620));

    [Fact]
    public void Case_9620() => Assert.Equal(19241, Calc.Add(9620, 9621));
}
#endif
