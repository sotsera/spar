#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.XunitSuite.Scale;

public sealed class ScaleTests11
{
    [Fact]
    public void Case_1101() => Assert.Equal(2203, Calc.Add(1101, 1102));

    [Fact]
    public void Case_1102() => Assert.Equal(2205, Calc.Add(1102, 1103));

    [Fact]
    public void Case_1103() => Assert.Equal(2207, Calc.Add(1103, 1104));

    [Fact]
    public void Case_1104() => Assert.Equal(2209, Calc.Add(1104, 1105));

    [Fact]
    public void Case_1105() => Assert.Equal(2211, Calc.Add(1105, 1106));

    [Fact]
    public void Case_1106() => Assert.Equal(2213, Calc.Add(1106, 1107));

    [Fact]
    public void Case_1107() => Assert.Equal(2215, Calc.Add(1107, 1108));

    [Fact]
    public void Case_1108() => Assert.Equal(2217, Calc.Add(1108, 1109));

    [Fact]
    public void Case_1109() => Assert.Equal(2219, Calc.Add(1109, 1110));

    [Fact]
    public void Case_1110() => Assert.Equal(2221, Calc.Add(1110, 1111));

    [Fact]
    public void Case_1111() => Assert.Equal(2223, Calc.Add(1111, 1112));

    [Fact]
    public void Case_1112() => Assert.Equal(2225, Calc.Add(1112, 1113));

    [Fact]
    public void Case_1113() => Assert.Equal(2227, Calc.Add(1113, 1114));

    [Fact]
    public void Case_1114() => Assert.Equal(2229, Calc.Add(1114, 1115));

    [Fact]
    public void Case_1115() => Assert.Equal(2231, Calc.Add(1115, 1116));

    [Fact]
    public void Case_1116() => Assert.Equal(2233, Calc.Add(1116, 1117));

    [Fact]
    public void Case_1117() => Assert.Equal(2235, Calc.Add(1117, 1118));

    [Fact]
    public void Case_1118() => Assert.Equal(2237, Calc.Add(1118, 1119));

    [Fact]
    public void Case_1119() => Assert.Equal(2239, Calc.Add(1119, 1120));

    [Fact]
    public void Case_1120() => Assert.Equal(2241, Calc.Add(1120, 1121));
}
#endif
