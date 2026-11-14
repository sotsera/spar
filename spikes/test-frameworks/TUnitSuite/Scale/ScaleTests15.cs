#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests15
{
    [Test]
    public async Task Case_1501() => await Assert.That(Calc.Add(1501, 1502)).IsEqualTo(3003);

    [Test]
    public async Task Case_1502() => await Assert.That(Calc.Add(1502, 1503)).IsEqualTo(3005);

    [Test]
    public async Task Case_1503() => await Assert.That(Calc.Add(1503, 1504)).IsEqualTo(3007);

    [Test]
    public async Task Case_1504() => await Assert.That(Calc.Add(1504, 1505)).IsEqualTo(3009);

    [Test]
    public async Task Case_1505() => await Assert.That(Calc.Add(1505, 1506)).IsEqualTo(3011);

    [Test]
    public async Task Case_1506() => await Assert.That(Calc.Add(1506, 1507)).IsEqualTo(3013);

    [Test]
    public async Task Case_1507() => await Assert.That(Calc.Add(1507, 1508)).IsEqualTo(3015);

    [Test]
    public async Task Case_1508() => await Assert.That(Calc.Add(1508, 1509)).IsEqualTo(3017);

    [Test]
    public async Task Case_1509() => await Assert.That(Calc.Add(1509, 1510)).IsEqualTo(3019);

    [Test]
    public async Task Case_1510() => await Assert.That(Calc.Add(1510, 1511)).IsEqualTo(3021);

    [Test]
    public async Task Case_1511() => await Assert.That(Calc.Add(1511, 1512)).IsEqualTo(3023);

    [Test]
    public async Task Case_1512() => await Assert.That(Calc.Add(1512, 1513)).IsEqualTo(3025);

    [Test]
    public async Task Case_1513() => await Assert.That(Calc.Add(1513, 1514)).IsEqualTo(3027);

    [Test]
    public async Task Case_1514() => await Assert.That(Calc.Add(1514, 1515)).IsEqualTo(3029);

    [Test]
    public async Task Case_1515() => await Assert.That(Calc.Add(1515, 1516)).IsEqualTo(3031);

    [Test]
    public async Task Case_1516() => await Assert.That(Calc.Add(1516, 1517)).IsEqualTo(3033);

    [Test]
    public async Task Case_1517() => await Assert.That(Calc.Add(1517, 1518)).IsEqualTo(3035);

    [Test]
    public async Task Case_1518() => await Assert.That(Calc.Add(1518, 1519)).IsEqualTo(3037);

    [Test]
    public async Task Case_1519() => await Assert.That(Calc.Add(1519, 1520)).IsEqualTo(3039);

    [Test]
    public async Task Case_1520() => await Assert.That(Calc.Add(1520, 1521)).IsEqualTo(3041);
}
#endif
