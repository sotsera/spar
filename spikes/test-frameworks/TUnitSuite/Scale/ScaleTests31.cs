#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests31
{
    [Test]
    public async Task Case_3101() => await Assert.That(Calc.Add(3101, 3102)).IsEqualTo(6203);

    [Test]
    public async Task Case_3102() => await Assert.That(Calc.Add(3102, 3103)).IsEqualTo(6205);

    [Test]
    public async Task Case_3103() => await Assert.That(Calc.Add(3103, 3104)).IsEqualTo(6207);

    [Test]
    public async Task Case_3104() => await Assert.That(Calc.Add(3104, 3105)).IsEqualTo(6209);

    [Test]
    public async Task Case_3105() => await Assert.That(Calc.Add(3105, 3106)).IsEqualTo(6211);

    [Test]
    public async Task Case_3106() => await Assert.That(Calc.Add(3106, 3107)).IsEqualTo(6213);

    [Test]
    public async Task Case_3107() => await Assert.That(Calc.Add(3107, 3108)).IsEqualTo(6215);

    [Test]
    public async Task Case_3108() => await Assert.That(Calc.Add(3108, 3109)).IsEqualTo(6217);

    [Test]
    public async Task Case_3109() => await Assert.That(Calc.Add(3109, 3110)).IsEqualTo(6219);

    [Test]
    public async Task Case_3110() => await Assert.That(Calc.Add(3110, 3111)).IsEqualTo(6221);

    [Test]
    public async Task Case_3111() => await Assert.That(Calc.Add(3111, 3112)).IsEqualTo(6223);

    [Test]
    public async Task Case_3112() => await Assert.That(Calc.Add(3112, 3113)).IsEqualTo(6225);

    [Test]
    public async Task Case_3113() => await Assert.That(Calc.Add(3113, 3114)).IsEqualTo(6227);

    [Test]
    public async Task Case_3114() => await Assert.That(Calc.Add(3114, 3115)).IsEqualTo(6229);

    [Test]
    public async Task Case_3115() => await Assert.That(Calc.Add(3115, 3116)).IsEqualTo(6231);

    [Test]
    public async Task Case_3116() => await Assert.That(Calc.Add(3116, 3117)).IsEqualTo(6233);

    [Test]
    public async Task Case_3117() => await Assert.That(Calc.Add(3117, 3118)).IsEqualTo(6235);

    [Test]
    public async Task Case_3118() => await Assert.That(Calc.Add(3118, 3119)).IsEqualTo(6237);

    [Test]
    public async Task Case_3119() => await Assert.That(Calc.Add(3119, 3120)).IsEqualTo(6239);

    [Test]
    public async Task Case_3120() => await Assert.That(Calc.Add(3120, 3121)).IsEqualTo(6241);
}
#endif
