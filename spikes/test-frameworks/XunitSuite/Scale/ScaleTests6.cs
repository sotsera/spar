#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.XunitSuite.Scale;

public sealed class ScaleTests6
{
    [Fact]
    public void Case_601() => Assert.Equal(1203, Calc.Add(601, 602));

    [Fact]
    public void Case_602() => Assert.Equal(1205, Calc.Add(602, 603));

    [Fact]
    public void Case_603() => Assert.Equal(1207, Calc.Add(603, 604));

    [Fact]
    public void Case_604() => Assert.Equal(1209, Calc.Add(604, 605));

    [Fact]
    public void Case_605() => Assert.Equal(1211, Calc.Add(605, 606));

    [Fact]
    public void Case_606() => Assert.Equal(1213, Calc.Add(606, 607));

    [Fact]
    public void Case_607() => Assert.Equal(1215, Calc.Add(607, 608));

    [Fact]
    public void Case_608() => Assert.Equal(1217, Calc.Add(608, 609));

    [Fact]
    public void Case_609() => Assert.Equal(1219, Calc.Add(609, 610));

    [Fact]
    public void Case_610() => Assert.Equal(1221, Calc.Add(610, 611));

    [Fact]
    public void Case_611() => Assert.Equal(1223, Calc.Add(611, 612));

    [Fact]
    public void Case_612() => Assert.Equal(1225, Calc.Add(612, 613));

    [Fact]
    public void Case_613() => Assert.Equal(1227, Calc.Add(613, 614));

    [Fact]
    public void Case_614() => Assert.Equal(1229, Calc.Add(614, 615));

    [Fact]
    public void Case_615() => Assert.Equal(1231, Calc.Add(615, 616));

    [Fact]
    public void Case_616() => Assert.Equal(1233, Calc.Add(616, 617));

    [Fact]
    public void Case_617() => Assert.Equal(1235, Calc.Add(617, 618));

    [Fact]
    public void Case_618() => Assert.Equal(1237, Calc.Add(618, 619));

    [Fact]
    public void Case_619() => Assert.Equal(1239, Calc.Add(619, 620));

    [Fact]
    public void Case_620() => Assert.Equal(1241, Calc.Add(620, 621));
}
#endif
