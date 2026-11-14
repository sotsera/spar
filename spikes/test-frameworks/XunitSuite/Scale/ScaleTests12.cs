#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.XunitSuite.Scale;

public sealed class ScaleTests12
{
    [Fact]
    public void Case_1201() => Assert.Equal(2403, Calc.Add(1201, 1202));

    [Fact]
    public void Case_1202() => Assert.Equal(2405, Calc.Add(1202, 1203));

    [Fact]
    public void Case_1203() => Assert.Equal(2407, Calc.Add(1203, 1204));

    [Fact]
    public void Case_1204() => Assert.Equal(2409, Calc.Add(1204, 1205));

    [Fact]
    public void Case_1205() => Assert.Equal(2411, Calc.Add(1205, 1206));

    [Fact]
    public void Case_1206() => Assert.Equal(2413, Calc.Add(1206, 1207));

    [Fact]
    public void Case_1207() => Assert.Equal(2415, Calc.Add(1207, 1208));

    [Fact]
    public void Case_1208() => Assert.Equal(2417, Calc.Add(1208, 1209));

    [Fact]
    public void Case_1209() => Assert.Equal(2419, Calc.Add(1209, 1210));

    [Fact]
    public void Case_1210() => Assert.Equal(2421, Calc.Add(1210, 1211));

    [Fact]
    public void Case_1211() => Assert.Equal(2423, Calc.Add(1211, 1212));

    [Fact]
    public void Case_1212() => Assert.Equal(2425, Calc.Add(1212, 1213));

    [Fact]
    public void Case_1213() => Assert.Equal(2427, Calc.Add(1213, 1214));

    [Fact]
    public void Case_1214() => Assert.Equal(2429, Calc.Add(1214, 1215));

    [Fact]
    public void Case_1215() => Assert.Equal(2431, Calc.Add(1215, 1216));

    [Fact]
    public void Case_1216() => Assert.Equal(2433, Calc.Add(1216, 1217));

    [Fact]
    public void Case_1217() => Assert.Equal(2435, Calc.Add(1217, 1218));

    [Fact]
    public void Case_1218() => Assert.Equal(2437, Calc.Add(1218, 1219));

    [Fact]
    public void Case_1219() => Assert.Equal(2439, Calc.Add(1219, 1220));

    [Fact]
    public void Case_1220() => Assert.Equal(2441, Calc.Add(1220, 1221));
}
#endif
