#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests25
{
    [Test]
    public async Task Case_2501() => await Assert.That(Calc.Add(2501, 2502)).IsEqualTo(5003);

    [Test]
    public async Task Case_2502() => await Assert.That(Calc.Add(2502, 2503)).IsEqualTo(5005);

    [Test]
    public async Task Case_2503() => await Assert.That(Calc.Add(2503, 2504)).IsEqualTo(5007);

    [Test]
    public async Task Case_2504() => await Assert.That(Calc.Add(2504, 2505)).IsEqualTo(5009);

    [Test]
    public async Task Case_2505() => await Assert.That(Calc.Add(2505, 2506)).IsEqualTo(5011);

    [Test]
    public async Task Case_2506() => await Assert.That(Calc.Add(2506, 2507)).IsEqualTo(5013);

    [Test]
    public async Task Case_2507() => await Assert.That(Calc.Add(2507, 2508)).IsEqualTo(5015);

    [Test]
    public async Task Case_2508() => await Assert.That(Calc.Add(2508, 2509)).IsEqualTo(5017);

    [Test]
    public async Task Case_2509() => await Assert.That(Calc.Add(2509, 2510)).IsEqualTo(5019);

    [Test]
    public async Task Case_2510() => await Assert.That(Calc.Add(2510, 2511)).IsEqualTo(5021);

    [Test]
    public async Task Case_2511() => await Assert.That(Calc.Add(2511, 2512)).IsEqualTo(5023);

    [Test]
    public async Task Case_2512() => await Assert.That(Calc.Add(2512, 2513)).IsEqualTo(5025);

    [Test]
    public async Task Case_2513() => await Assert.That(Calc.Add(2513, 2514)).IsEqualTo(5027);

    [Test]
    public async Task Case_2514() => await Assert.That(Calc.Add(2514, 2515)).IsEqualTo(5029);

    [Test]
    public async Task Case_2515() => await Assert.That(Calc.Add(2515, 2516)).IsEqualTo(5031);

    [Test]
    public async Task Case_2516() => await Assert.That(Calc.Add(2516, 2517)).IsEqualTo(5033);

    [Test]
    public async Task Case_2517() => await Assert.That(Calc.Add(2517, 2518)).IsEqualTo(5035);

    [Test]
    public async Task Case_2518() => await Assert.That(Calc.Add(2518, 2519)).IsEqualTo(5037);

    [Test]
    public async Task Case_2519() => await Assert.That(Calc.Add(2519, 2520)).IsEqualTo(5039);

    [Test]
    public async Task Case_2520() => await Assert.That(Calc.Add(2520, 2521)).IsEqualTo(5041);
}
#endif
