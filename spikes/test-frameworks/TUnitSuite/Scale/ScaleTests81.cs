#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests81
{
    [Test]
    public async Task Case_8101() => await Assert.That(Calc.Add(8101, 8102)).IsEqualTo(16203);

    [Test]
    public async Task Case_8102() => await Assert.That(Calc.Add(8102, 8103)).IsEqualTo(16205);

    [Test]
    public async Task Case_8103() => await Assert.That(Calc.Add(8103, 8104)).IsEqualTo(16207);

    [Test]
    public async Task Case_8104() => await Assert.That(Calc.Add(8104, 8105)).IsEqualTo(16209);

    [Test]
    public async Task Case_8105() => await Assert.That(Calc.Add(8105, 8106)).IsEqualTo(16211);

    [Test]
    public async Task Case_8106() => await Assert.That(Calc.Add(8106, 8107)).IsEqualTo(16213);

    [Test]
    public async Task Case_8107() => await Assert.That(Calc.Add(8107, 8108)).IsEqualTo(16215);

    [Test]
    public async Task Case_8108() => await Assert.That(Calc.Add(8108, 8109)).IsEqualTo(16217);

    [Test]
    public async Task Case_8109() => await Assert.That(Calc.Add(8109, 8110)).IsEqualTo(16219);

    [Test]
    public async Task Case_8110() => await Assert.That(Calc.Add(8110, 8111)).IsEqualTo(16221);

    [Test]
    public async Task Case_8111() => await Assert.That(Calc.Add(8111, 8112)).IsEqualTo(16223);

    [Test]
    public async Task Case_8112() => await Assert.That(Calc.Add(8112, 8113)).IsEqualTo(16225);

    [Test]
    public async Task Case_8113() => await Assert.That(Calc.Add(8113, 8114)).IsEqualTo(16227);

    [Test]
    public async Task Case_8114() => await Assert.That(Calc.Add(8114, 8115)).IsEqualTo(16229);

    [Test]
    public async Task Case_8115() => await Assert.That(Calc.Add(8115, 8116)).IsEqualTo(16231);

    [Test]
    public async Task Case_8116() => await Assert.That(Calc.Add(8116, 8117)).IsEqualTo(16233);

    [Test]
    public async Task Case_8117() => await Assert.That(Calc.Add(8117, 8118)).IsEqualTo(16235);

    [Test]
    public async Task Case_8118() => await Assert.That(Calc.Add(8118, 8119)).IsEqualTo(16237);

    [Test]
    public async Task Case_8119() => await Assert.That(Calc.Add(8119, 8120)).IsEqualTo(16239);

    [Test]
    public async Task Case_8120() => await Assert.That(Calc.Add(8120, 8121)).IsEqualTo(16241);
}
#endif
