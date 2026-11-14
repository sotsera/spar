#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.XunitSuite.Scale;

public sealed class ScaleTests100
{
    [Fact]
    public void Case_10001() => Assert.Equal(20003, Calc.Add(10001, 10002));

    [Fact]
    public void Case_10002() => Assert.Equal(20005, Calc.Add(10002, 10003));

    [Fact]
    public void Case_10003() => Assert.Equal(20007, Calc.Add(10003, 10004));

    [Fact]
    public void Case_10004() => Assert.Equal(20009, Calc.Add(10004, 10005));

    [Fact]
    public void Case_10005() => Assert.Equal(20011, Calc.Add(10005, 10006));

    [Fact]
    public void Case_10006() => Assert.Equal(20013, Calc.Add(10006, 10007));

    [Fact]
    public void Case_10007() => Assert.Equal(20015, Calc.Add(10007, 10008));

    [Fact]
    public void Case_10008() => Assert.Equal(20017, Calc.Add(10008, 10009));

    [Fact]
    public void Case_10009() => Assert.Equal(20019, Calc.Add(10009, 10010));

    [Fact]
    public void Case_10010() => Assert.Equal(20021, Calc.Add(10010, 10011));

    [Fact]
    public void Case_10011() => Assert.Equal(20023, Calc.Add(10011, 10012));

    [Fact]
    public void Case_10012() => Assert.Equal(20025, Calc.Add(10012, 10013));

    [Fact]
    public void Case_10013() => Assert.Equal(20027, Calc.Add(10013, 10014));

    [Fact]
    public void Case_10014() => Assert.Equal(20029, Calc.Add(10014, 10015));

    [Fact]
    public void Case_10015() => Assert.Equal(20031, Calc.Add(10015, 10016));

    [Fact]
    public void Case_10016() => Assert.Equal(20033, Calc.Add(10016, 10017));

    [Fact]
    public void Case_10017() => Assert.Equal(20035, Calc.Add(10017, 10018));

    [Fact]
    public void Case_10018() => Assert.Equal(20037, Calc.Add(10018, 10019));

    [Fact]
    public void Case_10019() => Assert.Equal(20039, Calc.Add(10019, 10020));

    [Fact]
    public void Case_10020() => Assert.Equal(20041, Calc.Add(10020, 10021));
}
#endif
