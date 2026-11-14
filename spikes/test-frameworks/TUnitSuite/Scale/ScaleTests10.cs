#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests10
{
    [Test]
    public async Task Case_1001() => await Assert.That(Calc.Add(1001, 1002)).IsEqualTo(2003);

    [Test]
    public async Task Case_1002() => await Assert.That(Calc.Add(1002, 1003)).IsEqualTo(2005);

    [Test]
    public async Task Case_1003() => await Assert.That(Calc.Add(1003, 1004)).IsEqualTo(2007);

    [Test]
    public async Task Case_1004() => await Assert.That(Calc.Add(1004, 1005)).IsEqualTo(2009);

    [Test]
    public async Task Case_1005() => await Assert.That(Calc.Add(1005, 1006)).IsEqualTo(2011);

    [Test]
    public async Task Case_1006() => await Assert.That(Calc.Add(1006, 1007)).IsEqualTo(2013);

    [Test]
    public async Task Case_1007() => await Assert.That(Calc.Add(1007, 1008)).IsEqualTo(2015);

    [Test]
    public async Task Case_1008() => await Assert.That(Calc.Add(1008, 1009)).IsEqualTo(2017);

    [Test]
    public async Task Case_1009() => await Assert.That(Calc.Add(1009, 1010)).IsEqualTo(2019);

    [Test]
    public async Task Case_1010() => await Assert.That(Calc.Add(1010, 1011)).IsEqualTo(2021);

    [Test]
    public async Task Case_1011() => await Assert.That(Calc.Add(1011, 1012)).IsEqualTo(2023);

    [Test]
    public async Task Case_1012() => await Assert.That(Calc.Add(1012, 1013)).IsEqualTo(2025);

    [Test]
    public async Task Case_1013() => await Assert.That(Calc.Add(1013, 1014)).IsEqualTo(2027);

    [Test]
    public async Task Case_1014() => await Assert.That(Calc.Add(1014, 1015)).IsEqualTo(2029);

    [Test]
    public async Task Case_1015() => await Assert.That(Calc.Add(1015, 1016)).IsEqualTo(2031);

    [Test]
    public async Task Case_1016() => await Assert.That(Calc.Add(1016, 1017)).IsEqualTo(2033);

    [Test]
    public async Task Case_1017() => await Assert.That(Calc.Add(1017, 1018)).IsEqualTo(2035);

    [Test]
    public async Task Case_1018() => await Assert.That(Calc.Add(1018, 1019)).IsEqualTo(2037);

    [Test]
    public async Task Case_1019() => await Assert.That(Calc.Add(1019, 1020)).IsEqualTo(2039);

    [Test]
    public async Task Case_1020() => await Assert.That(Calc.Add(1020, 1021)).IsEqualTo(2041);
}
#endif
