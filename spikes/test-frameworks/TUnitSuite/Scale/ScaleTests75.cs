#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests75
{
    [Test]
    public async Task Case_7501() => await Assert.That(Calc.Add(7501, 7502)).IsEqualTo(15003);

    [Test]
    public async Task Case_7502() => await Assert.That(Calc.Add(7502, 7503)).IsEqualTo(15005);

    [Test]
    public async Task Case_7503() => await Assert.That(Calc.Add(7503, 7504)).IsEqualTo(15007);

    [Test]
    public async Task Case_7504() => await Assert.That(Calc.Add(7504, 7505)).IsEqualTo(15009);

    [Test]
    public async Task Case_7505() => await Assert.That(Calc.Add(7505, 7506)).IsEqualTo(15011);

    [Test]
    public async Task Case_7506() => await Assert.That(Calc.Add(7506, 7507)).IsEqualTo(15013);

    [Test]
    public async Task Case_7507() => await Assert.That(Calc.Add(7507, 7508)).IsEqualTo(15015);

    [Test]
    public async Task Case_7508() => await Assert.That(Calc.Add(7508, 7509)).IsEqualTo(15017);

    [Test]
    public async Task Case_7509() => await Assert.That(Calc.Add(7509, 7510)).IsEqualTo(15019);

    [Test]
    public async Task Case_7510() => await Assert.That(Calc.Add(7510, 7511)).IsEqualTo(15021);

    [Test]
    public async Task Case_7511() => await Assert.That(Calc.Add(7511, 7512)).IsEqualTo(15023);

    [Test]
    public async Task Case_7512() => await Assert.That(Calc.Add(7512, 7513)).IsEqualTo(15025);

    [Test]
    public async Task Case_7513() => await Assert.That(Calc.Add(7513, 7514)).IsEqualTo(15027);

    [Test]
    public async Task Case_7514() => await Assert.That(Calc.Add(7514, 7515)).IsEqualTo(15029);

    [Test]
    public async Task Case_7515() => await Assert.That(Calc.Add(7515, 7516)).IsEqualTo(15031);

    [Test]
    public async Task Case_7516() => await Assert.That(Calc.Add(7516, 7517)).IsEqualTo(15033);

    [Test]
    public async Task Case_7517() => await Assert.That(Calc.Add(7517, 7518)).IsEqualTo(15035);

    [Test]
    public async Task Case_7518() => await Assert.That(Calc.Add(7518, 7519)).IsEqualTo(15037);

    [Test]
    public async Task Case_7519() => await Assert.That(Calc.Add(7519, 7520)).IsEqualTo(15039);

    [Test]
    public async Task Case_7520() => await Assert.That(Calc.Add(7520, 7521)).IsEqualTo(15041);
}
#endif
