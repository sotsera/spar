#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests32
{
    [Test]
    public async Task Case_3201() => await Assert.That(Calc.Add(3201, 3202)).IsEqualTo(6403);

    [Test]
    public async Task Case_3202() => await Assert.That(Calc.Add(3202, 3203)).IsEqualTo(6405);

    [Test]
    public async Task Case_3203() => await Assert.That(Calc.Add(3203, 3204)).IsEqualTo(6407);

    [Test]
    public async Task Case_3204() => await Assert.That(Calc.Add(3204, 3205)).IsEqualTo(6409);

    [Test]
    public async Task Case_3205() => await Assert.That(Calc.Add(3205, 3206)).IsEqualTo(6411);

    [Test]
    public async Task Case_3206() => await Assert.That(Calc.Add(3206, 3207)).IsEqualTo(6413);

    [Test]
    public async Task Case_3207() => await Assert.That(Calc.Add(3207, 3208)).IsEqualTo(6415);

    [Test]
    public async Task Case_3208() => await Assert.That(Calc.Add(3208, 3209)).IsEqualTo(6417);

    [Test]
    public async Task Case_3209() => await Assert.That(Calc.Add(3209, 3210)).IsEqualTo(6419);

    [Test]
    public async Task Case_3210() => await Assert.That(Calc.Add(3210, 3211)).IsEqualTo(6421);

    [Test]
    public async Task Case_3211() => await Assert.That(Calc.Add(3211, 3212)).IsEqualTo(6423);

    [Test]
    public async Task Case_3212() => await Assert.That(Calc.Add(3212, 3213)).IsEqualTo(6425);

    [Test]
    public async Task Case_3213() => await Assert.That(Calc.Add(3213, 3214)).IsEqualTo(6427);

    [Test]
    public async Task Case_3214() => await Assert.That(Calc.Add(3214, 3215)).IsEqualTo(6429);

    [Test]
    public async Task Case_3215() => await Assert.That(Calc.Add(3215, 3216)).IsEqualTo(6431);

    [Test]
    public async Task Case_3216() => await Assert.That(Calc.Add(3216, 3217)).IsEqualTo(6433);

    [Test]
    public async Task Case_3217() => await Assert.That(Calc.Add(3217, 3218)).IsEqualTo(6435);

    [Test]
    public async Task Case_3218() => await Assert.That(Calc.Add(3218, 3219)).IsEqualTo(6437);

    [Test]
    public async Task Case_3219() => await Assert.That(Calc.Add(3219, 3220)).IsEqualTo(6439);

    [Test]
    public async Task Case_3220() => await Assert.That(Calc.Add(3220, 3221)).IsEqualTo(6441);
}
#endif
