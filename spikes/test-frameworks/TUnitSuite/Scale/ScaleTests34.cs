#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests34
{
    [Test]
    public async Task Case_3401() => await Assert.That(Calc.Add(3401, 3402)).IsEqualTo(6803);

    [Test]
    public async Task Case_3402() => await Assert.That(Calc.Add(3402, 3403)).IsEqualTo(6805);

    [Test]
    public async Task Case_3403() => await Assert.That(Calc.Add(3403, 3404)).IsEqualTo(6807);

    [Test]
    public async Task Case_3404() => await Assert.That(Calc.Add(3404, 3405)).IsEqualTo(6809);

    [Test]
    public async Task Case_3405() => await Assert.That(Calc.Add(3405, 3406)).IsEqualTo(6811);

    [Test]
    public async Task Case_3406() => await Assert.That(Calc.Add(3406, 3407)).IsEqualTo(6813);

    [Test]
    public async Task Case_3407() => await Assert.That(Calc.Add(3407, 3408)).IsEqualTo(6815);

    [Test]
    public async Task Case_3408() => await Assert.That(Calc.Add(3408, 3409)).IsEqualTo(6817);

    [Test]
    public async Task Case_3409() => await Assert.That(Calc.Add(3409, 3410)).IsEqualTo(6819);

    [Test]
    public async Task Case_3410() => await Assert.That(Calc.Add(3410, 3411)).IsEqualTo(6821);

    [Test]
    public async Task Case_3411() => await Assert.That(Calc.Add(3411, 3412)).IsEqualTo(6823);

    [Test]
    public async Task Case_3412() => await Assert.That(Calc.Add(3412, 3413)).IsEqualTo(6825);

    [Test]
    public async Task Case_3413() => await Assert.That(Calc.Add(3413, 3414)).IsEqualTo(6827);

    [Test]
    public async Task Case_3414() => await Assert.That(Calc.Add(3414, 3415)).IsEqualTo(6829);

    [Test]
    public async Task Case_3415() => await Assert.That(Calc.Add(3415, 3416)).IsEqualTo(6831);

    [Test]
    public async Task Case_3416() => await Assert.That(Calc.Add(3416, 3417)).IsEqualTo(6833);

    [Test]
    public async Task Case_3417() => await Assert.That(Calc.Add(3417, 3418)).IsEqualTo(6835);

    [Test]
    public async Task Case_3418() => await Assert.That(Calc.Add(3418, 3419)).IsEqualTo(6837);

    [Test]
    public async Task Case_3419() => await Assert.That(Calc.Add(3419, 3420)).IsEqualTo(6839);

    [Test]
    public async Task Case_3420() => await Assert.That(Calc.Add(3420, 3421)).IsEqualTo(6841);
}
#endif
