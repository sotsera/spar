#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests11
{
    [Test]
    public async Task Case_1101() => await Assert.That(Calc.Add(1101, 1102)).IsEqualTo(2203);

    [Test]
    public async Task Case_1102() => await Assert.That(Calc.Add(1102, 1103)).IsEqualTo(2205);

    [Test]
    public async Task Case_1103() => await Assert.That(Calc.Add(1103, 1104)).IsEqualTo(2207);

    [Test]
    public async Task Case_1104() => await Assert.That(Calc.Add(1104, 1105)).IsEqualTo(2209);

    [Test]
    public async Task Case_1105() => await Assert.That(Calc.Add(1105, 1106)).IsEqualTo(2211);

    [Test]
    public async Task Case_1106() => await Assert.That(Calc.Add(1106, 1107)).IsEqualTo(2213);

    [Test]
    public async Task Case_1107() => await Assert.That(Calc.Add(1107, 1108)).IsEqualTo(2215);

    [Test]
    public async Task Case_1108() => await Assert.That(Calc.Add(1108, 1109)).IsEqualTo(2217);

    [Test]
    public async Task Case_1109() => await Assert.That(Calc.Add(1109, 1110)).IsEqualTo(2219);

    [Test]
    public async Task Case_1110() => await Assert.That(Calc.Add(1110, 1111)).IsEqualTo(2221);

    [Test]
    public async Task Case_1111() => await Assert.That(Calc.Add(1111, 1112)).IsEqualTo(2223);

    [Test]
    public async Task Case_1112() => await Assert.That(Calc.Add(1112, 1113)).IsEqualTo(2225);

    [Test]
    public async Task Case_1113() => await Assert.That(Calc.Add(1113, 1114)).IsEqualTo(2227);

    [Test]
    public async Task Case_1114() => await Assert.That(Calc.Add(1114, 1115)).IsEqualTo(2229);

    [Test]
    public async Task Case_1115() => await Assert.That(Calc.Add(1115, 1116)).IsEqualTo(2231);

    [Test]
    public async Task Case_1116() => await Assert.That(Calc.Add(1116, 1117)).IsEqualTo(2233);

    [Test]
    public async Task Case_1117() => await Assert.That(Calc.Add(1117, 1118)).IsEqualTo(2235);

    [Test]
    public async Task Case_1118() => await Assert.That(Calc.Add(1118, 1119)).IsEqualTo(2237);

    [Test]
    public async Task Case_1119() => await Assert.That(Calc.Add(1119, 1120)).IsEqualTo(2239);

    [Test]
    public async Task Case_1120() => await Assert.That(Calc.Add(1120, 1121)).IsEqualTo(2241);
}
#endif
