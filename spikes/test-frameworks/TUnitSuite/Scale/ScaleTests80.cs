#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests80
{
    [Test]
    public async Task Case_8001() => await Assert.That(Calc.Add(8001, 8002)).IsEqualTo(16003);

    [Test]
    public async Task Case_8002() => await Assert.That(Calc.Add(8002, 8003)).IsEqualTo(16005);

    [Test]
    public async Task Case_8003() => await Assert.That(Calc.Add(8003, 8004)).IsEqualTo(16007);

    [Test]
    public async Task Case_8004() => await Assert.That(Calc.Add(8004, 8005)).IsEqualTo(16009);

    [Test]
    public async Task Case_8005() => await Assert.That(Calc.Add(8005, 8006)).IsEqualTo(16011);

    [Test]
    public async Task Case_8006() => await Assert.That(Calc.Add(8006, 8007)).IsEqualTo(16013);

    [Test]
    public async Task Case_8007() => await Assert.That(Calc.Add(8007, 8008)).IsEqualTo(16015);

    [Test]
    public async Task Case_8008() => await Assert.That(Calc.Add(8008, 8009)).IsEqualTo(16017);

    [Test]
    public async Task Case_8009() => await Assert.That(Calc.Add(8009, 8010)).IsEqualTo(16019);

    [Test]
    public async Task Case_8010() => await Assert.That(Calc.Add(8010, 8011)).IsEqualTo(16021);

    [Test]
    public async Task Case_8011() => await Assert.That(Calc.Add(8011, 8012)).IsEqualTo(16023);

    [Test]
    public async Task Case_8012() => await Assert.That(Calc.Add(8012, 8013)).IsEqualTo(16025);

    [Test]
    public async Task Case_8013() => await Assert.That(Calc.Add(8013, 8014)).IsEqualTo(16027);

    [Test]
    public async Task Case_8014() => await Assert.That(Calc.Add(8014, 8015)).IsEqualTo(16029);

    [Test]
    public async Task Case_8015() => await Assert.That(Calc.Add(8015, 8016)).IsEqualTo(16031);

    [Test]
    public async Task Case_8016() => await Assert.That(Calc.Add(8016, 8017)).IsEqualTo(16033);

    [Test]
    public async Task Case_8017() => await Assert.That(Calc.Add(8017, 8018)).IsEqualTo(16035);

    [Test]
    public async Task Case_8018() => await Assert.That(Calc.Add(8018, 8019)).IsEqualTo(16037);

    [Test]
    public async Task Case_8019() => await Assert.That(Calc.Add(8019, 8020)).IsEqualTo(16039);

    [Test]
    public async Task Case_8020() => await Assert.That(Calc.Add(8020, 8021)).IsEqualTo(16041);
}
#endif
