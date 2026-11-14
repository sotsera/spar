#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests13
{
    [Test]
    public async Task Case_1301() => await Assert.That(Calc.Add(1301, 1302)).IsEqualTo(2603);

    [Test]
    public async Task Case_1302() => await Assert.That(Calc.Add(1302, 1303)).IsEqualTo(2605);

    [Test]
    public async Task Case_1303() => await Assert.That(Calc.Add(1303, 1304)).IsEqualTo(2607);

    [Test]
    public async Task Case_1304() => await Assert.That(Calc.Add(1304, 1305)).IsEqualTo(2609);

    [Test]
    public async Task Case_1305() => await Assert.That(Calc.Add(1305, 1306)).IsEqualTo(2611);

    [Test]
    public async Task Case_1306() => await Assert.That(Calc.Add(1306, 1307)).IsEqualTo(2613);

    [Test]
    public async Task Case_1307() => await Assert.That(Calc.Add(1307, 1308)).IsEqualTo(2615);

    [Test]
    public async Task Case_1308() => await Assert.That(Calc.Add(1308, 1309)).IsEqualTo(2617);

    [Test]
    public async Task Case_1309() => await Assert.That(Calc.Add(1309, 1310)).IsEqualTo(2619);

    [Test]
    public async Task Case_1310() => await Assert.That(Calc.Add(1310, 1311)).IsEqualTo(2621);

    [Test]
    public async Task Case_1311() => await Assert.That(Calc.Add(1311, 1312)).IsEqualTo(2623);

    [Test]
    public async Task Case_1312() => await Assert.That(Calc.Add(1312, 1313)).IsEqualTo(2625);

    [Test]
    public async Task Case_1313() => await Assert.That(Calc.Add(1313, 1314)).IsEqualTo(2627);

    [Test]
    public async Task Case_1314() => await Assert.That(Calc.Add(1314, 1315)).IsEqualTo(2629);

    [Test]
    public async Task Case_1315() => await Assert.That(Calc.Add(1315, 1316)).IsEqualTo(2631);

    [Test]
    public async Task Case_1316() => await Assert.That(Calc.Add(1316, 1317)).IsEqualTo(2633);

    [Test]
    public async Task Case_1317() => await Assert.That(Calc.Add(1317, 1318)).IsEqualTo(2635);

    [Test]
    public async Task Case_1318() => await Assert.That(Calc.Add(1318, 1319)).IsEqualTo(2637);

    [Test]
    public async Task Case_1319() => await Assert.That(Calc.Add(1319, 1320)).IsEqualTo(2639);

    [Test]
    public async Task Case_1320() => await Assert.That(Calc.Add(1320, 1321)).IsEqualTo(2641);
}
#endif
