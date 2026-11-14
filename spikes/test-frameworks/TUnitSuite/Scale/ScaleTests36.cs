#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests36
{
    [Test]
    public async Task Case_3601() => await Assert.That(Calc.Add(3601, 3602)).IsEqualTo(7203);

    [Test]
    public async Task Case_3602() => await Assert.That(Calc.Add(3602, 3603)).IsEqualTo(7205);

    [Test]
    public async Task Case_3603() => await Assert.That(Calc.Add(3603, 3604)).IsEqualTo(7207);

    [Test]
    public async Task Case_3604() => await Assert.That(Calc.Add(3604, 3605)).IsEqualTo(7209);

    [Test]
    public async Task Case_3605() => await Assert.That(Calc.Add(3605, 3606)).IsEqualTo(7211);

    [Test]
    public async Task Case_3606() => await Assert.That(Calc.Add(3606, 3607)).IsEqualTo(7213);

    [Test]
    public async Task Case_3607() => await Assert.That(Calc.Add(3607, 3608)).IsEqualTo(7215);

    [Test]
    public async Task Case_3608() => await Assert.That(Calc.Add(3608, 3609)).IsEqualTo(7217);

    [Test]
    public async Task Case_3609() => await Assert.That(Calc.Add(3609, 3610)).IsEqualTo(7219);

    [Test]
    public async Task Case_3610() => await Assert.That(Calc.Add(3610, 3611)).IsEqualTo(7221);

    [Test]
    public async Task Case_3611() => await Assert.That(Calc.Add(3611, 3612)).IsEqualTo(7223);

    [Test]
    public async Task Case_3612() => await Assert.That(Calc.Add(3612, 3613)).IsEqualTo(7225);

    [Test]
    public async Task Case_3613() => await Assert.That(Calc.Add(3613, 3614)).IsEqualTo(7227);

    [Test]
    public async Task Case_3614() => await Assert.That(Calc.Add(3614, 3615)).IsEqualTo(7229);

    [Test]
    public async Task Case_3615() => await Assert.That(Calc.Add(3615, 3616)).IsEqualTo(7231);

    [Test]
    public async Task Case_3616() => await Assert.That(Calc.Add(3616, 3617)).IsEqualTo(7233);

    [Test]
    public async Task Case_3617() => await Assert.That(Calc.Add(3617, 3618)).IsEqualTo(7235);

    [Test]
    public async Task Case_3618() => await Assert.That(Calc.Add(3618, 3619)).IsEqualTo(7237);

    [Test]
    public async Task Case_3619() => await Assert.That(Calc.Add(3619, 3620)).IsEqualTo(7239);

    [Test]
    public async Task Case_3620() => await Assert.That(Calc.Add(3620, 3621)).IsEqualTo(7241);
}
#endif
