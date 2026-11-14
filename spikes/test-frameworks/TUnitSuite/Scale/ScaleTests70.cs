#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests70
{
    [Test]
    public async Task Case_7001() => await Assert.That(Calc.Add(7001, 7002)).IsEqualTo(14003);

    [Test]
    public async Task Case_7002() => await Assert.That(Calc.Add(7002, 7003)).IsEqualTo(14005);

    [Test]
    public async Task Case_7003() => await Assert.That(Calc.Add(7003, 7004)).IsEqualTo(14007);

    [Test]
    public async Task Case_7004() => await Assert.That(Calc.Add(7004, 7005)).IsEqualTo(14009);

    [Test]
    public async Task Case_7005() => await Assert.That(Calc.Add(7005, 7006)).IsEqualTo(14011);

    [Test]
    public async Task Case_7006() => await Assert.That(Calc.Add(7006, 7007)).IsEqualTo(14013);

    [Test]
    public async Task Case_7007() => await Assert.That(Calc.Add(7007, 7008)).IsEqualTo(14015);

    [Test]
    public async Task Case_7008() => await Assert.That(Calc.Add(7008, 7009)).IsEqualTo(14017);

    [Test]
    public async Task Case_7009() => await Assert.That(Calc.Add(7009, 7010)).IsEqualTo(14019);

    [Test]
    public async Task Case_7010() => await Assert.That(Calc.Add(7010, 7011)).IsEqualTo(14021);

    [Test]
    public async Task Case_7011() => await Assert.That(Calc.Add(7011, 7012)).IsEqualTo(14023);

    [Test]
    public async Task Case_7012() => await Assert.That(Calc.Add(7012, 7013)).IsEqualTo(14025);

    [Test]
    public async Task Case_7013() => await Assert.That(Calc.Add(7013, 7014)).IsEqualTo(14027);

    [Test]
    public async Task Case_7014() => await Assert.That(Calc.Add(7014, 7015)).IsEqualTo(14029);

    [Test]
    public async Task Case_7015() => await Assert.That(Calc.Add(7015, 7016)).IsEqualTo(14031);

    [Test]
    public async Task Case_7016() => await Assert.That(Calc.Add(7016, 7017)).IsEqualTo(14033);

    [Test]
    public async Task Case_7017() => await Assert.That(Calc.Add(7017, 7018)).IsEqualTo(14035);

    [Test]
    public async Task Case_7018() => await Assert.That(Calc.Add(7018, 7019)).IsEqualTo(14037);

    [Test]
    public async Task Case_7019() => await Assert.That(Calc.Add(7019, 7020)).IsEqualTo(14039);

    [Test]
    public async Task Case_7020() => await Assert.That(Calc.Add(7020, 7021)).IsEqualTo(14041);
}
#endif
