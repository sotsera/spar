#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests18
{
    [Test]
    public async Task Case_1801() => await Assert.That(Calc.Add(1801, 1802)).IsEqualTo(3603);

    [Test]
    public async Task Case_1802() => await Assert.That(Calc.Add(1802, 1803)).IsEqualTo(3605);

    [Test]
    public async Task Case_1803() => await Assert.That(Calc.Add(1803, 1804)).IsEqualTo(3607);

    [Test]
    public async Task Case_1804() => await Assert.That(Calc.Add(1804, 1805)).IsEqualTo(3609);

    [Test]
    public async Task Case_1805() => await Assert.That(Calc.Add(1805, 1806)).IsEqualTo(3611);

    [Test]
    public async Task Case_1806() => await Assert.That(Calc.Add(1806, 1807)).IsEqualTo(3613);

    [Test]
    public async Task Case_1807() => await Assert.That(Calc.Add(1807, 1808)).IsEqualTo(3615);

    [Test]
    public async Task Case_1808() => await Assert.That(Calc.Add(1808, 1809)).IsEqualTo(3617);

    [Test]
    public async Task Case_1809() => await Assert.That(Calc.Add(1809, 1810)).IsEqualTo(3619);

    [Test]
    public async Task Case_1810() => await Assert.That(Calc.Add(1810, 1811)).IsEqualTo(3621);

    [Test]
    public async Task Case_1811() => await Assert.That(Calc.Add(1811, 1812)).IsEqualTo(3623);

    [Test]
    public async Task Case_1812() => await Assert.That(Calc.Add(1812, 1813)).IsEqualTo(3625);

    [Test]
    public async Task Case_1813() => await Assert.That(Calc.Add(1813, 1814)).IsEqualTo(3627);

    [Test]
    public async Task Case_1814() => await Assert.That(Calc.Add(1814, 1815)).IsEqualTo(3629);

    [Test]
    public async Task Case_1815() => await Assert.That(Calc.Add(1815, 1816)).IsEqualTo(3631);

    [Test]
    public async Task Case_1816() => await Assert.That(Calc.Add(1816, 1817)).IsEqualTo(3633);

    [Test]
    public async Task Case_1817() => await Assert.That(Calc.Add(1817, 1818)).IsEqualTo(3635);

    [Test]
    public async Task Case_1818() => await Assert.That(Calc.Add(1818, 1819)).IsEqualTo(3637);

    [Test]
    public async Task Case_1819() => await Assert.That(Calc.Add(1819, 1820)).IsEqualTo(3639);

    [Test]
    public async Task Case_1820() => await Assert.That(Calc.Add(1820, 1821)).IsEqualTo(3641);
}
#endif
