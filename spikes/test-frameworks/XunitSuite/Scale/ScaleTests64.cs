#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.XunitSuite.Scale;

public sealed class ScaleTests64
{
    [Fact]
    public void Case_6401() => Assert.Equal(12803, Calc.Add(6401, 6402));

    [Fact]
    public void Case_6402() => Assert.Equal(12805, Calc.Add(6402, 6403));

    [Fact]
    public void Case_6403() => Assert.Equal(12807, Calc.Add(6403, 6404));

    [Fact]
    public void Case_6404() => Assert.Equal(12809, Calc.Add(6404, 6405));

    [Fact]
    public void Case_6405() => Assert.Equal(12811, Calc.Add(6405, 6406));

    [Fact]
    public void Case_6406() => Assert.Equal(12813, Calc.Add(6406, 6407));

    [Fact]
    public void Case_6407() => Assert.Equal(12815, Calc.Add(6407, 6408));

    [Fact]
    public void Case_6408() => Assert.Equal(12817, Calc.Add(6408, 6409));

    [Fact]
    public void Case_6409() => Assert.Equal(12819, Calc.Add(6409, 6410));

    [Fact]
    public void Case_6410() => Assert.Equal(12821, Calc.Add(6410, 6411));

    [Fact]
    public void Case_6411() => Assert.Equal(12823, Calc.Add(6411, 6412));

    [Fact]
    public void Case_6412() => Assert.Equal(12825, Calc.Add(6412, 6413));

    [Fact]
    public void Case_6413() => Assert.Equal(12827, Calc.Add(6413, 6414));

    [Fact]
    public void Case_6414() => Assert.Equal(12829, Calc.Add(6414, 6415));

    [Fact]
    public void Case_6415() => Assert.Equal(12831, Calc.Add(6415, 6416));

    [Fact]
    public void Case_6416() => Assert.Equal(12833, Calc.Add(6416, 6417));

    [Fact]
    public void Case_6417() => Assert.Equal(12835, Calc.Add(6417, 6418));

    [Fact]
    public void Case_6418() => Assert.Equal(12837, Calc.Add(6418, 6419));

    [Fact]
    public void Case_6419() => Assert.Equal(12839, Calc.Add(6419, 6420));

    [Fact]
    public void Case_6420() => Assert.Equal(12841, Calc.Add(6420, 6421));
}
#endif
