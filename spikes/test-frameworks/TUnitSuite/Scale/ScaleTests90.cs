#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests90
{
    [Test]
    public async Task Case_9001() => await Assert.That(Calc.Add(9001, 9002)).IsEqualTo(18003);

    [Test]
    public async Task Case_9002() => await Assert.That(Calc.Add(9002, 9003)).IsEqualTo(18005);

    [Test]
    public async Task Case_9003() => await Assert.That(Calc.Add(9003, 9004)).IsEqualTo(18007);

    [Test]
    public async Task Case_9004() => await Assert.That(Calc.Add(9004, 9005)).IsEqualTo(18009);

    [Test]
    public async Task Case_9005() => await Assert.That(Calc.Add(9005, 9006)).IsEqualTo(18011);

    [Test]
    public async Task Case_9006() => await Assert.That(Calc.Add(9006, 9007)).IsEqualTo(18013);

    [Test]
    public async Task Case_9007() => await Assert.That(Calc.Add(9007, 9008)).IsEqualTo(18015);

    [Test]
    public async Task Case_9008() => await Assert.That(Calc.Add(9008, 9009)).IsEqualTo(18017);

    [Test]
    public async Task Case_9009() => await Assert.That(Calc.Add(9009, 9010)).IsEqualTo(18019);

    [Test]
    public async Task Case_9010() => await Assert.That(Calc.Add(9010, 9011)).IsEqualTo(18021);

    [Test]
    public async Task Case_9011() => await Assert.That(Calc.Add(9011, 9012)).IsEqualTo(18023);

    [Test]
    public async Task Case_9012() => await Assert.That(Calc.Add(9012, 9013)).IsEqualTo(18025);

    [Test]
    public async Task Case_9013() => await Assert.That(Calc.Add(9013, 9014)).IsEqualTo(18027);

    [Test]
    public async Task Case_9014() => await Assert.That(Calc.Add(9014, 9015)).IsEqualTo(18029);

    [Test]
    public async Task Case_9015() => await Assert.That(Calc.Add(9015, 9016)).IsEqualTo(18031);

    [Test]
    public async Task Case_9016() => await Assert.That(Calc.Add(9016, 9017)).IsEqualTo(18033);

    [Test]
    public async Task Case_9017() => await Assert.That(Calc.Add(9017, 9018)).IsEqualTo(18035);

    [Test]
    public async Task Case_9018() => await Assert.That(Calc.Add(9018, 9019)).IsEqualTo(18037);

    [Test]
    public async Task Case_9019() => await Assert.That(Calc.Add(9019, 9020)).IsEqualTo(18039);

    [Test]
    public async Task Case_9020() => await Assert.That(Calc.Add(9020, 9021)).IsEqualTo(18041);
}
#endif
