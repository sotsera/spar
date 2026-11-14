#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests2
{
    [Test]
    public async Task Case_201() => await Assert.That(Calc.Add(201, 202)).IsEqualTo(403);

    [Test]
    public async Task Case_202() => await Assert.That(Calc.Add(202, 203)).IsEqualTo(405);

    [Test]
    public async Task Case_203() => await Assert.That(Calc.Add(203, 204)).IsEqualTo(407);

    [Test]
    public async Task Case_204() => await Assert.That(Calc.Add(204, 205)).IsEqualTo(409);

    [Test]
    public async Task Case_205() => await Assert.That(Calc.Add(205, 206)).IsEqualTo(411);

    [Test]
    public async Task Case_206() => await Assert.That(Calc.Add(206, 207)).IsEqualTo(413);

    [Test]
    public async Task Case_207() => await Assert.That(Calc.Add(207, 208)).IsEqualTo(415);

    [Test]
    public async Task Case_208() => await Assert.That(Calc.Add(208, 209)).IsEqualTo(417);

    [Test]
    public async Task Case_209() => await Assert.That(Calc.Add(209, 210)).IsEqualTo(419);

    [Test]
    public async Task Case_210() => await Assert.That(Calc.Add(210, 211)).IsEqualTo(421);

    [Test]
    public async Task Case_211() => await Assert.That(Calc.Add(211, 212)).IsEqualTo(423);

    [Test]
    public async Task Case_212() => await Assert.That(Calc.Add(212, 213)).IsEqualTo(425);

    [Test]
    public async Task Case_213() => await Assert.That(Calc.Add(213, 214)).IsEqualTo(427);

    [Test]
    public async Task Case_214() => await Assert.That(Calc.Add(214, 215)).IsEqualTo(429);

    [Test]
    public async Task Case_215() => await Assert.That(Calc.Add(215, 216)).IsEqualTo(431);

    [Test]
    public async Task Case_216() => await Assert.That(Calc.Add(216, 217)).IsEqualTo(433);

    [Test]
    public async Task Case_217() => await Assert.That(Calc.Add(217, 218)).IsEqualTo(435);

    [Test]
    public async Task Case_218() => await Assert.That(Calc.Add(218, 219)).IsEqualTo(437);

    [Test]
    public async Task Case_219() => await Assert.That(Calc.Add(219, 220)).IsEqualTo(439);

    [Test]
    public async Task Case_220() => await Assert.That(Calc.Add(220, 221)).IsEqualTo(441);
}
#endif
