#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests42
{
    [Test]
    public async Task Case_4201() => await Assert.That(Calc.Add(4201, 4202)).IsEqualTo(8403);

    [Test]
    public async Task Case_4202() => await Assert.That(Calc.Add(4202, 4203)).IsEqualTo(8405);

    [Test]
    public async Task Case_4203() => await Assert.That(Calc.Add(4203, 4204)).IsEqualTo(8407);

    [Test]
    public async Task Case_4204() => await Assert.That(Calc.Add(4204, 4205)).IsEqualTo(8409);

    [Test]
    public async Task Case_4205() => await Assert.That(Calc.Add(4205, 4206)).IsEqualTo(8411);

    [Test]
    public async Task Case_4206() => await Assert.That(Calc.Add(4206, 4207)).IsEqualTo(8413);

    [Test]
    public async Task Case_4207() => await Assert.That(Calc.Add(4207, 4208)).IsEqualTo(8415);

    [Test]
    public async Task Case_4208() => await Assert.That(Calc.Add(4208, 4209)).IsEqualTo(8417);

    [Test]
    public async Task Case_4209() => await Assert.That(Calc.Add(4209, 4210)).IsEqualTo(8419);

    [Test]
    public async Task Case_4210() => await Assert.That(Calc.Add(4210, 4211)).IsEqualTo(8421);

    [Test]
    public async Task Case_4211() => await Assert.That(Calc.Add(4211, 4212)).IsEqualTo(8423);

    [Test]
    public async Task Case_4212() => await Assert.That(Calc.Add(4212, 4213)).IsEqualTo(8425);

    [Test]
    public async Task Case_4213() => await Assert.That(Calc.Add(4213, 4214)).IsEqualTo(8427);

    [Test]
    public async Task Case_4214() => await Assert.That(Calc.Add(4214, 4215)).IsEqualTo(8429);

    [Test]
    public async Task Case_4215() => await Assert.That(Calc.Add(4215, 4216)).IsEqualTo(8431);

    [Test]
    public async Task Case_4216() => await Assert.That(Calc.Add(4216, 4217)).IsEqualTo(8433);

    [Test]
    public async Task Case_4217() => await Assert.That(Calc.Add(4217, 4218)).IsEqualTo(8435);

    [Test]
    public async Task Case_4218() => await Assert.That(Calc.Add(4218, 4219)).IsEqualTo(8437);

    [Test]
    public async Task Case_4219() => await Assert.That(Calc.Add(4219, 4220)).IsEqualTo(8439);

    [Test]
    public async Task Case_4220() => await Assert.That(Calc.Add(4220, 4221)).IsEqualTo(8441);
}
#endif
