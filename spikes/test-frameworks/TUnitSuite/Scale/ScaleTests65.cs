#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests65
{
    [Test]
    public async Task Case_6501() => await Assert.That(Calc.Add(6501, 6502)).IsEqualTo(13003);

    [Test]
    public async Task Case_6502() => await Assert.That(Calc.Add(6502, 6503)).IsEqualTo(13005);

    [Test]
    public async Task Case_6503() => await Assert.That(Calc.Add(6503, 6504)).IsEqualTo(13007);

    [Test]
    public async Task Case_6504() => await Assert.That(Calc.Add(6504, 6505)).IsEqualTo(13009);

    [Test]
    public async Task Case_6505() => await Assert.That(Calc.Add(6505, 6506)).IsEqualTo(13011);

    [Test]
    public async Task Case_6506() => await Assert.That(Calc.Add(6506, 6507)).IsEqualTo(13013);

    [Test]
    public async Task Case_6507() => await Assert.That(Calc.Add(6507, 6508)).IsEqualTo(13015);

    [Test]
    public async Task Case_6508() => await Assert.That(Calc.Add(6508, 6509)).IsEqualTo(13017);

    [Test]
    public async Task Case_6509() => await Assert.That(Calc.Add(6509, 6510)).IsEqualTo(13019);

    [Test]
    public async Task Case_6510() => await Assert.That(Calc.Add(6510, 6511)).IsEqualTo(13021);

    [Test]
    public async Task Case_6511() => await Assert.That(Calc.Add(6511, 6512)).IsEqualTo(13023);

    [Test]
    public async Task Case_6512() => await Assert.That(Calc.Add(6512, 6513)).IsEqualTo(13025);

    [Test]
    public async Task Case_6513() => await Assert.That(Calc.Add(6513, 6514)).IsEqualTo(13027);

    [Test]
    public async Task Case_6514() => await Assert.That(Calc.Add(6514, 6515)).IsEqualTo(13029);

    [Test]
    public async Task Case_6515() => await Assert.That(Calc.Add(6515, 6516)).IsEqualTo(13031);

    [Test]
    public async Task Case_6516() => await Assert.That(Calc.Add(6516, 6517)).IsEqualTo(13033);

    [Test]
    public async Task Case_6517() => await Assert.That(Calc.Add(6517, 6518)).IsEqualTo(13035);

    [Test]
    public async Task Case_6518() => await Assert.That(Calc.Add(6518, 6519)).IsEqualTo(13037);

    [Test]
    public async Task Case_6519() => await Assert.That(Calc.Add(6519, 6520)).IsEqualTo(13039);

    [Test]
    public async Task Case_6520() => await Assert.That(Calc.Add(6520, 6521)).IsEqualTo(13041);
}
#endif
