#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests41
{
    [Test]
    public async Task Case_4101() => await Assert.That(Calc.Add(4101, 4102)).IsEqualTo(8203);

    [Test]
    public async Task Case_4102() => await Assert.That(Calc.Add(4102, 4103)).IsEqualTo(8205);

    [Test]
    public async Task Case_4103() => await Assert.That(Calc.Add(4103, 4104)).IsEqualTo(8207);

    [Test]
    public async Task Case_4104() => await Assert.That(Calc.Add(4104, 4105)).IsEqualTo(8209);

    [Test]
    public async Task Case_4105() => await Assert.That(Calc.Add(4105, 4106)).IsEqualTo(8211);

    [Test]
    public async Task Case_4106() => await Assert.That(Calc.Add(4106, 4107)).IsEqualTo(8213);

    [Test]
    public async Task Case_4107() => await Assert.That(Calc.Add(4107, 4108)).IsEqualTo(8215);

    [Test]
    public async Task Case_4108() => await Assert.That(Calc.Add(4108, 4109)).IsEqualTo(8217);

    [Test]
    public async Task Case_4109() => await Assert.That(Calc.Add(4109, 4110)).IsEqualTo(8219);

    [Test]
    public async Task Case_4110() => await Assert.That(Calc.Add(4110, 4111)).IsEqualTo(8221);

    [Test]
    public async Task Case_4111() => await Assert.That(Calc.Add(4111, 4112)).IsEqualTo(8223);

    [Test]
    public async Task Case_4112() => await Assert.That(Calc.Add(4112, 4113)).IsEqualTo(8225);

    [Test]
    public async Task Case_4113() => await Assert.That(Calc.Add(4113, 4114)).IsEqualTo(8227);

    [Test]
    public async Task Case_4114() => await Assert.That(Calc.Add(4114, 4115)).IsEqualTo(8229);

    [Test]
    public async Task Case_4115() => await Assert.That(Calc.Add(4115, 4116)).IsEqualTo(8231);

    [Test]
    public async Task Case_4116() => await Assert.That(Calc.Add(4116, 4117)).IsEqualTo(8233);

    [Test]
    public async Task Case_4117() => await Assert.That(Calc.Add(4117, 4118)).IsEqualTo(8235);

    [Test]
    public async Task Case_4118() => await Assert.That(Calc.Add(4118, 4119)).IsEqualTo(8237);

    [Test]
    public async Task Case_4119() => await Assert.That(Calc.Add(4119, 4120)).IsEqualTo(8239);

    [Test]
    public async Task Case_4120() => await Assert.That(Calc.Add(4120, 4121)).IsEqualTo(8241);
}
#endif
