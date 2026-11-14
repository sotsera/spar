#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.XunitSuite.Scale;

public sealed class ScaleTests10
{
    [Fact]
    public void Case_1001() => Assert.Equal(2003, Calc.Add(1001, 1002));

    [Fact]
    public void Case_1002() => Assert.Equal(2005, Calc.Add(1002, 1003));

    [Fact]
    public void Case_1003() => Assert.Equal(2007, Calc.Add(1003, 1004));

    [Fact]
    public void Case_1004() => Assert.Equal(2009, Calc.Add(1004, 1005));

    [Fact]
    public void Case_1005() => Assert.Equal(2011, Calc.Add(1005, 1006));

    [Fact]
    public void Case_1006() => Assert.Equal(2013, Calc.Add(1006, 1007));

    [Fact]
    public void Case_1007() => Assert.Equal(2015, Calc.Add(1007, 1008));

    [Fact]
    public void Case_1008() => Assert.Equal(2017, Calc.Add(1008, 1009));

    [Fact]
    public void Case_1009() => Assert.Equal(2019, Calc.Add(1009, 1010));

    [Fact]
    public void Case_1010() => Assert.Equal(2021, Calc.Add(1010, 1011));

    [Fact]
    public void Case_1011() => Assert.Equal(2023, Calc.Add(1011, 1012));

    [Fact]
    public void Case_1012() => Assert.Equal(2025, Calc.Add(1012, 1013));

    [Fact]
    public void Case_1013() => Assert.Equal(2027, Calc.Add(1013, 1014));

    [Fact]
    public void Case_1014() => Assert.Equal(2029, Calc.Add(1014, 1015));

    [Fact]
    public void Case_1015() => Assert.Equal(2031, Calc.Add(1015, 1016));

    [Fact]
    public void Case_1016() => Assert.Equal(2033, Calc.Add(1016, 1017));

    [Fact]
    public void Case_1017() => Assert.Equal(2035, Calc.Add(1017, 1018));

    [Fact]
    public void Case_1018() => Assert.Equal(2037, Calc.Add(1018, 1019));

    [Fact]
    public void Case_1019() => Assert.Equal(2039, Calc.Add(1019, 1020));

    [Fact]
    public void Case_1020() => Assert.Equal(2041, Calc.Add(1020, 1021));
}
#endif
