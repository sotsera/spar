#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests58
{
    [Test]
    public async Task Case_5801() => await Assert.That(Calc.Add(5801, 5802)).IsEqualTo(11603);

    [Test]
    public async Task Case_5802() => await Assert.That(Calc.Add(5802, 5803)).IsEqualTo(11605);

    [Test]
    public async Task Case_5803() => await Assert.That(Calc.Add(5803, 5804)).IsEqualTo(11607);

    [Test]
    public async Task Case_5804() => await Assert.That(Calc.Add(5804, 5805)).IsEqualTo(11609);

    [Test]
    public async Task Case_5805() => await Assert.That(Calc.Add(5805, 5806)).IsEqualTo(11611);

    [Test]
    public async Task Case_5806() => await Assert.That(Calc.Add(5806, 5807)).IsEqualTo(11613);

    [Test]
    public async Task Case_5807() => await Assert.That(Calc.Add(5807, 5808)).IsEqualTo(11615);

    [Test]
    public async Task Case_5808() => await Assert.That(Calc.Add(5808, 5809)).IsEqualTo(11617);

    [Test]
    public async Task Case_5809() => await Assert.That(Calc.Add(5809, 5810)).IsEqualTo(11619);

    [Test]
    public async Task Case_5810() => await Assert.That(Calc.Add(5810, 5811)).IsEqualTo(11621);

    [Test]
    public async Task Case_5811() => await Assert.That(Calc.Add(5811, 5812)).IsEqualTo(11623);

    [Test]
    public async Task Case_5812() => await Assert.That(Calc.Add(5812, 5813)).IsEqualTo(11625);

    [Test]
    public async Task Case_5813() => await Assert.That(Calc.Add(5813, 5814)).IsEqualTo(11627);

    [Test]
    public async Task Case_5814() => await Assert.That(Calc.Add(5814, 5815)).IsEqualTo(11629);

    [Test]
    public async Task Case_5815() => await Assert.That(Calc.Add(5815, 5816)).IsEqualTo(11631);

    [Test]
    public async Task Case_5816() => await Assert.That(Calc.Add(5816, 5817)).IsEqualTo(11633);

    [Test]
    public async Task Case_5817() => await Assert.That(Calc.Add(5817, 5818)).IsEqualTo(11635);

    [Test]
    public async Task Case_5818() => await Assert.That(Calc.Add(5818, 5819)).IsEqualTo(11637);

    [Test]
    public async Task Case_5819() => await Assert.That(Calc.Add(5819, 5820)).IsEqualTo(11639);

    [Test]
    public async Task Case_5820() => await Assert.That(Calc.Add(5820, 5821)).IsEqualTo(11641);
}
#endif
