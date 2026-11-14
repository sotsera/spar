#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.XunitSuite.Scale;

public sealed class ScaleTests4
{
    [Fact]
    public void Case_401() => Assert.Equal(803, Calc.Add(401, 402));

    [Fact]
    public void Case_402() => Assert.Equal(805, Calc.Add(402, 403));

    [Fact]
    public void Case_403() => Assert.Equal(807, Calc.Add(403, 404));

    [Fact]
    public void Case_404() => Assert.Equal(809, Calc.Add(404, 405));

    [Fact]
    public void Case_405() => Assert.Equal(811, Calc.Add(405, 406));

    [Fact]
    public void Case_406() => Assert.Equal(813, Calc.Add(406, 407));

    [Fact]
    public void Case_407() => Assert.Equal(815, Calc.Add(407, 408));

    [Fact]
    public void Case_408() => Assert.Equal(817, Calc.Add(408, 409));

    [Fact]
    public void Case_409() => Assert.Equal(819, Calc.Add(409, 410));

    [Fact]
    public void Case_410() => Assert.Equal(821, Calc.Add(410, 411));

    [Fact]
    public void Case_411() => Assert.Equal(823, Calc.Add(411, 412));

    [Fact]
    public void Case_412() => Assert.Equal(825, Calc.Add(412, 413));

    [Fact]
    public void Case_413() => Assert.Equal(827, Calc.Add(413, 414));

    [Fact]
    public void Case_414() => Assert.Equal(829, Calc.Add(414, 415));

    [Fact]
    public void Case_415() => Assert.Equal(831, Calc.Add(415, 416));

    [Fact]
    public void Case_416() => Assert.Equal(833, Calc.Add(416, 417));

    [Fact]
    public void Case_417() => Assert.Equal(835, Calc.Add(417, 418));

    [Fact]
    public void Case_418() => Assert.Equal(837, Calc.Add(418, 419));

    [Fact]
    public void Case_419() => Assert.Equal(839, Calc.Add(419, 420));

    [Fact]
    public void Case_420() => Assert.Equal(841, Calc.Add(420, 421));
}
#endif
