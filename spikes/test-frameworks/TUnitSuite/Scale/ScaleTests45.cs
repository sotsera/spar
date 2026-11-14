#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests45
{
    [Test]
    public async Task Case_4501() => await Assert.That(Calc.Add(4501, 4502)).IsEqualTo(9003);

    [Test]
    public async Task Case_4502() => await Assert.That(Calc.Add(4502, 4503)).IsEqualTo(9005);

    [Test]
    public async Task Case_4503() => await Assert.That(Calc.Add(4503, 4504)).IsEqualTo(9007);

    [Test]
    public async Task Case_4504() => await Assert.That(Calc.Add(4504, 4505)).IsEqualTo(9009);

    [Test]
    public async Task Case_4505() => await Assert.That(Calc.Add(4505, 4506)).IsEqualTo(9011);

    [Test]
    public async Task Case_4506() => await Assert.That(Calc.Add(4506, 4507)).IsEqualTo(9013);

    [Test]
    public async Task Case_4507() => await Assert.That(Calc.Add(4507, 4508)).IsEqualTo(9015);

    [Test]
    public async Task Case_4508() => await Assert.That(Calc.Add(4508, 4509)).IsEqualTo(9017);

    [Test]
    public async Task Case_4509() => await Assert.That(Calc.Add(4509, 4510)).IsEqualTo(9019);

    [Test]
    public async Task Case_4510() => await Assert.That(Calc.Add(4510, 4511)).IsEqualTo(9021);

    [Test]
    public async Task Case_4511() => await Assert.That(Calc.Add(4511, 4512)).IsEqualTo(9023);

    [Test]
    public async Task Case_4512() => await Assert.That(Calc.Add(4512, 4513)).IsEqualTo(9025);

    [Test]
    public async Task Case_4513() => await Assert.That(Calc.Add(4513, 4514)).IsEqualTo(9027);

    [Test]
    public async Task Case_4514() => await Assert.That(Calc.Add(4514, 4515)).IsEqualTo(9029);

    [Test]
    public async Task Case_4515() => await Assert.That(Calc.Add(4515, 4516)).IsEqualTo(9031);

    [Test]
    public async Task Case_4516() => await Assert.That(Calc.Add(4516, 4517)).IsEqualTo(9033);

    [Test]
    public async Task Case_4517() => await Assert.That(Calc.Add(4517, 4518)).IsEqualTo(9035);

    [Test]
    public async Task Case_4518() => await Assert.That(Calc.Add(4518, 4519)).IsEqualTo(9037);

    [Test]
    public async Task Case_4519() => await Assert.That(Calc.Add(4519, 4520)).IsEqualTo(9039);

    [Test]
    public async Task Case_4520() => await Assert.That(Calc.Add(4520, 4521)).IsEqualTo(9041);
}
#endif
