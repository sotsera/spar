#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests96
{
    [Test]
    public async Task Case_9601() => await Assert.That(Calc.Add(9601, 9602)).IsEqualTo(19203);

    [Test]
    public async Task Case_9602() => await Assert.That(Calc.Add(9602, 9603)).IsEqualTo(19205);

    [Test]
    public async Task Case_9603() => await Assert.That(Calc.Add(9603, 9604)).IsEqualTo(19207);

    [Test]
    public async Task Case_9604() => await Assert.That(Calc.Add(9604, 9605)).IsEqualTo(19209);

    [Test]
    public async Task Case_9605() => await Assert.That(Calc.Add(9605, 9606)).IsEqualTo(19211);

    [Test]
    public async Task Case_9606() => await Assert.That(Calc.Add(9606, 9607)).IsEqualTo(19213);

    [Test]
    public async Task Case_9607() => await Assert.That(Calc.Add(9607, 9608)).IsEqualTo(19215);

    [Test]
    public async Task Case_9608() => await Assert.That(Calc.Add(9608, 9609)).IsEqualTo(19217);

    [Test]
    public async Task Case_9609() => await Assert.That(Calc.Add(9609, 9610)).IsEqualTo(19219);

    [Test]
    public async Task Case_9610() => await Assert.That(Calc.Add(9610, 9611)).IsEqualTo(19221);

    [Test]
    public async Task Case_9611() => await Assert.That(Calc.Add(9611, 9612)).IsEqualTo(19223);

    [Test]
    public async Task Case_9612() => await Assert.That(Calc.Add(9612, 9613)).IsEqualTo(19225);

    [Test]
    public async Task Case_9613() => await Assert.That(Calc.Add(9613, 9614)).IsEqualTo(19227);

    [Test]
    public async Task Case_9614() => await Assert.That(Calc.Add(9614, 9615)).IsEqualTo(19229);

    [Test]
    public async Task Case_9615() => await Assert.That(Calc.Add(9615, 9616)).IsEqualTo(19231);

    [Test]
    public async Task Case_9616() => await Assert.That(Calc.Add(9616, 9617)).IsEqualTo(19233);

    [Test]
    public async Task Case_9617() => await Assert.That(Calc.Add(9617, 9618)).IsEqualTo(19235);

    [Test]
    public async Task Case_9618() => await Assert.That(Calc.Add(9618, 9619)).IsEqualTo(19237);

    [Test]
    public async Task Case_9619() => await Assert.That(Calc.Add(9619, 9620)).IsEqualTo(19239);

    [Test]
    public async Task Case_9620() => await Assert.That(Calc.Add(9620, 9621)).IsEqualTo(19241);
}
#endif
