#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests7
{
    [Test]
    public async Task Case_701() => await Assert.That(Calc.Add(701, 702)).IsEqualTo(1403);

    [Test]
    public async Task Case_702() => await Assert.That(Calc.Add(702, 703)).IsEqualTo(1405);

    [Test]
    public async Task Case_703() => await Assert.That(Calc.Add(703, 704)).IsEqualTo(1407);

    [Test]
    public async Task Case_704() => await Assert.That(Calc.Add(704, 705)).IsEqualTo(1409);

    [Test]
    public async Task Case_705() => await Assert.That(Calc.Add(705, 706)).IsEqualTo(1411);

    [Test]
    public async Task Case_706() => await Assert.That(Calc.Add(706, 707)).IsEqualTo(1413);

    [Test]
    public async Task Case_707() => await Assert.That(Calc.Add(707, 708)).IsEqualTo(1415);

    [Test]
    public async Task Case_708() => await Assert.That(Calc.Add(708, 709)).IsEqualTo(1417);

    [Test]
    public async Task Case_709() => await Assert.That(Calc.Add(709, 710)).IsEqualTo(1419);

    [Test]
    public async Task Case_710() => await Assert.That(Calc.Add(710, 711)).IsEqualTo(1421);

    [Test]
    public async Task Case_711() => await Assert.That(Calc.Add(711, 712)).IsEqualTo(1423);

    [Test]
    public async Task Case_712() => await Assert.That(Calc.Add(712, 713)).IsEqualTo(1425);

    [Test]
    public async Task Case_713() => await Assert.That(Calc.Add(713, 714)).IsEqualTo(1427);

    [Test]
    public async Task Case_714() => await Assert.That(Calc.Add(714, 715)).IsEqualTo(1429);

    [Test]
    public async Task Case_715() => await Assert.That(Calc.Add(715, 716)).IsEqualTo(1431);

    [Test]
    public async Task Case_716() => await Assert.That(Calc.Add(716, 717)).IsEqualTo(1433);

    [Test]
    public async Task Case_717() => await Assert.That(Calc.Add(717, 718)).IsEqualTo(1435);

    [Test]
    public async Task Case_718() => await Assert.That(Calc.Add(718, 719)).IsEqualTo(1437);

    [Test]
    public async Task Case_719() => await Assert.That(Calc.Add(719, 720)).IsEqualTo(1439);

    [Test]
    public async Task Case_720() => await Assert.That(Calc.Add(720, 721)).IsEqualTo(1441);
}
#endif
