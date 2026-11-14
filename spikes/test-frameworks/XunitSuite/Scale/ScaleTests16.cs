#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.XunitSuite.Scale;

public sealed class ScaleTests16
{
    [Fact]
    public void Case_1601() => Assert.Equal(3203, Calc.Add(1601, 1602));

    [Fact]
    public void Case_1602() => Assert.Equal(3205, Calc.Add(1602, 1603));

    [Fact]
    public void Case_1603() => Assert.Equal(3207, Calc.Add(1603, 1604));

    [Fact]
    public void Case_1604() => Assert.Equal(3209, Calc.Add(1604, 1605));

    [Fact]
    public void Case_1605() => Assert.Equal(3211, Calc.Add(1605, 1606));

    [Fact]
    public void Case_1606() => Assert.Equal(3213, Calc.Add(1606, 1607));

    [Fact]
    public void Case_1607() => Assert.Equal(3215, Calc.Add(1607, 1608));

    [Fact]
    public void Case_1608() => Assert.Equal(3217, Calc.Add(1608, 1609));

    [Fact]
    public void Case_1609() => Assert.Equal(3219, Calc.Add(1609, 1610));

    [Fact]
    public void Case_1610() => Assert.Equal(3221, Calc.Add(1610, 1611));

    [Fact]
    public void Case_1611() => Assert.Equal(3223, Calc.Add(1611, 1612));

    [Fact]
    public void Case_1612() => Assert.Equal(3225, Calc.Add(1612, 1613));

    [Fact]
    public void Case_1613() => Assert.Equal(3227, Calc.Add(1613, 1614));

    [Fact]
    public void Case_1614() => Assert.Equal(3229, Calc.Add(1614, 1615));

    [Fact]
    public void Case_1615() => Assert.Equal(3231, Calc.Add(1615, 1616));

    [Fact]
    public void Case_1616() => Assert.Equal(3233, Calc.Add(1616, 1617));

    [Fact]
    public void Case_1617() => Assert.Equal(3235, Calc.Add(1617, 1618));

    [Fact]
    public void Case_1618() => Assert.Equal(3237, Calc.Add(1618, 1619));

    [Fact]
    public void Case_1619() => Assert.Equal(3239, Calc.Add(1619, 1620));

    [Fact]
    public void Case_1620() => Assert.Equal(3241, Calc.Add(1620, 1621));
}
#endif
