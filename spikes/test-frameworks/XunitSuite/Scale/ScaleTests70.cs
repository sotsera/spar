#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.XunitSuite.Scale;

public sealed class ScaleTests70
{
    [Fact]
    public void Case_7001() => Assert.Equal(14003, Calc.Add(7001, 7002));

    [Fact]
    public void Case_7002() => Assert.Equal(14005, Calc.Add(7002, 7003));

    [Fact]
    public void Case_7003() => Assert.Equal(14007, Calc.Add(7003, 7004));

    [Fact]
    public void Case_7004() => Assert.Equal(14009, Calc.Add(7004, 7005));

    [Fact]
    public void Case_7005() => Assert.Equal(14011, Calc.Add(7005, 7006));

    [Fact]
    public void Case_7006() => Assert.Equal(14013, Calc.Add(7006, 7007));

    [Fact]
    public void Case_7007() => Assert.Equal(14015, Calc.Add(7007, 7008));

    [Fact]
    public void Case_7008() => Assert.Equal(14017, Calc.Add(7008, 7009));

    [Fact]
    public void Case_7009() => Assert.Equal(14019, Calc.Add(7009, 7010));

    [Fact]
    public void Case_7010() => Assert.Equal(14021, Calc.Add(7010, 7011));

    [Fact]
    public void Case_7011() => Assert.Equal(14023, Calc.Add(7011, 7012));

    [Fact]
    public void Case_7012() => Assert.Equal(14025, Calc.Add(7012, 7013));

    [Fact]
    public void Case_7013() => Assert.Equal(14027, Calc.Add(7013, 7014));

    [Fact]
    public void Case_7014() => Assert.Equal(14029, Calc.Add(7014, 7015));

    [Fact]
    public void Case_7015() => Assert.Equal(14031, Calc.Add(7015, 7016));

    [Fact]
    public void Case_7016() => Assert.Equal(14033, Calc.Add(7016, 7017));

    [Fact]
    public void Case_7017() => Assert.Equal(14035, Calc.Add(7017, 7018));

    [Fact]
    public void Case_7018() => Assert.Equal(14037, Calc.Add(7018, 7019));

    [Fact]
    public void Case_7019() => Assert.Equal(14039, Calc.Add(7019, 7020));

    [Fact]
    public void Case_7020() => Assert.Equal(14041, Calc.Add(7020, 7021));
}
#endif
