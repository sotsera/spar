#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests40
{
    [Test]
    public async Task Case_4001() => await Assert.That(Calc.Add(4001, 4002)).IsEqualTo(8003);

    [Test]
    public async Task Case_4002() => await Assert.That(Calc.Add(4002, 4003)).IsEqualTo(8005);

    [Test]
    public async Task Case_4003() => await Assert.That(Calc.Add(4003, 4004)).IsEqualTo(8007);

    [Test]
    public async Task Case_4004() => await Assert.That(Calc.Add(4004, 4005)).IsEqualTo(8009);

    [Test]
    public async Task Case_4005() => await Assert.That(Calc.Add(4005, 4006)).IsEqualTo(8011);

    [Test]
    public async Task Case_4006() => await Assert.That(Calc.Add(4006, 4007)).IsEqualTo(8013);

    [Test]
    public async Task Case_4007() => await Assert.That(Calc.Add(4007, 4008)).IsEqualTo(8015);

    [Test]
    public async Task Case_4008() => await Assert.That(Calc.Add(4008, 4009)).IsEqualTo(8017);

    [Test]
    public async Task Case_4009() => await Assert.That(Calc.Add(4009, 4010)).IsEqualTo(8019);

    [Test]
    public async Task Case_4010() => await Assert.That(Calc.Add(4010, 4011)).IsEqualTo(8021);

    [Test]
    public async Task Case_4011() => await Assert.That(Calc.Add(4011, 4012)).IsEqualTo(8023);

    [Test]
    public async Task Case_4012() => await Assert.That(Calc.Add(4012, 4013)).IsEqualTo(8025);

    [Test]
    public async Task Case_4013() => await Assert.That(Calc.Add(4013, 4014)).IsEqualTo(8027);

    [Test]
    public async Task Case_4014() => await Assert.That(Calc.Add(4014, 4015)).IsEqualTo(8029);

    [Test]
    public async Task Case_4015() => await Assert.That(Calc.Add(4015, 4016)).IsEqualTo(8031);

    [Test]
    public async Task Case_4016() => await Assert.That(Calc.Add(4016, 4017)).IsEqualTo(8033);

    [Test]
    public async Task Case_4017() => await Assert.That(Calc.Add(4017, 4018)).IsEqualTo(8035);

    [Test]
    public async Task Case_4018() => await Assert.That(Calc.Add(4018, 4019)).IsEqualTo(8037);

    [Test]
    public async Task Case_4019() => await Assert.That(Calc.Add(4019, 4020)).IsEqualTo(8039);

    [Test]
    public async Task Case_4020() => await Assert.That(Calc.Add(4020, 4021)).IsEqualTo(8041);
}
#endif
