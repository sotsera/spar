#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests17
{
    [Test]
    public async Task Case_1701() => await Assert.That(Calc.Add(1701, 1702)).IsEqualTo(3403);

    [Test]
    public async Task Case_1702() => await Assert.That(Calc.Add(1702, 1703)).IsEqualTo(3405);

    [Test]
    public async Task Case_1703() => await Assert.That(Calc.Add(1703, 1704)).IsEqualTo(3407);

    [Test]
    public async Task Case_1704() => await Assert.That(Calc.Add(1704, 1705)).IsEqualTo(3409);

    [Test]
    public async Task Case_1705() => await Assert.That(Calc.Add(1705, 1706)).IsEqualTo(3411);

    [Test]
    public async Task Case_1706() => await Assert.That(Calc.Add(1706, 1707)).IsEqualTo(3413);

    [Test]
    public async Task Case_1707() => await Assert.That(Calc.Add(1707, 1708)).IsEqualTo(3415);

    [Test]
    public async Task Case_1708() => await Assert.That(Calc.Add(1708, 1709)).IsEqualTo(3417);

    [Test]
    public async Task Case_1709() => await Assert.That(Calc.Add(1709, 1710)).IsEqualTo(3419);

    [Test]
    public async Task Case_1710() => await Assert.That(Calc.Add(1710, 1711)).IsEqualTo(3421);

    [Test]
    public async Task Case_1711() => await Assert.That(Calc.Add(1711, 1712)).IsEqualTo(3423);

    [Test]
    public async Task Case_1712() => await Assert.That(Calc.Add(1712, 1713)).IsEqualTo(3425);

    [Test]
    public async Task Case_1713() => await Assert.That(Calc.Add(1713, 1714)).IsEqualTo(3427);

    [Test]
    public async Task Case_1714() => await Assert.That(Calc.Add(1714, 1715)).IsEqualTo(3429);

    [Test]
    public async Task Case_1715() => await Assert.That(Calc.Add(1715, 1716)).IsEqualTo(3431);

    [Test]
    public async Task Case_1716() => await Assert.That(Calc.Add(1716, 1717)).IsEqualTo(3433);

    [Test]
    public async Task Case_1717() => await Assert.That(Calc.Add(1717, 1718)).IsEqualTo(3435);

    [Test]
    public async Task Case_1718() => await Assert.That(Calc.Add(1718, 1719)).IsEqualTo(3437);

    [Test]
    public async Task Case_1719() => await Assert.That(Calc.Add(1719, 1720)).IsEqualTo(3439);

    [Test]
    public async Task Case_1720() => await Assert.That(Calc.Add(1720, 1721)).IsEqualTo(3441);
}
#endif
