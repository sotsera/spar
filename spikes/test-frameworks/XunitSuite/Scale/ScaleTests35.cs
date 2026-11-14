#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.XunitSuite.Scale;

public sealed class ScaleTests35
{
    [Fact]
    public void Case_3501() => Assert.Equal(7003, Calc.Add(3501, 3502));

    [Fact]
    public void Case_3502() => Assert.Equal(7005, Calc.Add(3502, 3503));

    [Fact]
    public void Case_3503() => Assert.Equal(7007, Calc.Add(3503, 3504));

    [Fact]
    public void Case_3504() => Assert.Equal(7009, Calc.Add(3504, 3505));

    [Fact]
    public void Case_3505() => Assert.Equal(7011, Calc.Add(3505, 3506));

    [Fact]
    public void Case_3506() => Assert.Equal(7013, Calc.Add(3506, 3507));

    [Fact]
    public void Case_3507() => Assert.Equal(7015, Calc.Add(3507, 3508));

    [Fact]
    public void Case_3508() => Assert.Equal(7017, Calc.Add(3508, 3509));

    [Fact]
    public void Case_3509() => Assert.Equal(7019, Calc.Add(3509, 3510));

    [Fact]
    public void Case_3510() => Assert.Equal(7021, Calc.Add(3510, 3511));

    [Fact]
    public void Case_3511() => Assert.Equal(7023, Calc.Add(3511, 3512));

    [Fact]
    public void Case_3512() => Assert.Equal(7025, Calc.Add(3512, 3513));

    [Fact]
    public void Case_3513() => Assert.Equal(7027, Calc.Add(3513, 3514));

    [Fact]
    public void Case_3514() => Assert.Equal(7029, Calc.Add(3514, 3515));

    [Fact]
    public void Case_3515() => Assert.Equal(7031, Calc.Add(3515, 3516));

    [Fact]
    public void Case_3516() => Assert.Equal(7033, Calc.Add(3516, 3517));

    [Fact]
    public void Case_3517() => Assert.Equal(7035, Calc.Add(3517, 3518));

    [Fact]
    public void Case_3518() => Assert.Equal(7037, Calc.Add(3518, 3519));

    [Fact]
    public void Case_3519() => Assert.Equal(7039, Calc.Add(3519, 3520));

    [Fact]
    public void Case_3520() => Assert.Equal(7041, Calc.Add(3520, 3521));
}
#endif
