#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests83
{
    [Test]
    public async Task Case_8301() => await Assert.That(Calc.Add(8301, 8302)).IsEqualTo(16603);

    [Test]
    public async Task Case_8302() => await Assert.That(Calc.Add(8302, 8303)).IsEqualTo(16605);

    [Test]
    public async Task Case_8303() => await Assert.That(Calc.Add(8303, 8304)).IsEqualTo(16607);

    [Test]
    public async Task Case_8304() => await Assert.That(Calc.Add(8304, 8305)).IsEqualTo(16609);

    [Test]
    public async Task Case_8305() => await Assert.That(Calc.Add(8305, 8306)).IsEqualTo(16611);

    [Test]
    public async Task Case_8306() => await Assert.That(Calc.Add(8306, 8307)).IsEqualTo(16613);

    [Test]
    public async Task Case_8307() => await Assert.That(Calc.Add(8307, 8308)).IsEqualTo(16615);

    [Test]
    public async Task Case_8308() => await Assert.That(Calc.Add(8308, 8309)).IsEqualTo(16617);

    [Test]
    public async Task Case_8309() => await Assert.That(Calc.Add(8309, 8310)).IsEqualTo(16619);

    [Test]
    public async Task Case_8310() => await Assert.That(Calc.Add(8310, 8311)).IsEqualTo(16621);

    [Test]
    public async Task Case_8311() => await Assert.That(Calc.Add(8311, 8312)).IsEqualTo(16623);

    [Test]
    public async Task Case_8312() => await Assert.That(Calc.Add(8312, 8313)).IsEqualTo(16625);

    [Test]
    public async Task Case_8313() => await Assert.That(Calc.Add(8313, 8314)).IsEqualTo(16627);

    [Test]
    public async Task Case_8314() => await Assert.That(Calc.Add(8314, 8315)).IsEqualTo(16629);

    [Test]
    public async Task Case_8315() => await Assert.That(Calc.Add(8315, 8316)).IsEqualTo(16631);

    [Test]
    public async Task Case_8316() => await Assert.That(Calc.Add(8316, 8317)).IsEqualTo(16633);

    [Test]
    public async Task Case_8317() => await Assert.That(Calc.Add(8317, 8318)).IsEqualTo(16635);

    [Test]
    public async Task Case_8318() => await Assert.That(Calc.Add(8318, 8319)).IsEqualTo(16637);

    [Test]
    public async Task Case_8319() => await Assert.That(Calc.Add(8319, 8320)).IsEqualTo(16639);

    [Test]
    public async Task Case_8320() => await Assert.That(Calc.Add(8320, 8321)).IsEqualTo(16641);
}
#endif
