#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.XunitSuite.Scale;

public sealed class ScaleTests40
{
    [Fact]
    public void Case_4001() => Assert.Equal(8003, Calc.Add(4001, 4002));

    [Fact]
    public void Case_4002() => Assert.Equal(8005, Calc.Add(4002, 4003));

    [Fact]
    public void Case_4003() => Assert.Equal(8007, Calc.Add(4003, 4004));

    [Fact]
    public void Case_4004() => Assert.Equal(8009, Calc.Add(4004, 4005));

    [Fact]
    public void Case_4005() => Assert.Equal(8011, Calc.Add(4005, 4006));

    [Fact]
    public void Case_4006() => Assert.Equal(8013, Calc.Add(4006, 4007));

    [Fact]
    public void Case_4007() => Assert.Equal(8015, Calc.Add(4007, 4008));

    [Fact]
    public void Case_4008() => Assert.Equal(8017, Calc.Add(4008, 4009));

    [Fact]
    public void Case_4009() => Assert.Equal(8019, Calc.Add(4009, 4010));

    [Fact]
    public void Case_4010() => Assert.Equal(8021, Calc.Add(4010, 4011));

    [Fact]
    public void Case_4011() => Assert.Equal(8023, Calc.Add(4011, 4012));

    [Fact]
    public void Case_4012() => Assert.Equal(8025, Calc.Add(4012, 4013));

    [Fact]
    public void Case_4013() => Assert.Equal(8027, Calc.Add(4013, 4014));

    [Fact]
    public void Case_4014() => Assert.Equal(8029, Calc.Add(4014, 4015));

    [Fact]
    public void Case_4015() => Assert.Equal(8031, Calc.Add(4015, 4016));

    [Fact]
    public void Case_4016() => Assert.Equal(8033, Calc.Add(4016, 4017));

    [Fact]
    public void Case_4017() => Assert.Equal(8035, Calc.Add(4017, 4018));

    [Fact]
    public void Case_4018() => Assert.Equal(8037, Calc.Add(4018, 4019));

    [Fact]
    public void Case_4019() => Assert.Equal(8039, Calc.Add(4019, 4020));

    [Fact]
    public void Case_4020() => Assert.Equal(8041, Calc.Add(4020, 4021));
}
#endif
