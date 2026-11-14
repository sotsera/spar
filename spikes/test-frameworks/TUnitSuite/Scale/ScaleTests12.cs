#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests12
{
    [Test]
    public async Task Case_1201() => await Assert.That(Calc.Add(1201, 1202)).IsEqualTo(2403);

    [Test]
    public async Task Case_1202() => await Assert.That(Calc.Add(1202, 1203)).IsEqualTo(2405);

    [Test]
    public async Task Case_1203() => await Assert.That(Calc.Add(1203, 1204)).IsEqualTo(2407);

    [Test]
    public async Task Case_1204() => await Assert.That(Calc.Add(1204, 1205)).IsEqualTo(2409);

    [Test]
    public async Task Case_1205() => await Assert.That(Calc.Add(1205, 1206)).IsEqualTo(2411);

    [Test]
    public async Task Case_1206() => await Assert.That(Calc.Add(1206, 1207)).IsEqualTo(2413);

    [Test]
    public async Task Case_1207() => await Assert.That(Calc.Add(1207, 1208)).IsEqualTo(2415);

    [Test]
    public async Task Case_1208() => await Assert.That(Calc.Add(1208, 1209)).IsEqualTo(2417);

    [Test]
    public async Task Case_1209() => await Assert.That(Calc.Add(1209, 1210)).IsEqualTo(2419);

    [Test]
    public async Task Case_1210() => await Assert.That(Calc.Add(1210, 1211)).IsEqualTo(2421);

    [Test]
    public async Task Case_1211() => await Assert.That(Calc.Add(1211, 1212)).IsEqualTo(2423);

    [Test]
    public async Task Case_1212() => await Assert.That(Calc.Add(1212, 1213)).IsEqualTo(2425);

    [Test]
    public async Task Case_1213() => await Assert.That(Calc.Add(1213, 1214)).IsEqualTo(2427);

    [Test]
    public async Task Case_1214() => await Assert.That(Calc.Add(1214, 1215)).IsEqualTo(2429);

    [Test]
    public async Task Case_1215() => await Assert.That(Calc.Add(1215, 1216)).IsEqualTo(2431);

    [Test]
    public async Task Case_1216() => await Assert.That(Calc.Add(1216, 1217)).IsEqualTo(2433);

    [Test]
    public async Task Case_1217() => await Assert.That(Calc.Add(1217, 1218)).IsEqualTo(2435);

    [Test]
    public async Task Case_1218() => await Assert.That(Calc.Add(1218, 1219)).IsEqualTo(2437);

    [Test]
    public async Task Case_1219() => await Assert.That(Calc.Add(1219, 1220)).IsEqualTo(2439);

    [Test]
    public async Task Case_1220() => await Assert.That(Calc.Add(1220, 1221)).IsEqualTo(2441);
}
#endif
