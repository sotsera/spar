#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.XunitSuite.Scale;

public sealed class ScaleTests2
{
    [Fact]
    public void Case_201() => Assert.Equal(403, Calc.Add(201, 202));

    [Fact]
    public void Case_202() => Assert.Equal(405, Calc.Add(202, 203));

    [Fact]
    public void Case_203() => Assert.Equal(407, Calc.Add(203, 204));

    [Fact]
    public void Case_204() => Assert.Equal(409, Calc.Add(204, 205));

    [Fact]
    public void Case_205() => Assert.Equal(411, Calc.Add(205, 206));

    [Fact]
    public void Case_206() => Assert.Equal(413, Calc.Add(206, 207));

    [Fact]
    public void Case_207() => Assert.Equal(415, Calc.Add(207, 208));

    [Fact]
    public void Case_208() => Assert.Equal(417, Calc.Add(208, 209));

    [Fact]
    public void Case_209() => Assert.Equal(419, Calc.Add(209, 210));

    [Fact]
    public void Case_210() => Assert.Equal(421, Calc.Add(210, 211));

    [Fact]
    public void Case_211() => Assert.Equal(423, Calc.Add(211, 212));

    [Fact]
    public void Case_212() => Assert.Equal(425, Calc.Add(212, 213));

    [Fact]
    public void Case_213() => Assert.Equal(427, Calc.Add(213, 214));

    [Fact]
    public void Case_214() => Assert.Equal(429, Calc.Add(214, 215));

    [Fact]
    public void Case_215() => Assert.Equal(431, Calc.Add(215, 216));

    [Fact]
    public void Case_216() => Assert.Equal(433, Calc.Add(216, 217));

    [Fact]
    public void Case_217() => Assert.Equal(435, Calc.Add(217, 218));

    [Fact]
    public void Case_218() => Assert.Equal(437, Calc.Add(218, 219));

    [Fact]
    public void Case_219() => Assert.Equal(439, Calc.Add(219, 220));

    [Fact]
    public void Case_220() => Assert.Equal(441, Calc.Add(220, 221));
}
#endif
