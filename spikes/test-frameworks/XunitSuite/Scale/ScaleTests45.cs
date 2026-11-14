#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.XunitSuite.Scale;

public sealed class ScaleTests45
{
    [Fact]
    public void Case_4501() => Assert.Equal(9003, Calc.Add(4501, 4502));

    [Fact]
    public void Case_4502() => Assert.Equal(9005, Calc.Add(4502, 4503));

    [Fact]
    public void Case_4503() => Assert.Equal(9007, Calc.Add(4503, 4504));

    [Fact]
    public void Case_4504() => Assert.Equal(9009, Calc.Add(4504, 4505));

    [Fact]
    public void Case_4505() => Assert.Equal(9011, Calc.Add(4505, 4506));

    [Fact]
    public void Case_4506() => Assert.Equal(9013, Calc.Add(4506, 4507));

    [Fact]
    public void Case_4507() => Assert.Equal(9015, Calc.Add(4507, 4508));

    [Fact]
    public void Case_4508() => Assert.Equal(9017, Calc.Add(4508, 4509));

    [Fact]
    public void Case_4509() => Assert.Equal(9019, Calc.Add(4509, 4510));

    [Fact]
    public void Case_4510() => Assert.Equal(9021, Calc.Add(4510, 4511));

    [Fact]
    public void Case_4511() => Assert.Equal(9023, Calc.Add(4511, 4512));

    [Fact]
    public void Case_4512() => Assert.Equal(9025, Calc.Add(4512, 4513));

    [Fact]
    public void Case_4513() => Assert.Equal(9027, Calc.Add(4513, 4514));

    [Fact]
    public void Case_4514() => Assert.Equal(9029, Calc.Add(4514, 4515));

    [Fact]
    public void Case_4515() => Assert.Equal(9031, Calc.Add(4515, 4516));

    [Fact]
    public void Case_4516() => Assert.Equal(9033, Calc.Add(4516, 4517));

    [Fact]
    public void Case_4517() => Assert.Equal(9035, Calc.Add(4517, 4518));

    [Fact]
    public void Case_4518() => Assert.Equal(9037, Calc.Add(4518, 4519));

    [Fact]
    public void Case_4519() => Assert.Equal(9039, Calc.Add(4519, 4520));

    [Fact]
    public void Case_4520() => Assert.Equal(9041, Calc.Add(4520, 4521));
}
#endif
