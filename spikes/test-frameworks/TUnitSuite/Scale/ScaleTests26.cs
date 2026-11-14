#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests26
{
    [Test]
    public async Task Case_2601() => await Assert.That(Calc.Add(2601, 2602)).IsEqualTo(5203);

    [Test]
    public async Task Case_2602() => await Assert.That(Calc.Add(2602, 2603)).IsEqualTo(5205);

    [Test]
    public async Task Case_2603() => await Assert.That(Calc.Add(2603, 2604)).IsEqualTo(5207);

    [Test]
    public async Task Case_2604() => await Assert.That(Calc.Add(2604, 2605)).IsEqualTo(5209);

    [Test]
    public async Task Case_2605() => await Assert.That(Calc.Add(2605, 2606)).IsEqualTo(5211);

    [Test]
    public async Task Case_2606() => await Assert.That(Calc.Add(2606, 2607)).IsEqualTo(5213);

    [Test]
    public async Task Case_2607() => await Assert.That(Calc.Add(2607, 2608)).IsEqualTo(5215);

    [Test]
    public async Task Case_2608() => await Assert.That(Calc.Add(2608, 2609)).IsEqualTo(5217);

    [Test]
    public async Task Case_2609() => await Assert.That(Calc.Add(2609, 2610)).IsEqualTo(5219);

    [Test]
    public async Task Case_2610() => await Assert.That(Calc.Add(2610, 2611)).IsEqualTo(5221);

    [Test]
    public async Task Case_2611() => await Assert.That(Calc.Add(2611, 2612)).IsEqualTo(5223);

    [Test]
    public async Task Case_2612() => await Assert.That(Calc.Add(2612, 2613)).IsEqualTo(5225);

    [Test]
    public async Task Case_2613() => await Assert.That(Calc.Add(2613, 2614)).IsEqualTo(5227);

    [Test]
    public async Task Case_2614() => await Assert.That(Calc.Add(2614, 2615)).IsEqualTo(5229);

    [Test]
    public async Task Case_2615() => await Assert.That(Calc.Add(2615, 2616)).IsEqualTo(5231);

    [Test]
    public async Task Case_2616() => await Assert.That(Calc.Add(2616, 2617)).IsEqualTo(5233);

    [Test]
    public async Task Case_2617() => await Assert.That(Calc.Add(2617, 2618)).IsEqualTo(5235);

    [Test]
    public async Task Case_2618() => await Assert.That(Calc.Add(2618, 2619)).IsEqualTo(5237);

    [Test]
    public async Task Case_2619() => await Assert.That(Calc.Add(2619, 2620)).IsEqualTo(5239);

    [Test]
    public async Task Case_2620() => await Assert.That(Calc.Add(2620, 2621)).IsEqualTo(5241);
}
#endif
