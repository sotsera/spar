#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests68
{
    [Test]
    public async Task Case_6801() => await Assert.That(Calc.Add(6801, 6802)).IsEqualTo(13603);

    [Test]
    public async Task Case_6802() => await Assert.That(Calc.Add(6802, 6803)).IsEqualTo(13605);

    [Test]
    public async Task Case_6803() => await Assert.That(Calc.Add(6803, 6804)).IsEqualTo(13607);

    [Test]
    public async Task Case_6804() => await Assert.That(Calc.Add(6804, 6805)).IsEqualTo(13609);

    [Test]
    public async Task Case_6805() => await Assert.That(Calc.Add(6805, 6806)).IsEqualTo(13611);

    [Test]
    public async Task Case_6806() => await Assert.That(Calc.Add(6806, 6807)).IsEqualTo(13613);

    [Test]
    public async Task Case_6807() => await Assert.That(Calc.Add(6807, 6808)).IsEqualTo(13615);

    [Test]
    public async Task Case_6808() => await Assert.That(Calc.Add(6808, 6809)).IsEqualTo(13617);

    [Test]
    public async Task Case_6809() => await Assert.That(Calc.Add(6809, 6810)).IsEqualTo(13619);

    [Test]
    public async Task Case_6810() => await Assert.That(Calc.Add(6810, 6811)).IsEqualTo(13621);

    [Test]
    public async Task Case_6811() => await Assert.That(Calc.Add(6811, 6812)).IsEqualTo(13623);

    [Test]
    public async Task Case_6812() => await Assert.That(Calc.Add(6812, 6813)).IsEqualTo(13625);

    [Test]
    public async Task Case_6813() => await Assert.That(Calc.Add(6813, 6814)).IsEqualTo(13627);

    [Test]
    public async Task Case_6814() => await Assert.That(Calc.Add(6814, 6815)).IsEqualTo(13629);

    [Test]
    public async Task Case_6815() => await Assert.That(Calc.Add(6815, 6816)).IsEqualTo(13631);

    [Test]
    public async Task Case_6816() => await Assert.That(Calc.Add(6816, 6817)).IsEqualTo(13633);

    [Test]
    public async Task Case_6817() => await Assert.That(Calc.Add(6817, 6818)).IsEqualTo(13635);

    [Test]
    public async Task Case_6818() => await Assert.That(Calc.Add(6818, 6819)).IsEqualTo(13637);

    [Test]
    public async Task Case_6819() => await Assert.That(Calc.Add(6819, 6820)).IsEqualTo(13639);

    [Test]
    public async Task Case_6820() => await Assert.That(Calc.Add(6820, 6821)).IsEqualTo(13641);
}
#endif
