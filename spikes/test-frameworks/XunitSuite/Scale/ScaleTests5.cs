#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.XunitSuite.Scale;

public sealed class ScaleTests5
{
    [Fact]
    public void Case_501() => Assert.Equal(1003, Calc.Add(501, 502));

    [Fact]
    public void Case_502() => Assert.Equal(1005, Calc.Add(502, 503));

    [Fact]
    public void Case_503() => Assert.Equal(1007, Calc.Add(503, 504));

    [Fact]
    public void Case_504() => Assert.Equal(1009, Calc.Add(504, 505));

    [Fact]
    public void Case_505() => Assert.Equal(1011, Calc.Add(505, 506));

    [Fact]
    public void Case_506() => Assert.Equal(1013, Calc.Add(506, 507));

    [Fact]
    public void Case_507() => Assert.Equal(1015, Calc.Add(507, 508));

    [Fact]
    public void Case_508() => Assert.Equal(1017, Calc.Add(508, 509));

    [Fact]
    public void Case_509() => Assert.Equal(1019, Calc.Add(509, 510));

    [Fact]
    public void Case_510() => Assert.Equal(1021, Calc.Add(510, 511));

    [Fact]
    public void Case_511() => Assert.Equal(1023, Calc.Add(511, 512));

    [Fact]
    public void Case_512() => Assert.Equal(1025, Calc.Add(512, 513));

    [Fact]
    public void Case_513() => Assert.Equal(1027, Calc.Add(513, 514));

    [Fact]
    public void Case_514() => Assert.Equal(1029, Calc.Add(514, 515));

    [Fact]
    public void Case_515() => Assert.Equal(1031, Calc.Add(515, 516));

    [Fact]
    public void Case_516() => Assert.Equal(1033, Calc.Add(516, 517));

    [Fact]
    public void Case_517() => Assert.Equal(1035, Calc.Add(517, 518));

    [Fact]
    public void Case_518() => Assert.Equal(1037, Calc.Add(518, 519));

    [Fact]
    public void Case_519() => Assert.Equal(1039, Calc.Add(519, 520));

    [Fact]
    public void Case_520() => Assert.Equal(1041, Calc.Add(520, 521));
}
#endif
