#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests95
{
    [Test]
    public async Task Case_9501() => await Assert.That(Calc.Add(9501, 9502)).IsEqualTo(19003);

    [Test]
    public async Task Case_9502() => await Assert.That(Calc.Add(9502, 9503)).IsEqualTo(19005);

    [Test]
    public async Task Case_9503() => await Assert.That(Calc.Add(9503, 9504)).IsEqualTo(19007);

    [Test]
    public async Task Case_9504() => await Assert.That(Calc.Add(9504, 9505)).IsEqualTo(19009);

    [Test]
    public async Task Case_9505() => await Assert.That(Calc.Add(9505, 9506)).IsEqualTo(19011);

    [Test]
    public async Task Case_9506() => await Assert.That(Calc.Add(9506, 9507)).IsEqualTo(19013);

    [Test]
    public async Task Case_9507() => await Assert.That(Calc.Add(9507, 9508)).IsEqualTo(19015);

    [Test]
    public async Task Case_9508() => await Assert.That(Calc.Add(9508, 9509)).IsEqualTo(19017);

    [Test]
    public async Task Case_9509() => await Assert.That(Calc.Add(9509, 9510)).IsEqualTo(19019);

    [Test]
    public async Task Case_9510() => await Assert.That(Calc.Add(9510, 9511)).IsEqualTo(19021);

    [Test]
    public async Task Case_9511() => await Assert.That(Calc.Add(9511, 9512)).IsEqualTo(19023);

    [Test]
    public async Task Case_9512() => await Assert.That(Calc.Add(9512, 9513)).IsEqualTo(19025);

    [Test]
    public async Task Case_9513() => await Assert.That(Calc.Add(9513, 9514)).IsEqualTo(19027);

    [Test]
    public async Task Case_9514() => await Assert.That(Calc.Add(9514, 9515)).IsEqualTo(19029);

    [Test]
    public async Task Case_9515() => await Assert.That(Calc.Add(9515, 9516)).IsEqualTo(19031);

    [Test]
    public async Task Case_9516() => await Assert.That(Calc.Add(9516, 9517)).IsEqualTo(19033);

    [Test]
    public async Task Case_9517() => await Assert.That(Calc.Add(9517, 9518)).IsEqualTo(19035);

    [Test]
    public async Task Case_9518() => await Assert.That(Calc.Add(9518, 9519)).IsEqualTo(19037);

    [Test]
    public async Task Case_9519() => await Assert.That(Calc.Add(9519, 9520)).IsEqualTo(19039);

    [Test]
    public async Task Case_9520() => await Assert.That(Calc.Add(9520, 9521)).IsEqualTo(19041);
}
#endif
