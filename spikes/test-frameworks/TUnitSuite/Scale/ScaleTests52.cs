#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests52
{
    [Test]
    public async Task Case_5201() => await Assert.That(Calc.Add(5201, 5202)).IsEqualTo(10403);

    [Test]
    public async Task Case_5202() => await Assert.That(Calc.Add(5202, 5203)).IsEqualTo(10405);

    [Test]
    public async Task Case_5203() => await Assert.That(Calc.Add(5203, 5204)).IsEqualTo(10407);

    [Test]
    public async Task Case_5204() => await Assert.That(Calc.Add(5204, 5205)).IsEqualTo(10409);

    [Test]
    public async Task Case_5205() => await Assert.That(Calc.Add(5205, 5206)).IsEqualTo(10411);

    [Test]
    public async Task Case_5206() => await Assert.That(Calc.Add(5206, 5207)).IsEqualTo(10413);

    [Test]
    public async Task Case_5207() => await Assert.That(Calc.Add(5207, 5208)).IsEqualTo(10415);

    [Test]
    public async Task Case_5208() => await Assert.That(Calc.Add(5208, 5209)).IsEqualTo(10417);

    [Test]
    public async Task Case_5209() => await Assert.That(Calc.Add(5209, 5210)).IsEqualTo(10419);

    [Test]
    public async Task Case_5210() => await Assert.That(Calc.Add(5210, 5211)).IsEqualTo(10421);

    [Test]
    public async Task Case_5211() => await Assert.That(Calc.Add(5211, 5212)).IsEqualTo(10423);

    [Test]
    public async Task Case_5212() => await Assert.That(Calc.Add(5212, 5213)).IsEqualTo(10425);

    [Test]
    public async Task Case_5213() => await Assert.That(Calc.Add(5213, 5214)).IsEqualTo(10427);

    [Test]
    public async Task Case_5214() => await Assert.That(Calc.Add(5214, 5215)).IsEqualTo(10429);

    [Test]
    public async Task Case_5215() => await Assert.That(Calc.Add(5215, 5216)).IsEqualTo(10431);

    [Test]
    public async Task Case_5216() => await Assert.That(Calc.Add(5216, 5217)).IsEqualTo(10433);

    [Test]
    public async Task Case_5217() => await Assert.That(Calc.Add(5217, 5218)).IsEqualTo(10435);

    [Test]
    public async Task Case_5218() => await Assert.That(Calc.Add(5218, 5219)).IsEqualTo(10437);

    [Test]
    public async Task Case_5219() => await Assert.That(Calc.Add(5219, 5220)).IsEqualTo(10439);

    [Test]
    public async Task Case_5220() => await Assert.That(Calc.Add(5220, 5221)).IsEqualTo(10441);
}
#endif
