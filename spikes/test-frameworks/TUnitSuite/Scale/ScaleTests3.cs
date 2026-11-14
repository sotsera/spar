#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests3
{
    [Test]
    public async Task Case_301() => await Assert.That(Calc.Add(301, 302)).IsEqualTo(603);

    [Test]
    public async Task Case_302() => await Assert.That(Calc.Add(302, 303)).IsEqualTo(605);

    [Test]
    public async Task Case_303() => await Assert.That(Calc.Add(303, 304)).IsEqualTo(607);

    [Test]
    public async Task Case_304() => await Assert.That(Calc.Add(304, 305)).IsEqualTo(609);

    [Test]
    public async Task Case_305() => await Assert.That(Calc.Add(305, 306)).IsEqualTo(611);

    [Test]
    public async Task Case_306() => await Assert.That(Calc.Add(306, 307)).IsEqualTo(613);

    [Test]
    public async Task Case_307() => await Assert.That(Calc.Add(307, 308)).IsEqualTo(615);

    [Test]
    public async Task Case_308() => await Assert.That(Calc.Add(308, 309)).IsEqualTo(617);

    [Test]
    public async Task Case_309() => await Assert.That(Calc.Add(309, 310)).IsEqualTo(619);

    [Test]
    public async Task Case_310() => await Assert.That(Calc.Add(310, 311)).IsEqualTo(621);

    [Test]
    public async Task Case_311() => await Assert.That(Calc.Add(311, 312)).IsEqualTo(623);

    [Test]
    public async Task Case_312() => await Assert.That(Calc.Add(312, 313)).IsEqualTo(625);

    [Test]
    public async Task Case_313() => await Assert.That(Calc.Add(313, 314)).IsEqualTo(627);

    [Test]
    public async Task Case_314() => await Assert.That(Calc.Add(314, 315)).IsEqualTo(629);

    [Test]
    public async Task Case_315() => await Assert.That(Calc.Add(315, 316)).IsEqualTo(631);

    [Test]
    public async Task Case_316() => await Assert.That(Calc.Add(316, 317)).IsEqualTo(633);

    [Test]
    public async Task Case_317() => await Assert.That(Calc.Add(317, 318)).IsEqualTo(635);

    [Test]
    public async Task Case_318() => await Assert.That(Calc.Add(318, 319)).IsEqualTo(637);

    [Test]
    public async Task Case_319() => await Assert.That(Calc.Add(319, 320)).IsEqualTo(639);

    [Test]
    public async Task Case_320() => await Assert.That(Calc.Add(320, 321)).IsEqualTo(641);
}
#endif
