#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests56
{
    [Test]
    public async Task Case_5601() => await Assert.That(Calc.Add(5601, 5602)).IsEqualTo(11203);

    [Test]
    public async Task Case_5602() => await Assert.That(Calc.Add(5602, 5603)).IsEqualTo(11205);

    [Test]
    public async Task Case_5603() => await Assert.That(Calc.Add(5603, 5604)).IsEqualTo(11207);

    [Test]
    public async Task Case_5604() => await Assert.That(Calc.Add(5604, 5605)).IsEqualTo(11209);

    [Test]
    public async Task Case_5605() => await Assert.That(Calc.Add(5605, 5606)).IsEqualTo(11211);

    [Test]
    public async Task Case_5606() => await Assert.That(Calc.Add(5606, 5607)).IsEqualTo(11213);

    [Test]
    public async Task Case_5607() => await Assert.That(Calc.Add(5607, 5608)).IsEqualTo(11215);

    [Test]
    public async Task Case_5608() => await Assert.That(Calc.Add(5608, 5609)).IsEqualTo(11217);

    [Test]
    public async Task Case_5609() => await Assert.That(Calc.Add(5609, 5610)).IsEqualTo(11219);

    [Test]
    public async Task Case_5610() => await Assert.That(Calc.Add(5610, 5611)).IsEqualTo(11221);

    [Test]
    public async Task Case_5611() => await Assert.That(Calc.Add(5611, 5612)).IsEqualTo(11223);

    [Test]
    public async Task Case_5612() => await Assert.That(Calc.Add(5612, 5613)).IsEqualTo(11225);

    [Test]
    public async Task Case_5613() => await Assert.That(Calc.Add(5613, 5614)).IsEqualTo(11227);

    [Test]
    public async Task Case_5614() => await Assert.That(Calc.Add(5614, 5615)).IsEqualTo(11229);

    [Test]
    public async Task Case_5615() => await Assert.That(Calc.Add(5615, 5616)).IsEqualTo(11231);

    [Test]
    public async Task Case_5616() => await Assert.That(Calc.Add(5616, 5617)).IsEqualTo(11233);

    [Test]
    public async Task Case_5617() => await Assert.That(Calc.Add(5617, 5618)).IsEqualTo(11235);

    [Test]
    public async Task Case_5618() => await Assert.That(Calc.Add(5618, 5619)).IsEqualTo(11237);

    [Test]
    public async Task Case_5619() => await Assert.That(Calc.Add(5619, 5620)).IsEqualTo(11239);

    [Test]
    public async Task Case_5620() => await Assert.That(Calc.Add(5620, 5621)).IsEqualTo(11241);
}
#endif
