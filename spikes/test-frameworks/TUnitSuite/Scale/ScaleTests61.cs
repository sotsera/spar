#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests61
{
    [Test]
    public async Task Case_6101() => await Assert.That(Calc.Add(6101, 6102)).IsEqualTo(12203);

    [Test]
    public async Task Case_6102() => await Assert.That(Calc.Add(6102, 6103)).IsEqualTo(12205);

    [Test]
    public async Task Case_6103() => await Assert.That(Calc.Add(6103, 6104)).IsEqualTo(12207);

    [Test]
    public async Task Case_6104() => await Assert.That(Calc.Add(6104, 6105)).IsEqualTo(12209);

    [Test]
    public async Task Case_6105() => await Assert.That(Calc.Add(6105, 6106)).IsEqualTo(12211);

    [Test]
    public async Task Case_6106() => await Assert.That(Calc.Add(6106, 6107)).IsEqualTo(12213);

    [Test]
    public async Task Case_6107() => await Assert.That(Calc.Add(6107, 6108)).IsEqualTo(12215);

    [Test]
    public async Task Case_6108() => await Assert.That(Calc.Add(6108, 6109)).IsEqualTo(12217);

    [Test]
    public async Task Case_6109() => await Assert.That(Calc.Add(6109, 6110)).IsEqualTo(12219);

    [Test]
    public async Task Case_6110() => await Assert.That(Calc.Add(6110, 6111)).IsEqualTo(12221);

    [Test]
    public async Task Case_6111() => await Assert.That(Calc.Add(6111, 6112)).IsEqualTo(12223);

    [Test]
    public async Task Case_6112() => await Assert.That(Calc.Add(6112, 6113)).IsEqualTo(12225);

    [Test]
    public async Task Case_6113() => await Assert.That(Calc.Add(6113, 6114)).IsEqualTo(12227);

    [Test]
    public async Task Case_6114() => await Assert.That(Calc.Add(6114, 6115)).IsEqualTo(12229);

    [Test]
    public async Task Case_6115() => await Assert.That(Calc.Add(6115, 6116)).IsEqualTo(12231);

    [Test]
    public async Task Case_6116() => await Assert.That(Calc.Add(6116, 6117)).IsEqualTo(12233);

    [Test]
    public async Task Case_6117() => await Assert.That(Calc.Add(6117, 6118)).IsEqualTo(12235);

    [Test]
    public async Task Case_6118() => await Assert.That(Calc.Add(6118, 6119)).IsEqualTo(12237);

    [Test]
    public async Task Case_6119() => await Assert.That(Calc.Add(6119, 6120)).IsEqualTo(12239);

    [Test]
    public async Task Case_6120() => await Assert.That(Calc.Add(6120, 6121)).IsEqualTo(12241);
}
#endif
