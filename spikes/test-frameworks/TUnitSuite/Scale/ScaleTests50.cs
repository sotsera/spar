#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests50
{
    [Test]
    public async Task Case_5001() => await Assert.That(Calc.Add(5001, 5002)).IsEqualTo(10003);

    [Test]
    public async Task Case_5002() => await Assert.That(Calc.Add(5002, 5003)).IsEqualTo(10005);

    [Test]
    public async Task Case_5003() => await Assert.That(Calc.Add(5003, 5004)).IsEqualTo(10007);

    [Test]
    public async Task Case_5004() => await Assert.That(Calc.Add(5004, 5005)).IsEqualTo(10009);

    [Test]
    public async Task Case_5005() => await Assert.That(Calc.Add(5005, 5006)).IsEqualTo(10011);

    [Test]
    public async Task Case_5006() => await Assert.That(Calc.Add(5006, 5007)).IsEqualTo(10013);

    [Test]
    public async Task Case_5007() => await Assert.That(Calc.Add(5007, 5008)).IsEqualTo(10015);

    [Test]
    public async Task Case_5008() => await Assert.That(Calc.Add(5008, 5009)).IsEqualTo(10017);

    [Test]
    public async Task Case_5009() => await Assert.That(Calc.Add(5009, 5010)).IsEqualTo(10019);

    [Test]
    public async Task Case_5010() => await Assert.That(Calc.Add(5010, 5011)).IsEqualTo(10021);

    [Test]
    public async Task Case_5011() => await Assert.That(Calc.Add(5011, 5012)).IsEqualTo(10023);

    [Test]
    public async Task Case_5012() => await Assert.That(Calc.Add(5012, 5013)).IsEqualTo(10025);

    [Test]
    public async Task Case_5013() => await Assert.That(Calc.Add(5013, 5014)).IsEqualTo(10027);

    [Test]
    public async Task Case_5014() => await Assert.That(Calc.Add(5014, 5015)).IsEqualTo(10029);

    [Test]
    public async Task Case_5015() => await Assert.That(Calc.Add(5015, 5016)).IsEqualTo(10031);

    [Test]
    public async Task Case_5016() => await Assert.That(Calc.Add(5016, 5017)).IsEqualTo(10033);

    [Test]
    public async Task Case_5017() => await Assert.That(Calc.Add(5017, 5018)).IsEqualTo(10035);

    [Test]
    public async Task Case_5018() => await Assert.That(Calc.Add(5018, 5019)).IsEqualTo(10037);

    [Test]
    public async Task Case_5019() => await Assert.That(Calc.Add(5019, 5020)).IsEqualTo(10039);

    [Test]
    public async Task Case_5020() => await Assert.That(Calc.Add(5020, 5021)).IsEqualTo(10041);
}
#endif
