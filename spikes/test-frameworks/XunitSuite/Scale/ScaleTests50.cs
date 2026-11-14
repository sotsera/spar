#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.XunitSuite.Scale;

public sealed class ScaleTests50
{
    [Fact]
    public void Case_5001() => Assert.Equal(10003, Calc.Add(5001, 5002));

    [Fact]
    public void Case_5002() => Assert.Equal(10005, Calc.Add(5002, 5003));

    [Fact]
    public void Case_5003() => Assert.Equal(10007, Calc.Add(5003, 5004));

    [Fact]
    public void Case_5004() => Assert.Equal(10009, Calc.Add(5004, 5005));

    [Fact]
    public void Case_5005() => Assert.Equal(10011, Calc.Add(5005, 5006));

    [Fact]
    public void Case_5006() => Assert.Equal(10013, Calc.Add(5006, 5007));

    [Fact]
    public void Case_5007() => Assert.Equal(10015, Calc.Add(5007, 5008));

    [Fact]
    public void Case_5008() => Assert.Equal(10017, Calc.Add(5008, 5009));

    [Fact]
    public void Case_5009() => Assert.Equal(10019, Calc.Add(5009, 5010));

    [Fact]
    public void Case_5010() => Assert.Equal(10021, Calc.Add(5010, 5011));

    [Fact]
    public void Case_5011() => Assert.Equal(10023, Calc.Add(5011, 5012));

    [Fact]
    public void Case_5012() => Assert.Equal(10025, Calc.Add(5012, 5013));

    [Fact]
    public void Case_5013() => Assert.Equal(10027, Calc.Add(5013, 5014));

    [Fact]
    public void Case_5014() => Assert.Equal(10029, Calc.Add(5014, 5015));

    [Fact]
    public void Case_5015() => Assert.Equal(10031, Calc.Add(5015, 5016));

    [Fact]
    public void Case_5016() => Assert.Equal(10033, Calc.Add(5016, 5017));

    [Fact]
    public void Case_5017() => Assert.Equal(10035, Calc.Add(5017, 5018));

    [Fact]
    public void Case_5018() => Assert.Equal(10037, Calc.Add(5018, 5019));

    [Fact]
    public void Case_5019() => Assert.Equal(10039, Calc.Add(5019, 5020));

    [Fact]
    public void Case_5020() => Assert.Equal(10041, Calc.Add(5020, 5021));
}
#endif
