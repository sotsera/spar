#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.XunitSuite.Scale;

public sealed class ScaleTests80
{
    [Fact]
    public void Case_8001() => Assert.Equal(16003, Calc.Add(8001, 8002));

    [Fact]
    public void Case_8002() => Assert.Equal(16005, Calc.Add(8002, 8003));

    [Fact]
    public void Case_8003() => Assert.Equal(16007, Calc.Add(8003, 8004));

    [Fact]
    public void Case_8004() => Assert.Equal(16009, Calc.Add(8004, 8005));

    [Fact]
    public void Case_8005() => Assert.Equal(16011, Calc.Add(8005, 8006));

    [Fact]
    public void Case_8006() => Assert.Equal(16013, Calc.Add(8006, 8007));

    [Fact]
    public void Case_8007() => Assert.Equal(16015, Calc.Add(8007, 8008));

    [Fact]
    public void Case_8008() => Assert.Equal(16017, Calc.Add(8008, 8009));

    [Fact]
    public void Case_8009() => Assert.Equal(16019, Calc.Add(8009, 8010));

    [Fact]
    public void Case_8010() => Assert.Equal(16021, Calc.Add(8010, 8011));

    [Fact]
    public void Case_8011() => Assert.Equal(16023, Calc.Add(8011, 8012));

    [Fact]
    public void Case_8012() => Assert.Equal(16025, Calc.Add(8012, 8013));

    [Fact]
    public void Case_8013() => Assert.Equal(16027, Calc.Add(8013, 8014));

    [Fact]
    public void Case_8014() => Assert.Equal(16029, Calc.Add(8014, 8015));

    [Fact]
    public void Case_8015() => Assert.Equal(16031, Calc.Add(8015, 8016));

    [Fact]
    public void Case_8016() => Assert.Equal(16033, Calc.Add(8016, 8017));

    [Fact]
    public void Case_8017() => Assert.Equal(16035, Calc.Add(8017, 8018));

    [Fact]
    public void Case_8018() => Assert.Equal(16037, Calc.Add(8018, 8019));

    [Fact]
    public void Case_8019() => Assert.Equal(16039, Calc.Add(8019, 8020));

    [Fact]
    public void Case_8020() => Assert.Equal(16041, Calc.Add(8020, 8021));
}
#endif
