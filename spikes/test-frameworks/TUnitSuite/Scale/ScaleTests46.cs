#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests46
{
    [Test]
    public async Task Case_4601() => await Assert.That(Calc.Add(4601, 4602)).IsEqualTo(9203);

    [Test]
    public async Task Case_4602() => await Assert.That(Calc.Add(4602, 4603)).IsEqualTo(9205);

    [Test]
    public async Task Case_4603() => await Assert.That(Calc.Add(4603, 4604)).IsEqualTo(9207);

    [Test]
    public async Task Case_4604() => await Assert.That(Calc.Add(4604, 4605)).IsEqualTo(9209);

    [Test]
    public async Task Case_4605() => await Assert.That(Calc.Add(4605, 4606)).IsEqualTo(9211);

    [Test]
    public async Task Case_4606() => await Assert.That(Calc.Add(4606, 4607)).IsEqualTo(9213);

    [Test]
    public async Task Case_4607() => await Assert.That(Calc.Add(4607, 4608)).IsEqualTo(9215);

    [Test]
    public async Task Case_4608() => await Assert.That(Calc.Add(4608, 4609)).IsEqualTo(9217);

    [Test]
    public async Task Case_4609() => await Assert.That(Calc.Add(4609, 4610)).IsEqualTo(9219);

    [Test]
    public async Task Case_4610() => await Assert.That(Calc.Add(4610, 4611)).IsEqualTo(9221);

    [Test]
    public async Task Case_4611() => await Assert.That(Calc.Add(4611, 4612)).IsEqualTo(9223);

    [Test]
    public async Task Case_4612() => await Assert.That(Calc.Add(4612, 4613)).IsEqualTo(9225);

    [Test]
    public async Task Case_4613() => await Assert.That(Calc.Add(4613, 4614)).IsEqualTo(9227);

    [Test]
    public async Task Case_4614() => await Assert.That(Calc.Add(4614, 4615)).IsEqualTo(9229);

    [Test]
    public async Task Case_4615() => await Assert.That(Calc.Add(4615, 4616)).IsEqualTo(9231);

    [Test]
    public async Task Case_4616() => await Assert.That(Calc.Add(4616, 4617)).IsEqualTo(9233);

    [Test]
    public async Task Case_4617() => await Assert.That(Calc.Add(4617, 4618)).IsEqualTo(9235);

    [Test]
    public async Task Case_4618() => await Assert.That(Calc.Add(4618, 4619)).IsEqualTo(9237);

    [Test]
    public async Task Case_4619() => await Assert.That(Calc.Add(4619, 4620)).IsEqualTo(9239);

    [Test]
    public async Task Case_4620() => await Assert.That(Calc.Add(4620, 4621)).IsEqualTo(9241);
}
#endif
