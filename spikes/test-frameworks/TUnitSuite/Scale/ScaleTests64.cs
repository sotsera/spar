#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests64
{
    [Test]
    public async Task Case_6401() => await Assert.That(Calc.Add(6401, 6402)).IsEqualTo(12803);

    [Test]
    public async Task Case_6402() => await Assert.That(Calc.Add(6402, 6403)).IsEqualTo(12805);

    [Test]
    public async Task Case_6403() => await Assert.That(Calc.Add(6403, 6404)).IsEqualTo(12807);

    [Test]
    public async Task Case_6404() => await Assert.That(Calc.Add(6404, 6405)).IsEqualTo(12809);

    [Test]
    public async Task Case_6405() => await Assert.That(Calc.Add(6405, 6406)).IsEqualTo(12811);

    [Test]
    public async Task Case_6406() => await Assert.That(Calc.Add(6406, 6407)).IsEqualTo(12813);

    [Test]
    public async Task Case_6407() => await Assert.That(Calc.Add(6407, 6408)).IsEqualTo(12815);

    [Test]
    public async Task Case_6408() => await Assert.That(Calc.Add(6408, 6409)).IsEqualTo(12817);

    [Test]
    public async Task Case_6409() => await Assert.That(Calc.Add(6409, 6410)).IsEqualTo(12819);

    [Test]
    public async Task Case_6410() => await Assert.That(Calc.Add(6410, 6411)).IsEqualTo(12821);

    [Test]
    public async Task Case_6411() => await Assert.That(Calc.Add(6411, 6412)).IsEqualTo(12823);

    [Test]
    public async Task Case_6412() => await Assert.That(Calc.Add(6412, 6413)).IsEqualTo(12825);

    [Test]
    public async Task Case_6413() => await Assert.That(Calc.Add(6413, 6414)).IsEqualTo(12827);

    [Test]
    public async Task Case_6414() => await Assert.That(Calc.Add(6414, 6415)).IsEqualTo(12829);

    [Test]
    public async Task Case_6415() => await Assert.That(Calc.Add(6415, 6416)).IsEqualTo(12831);

    [Test]
    public async Task Case_6416() => await Assert.That(Calc.Add(6416, 6417)).IsEqualTo(12833);

    [Test]
    public async Task Case_6417() => await Assert.That(Calc.Add(6417, 6418)).IsEqualTo(12835);

    [Test]
    public async Task Case_6418() => await Assert.That(Calc.Add(6418, 6419)).IsEqualTo(12837);

    [Test]
    public async Task Case_6419() => await Assert.That(Calc.Add(6419, 6420)).IsEqualTo(12839);

    [Test]
    public async Task Case_6420() => await Assert.That(Calc.Add(6420, 6421)).IsEqualTo(12841);
}
#endif
