#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests28
{
    [Test]
    public async Task Case_2801() => await Assert.That(Calc.Add(2801, 2802)).IsEqualTo(5603);

    [Test]
    public async Task Case_2802() => await Assert.That(Calc.Add(2802, 2803)).IsEqualTo(5605);

    [Test]
    public async Task Case_2803() => await Assert.That(Calc.Add(2803, 2804)).IsEqualTo(5607);

    [Test]
    public async Task Case_2804() => await Assert.That(Calc.Add(2804, 2805)).IsEqualTo(5609);

    [Test]
    public async Task Case_2805() => await Assert.That(Calc.Add(2805, 2806)).IsEqualTo(5611);

    [Test]
    public async Task Case_2806() => await Assert.That(Calc.Add(2806, 2807)).IsEqualTo(5613);

    [Test]
    public async Task Case_2807() => await Assert.That(Calc.Add(2807, 2808)).IsEqualTo(5615);

    [Test]
    public async Task Case_2808() => await Assert.That(Calc.Add(2808, 2809)).IsEqualTo(5617);

    [Test]
    public async Task Case_2809() => await Assert.That(Calc.Add(2809, 2810)).IsEqualTo(5619);

    [Test]
    public async Task Case_2810() => await Assert.That(Calc.Add(2810, 2811)).IsEqualTo(5621);

    [Test]
    public async Task Case_2811() => await Assert.That(Calc.Add(2811, 2812)).IsEqualTo(5623);

    [Test]
    public async Task Case_2812() => await Assert.That(Calc.Add(2812, 2813)).IsEqualTo(5625);

    [Test]
    public async Task Case_2813() => await Assert.That(Calc.Add(2813, 2814)).IsEqualTo(5627);

    [Test]
    public async Task Case_2814() => await Assert.That(Calc.Add(2814, 2815)).IsEqualTo(5629);

    [Test]
    public async Task Case_2815() => await Assert.That(Calc.Add(2815, 2816)).IsEqualTo(5631);

    [Test]
    public async Task Case_2816() => await Assert.That(Calc.Add(2816, 2817)).IsEqualTo(5633);

    [Test]
    public async Task Case_2817() => await Assert.That(Calc.Add(2817, 2818)).IsEqualTo(5635);

    [Test]
    public async Task Case_2818() => await Assert.That(Calc.Add(2818, 2819)).IsEqualTo(5637);

    [Test]
    public async Task Case_2819() => await Assert.That(Calc.Add(2819, 2820)).IsEqualTo(5639);

    [Test]
    public async Task Case_2820() => await Assert.That(Calc.Add(2820, 2821)).IsEqualTo(5641);
}
#endif
