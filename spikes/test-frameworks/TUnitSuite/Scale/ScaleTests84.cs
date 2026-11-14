#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests84
{
    [Test]
    public async Task Case_8401() => await Assert.That(Calc.Add(8401, 8402)).IsEqualTo(16803);

    [Test]
    public async Task Case_8402() => await Assert.That(Calc.Add(8402, 8403)).IsEqualTo(16805);

    [Test]
    public async Task Case_8403() => await Assert.That(Calc.Add(8403, 8404)).IsEqualTo(16807);

    [Test]
    public async Task Case_8404() => await Assert.That(Calc.Add(8404, 8405)).IsEqualTo(16809);

    [Test]
    public async Task Case_8405() => await Assert.That(Calc.Add(8405, 8406)).IsEqualTo(16811);

    [Test]
    public async Task Case_8406() => await Assert.That(Calc.Add(8406, 8407)).IsEqualTo(16813);

    [Test]
    public async Task Case_8407() => await Assert.That(Calc.Add(8407, 8408)).IsEqualTo(16815);

    [Test]
    public async Task Case_8408() => await Assert.That(Calc.Add(8408, 8409)).IsEqualTo(16817);

    [Test]
    public async Task Case_8409() => await Assert.That(Calc.Add(8409, 8410)).IsEqualTo(16819);

    [Test]
    public async Task Case_8410() => await Assert.That(Calc.Add(8410, 8411)).IsEqualTo(16821);

    [Test]
    public async Task Case_8411() => await Assert.That(Calc.Add(8411, 8412)).IsEqualTo(16823);

    [Test]
    public async Task Case_8412() => await Assert.That(Calc.Add(8412, 8413)).IsEqualTo(16825);

    [Test]
    public async Task Case_8413() => await Assert.That(Calc.Add(8413, 8414)).IsEqualTo(16827);

    [Test]
    public async Task Case_8414() => await Assert.That(Calc.Add(8414, 8415)).IsEqualTo(16829);

    [Test]
    public async Task Case_8415() => await Assert.That(Calc.Add(8415, 8416)).IsEqualTo(16831);

    [Test]
    public async Task Case_8416() => await Assert.That(Calc.Add(8416, 8417)).IsEqualTo(16833);

    [Test]
    public async Task Case_8417() => await Assert.That(Calc.Add(8417, 8418)).IsEqualTo(16835);

    [Test]
    public async Task Case_8418() => await Assert.That(Calc.Add(8418, 8419)).IsEqualTo(16837);

    [Test]
    public async Task Case_8419() => await Assert.That(Calc.Add(8419, 8420)).IsEqualTo(16839);

    [Test]
    public async Task Case_8420() => await Assert.That(Calc.Add(8420, 8421)).IsEqualTo(16841);
}
#endif
