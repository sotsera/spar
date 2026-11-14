#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests21
{
    [Test]
    public async Task Case_2101() => await Assert.That(Calc.Add(2101, 2102)).IsEqualTo(4203);

    [Test]
    public async Task Case_2102() => await Assert.That(Calc.Add(2102, 2103)).IsEqualTo(4205);

    [Test]
    public async Task Case_2103() => await Assert.That(Calc.Add(2103, 2104)).IsEqualTo(4207);

    [Test]
    public async Task Case_2104() => await Assert.That(Calc.Add(2104, 2105)).IsEqualTo(4209);

    [Test]
    public async Task Case_2105() => await Assert.That(Calc.Add(2105, 2106)).IsEqualTo(4211);

    [Test]
    public async Task Case_2106() => await Assert.That(Calc.Add(2106, 2107)).IsEqualTo(4213);

    [Test]
    public async Task Case_2107() => await Assert.That(Calc.Add(2107, 2108)).IsEqualTo(4215);

    [Test]
    public async Task Case_2108() => await Assert.That(Calc.Add(2108, 2109)).IsEqualTo(4217);

    [Test]
    public async Task Case_2109() => await Assert.That(Calc.Add(2109, 2110)).IsEqualTo(4219);

    [Test]
    public async Task Case_2110() => await Assert.That(Calc.Add(2110, 2111)).IsEqualTo(4221);

    [Test]
    public async Task Case_2111() => await Assert.That(Calc.Add(2111, 2112)).IsEqualTo(4223);

    [Test]
    public async Task Case_2112() => await Assert.That(Calc.Add(2112, 2113)).IsEqualTo(4225);

    [Test]
    public async Task Case_2113() => await Assert.That(Calc.Add(2113, 2114)).IsEqualTo(4227);

    [Test]
    public async Task Case_2114() => await Assert.That(Calc.Add(2114, 2115)).IsEqualTo(4229);

    [Test]
    public async Task Case_2115() => await Assert.That(Calc.Add(2115, 2116)).IsEqualTo(4231);

    [Test]
    public async Task Case_2116() => await Assert.That(Calc.Add(2116, 2117)).IsEqualTo(4233);

    [Test]
    public async Task Case_2117() => await Assert.That(Calc.Add(2117, 2118)).IsEqualTo(4235);

    [Test]
    public async Task Case_2118() => await Assert.That(Calc.Add(2118, 2119)).IsEqualTo(4237);

    [Test]
    public async Task Case_2119() => await Assert.That(Calc.Add(2119, 2120)).IsEqualTo(4239);

    [Test]
    public async Task Case_2120() => await Assert.That(Calc.Add(2120, 2121)).IsEqualTo(4241);
}
#endif
