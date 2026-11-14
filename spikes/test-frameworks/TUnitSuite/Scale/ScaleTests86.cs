#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests86
{
    [Test]
    public async Task Case_8601() => await Assert.That(Calc.Add(8601, 8602)).IsEqualTo(17203);

    [Test]
    public async Task Case_8602() => await Assert.That(Calc.Add(8602, 8603)).IsEqualTo(17205);

    [Test]
    public async Task Case_8603() => await Assert.That(Calc.Add(8603, 8604)).IsEqualTo(17207);

    [Test]
    public async Task Case_8604() => await Assert.That(Calc.Add(8604, 8605)).IsEqualTo(17209);

    [Test]
    public async Task Case_8605() => await Assert.That(Calc.Add(8605, 8606)).IsEqualTo(17211);

    [Test]
    public async Task Case_8606() => await Assert.That(Calc.Add(8606, 8607)).IsEqualTo(17213);

    [Test]
    public async Task Case_8607() => await Assert.That(Calc.Add(8607, 8608)).IsEqualTo(17215);

    [Test]
    public async Task Case_8608() => await Assert.That(Calc.Add(8608, 8609)).IsEqualTo(17217);

    [Test]
    public async Task Case_8609() => await Assert.That(Calc.Add(8609, 8610)).IsEqualTo(17219);

    [Test]
    public async Task Case_8610() => await Assert.That(Calc.Add(8610, 8611)).IsEqualTo(17221);

    [Test]
    public async Task Case_8611() => await Assert.That(Calc.Add(8611, 8612)).IsEqualTo(17223);

    [Test]
    public async Task Case_8612() => await Assert.That(Calc.Add(8612, 8613)).IsEqualTo(17225);

    [Test]
    public async Task Case_8613() => await Assert.That(Calc.Add(8613, 8614)).IsEqualTo(17227);

    [Test]
    public async Task Case_8614() => await Assert.That(Calc.Add(8614, 8615)).IsEqualTo(17229);

    [Test]
    public async Task Case_8615() => await Assert.That(Calc.Add(8615, 8616)).IsEqualTo(17231);

    [Test]
    public async Task Case_8616() => await Assert.That(Calc.Add(8616, 8617)).IsEqualTo(17233);

    [Test]
    public async Task Case_8617() => await Assert.That(Calc.Add(8617, 8618)).IsEqualTo(17235);

    [Test]
    public async Task Case_8618() => await Assert.That(Calc.Add(8618, 8619)).IsEqualTo(17237);

    [Test]
    public async Task Case_8619() => await Assert.That(Calc.Add(8619, 8620)).IsEqualTo(17239);

    [Test]
    public async Task Case_8620() => await Assert.That(Calc.Add(8620, 8621)).IsEqualTo(17241);
}
#endif
