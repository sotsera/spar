#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests30
{
    [Test]
    public async Task Case_3001() => await Assert.That(Calc.Add(3001, 3002)).IsEqualTo(6003);

    [Test]
    public async Task Case_3002() => await Assert.That(Calc.Add(3002, 3003)).IsEqualTo(6005);

    [Test]
    public async Task Case_3003() => await Assert.That(Calc.Add(3003, 3004)).IsEqualTo(6007);

    [Test]
    public async Task Case_3004() => await Assert.That(Calc.Add(3004, 3005)).IsEqualTo(6009);

    [Test]
    public async Task Case_3005() => await Assert.That(Calc.Add(3005, 3006)).IsEqualTo(6011);

    [Test]
    public async Task Case_3006() => await Assert.That(Calc.Add(3006, 3007)).IsEqualTo(6013);

    [Test]
    public async Task Case_3007() => await Assert.That(Calc.Add(3007, 3008)).IsEqualTo(6015);

    [Test]
    public async Task Case_3008() => await Assert.That(Calc.Add(3008, 3009)).IsEqualTo(6017);

    [Test]
    public async Task Case_3009() => await Assert.That(Calc.Add(3009, 3010)).IsEqualTo(6019);

    [Test]
    public async Task Case_3010() => await Assert.That(Calc.Add(3010, 3011)).IsEqualTo(6021);

    [Test]
    public async Task Case_3011() => await Assert.That(Calc.Add(3011, 3012)).IsEqualTo(6023);

    [Test]
    public async Task Case_3012() => await Assert.That(Calc.Add(3012, 3013)).IsEqualTo(6025);

    [Test]
    public async Task Case_3013() => await Assert.That(Calc.Add(3013, 3014)).IsEqualTo(6027);

    [Test]
    public async Task Case_3014() => await Assert.That(Calc.Add(3014, 3015)).IsEqualTo(6029);

    [Test]
    public async Task Case_3015() => await Assert.That(Calc.Add(3015, 3016)).IsEqualTo(6031);

    [Test]
    public async Task Case_3016() => await Assert.That(Calc.Add(3016, 3017)).IsEqualTo(6033);

    [Test]
    public async Task Case_3017() => await Assert.That(Calc.Add(3017, 3018)).IsEqualTo(6035);

    [Test]
    public async Task Case_3018() => await Assert.That(Calc.Add(3018, 3019)).IsEqualTo(6037);

    [Test]
    public async Task Case_3019() => await Assert.That(Calc.Add(3019, 3020)).IsEqualTo(6039);

    [Test]
    public async Task Case_3020() => await Assert.That(Calc.Add(3020, 3021)).IsEqualTo(6041);
}
#endif
