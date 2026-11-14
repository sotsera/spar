#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests16
{
    [Test]
    public async Task Case_1601() => await Assert.That(Calc.Add(1601, 1602)).IsEqualTo(3203);

    [Test]
    public async Task Case_1602() => await Assert.That(Calc.Add(1602, 1603)).IsEqualTo(3205);

    [Test]
    public async Task Case_1603() => await Assert.That(Calc.Add(1603, 1604)).IsEqualTo(3207);

    [Test]
    public async Task Case_1604() => await Assert.That(Calc.Add(1604, 1605)).IsEqualTo(3209);

    [Test]
    public async Task Case_1605() => await Assert.That(Calc.Add(1605, 1606)).IsEqualTo(3211);

    [Test]
    public async Task Case_1606() => await Assert.That(Calc.Add(1606, 1607)).IsEqualTo(3213);

    [Test]
    public async Task Case_1607() => await Assert.That(Calc.Add(1607, 1608)).IsEqualTo(3215);

    [Test]
    public async Task Case_1608() => await Assert.That(Calc.Add(1608, 1609)).IsEqualTo(3217);

    [Test]
    public async Task Case_1609() => await Assert.That(Calc.Add(1609, 1610)).IsEqualTo(3219);

    [Test]
    public async Task Case_1610() => await Assert.That(Calc.Add(1610, 1611)).IsEqualTo(3221);

    [Test]
    public async Task Case_1611() => await Assert.That(Calc.Add(1611, 1612)).IsEqualTo(3223);

    [Test]
    public async Task Case_1612() => await Assert.That(Calc.Add(1612, 1613)).IsEqualTo(3225);

    [Test]
    public async Task Case_1613() => await Assert.That(Calc.Add(1613, 1614)).IsEqualTo(3227);

    [Test]
    public async Task Case_1614() => await Assert.That(Calc.Add(1614, 1615)).IsEqualTo(3229);

    [Test]
    public async Task Case_1615() => await Assert.That(Calc.Add(1615, 1616)).IsEqualTo(3231);

    [Test]
    public async Task Case_1616() => await Assert.That(Calc.Add(1616, 1617)).IsEqualTo(3233);

    [Test]
    public async Task Case_1617() => await Assert.That(Calc.Add(1617, 1618)).IsEqualTo(3235);

    [Test]
    public async Task Case_1618() => await Assert.That(Calc.Add(1618, 1619)).IsEqualTo(3237);

    [Test]
    public async Task Case_1619() => await Assert.That(Calc.Add(1619, 1620)).IsEqualTo(3239);

    [Test]
    public async Task Case_1620() => await Assert.That(Calc.Add(1620, 1621)).IsEqualTo(3241);
}
#endif
