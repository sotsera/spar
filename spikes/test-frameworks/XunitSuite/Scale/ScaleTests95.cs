#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.XunitSuite.Scale;

public sealed class ScaleTests95
{
    [Fact]
    public void Case_9501() => Assert.Equal(19003, Calc.Add(9501, 9502));

    [Fact]
    public void Case_9502() => Assert.Equal(19005, Calc.Add(9502, 9503));

    [Fact]
    public void Case_9503() => Assert.Equal(19007, Calc.Add(9503, 9504));

    [Fact]
    public void Case_9504() => Assert.Equal(19009, Calc.Add(9504, 9505));

    [Fact]
    public void Case_9505() => Assert.Equal(19011, Calc.Add(9505, 9506));

    [Fact]
    public void Case_9506() => Assert.Equal(19013, Calc.Add(9506, 9507));

    [Fact]
    public void Case_9507() => Assert.Equal(19015, Calc.Add(9507, 9508));

    [Fact]
    public void Case_9508() => Assert.Equal(19017, Calc.Add(9508, 9509));

    [Fact]
    public void Case_9509() => Assert.Equal(19019, Calc.Add(9509, 9510));

    [Fact]
    public void Case_9510() => Assert.Equal(19021, Calc.Add(9510, 9511));

    [Fact]
    public void Case_9511() => Assert.Equal(19023, Calc.Add(9511, 9512));

    [Fact]
    public void Case_9512() => Assert.Equal(19025, Calc.Add(9512, 9513));

    [Fact]
    public void Case_9513() => Assert.Equal(19027, Calc.Add(9513, 9514));

    [Fact]
    public void Case_9514() => Assert.Equal(19029, Calc.Add(9514, 9515));

    [Fact]
    public void Case_9515() => Assert.Equal(19031, Calc.Add(9515, 9516));

    [Fact]
    public void Case_9516() => Assert.Equal(19033, Calc.Add(9516, 9517));

    [Fact]
    public void Case_9517() => Assert.Equal(19035, Calc.Add(9517, 9518));

    [Fact]
    public void Case_9518() => Assert.Equal(19037, Calc.Add(9518, 9519));

    [Fact]
    public void Case_9519() => Assert.Equal(19039, Calc.Add(9519, 9520));

    [Fact]
    public void Case_9520() => Assert.Equal(19041, Calc.Add(9520, 9521));
}
#endif
