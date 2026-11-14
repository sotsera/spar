#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests53
{
    [Test]
    public async Task Case_5301() => await Assert.That(Calc.Add(5301, 5302)).IsEqualTo(10603);

    [Test]
    public async Task Case_5302() => await Assert.That(Calc.Add(5302, 5303)).IsEqualTo(10605);

    [Test]
    public async Task Case_5303() => await Assert.That(Calc.Add(5303, 5304)).IsEqualTo(10607);

    [Test]
    public async Task Case_5304() => await Assert.That(Calc.Add(5304, 5305)).IsEqualTo(10609);

    [Test]
    public async Task Case_5305() => await Assert.That(Calc.Add(5305, 5306)).IsEqualTo(10611);

    [Test]
    public async Task Case_5306() => await Assert.That(Calc.Add(5306, 5307)).IsEqualTo(10613);

    [Test]
    public async Task Case_5307() => await Assert.That(Calc.Add(5307, 5308)).IsEqualTo(10615);

    [Test]
    public async Task Case_5308() => await Assert.That(Calc.Add(5308, 5309)).IsEqualTo(10617);

    [Test]
    public async Task Case_5309() => await Assert.That(Calc.Add(5309, 5310)).IsEqualTo(10619);

    [Test]
    public async Task Case_5310() => await Assert.That(Calc.Add(5310, 5311)).IsEqualTo(10621);

    [Test]
    public async Task Case_5311() => await Assert.That(Calc.Add(5311, 5312)).IsEqualTo(10623);

    [Test]
    public async Task Case_5312() => await Assert.That(Calc.Add(5312, 5313)).IsEqualTo(10625);

    [Test]
    public async Task Case_5313() => await Assert.That(Calc.Add(5313, 5314)).IsEqualTo(10627);

    [Test]
    public async Task Case_5314() => await Assert.That(Calc.Add(5314, 5315)).IsEqualTo(10629);

    [Test]
    public async Task Case_5315() => await Assert.That(Calc.Add(5315, 5316)).IsEqualTo(10631);

    [Test]
    public async Task Case_5316() => await Assert.That(Calc.Add(5316, 5317)).IsEqualTo(10633);

    [Test]
    public async Task Case_5317() => await Assert.That(Calc.Add(5317, 5318)).IsEqualTo(10635);

    [Test]
    public async Task Case_5318() => await Assert.That(Calc.Add(5318, 5319)).IsEqualTo(10637);

    [Test]
    public async Task Case_5319() => await Assert.That(Calc.Add(5319, 5320)).IsEqualTo(10639);

    [Test]
    public async Task Case_5320() => await Assert.That(Calc.Add(5320, 5321)).IsEqualTo(10641);
}
#endif
