#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.XunitSuite.Scale;

public sealed class ScaleTests21
{
    [Fact]
    public void Case_2101() => Assert.Equal(4203, Calc.Add(2101, 2102));

    [Fact]
    public void Case_2102() => Assert.Equal(4205, Calc.Add(2102, 2103));

    [Fact]
    public void Case_2103() => Assert.Equal(4207, Calc.Add(2103, 2104));

    [Fact]
    public void Case_2104() => Assert.Equal(4209, Calc.Add(2104, 2105));

    [Fact]
    public void Case_2105() => Assert.Equal(4211, Calc.Add(2105, 2106));

    [Fact]
    public void Case_2106() => Assert.Equal(4213, Calc.Add(2106, 2107));

    [Fact]
    public void Case_2107() => Assert.Equal(4215, Calc.Add(2107, 2108));

    [Fact]
    public void Case_2108() => Assert.Equal(4217, Calc.Add(2108, 2109));

    [Fact]
    public void Case_2109() => Assert.Equal(4219, Calc.Add(2109, 2110));

    [Fact]
    public void Case_2110() => Assert.Equal(4221, Calc.Add(2110, 2111));

    [Fact]
    public void Case_2111() => Assert.Equal(4223, Calc.Add(2111, 2112));

    [Fact]
    public void Case_2112() => Assert.Equal(4225, Calc.Add(2112, 2113));

    [Fact]
    public void Case_2113() => Assert.Equal(4227, Calc.Add(2113, 2114));

    [Fact]
    public void Case_2114() => Assert.Equal(4229, Calc.Add(2114, 2115));

    [Fact]
    public void Case_2115() => Assert.Equal(4231, Calc.Add(2115, 2116));

    [Fact]
    public void Case_2116() => Assert.Equal(4233, Calc.Add(2116, 2117));

    [Fact]
    public void Case_2117() => Assert.Equal(4235, Calc.Add(2117, 2118));

    [Fact]
    public void Case_2118() => Assert.Equal(4237, Calc.Add(2118, 2119));

    [Fact]
    public void Case_2119() => Assert.Equal(4239, Calc.Add(2119, 2120));

    [Fact]
    public void Case_2120() => Assert.Equal(4241, Calc.Add(2120, 2121));
}
#endif
