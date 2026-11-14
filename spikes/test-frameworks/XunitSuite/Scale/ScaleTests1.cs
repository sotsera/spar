#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.XunitSuite.Scale;

public sealed class ScaleTests1
{
    [Fact]
    public void Case_101() => Assert.Equal(203, Calc.Add(101, 102));

    [Fact]
    public void Case_102() => Assert.Equal(205, Calc.Add(102, 103));

    [Fact]
    public void Case_103() => Assert.Equal(207, Calc.Add(103, 104));

    [Fact]
    public void Case_104() => Assert.Equal(209, Calc.Add(104, 105));

    [Fact]
    public void Case_105() => Assert.Equal(211, Calc.Add(105, 106));

    [Fact]
    public void Case_106() => Assert.Equal(213, Calc.Add(106, 107));

    [Fact]
    public void Case_107() => Assert.Equal(215, Calc.Add(107, 108));

    [Fact]
    public void Case_108() => Assert.Equal(217, Calc.Add(108, 109));

    [Fact]
    public void Case_109() => Assert.Equal(219, Calc.Add(109, 110));

    [Fact]
    public void Case_110() => Assert.Equal(221, Calc.Add(110, 111));

    [Fact]
    public void Case_111() => Assert.Equal(223, Calc.Add(111, 112));

    [Fact]
    public void Case_112() => Assert.Equal(225, Calc.Add(112, 113));

    [Fact]
    public void Case_113() => Assert.Equal(227, Calc.Add(113, 114));

    [Fact]
    public void Case_114() => Assert.Equal(229, Calc.Add(114, 115));

    [Fact]
    public void Case_115() => Assert.Equal(231, Calc.Add(115, 116));

    [Fact]
    public void Case_116() => Assert.Equal(233, Calc.Add(116, 117));

    [Fact]
    public void Case_117() => Assert.Equal(235, Calc.Add(117, 118));

    [Fact]
    public void Case_118() => Assert.Equal(237, Calc.Add(118, 119));

    [Fact]
    public void Case_119() => Assert.Equal(239, Calc.Add(119, 120));

    [Fact]
    public void Case_120() => Assert.Equal(241, Calc.Add(120, 121));
}
#endif
