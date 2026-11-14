#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests60
{
    [Test]
    public async Task Case_6001() => await Assert.That(Calc.Add(6001, 6002)).IsEqualTo(12003);

    [Test]
    public async Task Case_6002() => await Assert.That(Calc.Add(6002, 6003)).IsEqualTo(12005);

    [Test]
    public async Task Case_6003() => await Assert.That(Calc.Add(6003, 6004)).IsEqualTo(12007);

    [Test]
    public async Task Case_6004() => await Assert.That(Calc.Add(6004, 6005)).IsEqualTo(12009);

    [Test]
    public async Task Case_6005() => await Assert.That(Calc.Add(6005, 6006)).IsEqualTo(12011);

    [Test]
    public async Task Case_6006() => await Assert.That(Calc.Add(6006, 6007)).IsEqualTo(12013);

    [Test]
    public async Task Case_6007() => await Assert.That(Calc.Add(6007, 6008)).IsEqualTo(12015);

    [Test]
    public async Task Case_6008() => await Assert.That(Calc.Add(6008, 6009)).IsEqualTo(12017);

    [Test]
    public async Task Case_6009() => await Assert.That(Calc.Add(6009, 6010)).IsEqualTo(12019);

    [Test]
    public async Task Case_6010() => await Assert.That(Calc.Add(6010, 6011)).IsEqualTo(12021);

    [Test]
    public async Task Case_6011() => await Assert.That(Calc.Add(6011, 6012)).IsEqualTo(12023);

    [Test]
    public async Task Case_6012() => await Assert.That(Calc.Add(6012, 6013)).IsEqualTo(12025);

    [Test]
    public async Task Case_6013() => await Assert.That(Calc.Add(6013, 6014)).IsEqualTo(12027);

    [Test]
    public async Task Case_6014() => await Assert.That(Calc.Add(6014, 6015)).IsEqualTo(12029);

    [Test]
    public async Task Case_6015() => await Assert.That(Calc.Add(6015, 6016)).IsEqualTo(12031);

    [Test]
    public async Task Case_6016() => await Assert.That(Calc.Add(6016, 6017)).IsEqualTo(12033);

    [Test]
    public async Task Case_6017() => await Assert.That(Calc.Add(6017, 6018)).IsEqualTo(12035);

    [Test]
    public async Task Case_6018() => await Assert.That(Calc.Add(6018, 6019)).IsEqualTo(12037);

    [Test]
    public async Task Case_6019() => await Assert.That(Calc.Add(6019, 6020)).IsEqualTo(12039);

    [Test]
    public async Task Case_6020() => await Assert.That(Calc.Add(6020, 6021)).IsEqualTo(12041);
}
#endif
