#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests1
{
    [Test]
    public async Task Case_101() => await Assert.That(Calc.Add(101, 102)).IsEqualTo(203);

    [Test]
    public async Task Case_102() => await Assert.That(Calc.Add(102, 103)).IsEqualTo(205);

    [Test]
    public async Task Case_103() => await Assert.That(Calc.Add(103, 104)).IsEqualTo(207);

    [Test]
    public async Task Case_104() => await Assert.That(Calc.Add(104, 105)).IsEqualTo(209);

    [Test]
    public async Task Case_105() => await Assert.That(Calc.Add(105, 106)).IsEqualTo(211);

    [Test]
    public async Task Case_106() => await Assert.That(Calc.Add(106, 107)).IsEqualTo(213);

    [Test]
    public async Task Case_107() => await Assert.That(Calc.Add(107, 108)).IsEqualTo(215);

    [Test]
    public async Task Case_108() => await Assert.That(Calc.Add(108, 109)).IsEqualTo(217);

    [Test]
    public async Task Case_109() => await Assert.That(Calc.Add(109, 110)).IsEqualTo(219);

    [Test]
    public async Task Case_110() => await Assert.That(Calc.Add(110, 111)).IsEqualTo(221);

    [Test]
    public async Task Case_111() => await Assert.That(Calc.Add(111, 112)).IsEqualTo(223);

    [Test]
    public async Task Case_112() => await Assert.That(Calc.Add(112, 113)).IsEqualTo(225);

    [Test]
    public async Task Case_113() => await Assert.That(Calc.Add(113, 114)).IsEqualTo(227);

    [Test]
    public async Task Case_114() => await Assert.That(Calc.Add(114, 115)).IsEqualTo(229);

    [Test]
    public async Task Case_115() => await Assert.That(Calc.Add(115, 116)).IsEqualTo(231);

    [Test]
    public async Task Case_116() => await Assert.That(Calc.Add(116, 117)).IsEqualTo(233);

    [Test]
    public async Task Case_117() => await Assert.That(Calc.Add(117, 118)).IsEqualTo(235);

    [Test]
    public async Task Case_118() => await Assert.That(Calc.Add(118, 119)).IsEqualTo(237);

    [Test]
    public async Task Case_119() => await Assert.That(Calc.Add(119, 120)).IsEqualTo(239);

    [Test]
    public async Task Case_120() => await Assert.That(Calc.Add(120, 121)).IsEqualTo(241);
}
#endif
