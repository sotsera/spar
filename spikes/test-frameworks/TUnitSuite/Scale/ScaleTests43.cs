#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests43
{
    [Test]
    public async Task Case_4301() => await Assert.That(Calc.Add(4301, 4302)).IsEqualTo(8603);

    [Test]
    public async Task Case_4302() => await Assert.That(Calc.Add(4302, 4303)).IsEqualTo(8605);

    [Test]
    public async Task Case_4303() => await Assert.That(Calc.Add(4303, 4304)).IsEqualTo(8607);

    [Test]
    public async Task Case_4304() => await Assert.That(Calc.Add(4304, 4305)).IsEqualTo(8609);

    [Test]
    public async Task Case_4305() => await Assert.That(Calc.Add(4305, 4306)).IsEqualTo(8611);

    [Test]
    public async Task Case_4306() => await Assert.That(Calc.Add(4306, 4307)).IsEqualTo(8613);

    [Test]
    public async Task Case_4307() => await Assert.That(Calc.Add(4307, 4308)).IsEqualTo(8615);

    [Test]
    public async Task Case_4308() => await Assert.That(Calc.Add(4308, 4309)).IsEqualTo(8617);

    [Test]
    public async Task Case_4309() => await Assert.That(Calc.Add(4309, 4310)).IsEqualTo(8619);

    [Test]
    public async Task Case_4310() => await Assert.That(Calc.Add(4310, 4311)).IsEqualTo(8621);

    [Test]
    public async Task Case_4311() => await Assert.That(Calc.Add(4311, 4312)).IsEqualTo(8623);

    [Test]
    public async Task Case_4312() => await Assert.That(Calc.Add(4312, 4313)).IsEqualTo(8625);

    [Test]
    public async Task Case_4313() => await Assert.That(Calc.Add(4313, 4314)).IsEqualTo(8627);

    [Test]
    public async Task Case_4314() => await Assert.That(Calc.Add(4314, 4315)).IsEqualTo(8629);

    [Test]
    public async Task Case_4315() => await Assert.That(Calc.Add(4315, 4316)).IsEqualTo(8631);

    [Test]
    public async Task Case_4316() => await Assert.That(Calc.Add(4316, 4317)).IsEqualTo(8633);

    [Test]
    public async Task Case_4317() => await Assert.That(Calc.Add(4317, 4318)).IsEqualTo(8635);

    [Test]
    public async Task Case_4318() => await Assert.That(Calc.Add(4318, 4319)).IsEqualTo(8637);

    [Test]
    public async Task Case_4319() => await Assert.That(Calc.Add(4319, 4320)).IsEqualTo(8639);

    [Test]
    public async Task Case_4320() => await Assert.That(Calc.Add(4320, 4321)).IsEqualTo(8641);
}
#endif
