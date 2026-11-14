#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.XunitSuite.Scale;

public sealed class ScaleTests75
{
    [Fact]
    public void Case_7501() => Assert.Equal(15003, Calc.Add(7501, 7502));

    [Fact]
    public void Case_7502() => Assert.Equal(15005, Calc.Add(7502, 7503));

    [Fact]
    public void Case_7503() => Assert.Equal(15007, Calc.Add(7503, 7504));

    [Fact]
    public void Case_7504() => Assert.Equal(15009, Calc.Add(7504, 7505));

    [Fact]
    public void Case_7505() => Assert.Equal(15011, Calc.Add(7505, 7506));

    [Fact]
    public void Case_7506() => Assert.Equal(15013, Calc.Add(7506, 7507));

    [Fact]
    public void Case_7507() => Assert.Equal(15015, Calc.Add(7507, 7508));

    [Fact]
    public void Case_7508() => Assert.Equal(15017, Calc.Add(7508, 7509));

    [Fact]
    public void Case_7509() => Assert.Equal(15019, Calc.Add(7509, 7510));

    [Fact]
    public void Case_7510() => Assert.Equal(15021, Calc.Add(7510, 7511));

    [Fact]
    public void Case_7511() => Assert.Equal(15023, Calc.Add(7511, 7512));

    [Fact]
    public void Case_7512() => Assert.Equal(15025, Calc.Add(7512, 7513));

    [Fact]
    public void Case_7513() => Assert.Equal(15027, Calc.Add(7513, 7514));

    [Fact]
    public void Case_7514() => Assert.Equal(15029, Calc.Add(7514, 7515));

    [Fact]
    public void Case_7515() => Assert.Equal(15031, Calc.Add(7515, 7516));

    [Fact]
    public void Case_7516() => Assert.Equal(15033, Calc.Add(7516, 7517));

    [Fact]
    public void Case_7517() => Assert.Equal(15035, Calc.Add(7517, 7518));

    [Fact]
    public void Case_7518() => Assert.Equal(15037, Calc.Add(7518, 7519));

    [Fact]
    public void Case_7519() => Assert.Equal(15039, Calc.Add(7519, 7520));

    [Fact]
    public void Case_7520() => Assert.Equal(15041, Calc.Add(7520, 7521));
}
#endif
