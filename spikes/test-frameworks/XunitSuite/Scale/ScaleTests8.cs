#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.XunitSuite.Scale;

public sealed class ScaleTests8
{
    [Fact]
    public void Case_801() => Assert.Equal(1603, Calc.Add(801, 802));

    [Fact]
    public void Case_802() => Assert.Equal(1605, Calc.Add(802, 803));

    [Fact]
    public void Case_803() => Assert.Equal(1607, Calc.Add(803, 804));

    [Fact]
    public void Case_804() => Assert.Equal(1609, Calc.Add(804, 805));

    [Fact]
    public void Case_805() => Assert.Equal(1611, Calc.Add(805, 806));

    [Fact]
    public void Case_806() => Assert.Equal(1613, Calc.Add(806, 807));

    [Fact]
    public void Case_807() => Assert.Equal(1615, Calc.Add(807, 808));

    [Fact]
    public void Case_808() => Assert.Equal(1617, Calc.Add(808, 809));

    [Fact]
    public void Case_809() => Assert.Equal(1619, Calc.Add(809, 810));

    [Fact]
    public void Case_810() => Assert.Equal(1621, Calc.Add(810, 811));

    [Fact]
    public void Case_811() => Assert.Equal(1623, Calc.Add(811, 812));

    [Fact]
    public void Case_812() => Assert.Equal(1625, Calc.Add(812, 813));

    [Fact]
    public void Case_813() => Assert.Equal(1627, Calc.Add(813, 814));

    [Fact]
    public void Case_814() => Assert.Equal(1629, Calc.Add(814, 815));

    [Fact]
    public void Case_815() => Assert.Equal(1631, Calc.Add(815, 816));

    [Fact]
    public void Case_816() => Assert.Equal(1633, Calc.Add(816, 817));

    [Fact]
    public void Case_817() => Assert.Equal(1635, Calc.Add(817, 818));

    [Fact]
    public void Case_818() => Assert.Equal(1637, Calc.Add(818, 819));

    [Fact]
    public void Case_819() => Assert.Equal(1639, Calc.Add(819, 820));

    [Fact]
    public void Case_820() => Assert.Equal(1641, Calc.Add(820, 821));
}
#endif
