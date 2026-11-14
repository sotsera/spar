#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.XunitSuite.Scale;

public sealed class ScaleTests30
{
    [Fact]
    public void Case_3001() => Assert.Equal(6003, Calc.Add(3001, 3002));

    [Fact]
    public void Case_3002() => Assert.Equal(6005, Calc.Add(3002, 3003));

    [Fact]
    public void Case_3003() => Assert.Equal(6007, Calc.Add(3003, 3004));

    [Fact]
    public void Case_3004() => Assert.Equal(6009, Calc.Add(3004, 3005));

    [Fact]
    public void Case_3005() => Assert.Equal(6011, Calc.Add(3005, 3006));

    [Fact]
    public void Case_3006() => Assert.Equal(6013, Calc.Add(3006, 3007));

    [Fact]
    public void Case_3007() => Assert.Equal(6015, Calc.Add(3007, 3008));

    [Fact]
    public void Case_3008() => Assert.Equal(6017, Calc.Add(3008, 3009));

    [Fact]
    public void Case_3009() => Assert.Equal(6019, Calc.Add(3009, 3010));

    [Fact]
    public void Case_3010() => Assert.Equal(6021, Calc.Add(3010, 3011));

    [Fact]
    public void Case_3011() => Assert.Equal(6023, Calc.Add(3011, 3012));

    [Fact]
    public void Case_3012() => Assert.Equal(6025, Calc.Add(3012, 3013));

    [Fact]
    public void Case_3013() => Assert.Equal(6027, Calc.Add(3013, 3014));

    [Fact]
    public void Case_3014() => Assert.Equal(6029, Calc.Add(3014, 3015));

    [Fact]
    public void Case_3015() => Assert.Equal(6031, Calc.Add(3015, 3016));

    [Fact]
    public void Case_3016() => Assert.Equal(6033, Calc.Add(3016, 3017));

    [Fact]
    public void Case_3017() => Assert.Equal(6035, Calc.Add(3017, 3018));

    [Fact]
    public void Case_3018() => Assert.Equal(6037, Calc.Add(3018, 3019));

    [Fact]
    public void Case_3019() => Assert.Equal(6039, Calc.Add(3019, 3020));

    [Fact]
    public void Case_3020() => Assert.Equal(6041, Calc.Add(3020, 3021));
}
#endif
