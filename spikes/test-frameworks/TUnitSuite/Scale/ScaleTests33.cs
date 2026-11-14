#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests33
{
    [Test]
    public async Task Case_3301() => await Assert.That(Calc.Add(3301, 3302)).IsEqualTo(6603);

    [Test]
    public async Task Case_3302() => await Assert.That(Calc.Add(3302, 3303)).IsEqualTo(6605);

    [Test]
    public async Task Case_3303() => await Assert.That(Calc.Add(3303, 3304)).IsEqualTo(6607);

    [Test]
    public async Task Case_3304() => await Assert.That(Calc.Add(3304, 3305)).IsEqualTo(6609);

    [Test]
    public async Task Case_3305() => await Assert.That(Calc.Add(3305, 3306)).IsEqualTo(6611);

    [Test]
    public async Task Case_3306() => await Assert.That(Calc.Add(3306, 3307)).IsEqualTo(6613);

    [Test]
    public async Task Case_3307() => await Assert.That(Calc.Add(3307, 3308)).IsEqualTo(6615);

    [Test]
    public async Task Case_3308() => await Assert.That(Calc.Add(3308, 3309)).IsEqualTo(6617);

    [Test]
    public async Task Case_3309() => await Assert.That(Calc.Add(3309, 3310)).IsEqualTo(6619);

    [Test]
    public async Task Case_3310() => await Assert.That(Calc.Add(3310, 3311)).IsEqualTo(6621);

    [Test]
    public async Task Case_3311() => await Assert.That(Calc.Add(3311, 3312)).IsEqualTo(6623);

    [Test]
    public async Task Case_3312() => await Assert.That(Calc.Add(3312, 3313)).IsEqualTo(6625);

    [Test]
    public async Task Case_3313() => await Assert.That(Calc.Add(3313, 3314)).IsEqualTo(6627);

    [Test]
    public async Task Case_3314() => await Assert.That(Calc.Add(3314, 3315)).IsEqualTo(6629);

    [Test]
    public async Task Case_3315() => await Assert.That(Calc.Add(3315, 3316)).IsEqualTo(6631);

    [Test]
    public async Task Case_3316() => await Assert.That(Calc.Add(3316, 3317)).IsEqualTo(6633);

    [Test]
    public async Task Case_3317() => await Assert.That(Calc.Add(3317, 3318)).IsEqualTo(6635);

    [Test]
    public async Task Case_3318() => await Assert.That(Calc.Add(3318, 3319)).IsEqualTo(6637);

    [Test]
    public async Task Case_3319() => await Assert.That(Calc.Add(3319, 3320)).IsEqualTo(6639);

    [Test]
    public async Task Case_3320() => await Assert.That(Calc.Add(3320, 3321)).IsEqualTo(6641);
}
#endif
