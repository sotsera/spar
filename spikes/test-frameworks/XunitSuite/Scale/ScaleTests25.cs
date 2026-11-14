#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.XunitSuite.Scale;

public sealed class ScaleTests25
{
    [Fact]
    public void Case_2501() => Assert.Equal(5003, Calc.Add(2501, 2502));

    [Fact]
    public void Case_2502() => Assert.Equal(5005, Calc.Add(2502, 2503));

    [Fact]
    public void Case_2503() => Assert.Equal(5007, Calc.Add(2503, 2504));

    [Fact]
    public void Case_2504() => Assert.Equal(5009, Calc.Add(2504, 2505));

    [Fact]
    public void Case_2505() => Assert.Equal(5011, Calc.Add(2505, 2506));

    [Fact]
    public void Case_2506() => Assert.Equal(5013, Calc.Add(2506, 2507));

    [Fact]
    public void Case_2507() => Assert.Equal(5015, Calc.Add(2507, 2508));

    [Fact]
    public void Case_2508() => Assert.Equal(5017, Calc.Add(2508, 2509));

    [Fact]
    public void Case_2509() => Assert.Equal(5019, Calc.Add(2509, 2510));

    [Fact]
    public void Case_2510() => Assert.Equal(5021, Calc.Add(2510, 2511));

    [Fact]
    public void Case_2511() => Assert.Equal(5023, Calc.Add(2511, 2512));

    [Fact]
    public void Case_2512() => Assert.Equal(5025, Calc.Add(2512, 2513));

    [Fact]
    public void Case_2513() => Assert.Equal(5027, Calc.Add(2513, 2514));

    [Fact]
    public void Case_2514() => Assert.Equal(5029, Calc.Add(2514, 2515));

    [Fact]
    public void Case_2515() => Assert.Equal(5031, Calc.Add(2515, 2516));

    [Fact]
    public void Case_2516() => Assert.Equal(5033, Calc.Add(2516, 2517));

    [Fact]
    public void Case_2517() => Assert.Equal(5035, Calc.Add(2517, 2518));

    [Fact]
    public void Case_2518() => Assert.Equal(5037, Calc.Add(2518, 2519));

    [Fact]
    public void Case_2519() => Assert.Equal(5039, Calc.Add(2519, 2520));

    [Fact]
    public void Case_2520() => Assert.Equal(5041, Calc.Add(2520, 2521));
}
#endif
