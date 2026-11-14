#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests23
{
    [Test]
    public async Task Case_2301() => await Assert.That(Calc.Add(2301, 2302)).IsEqualTo(4603);

    [Test]
    public async Task Case_2302() => await Assert.That(Calc.Add(2302, 2303)).IsEqualTo(4605);

    [Test]
    public async Task Case_2303() => await Assert.That(Calc.Add(2303, 2304)).IsEqualTo(4607);

    [Test]
    public async Task Case_2304() => await Assert.That(Calc.Add(2304, 2305)).IsEqualTo(4609);

    [Test]
    public async Task Case_2305() => await Assert.That(Calc.Add(2305, 2306)).IsEqualTo(4611);

    [Test]
    public async Task Case_2306() => await Assert.That(Calc.Add(2306, 2307)).IsEqualTo(4613);

    [Test]
    public async Task Case_2307() => await Assert.That(Calc.Add(2307, 2308)).IsEqualTo(4615);

    [Test]
    public async Task Case_2308() => await Assert.That(Calc.Add(2308, 2309)).IsEqualTo(4617);

    [Test]
    public async Task Case_2309() => await Assert.That(Calc.Add(2309, 2310)).IsEqualTo(4619);

    [Test]
    public async Task Case_2310() => await Assert.That(Calc.Add(2310, 2311)).IsEqualTo(4621);

    [Test]
    public async Task Case_2311() => await Assert.That(Calc.Add(2311, 2312)).IsEqualTo(4623);

    [Test]
    public async Task Case_2312() => await Assert.That(Calc.Add(2312, 2313)).IsEqualTo(4625);

    [Test]
    public async Task Case_2313() => await Assert.That(Calc.Add(2313, 2314)).IsEqualTo(4627);

    [Test]
    public async Task Case_2314() => await Assert.That(Calc.Add(2314, 2315)).IsEqualTo(4629);

    [Test]
    public async Task Case_2315() => await Assert.That(Calc.Add(2315, 2316)).IsEqualTo(4631);

    [Test]
    public async Task Case_2316() => await Assert.That(Calc.Add(2316, 2317)).IsEqualTo(4633);

    [Test]
    public async Task Case_2317() => await Assert.That(Calc.Add(2317, 2318)).IsEqualTo(4635);

    [Test]
    public async Task Case_2318() => await Assert.That(Calc.Add(2318, 2319)).IsEqualTo(4637);

    [Test]
    public async Task Case_2319() => await Assert.That(Calc.Add(2319, 2320)).IsEqualTo(4639);

    [Test]
    public async Task Case_2320() => await Assert.That(Calc.Add(2320, 2321)).IsEqualTo(4641);
}
#endif
