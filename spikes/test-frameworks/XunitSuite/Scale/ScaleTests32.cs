#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.XunitSuite.Scale;

public sealed class ScaleTests32
{
    [Fact]
    public void Case_3201() => Assert.Equal(6403, Calc.Add(3201, 3202));

    [Fact]
    public void Case_3202() => Assert.Equal(6405, Calc.Add(3202, 3203));

    [Fact]
    public void Case_3203() => Assert.Equal(6407, Calc.Add(3203, 3204));

    [Fact]
    public void Case_3204() => Assert.Equal(6409, Calc.Add(3204, 3205));

    [Fact]
    public void Case_3205() => Assert.Equal(6411, Calc.Add(3205, 3206));

    [Fact]
    public void Case_3206() => Assert.Equal(6413, Calc.Add(3206, 3207));

    [Fact]
    public void Case_3207() => Assert.Equal(6415, Calc.Add(3207, 3208));

    [Fact]
    public void Case_3208() => Assert.Equal(6417, Calc.Add(3208, 3209));

    [Fact]
    public void Case_3209() => Assert.Equal(6419, Calc.Add(3209, 3210));

    [Fact]
    public void Case_3210() => Assert.Equal(6421, Calc.Add(3210, 3211));

    [Fact]
    public void Case_3211() => Assert.Equal(6423, Calc.Add(3211, 3212));

    [Fact]
    public void Case_3212() => Assert.Equal(6425, Calc.Add(3212, 3213));

    [Fact]
    public void Case_3213() => Assert.Equal(6427, Calc.Add(3213, 3214));

    [Fact]
    public void Case_3214() => Assert.Equal(6429, Calc.Add(3214, 3215));

    [Fact]
    public void Case_3215() => Assert.Equal(6431, Calc.Add(3215, 3216));

    [Fact]
    public void Case_3216() => Assert.Equal(6433, Calc.Add(3216, 3217));

    [Fact]
    public void Case_3217() => Assert.Equal(6435, Calc.Add(3217, 3218));

    [Fact]
    public void Case_3218() => Assert.Equal(6437, Calc.Add(3218, 3219));

    [Fact]
    public void Case_3219() => Assert.Equal(6439, Calc.Add(3219, 3220));

    [Fact]
    public void Case_3220() => Assert.Equal(6441, Calc.Add(3220, 3221));
}
#endif
