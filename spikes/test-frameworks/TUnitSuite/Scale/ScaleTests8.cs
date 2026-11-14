#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests8
{
    [Test]
    public async Task Case_801() => await Assert.That(Calc.Add(801, 802)).IsEqualTo(1603);

    [Test]
    public async Task Case_802() => await Assert.That(Calc.Add(802, 803)).IsEqualTo(1605);

    [Test]
    public async Task Case_803() => await Assert.That(Calc.Add(803, 804)).IsEqualTo(1607);

    [Test]
    public async Task Case_804() => await Assert.That(Calc.Add(804, 805)).IsEqualTo(1609);

    [Test]
    public async Task Case_805() => await Assert.That(Calc.Add(805, 806)).IsEqualTo(1611);

    [Test]
    public async Task Case_806() => await Assert.That(Calc.Add(806, 807)).IsEqualTo(1613);

    [Test]
    public async Task Case_807() => await Assert.That(Calc.Add(807, 808)).IsEqualTo(1615);

    [Test]
    public async Task Case_808() => await Assert.That(Calc.Add(808, 809)).IsEqualTo(1617);

    [Test]
    public async Task Case_809() => await Assert.That(Calc.Add(809, 810)).IsEqualTo(1619);

    [Test]
    public async Task Case_810() => await Assert.That(Calc.Add(810, 811)).IsEqualTo(1621);

    [Test]
    public async Task Case_811() => await Assert.That(Calc.Add(811, 812)).IsEqualTo(1623);

    [Test]
    public async Task Case_812() => await Assert.That(Calc.Add(812, 813)).IsEqualTo(1625);

    [Test]
    public async Task Case_813() => await Assert.That(Calc.Add(813, 814)).IsEqualTo(1627);

    [Test]
    public async Task Case_814() => await Assert.That(Calc.Add(814, 815)).IsEqualTo(1629);

    [Test]
    public async Task Case_815() => await Assert.That(Calc.Add(815, 816)).IsEqualTo(1631);

    [Test]
    public async Task Case_816() => await Assert.That(Calc.Add(816, 817)).IsEqualTo(1633);

    [Test]
    public async Task Case_817() => await Assert.That(Calc.Add(817, 818)).IsEqualTo(1635);

    [Test]
    public async Task Case_818() => await Assert.That(Calc.Add(818, 819)).IsEqualTo(1637);

    [Test]
    public async Task Case_819() => await Assert.That(Calc.Add(819, 820)).IsEqualTo(1639);

    [Test]
    public async Task Case_820() => await Assert.That(Calc.Add(820, 821)).IsEqualTo(1641);
}
#endif
