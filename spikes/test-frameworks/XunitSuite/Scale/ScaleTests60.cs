#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.XunitSuite.Scale;

public sealed class ScaleTests60
{
    [Fact]
    public void Case_6001() => Assert.Equal(12003, Calc.Add(6001, 6002));

    [Fact]
    public void Case_6002() => Assert.Equal(12005, Calc.Add(6002, 6003));

    [Fact]
    public void Case_6003() => Assert.Equal(12007, Calc.Add(6003, 6004));

    [Fact]
    public void Case_6004() => Assert.Equal(12009, Calc.Add(6004, 6005));

    [Fact]
    public void Case_6005() => Assert.Equal(12011, Calc.Add(6005, 6006));

    [Fact]
    public void Case_6006() => Assert.Equal(12013, Calc.Add(6006, 6007));

    [Fact]
    public void Case_6007() => Assert.Equal(12015, Calc.Add(6007, 6008));

    [Fact]
    public void Case_6008() => Assert.Equal(12017, Calc.Add(6008, 6009));

    [Fact]
    public void Case_6009() => Assert.Equal(12019, Calc.Add(6009, 6010));

    [Fact]
    public void Case_6010() => Assert.Equal(12021, Calc.Add(6010, 6011));

    [Fact]
    public void Case_6011() => Assert.Equal(12023, Calc.Add(6011, 6012));

    [Fact]
    public void Case_6012() => Assert.Equal(12025, Calc.Add(6012, 6013));

    [Fact]
    public void Case_6013() => Assert.Equal(12027, Calc.Add(6013, 6014));

    [Fact]
    public void Case_6014() => Assert.Equal(12029, Calc.Add(6014, 6015));

    [Fact]
    public void Case_6015() => Assert.Equal(12031, Calc.Add(6015, 6016));

    [Fact]
    public void Case_6016() => Assert.Equal(12033, Calc.Add(6016, 6017));

    [Fact]
    public void Case_6017() => Assert.Equal(12035, Calc.Add(6017, 6018));

    [Fact]
    public void Case_6018() => Assert.Equal(12037, Calc.Add(6018, 6019));

    [Fact]
    public void Case_6019() => Assert.Equal(12039, Calc.Add(6019, 6020));

    [Fact]
    public void Case_6020() => Assert.Equal(12041, Calc.Add(6020, 6021));
}
#endif
