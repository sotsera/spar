#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.XunitSuite.Scale;

public sealed class ScaleTests3
{
    [Fact]
    public void Case_301() => Assert.Equal(603, Calc.Add(301, 302));

    [Fact]
    public void Case_302() => Assert.Equal(605, Calc.Add(302, 303));

    [Fact]
    public void Case_303() => Assert.Equal(607, Calc.Add(303, 304));

    [Fact]
    public void Case_304() => Assert.Equal(609, Calc.Add(304, 305));

    [Fact]
    public void Case_305() => Assert.Equal(611, Calc.Add(305, 306));

    [Fact]
    public void Case_306() => Assert.Equal(613, Calc.Add(306, 307));

    [Fact]
    public void Case_307() => Assert.Equal(615, Calc.Add(307, 308));

    [Fact]
    public void Case_308() => Assert.Equal(617, Calc.Add(308, 309));

    [Fact]
    public void Case_309() => Assert.Equal(619, Calc.Add(309, 310));

    [Fact]
    public void Case_310() => Assert.Equal(621, Calc.Add(310, 311));

    [Fact]
    public void Case_311() => Assert.Equal(623, Calc.Add(311, 312));

    [Fact]
    public void Case_312() => Assert.Equal(625, Calc.Add(312, 313));

    [Fact]
    public void Case_313() => Assert.Equal(627, Calc.Add(313, 314));

    [Fact]
    public void Case_314() => Assert.Equal(629, Calc.Add(314, 315));

    [Fact]
    public void Case_315() => Assert.Equal(631, Calc.Add(315, 316));

    [Fact]
    public void Case_316() => Assert.Equal(633, Calc.Add(316, 317));

    [Fact]
    public void Case_317() => Assert.Equal(635, Calc.Add(317, 318));

    [Fact]
    public void Case_318() => Assert.Equal(637, Calc.Add(318, 319));

    [Fact]
    public void Case_319() => Assert.Equal(639, Calc.Add(319, 320));

    [Fact]
    public void Case_320() => Assert.Equal(641, Calc.Add(320, 321));
}
#endif
