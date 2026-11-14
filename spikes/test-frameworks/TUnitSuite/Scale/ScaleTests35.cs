#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests35
{
    [Test]
    public async Task Case_3501() => await Assert.That(Calc.Add(3501, 3502)).IsEqualTo(7003);

    [Test]
    public async Task Case_3502() => await Assert.That(Calc.Add(3502, 3503)).IsEqualTo(7005);

    [Test]
    public async Task Case_3503() => await Assert.That(Calc.Add(3503, 3504)).IsEqualTo(7007);

    [Test]
    public async Task Case_3504() => await Assert.That(Calc.Add(3504, 3505)).IsEqualTo(7009);

    [Test]
    public async Task Case_3505() => await Assert.That(Calc.Add(3505, 3506)).IsEqualTo(7011);

    [Test]
    public async Task Case_3506() => await Assert.That(Calc.Add(3506, 3507)).IsEqualTo(7013);

    [Test]
    public async Task Case_3507() => await Assert.That(Calc.Add(3507, 3508)).IsEqualTo(7015);

    [Test]
    public async Task Case_3508() => await Assert.That(Calc.Add(3508, 3509)).IsEqualTo(7017);

    [Test]
    public async Task Case_3509() => await Assert.That(Calc.Add(3509, 3510)).IsEqualTo(7019);

    [Test]
    public async Task Case_3510() => await Assert.That(Calc.Add(3510, 3511)).IsEqualTo(7021);

    [Test]
    public async Task Case_3511() => await Assert.That(Calc.Add(3511, 3512)).IsEqualTo(7023);

    [Test]
    public async Task Case_3512() => await Assert.That(Calc.Add(3512, 3513)).IsEqualTo(7025);

    [Test]
    public async Task Case_3513() => await Assert.That(Calc.Add(3513, 3514)).IsEqualTo(7027);

    [Test]
    public async Task Case_3514() => await Assert.That(Calc.Add(3514, 3515)).IsEqualTo(7029);

    [Test]
    public async Task Case_3515() => await Assert.That(Calc.Add(3515, 3516)).IsEqualTo(7031);

    [Test]
    public async Task Case_3516() => await Assert.That(Calc.Add(3516, 3517)).IsEqualTo(7033);

    [Test]
    public async Task Case_3517() => await Assert.That(Calc.Add(3517, 3518)).IsEqualTo(7035);

    [Test]
    public async Task Case_3518() => await Assert.That(Calc.Add(3518, 3519)).IsEqualTo(7037);

    [Test]
    public async Task Case_3519() => await Assert.That(Calc.Add(3519, 3520)).IsEqualTo(7039);

    [Test]
    public async Task Case_3520() => await Assert.That(Calc.Add(3520, 3521)).IsEqualTo(7041);
}
#endif
