#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests100
{
    [Test]
    public async Task Case_10001() => await Assert.That(Calc.Add(10001, 10002)).IsEqualTo(20003);

    [Test]
    public async Task Case_10002() => await Assert.That(Calc.Add(10002, 10003)).IsEqualTo(20005);

    [Test]
    public async Task Case_10003() => await Assert.That(Calc.Add(10003, 10004)).IsEqualTo(20007);

    [Test]
    public async Task Case_10004() => await Assert.That(Calc.Add(10004, 10005)).IsEqualTo(20009);

    [Test]
    public async Task Case_10005() => await Assert.That(Calc.Add(10005, 10006)).IsEqualTo(20011);

    [Test]
    public async Task Case_10006() => await Assert.That(Calc.Add(10006, 10007)).IsEqualTo(20013);

    [Test]
    public async Task Case_10007() => await Assert.That(Calc.Add(10007, 10008)).IsEqualTo(20015);

    [Test]
    public async Task Case_10008() => await Assert.That(Calc.Add(10008, 10009)).IsEqualTo(20017);

    [Test]
    public async Task Case_10009() => await Assert.That(Calc.Add(10009, 10010)).IsEqualTo(20019);

    [Test]
    public async Task Case_10010() => await Assert.That(Calc.Add(10010, 10011)).IsEqualTo(20021);

    [Test]
    public async Task Case_10011() => await Assert.That(Calc.Add(10011, 10012)).IsEqualTo(20023);

    [Test]
    public async Task Case_10012() => await Assert.That(Calc.Add(10012, 10013)).IsEqualTo(20025);

    [Test]
    public async Task Case_10013() => await Assert.That(Calc.Add(10013, 10014)).IsEqualTo(20027);

    [Test]
    public async Task Case_10014() => await Assert.That(Calc.Add(10014, 10015)).IsEqualTo(20029);

    [Test]
    public async Task Case_10015() => await Assert.That(Calc.Add(10015, 10016)).IsEqualTo(20031);

    [Test]
    public async Task Case_10016() => await Assert.That(Calc.Add(10016, 10017)).IsEqualTo(20033);

    [Test]
    public async Task Case_10017() => await Assert.That(Calc.Add(10017, 10018)).IsEqualTo(20035);

    [Test]
    public async Task Case_10018() => await Assert.That(Calc.Add(10018, 10019)).IsEqualTo(20037);

    [Test]
    public async Task Case_10019() => await Assert.That(Calc.Add(10019, 10020)).IsEqualTo(20039);

    [Test]
    public async Task Case_10020() => await Assert.That(Calc.Add(10020, 10021)).IsEqualTo(20041);
}
#endif
