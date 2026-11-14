#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests48
{
    [Test]
    public async Task Case_4801() => await Assert.That(Calc.Add(4801, 4802)).IsEqualTo(9603);

    [Test]
    public async Task Case_4802() => await Assert.That(Calc.Add(4802, 4803)).IsEqualTo(9605);

    [Test]
    public async Task Case_4803() => await Assert.That(Calc.Add(4803, 4804)).IsEqualTo(9607);

    [Test]
    public async Task Case_4804() => await Assert.That(Calc.Add(4804, 4805)).IsEqualTo(9609);

    [Test]
    public async Task Case_4805() => await Assert.That(Calc.Add(4805, 4806)).IsEqualTo(9611);

    [Test]
    public async Task Case_4806() => await Assert.That(Calc.Add(4806, 4807)).IsEqualTo(9613);

    [Test]
    public async Task Case_4807() => await Assert.That(Calc.Add(4807, 4808)).IsEqualTo(9615);

    [Test]
    public async Task Case_4808() => await Assert.That(Calc.Add(4808, 4809)).IsEqualTo(9617);

    [Test]
    public async Task Case_4809() => await Assert.That(Calc.Add(4809, 4810)).IsEqualTo(9619);

    [Test]
    public async Task Case_4810() => await Assert.That(Calc.Add(4810, 4811)).IsEqualTo(9621);

    [Test]
    public async Task Case_4811() => await Assert.That(Calc.Add(4811, 4812)).IsEqualTo(9623);

    [Test]
    public async Task Case_4812() => await Assert.That(Calc.Add(4812, 4813)).IsEqualTo(9625);

    [Test]
    public async Task Case_4813() => await Assert.That(Calc.Add(4813, 4814)).IsEqualTo(9627);

    [Test]
    public async Task Case_4814() => await Assert.That(Calc.Add(4814, 4815)).IsEqualTo(9629);

    [Test]
    public async Task Case_4815() => await Assert.That(Calc.Add(4815, 4816)).IsEqualTo(9631);

    [Test]
    public async Task Case_4816() => await Assert.That(Calc.Add(4816, 4817)).IsEqualTo(9633);

    [Test]
    public async Task Case_4817() => await Assert.That(Calc.Add(4817, 4818)).IsEqualTo(9635);

    [Test]
    public async Task Case_4818() => await Assert.That(Calc.Add(4818, 4819)).IsEqualTo(9637);

    [Test]
    public async Task Case_4819() => await Assert.That(Calc.Add(4819, 4820)).IsEqualTo(9639);

    [Test]
    public async Task Case_4820() => await Assert.That(Calc.Add(4820, 4821)).IsEqualTo(9641);
}
#endif
