#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests72
{
    [Test]
    public async Task Case_7201() => await Assert.That(Calc.Add(7201, 7202)).IsEqualTo(14403);

    [Test]
    public async Task Case_7202() => await Assert.That(Calc.Add(7202, 7203)).IsEqualTo(14405);

    [Test]
    public async Task Case_7203() => await Assert.That(Calc.Add(7203, 7204)).IsEqualTo(14407);

    [Test]
    public async Task Case_7204() => await Assert.That(Calc.Add(7204, 7205)).IsEqualTo(14409);

    [Test]
    public async Task Case_7205() => await Assert.That(Calc.Add(7205, 7206)).IsEqualTo(14411);

    [Test]
    public async Task Case_7206() => await Assert.That(Calc.Add(7206, 7207)).IsEqualTo(14413);

    [Test]
    public async Task Case_7207() => await Assert.That(Calc.Add(7207, 7208)).IsEqualTo(14415);

    [Test]
    public async Task Case_7208() => await Assert.That(Calc.Add(7208, 7209)).IsEqualTo(14417);

    [Test]
    public async Task Case_7209() => await Assert.That(Calc.Add(7209, 7210)).IsEqualTo(14419);

    [Test]
    public async Task Case_7210() => await Assert.That(Calc.Add(7210, 7211)).IsEqualTo(14421);

    [Test]
    public async Task Case_7211() => await Assert.That(Calc.Add(7211, 7212)).IsEqualTo(14423);

    [Test]
    public async Task Case_7212() => await Assert.That(Calc.Add(7212, 7213)).IsEqualTo(14425);

    [Test]
    public async Task Case_7213() => await Assert.That(Calc.Add(7213, 7214)).IsEqualTo(14427);

    [Test]
    public async Task Case_7214() => await Assert.That(Calc.Add(7214, 7215)).IsEqualTo(14429);

    [Test]
    public async Task Case_7215() => await Assert.That(Calc.Add(7215, 7216)).IsEqualTo(14431);

    [Test]
    public async Task Case_7216() => await Assert.That(Calc.Add(7216, 7217)).IsEqualTo(14433);

    [Test]
    public async Task Case_7217() => await Assert.That(Calc.Add(7217, 7218)).IsEqualTo(14435);

    [Test]
    public async Task Case_7218() => await Assert.That(Calc.Add(7218, 7219)).IsEqualTo(14437);

    [Test]
    public async Task Case_7219() => await Assert.That(Calc.Add(7219, 7220)).IsEqualTo(14439);

    [Test]
    public async Task Case_7220() => await Assert.That(Calc.Add(7220, 7221)).IsEqualTo(14441);
}
#endif
