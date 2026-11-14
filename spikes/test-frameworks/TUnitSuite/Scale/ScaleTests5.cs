#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests5
{
    [Test]
    public async Task Case_501() => await Assert.That(Calc.Add(501, 502)).IsEqualTo(1003);

    [Test]
    public async Task Case_502() => await Assert.That(Calc.Add(502, 503)).IsEqualTo(1005);

    [Test]
    public async Task Case_503() => await Assert.That(Calc.Add(503, 504)).IsEqualTo(1007);

    [Test]
    public async Task Case_504() => await Assert.That(Calc.Add(504, 505)).IsEqualTo(1009);

    [Test]
    public async Task Case_505() => await Assert.That(Calc.Add(505, 506)).IsEqualTo(1011);

    [Test]
    public async Task Case_506() => await Assert.That(Calc.Add(506, 507)).IsEqualTo(1013);

    [Test]
    public async Task Case_507() => await Assert.That(Calc.Add(507, 508)).IsEqualTo(1015);

    [Test]
    public async Task Case_508() => await Assert.That(Calc.Add(508, 509)).IsEqualTo(1017);

    [Test]
    public async Task Case_509() => await Assert.That(Calc.Add(509, 510)).IsEqualTo(1019);

    [Test]
    public async Task Case_510() => await Assert.That(Calc.Add(510, 511)).IsEqualTo(1021);

    [Test]
    public async Task Case_511() => await Assert.That(Calc.Add(511, 512)).IsEqualTo(1023);

    [Test]
    public async Task Case_512() => await Assert.That(Calc.Add(512, 513)).IsEqualTo(1025);

    [Test]
    public async Task Case_513() => await Assert.That(Calc.Add(513, 514)).IsEqualTo(1027);

    [Test]
    public async Task Case_514() => await Assert.That(Calc.Add(514, 515)).IsEqualTo(1029);

    [Test]
    public async Task Case_515() => await Assert.That(Calc.Add(515, 516)).IsEqualTo(1031);

    [Test]
    public async Task Case_516() => await Assert.That(Calc.Add(516, 517)).IsEqualTo(1033);

    [Test]
    public async Task Case_517() => await Assert.That(Calc.Add(517, 518)).IsEqualTo(1035);

    [Test]
    public async Task Case_518() => await Assert.That(Calc.Add(518, 519)).IsEqualTo(1037);

    [Test]
    public async Task Case_519() => await Assert.That(Calc.Add(519, 520)).IsEqualTo(1039);

    [Test]
    public async Task Case_520() => await Assert.That(Calc.Add(520, 521)).IsEqualTo(1041);
}
#endif
