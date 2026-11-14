#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests24
{
    [Test]
    public async Task Case_2401() => await Assert.That(Calc.Add(2401, 2402)).IsEqualTo(4803);

    [Test]
    public async Task Case_2402() => await Assert.That(Calc.Add(2402, 2403)).IsEqualTo(4805);

    [Test]
    public async Task Case_2403() => await Assert.That(Calc.Add(2403, 2404)).IsEqualTo(4807);

    [Test]
    public async Task Case_2404() => await Assert.That(Calc.Add(2404, 2405)).IsEqualTo(4809);

    [Test]
    public async Task Case_2405() => await Assert.That(Calc.Add(2405, 2406)).IsEqualTo(4811);

    [Test]
    public async Task Case_2406() => await Assert.That(Calc.Add(2406, 2407)).IsEqualTo(4813);

    [Test]
    public async Task Case_2407() => await Assert.That(Calc.Add(2407, 2408)).IsEqualTo(4815);

    [Test]
    public async Task Case_2408() => await Assert.That(Calc.Add(2408, 2409)).IsEqualTo(4817);

    [Test]
    public async Task Case_2409() => await Assert.That(Calc.Add(2409, 2410)).IsEqualTo(4819);

    [Test]
    public async Task Case_2410() => await Assert.That(Calc.Add(2410, 2411)).IsEqualTo(4821);

    [Test]
    public async Task Case_2411() => await Assert.That(Calc.Add(2411, 2412)).IsEqualTo(4823);

    [Test]
    public async Task Case_2412() => await Assert.That(Calc.Add(2412, 2413)).IsEqualTo(4825);

    [Test]
    public async Task Case_2413() => await Assert.That(Calc.Add(2413, 2414)).IsEqualTo(4827);

    [Test]
    public async Task Case_2414() => await Assert.That(Calc.Add(2414, 2415)).IsEqualTo(4829);

    [Test]
    public async Task Case_2415() => await Assert.That(Calc.Add(2415, 2416)).IsEqualTo(4831);

    [Test]
    public async Task Case_2416() => await Assert.That(Calc.Add(2416, 2417)).IsEqualTo(4833);

    [Test]
    public async Task Case_2417() => await Assert.That(Calc.Add(2417, 2418)).IsEqualTo(4835);

    [Test]
    public async Task Case_2418() => await Assert.That(Calc.Add(2418, 2419)).IsEqualTo(4837);

    [Test]
    public async Task Case_2419() => await Assert.That(Calc.Add(2419, 2420)).IsEqualTo(4839);

    [Test]
    public async Task Case_2420() => await Assert.That(Calc.Add(2420, 2421)).IsEqualTo(4841);
}
#endif
