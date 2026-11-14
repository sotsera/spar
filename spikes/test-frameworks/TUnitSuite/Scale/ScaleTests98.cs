#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests98
{
    [Test]
    public async Task Case_9801() => await Assert.That(Calc.Add(9801, 9802)).IsEqualTo(19603);

    [Test]
    public async Task Case_9802() => await Assert.That(Calc.Add(9802, 9803)).IsEqualTo(19605);

    [Test]
    public async Task Case_9803() => await Assert.That(Calc.Add(9803, 9804)).IsEqualTo(19607);

    [Test]
    public async Task Case_9804() => await Assert.That(Calc.Add(9804, 9805)).IsEqualTo(19609);

    [Test]
    public async Task Case_9805() => await Assert.That(Calc.Add(9805, 9806)).IsEqualTo(19611);

    [Test]
    public async Task Case_9806() => await Assert.That(Calc.Add(9806, 9807)).IsEqualTo(19613);

    [Test]
    public async Task Case_9807() => await Assert.That(Calc.Add(9807, 9808)).IsEqualTo(19615);

    [Test]
    public async Task Case_9808() => await Assert.That(Calc.Add(9808, 9809)).IsEqualTo(19617);

    [Test]
    public async Task Case_9809() => await Assert.That(Calc.Add(9809, 9810)).IsEqualTo(19619);

    [Test]
    public async Task Case_9810() => await Assert.That(Calc.Add(9810, 9811)).IsEqualTo(19621);

    [Test]
    public async Task Case_9811() => await Assert.That(Calc.Add(9811, 9812)).IsEqualTo(19623);

    [Test]
    public async Task Case_9812() => await Assert.That(Calc.Add(9812, 9813)).IsEqualTo(19625);

    [Test]
    public async Task Case_9813() => await Assert.That(Calc.Add(9813, 9814)).IsEqualTo(19627);

    [Test]
    public async Task Case_9814() => await Assert.That(Calc.Add(9814, 9815)).IsEqualTo(19629);

    [Test]
    public async Task Case_9815() => await Assert.That(Calc.Add(9815, 9816)).IsEqualTo(19631);

    [Test]
    public async Task Case_9816() => await Assert.That(Calc.Add(9816, 9817)).IsEqualTo(19633);

    [Test]
    public async Task Case_9817() => await Assert.That(Calc.Add(9817, 9818)).IsEqualTo(19635);

    [Test]
    public async Task Case_9818() => await Assert.That(Calc.Add(9818, 9819)).IsEqualTo(19637);

    [Test]
    public async Task Case_9819() => await Assert.That(Calc.Add(9819, 9820)).IsEqualTo(19639);

    [Test]
    public async Task Case_9820() => await Assert.That(Calc.Add(9820, 9821)).IsEqualTo(19641);
}
#endif
