#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.XunitSuite.Scale;

public sealed class ScaleTests15
{
    [Fact]
    public void Case_1501() => Assert.Equal(3003, Calc.Add(1501, 1502));

    [Fact]
    public void Case_1502() => Assert.Equal(3005, Calc.Add(1502, 1503));

    [Fact]
    public void Case_1503() => Assert.Equal(3007, Calc.Add(1503, 1504));

    [Fact]
    public void Case_1504() => Assert.Equal(3009, Calc.Add(1504, 1505));

    [Fact]
    public void Case_1505() => Assert.Equal(3011, Calc.Add(1505, 1506));

    [Fact]
    public void Case_1506() => Assert.Equal(3013, Calc.Add(1506, 1507));

    [Fact]
    public void Case_1507() => Assert.Equal(3015, Calc.Add(1507, 1508));

    [Fact]
    public void Case_1508() => Assert.Equal(3017, Calc.Add(1508, 1509));

    [Fact]
    public void Case_1509() => Assert.Equal(3019, Calc.Add(1509, 1510));

    [Fact]
    public void Case_1510() => Assert.Equal(3021, Calc.Add(1510, 1511));

    [Fact]
    public void Case_1511() => Assert.Equal(3023, Calc.Add(1511, 1512));

    [Fact]
    public void Case_1512() => Assert.Equal(3025, Calc.Add(1512, 1513));

    [Fact]
    public void Case_1513() => Assert.Equal(3027, Calc.Add(1513, 1514));

    [Fact]
    public void Case_1514() => Assert.Equal(3029, Calc.Add(1514, 1515));

    [Fact]
    public void Case_1515() => Assert.Equal(3031, Calc.Add(1515, 1516));

    [Fact]
    public void Case_1516() => Assert.Equal(3033, Calc.Add(1516, 1517));

    [Fact]
    public void Case_1517() => Assert.Equal(3035, Calc.Add(1517, 1518));

    [Fact]
    public void Case_1518() => Assert.Equal(3037, Calc.Add(1518, 1519));

    [Fact]
    public void Case_1519() => Assert.Equal(3039, Calc.Add(1519, 1520));

    [Fact]
    public void Case_1520() => Assert.Equal(3041, Calc.Add(1520, 1521));
}
#endif
