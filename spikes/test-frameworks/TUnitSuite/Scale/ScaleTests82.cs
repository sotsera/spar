#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests82
{
    [Test]
    public async Task Case_8201() => await Assert.That(Calc.Add(8201, 8202)).IsEqualTo(16403);

    [Test]
    public async Task Case_8202() => await Assert.That(Calc.Add(8202, 8203)).IsEqualTo(16405);

    [Test]
    public async Task Case_8203() => await Assert.That(Calc.Add(8203, 8204)).IsEqualTo(16407);

    [Test]
    public async Task Case_8204() => await Assert.That(Calc.Add(8204, 8205)).IsEqualTo(16409);

    [Test]
    public async Task Case_8205() => await Assert.That(Calc.Add(8205, 8206)).IsEqualTo(16411);

    [Test]
    public async Task Case_8206() => await Assert.That(Calc.Add(8206, 8207)).IsEqualTo(16413);

    [Test]
    public async Task Case_8207() => await Assert.That(Calc.Add(8207, 8208)).IsEqualTo(16415);

    [Test]
    public async Task Case_8208() => await Assert.That(Calc.Add(8208, 8209)).IsEqualTo(16417);

    [Test]
    public async Task Case_8209() => await Assert.That(Calc.Add(8209, 8210)).IsEqualTo(16419);

    [Test]
    public async Task Case_8210() => await Assert.That(Calc.Add(8210, 8211)).IsEqualTo(16421);

    [Test]
    public async Task Case_8211() => await Assert.That(Calc.Add(8211, 8212)).IsEqualTo(16423);

    [Test]
    public async Task Case_8212() => await Assert.That(Calc.Add(8212, 8213)).IsEqualTo(16425);

    [Test]
    public async Task Case_8213() => await Assert.That(Calc.Add(8213, 8214)).IsEqualTo(16427);

    [Test]
    public async Task Case_8214() => await Assert.That(Calc.Add(8214, 8215)).IsEqualTo(16429);

    [Test]
    public async Task Case_8215() => await Assert.That(Calc.Add(8215, 8216)).IsEqualTo(16431);

    [Test]
    public async Task Case_8216() => await Assert.That(Calc.Add(8216, 8217)).IsEqualTo(16433);

    [Test]
    public async Task Case_8217() => await Assert.That(Calc.Add(8217, 8218)).IsEqualTo(16435);

    [Test]
    public async Task Case_8218() => await Assert.That(Calc.Add(8218, 8219)).IsEqualTo(16437);

    [Test]
    public async Task Case_8219() => await Assert.That(Calc.Add(8219, 8220)).IsEqualTo(16439);

    [Test]
    public async Task Case_8220() => await Assert.That(Calc.Add(8220, 8221)).IsEqualTo(16441);
}
#endif
