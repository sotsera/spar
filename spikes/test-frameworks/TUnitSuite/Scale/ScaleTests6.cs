#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests6
{
    [Test]
    public async Task Case_601() => await Assert.That(Calc.Add(601, 602)).IsEqualTo(1203);

    [Test]
    public async Task Case_602() => await Assert.That(Calc.Add(602, 603)).IsEqualTo(1205);

    [Test]
    public async Task Case_603() => await Assert.That(Calc.Add(603, 604)).IsEqualTo(1207);

    [Test]
    public async Task Case_604() => await Assert.That(Calc.Add(604, 605)).IsEqualTo(1209);

    [Test]
    public async Task Case_605() => await Assert.That(Calc.Add(605, 606)).IsEqualTo(1211);

    [Test]
    public async Task Case_606() => await Assert.That(Calc.Add(606, 607)).IsEqualTo(1213);

    [Test]
    public async Task Case_607() => await Assert.That(Calc.Add(607, 608)).IsEqualTo(1215);

    [Test]
    public async Task Case_608() => await Assert.That(Calc.Add(608, 609)).IsEqualTo(1217);

    [Test]
    public async Task Case_609() => await Assert.That(Calc.Add(609, 610)).IsEqualTo(1219);

    [Test]
    public async Task Case_610() => await Assert.That(Calc.Add(610, 611)).IsEqualTo(1221);

    [Test]
    public async Task Case_611() => await Assert.That(Calc.Add(611, 612)).IsEqualTo(1223);

    [Test]
    public async Task Case_612() => await Assert.That(Calc.Add(612, 613)).IsEqualTo(1225);

    [Test]
    public async Task Case_613() => await Assert.That(Calc.Add(613, 614)).IsEqualTo(1227);

    [Test]
    public async Task Case_614() => await Assert.That(Calc.Add(614, 615)).IsEqualTo(1229);

    [Test]
    public async Task Case_615() => await Assert.That(Calc.Add(615, 616)).IsEqualTo(1231);

    [Test]
    public async Task Case_616() => await Assert.That(Calc.Add(616, 617)).IsEqualTo(1233);

    [Test]
    public async Task Case_617() => await Assert.That(Calc.Add(617, 618)).IsEqualTo(1235);

    [Test]
    public async Task Case_618() => await Assert.That(Calc.Add(618, 619)).IsEqualTo(1237);

    [Test]
    public async Task Case_619() => await Assert.That(Calc.Add(619, 620)).IsEqualTo(1239);

    [Test]
    public async Task Case_620() => await Assert.That(Calc.Add(620, 621)).IsEqualTo(1241);
}
#endif
