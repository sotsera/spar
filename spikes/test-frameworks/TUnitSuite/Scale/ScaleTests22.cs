#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests22
{
    [Test]
    public async Task Case_2201() => await Assert.That(Calc.Add(2201, 2202)).IsEqualTo(4403);

    [Test]
    public async Task Case_2202() => await Assert.That(Calc.Add(2202, 2203)).IsEqualTo(4405);

    [Test]
    public async Task Case_2203() => await Assert.That(Calc.Add(2203, 2204)).IsEqualTo(4407);

    [Test]
    public async Task Case_2204() => await Assert.That(Calc.Add(2204, 2205)).IsEqualTo(4409);

    [Test]
    public async Task Case_2205() => await Assert.That(Calc.Add(2205, 2206)).IsEqualTo(4411);

    [Test]
    public async Task Case_2206() => await Assert.That(Calc.Add(2206, 2207)).IsEqualTo(4413);

    [Test]
    public async Task Case_2207() => await Assert.That(Calc.Add(2207, 2208)).IsEqualTo(4415);

    [Test]
    public async Task Case_2208() => await Assert.That(Calc.Add(2208, 2209)).IsEqualTo(4417);

    [Test]
    public async Task Case_2209() => await Assert.That(Calc.Add(2209, 2210)).IsEqualTo(4419);

    [Test]
    public async Task Case_2210() => await Assert.That(Calc.Add(2210, 2211)).IsEqualTo(4421);

    [Test]
    public async Task Case_2211() => await Assert.That(Calc.Add(2211, 2212)).IsEqualTo(4423);

    [Test]
    public async Task Case_2212() => await Assert.That(Calc.Add(2212, 2213)).IsEqualTo(4425);

    [Test]
    public async Task Case_2213() => await Assert.That(Calc.Add(2213, 2214)).IsEqualTo(4427);

    [Test]
    public async Task Case_2214() => await Assert.That(Calc.Add(2214, 2215)).IsEqualTo(4429);

    [Test]
    public async Task Case_2215() => await Assert.That(Calc.Add(2215, 2216)).IsEqualTo(4431);

    [Test]
    public async Task Case_2216() => await Assert.That(Calc.Add(2216, 2217)).IsEqualTo(4433);

    [Test]
    public async Task Case_2217() => await Assert.That(Calc.Add(2217, 2218)).IsEqualTo(4435);

    [Test]
    public async Task Case_2218() => await Assert.That(Calc.Add(2218, 2219)).IsEqualTo(4437);

    [Test]
    public async Task Case_2219() => await Assert.That(Calc.Add(2219, 2220)).IsEqualTo(4439);

    [Test]
    public async Task Case_2220() => await Assert.That(Calc.Add(2220, 2221)).IsEqualTo(4441);
}
#endif
