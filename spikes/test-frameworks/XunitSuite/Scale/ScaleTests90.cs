#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.XunitSuite.Scale;

public sealed class ScaleTests90
{
    [Fact]
    public void Case_9001() => Assert.Equal(18003, Calc.Add(9001, 9002));

    [Fact]
    public void Case_9002() => Assert.Equal(18005, Calc.Add(9002, 9003));

    [Fact]
    public void Case_9003() => Assert.Equal(18007, Calc.Add(9003, 9004));

    [Fact]
    public void Case_9004() => Assert.Equal(18009, Calc.Add(9004, 9005));

    [Fact]
    public void Case_9005() => Assert.Equal(18011, Calc.Add(9005, 9006));

    [Fact]
    public void Case_9006() => Assert.Equal(18013, Calc.Add(9006, 9007));

    [Fact]
    public void Case_9007() => Assert.Equal(18015, Calc.Add(9007, 9008));

    [Fact]
    public void Case_9008() => Assert.Equal(18017, Calc.Add(9008, 9009));

    [Fact]
    public void Case_9009() => Assert.Equal(18019, Calc.Add(9009, 9010));

    [Fact]
    public void Case_9010() => Assert.Equal(18021, Calc.Add(9010, 9011));

    [Fact]
    public void Case_9011() => Assert.Equal(18023, Calc.Add(9011, 9012));

    [Fact]
    public void Case_9012() => Assert.Equal(18025, Calc.Add(9012, 9013));

    [Fact]
    public void Case_9013() => Assert.Equal(18027, Calc.Add(9013, 9014));

    [Fact]
    public void Case_9014() => Assert.Equal(18029, Calc.Add(9014, 9015));

    [Fact]
    public void Case_9015() => Assert.Equal(18031, Calc.Add(9015, 9016));

    [Fact]
    public void Case_9016() => Assert.Equal(18033, Calc.Add(9016, 9017));

    [Fact]
    public void Case_9017() => Assert.Equal(18035, Calc.Add(9017, 9018));

    [Fact]
    public void Case_9018() => Assert.Equal(18037, Calc.Add(9018, 9019));

    [Fact]
    public void Case_9019() => Assert.Equal(18039, Calc.Add(9019, 9020));

    [Fact]
    public void Case_9020() => Assert.Equal(18041, Calc.Add(9020, 9021));
}
#endif
