#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests9
{
    [Test]
    public async Task Case_901() => await Assert.That(Calc.Add(901, 902)).IsEqualTo(1803);

    [Test]
    public async Task Case_902() => await Assert.That(Calc.Add(902, 903)).IsEqualTo(1805);

    [Test]
    public async Task Case_903() => await Assert.That(Calc.Add(903, 904)).IsEqualTo(1807);

    [Test]
    public async Task Case_904() => await Assert.That(Calc.Add(904, 905)).IsEqualTo(1809);

    [Test]
    public async Task Case_905() => await Assert.That(Calc.Add(905, 906)).IsEqualTo(1811);

    [Test]
    public async Task Case_906() => await Assert.That(Calc.Add(906, 907)).IsEqualTo(1813);

    [Test]
    public async Task Case_907() => await Assert.That(Calc.Add(907, 908)).IsEqualTo(1815);

    [Test]
    public async Task Case_908() => await Assert.That(Calc.Add(908, 909)).IsEqualTo(1817);

    [Test]
    public async Task Case_909() => await Assert.That(Calc.Add(909, 910)).IsEqualTo(1819);

    [Test]
    public async Task Case_910() => await Assert.That(Calc.Add(910, 911)).IsEqualTo(1821);

    [Test]
    public async Task Case_911() => await Assert.That(Calc.Add(911, 912)).IsEqualTo(1823);

    [Test]
    public async Task Case_912() => await Assert.That(Calc.Add(912, 913)).IsEqualTo(1825);

    [Test]
    public async Task Case_913() => await Assert.That(Calc.Add(913, 914)).IsEqualTo(1827);

    [Test]
    public async Task Case_914() => await Assert.That(Calc.Add(914, 915)).IsEqualTo(1829);

    [Test]
    public async Task Case_915() => await Assert.That(Calc.Add(915, 916)).IsEqualTo(1831);

    [Test]
    public async Task Case_916() => await Assert.That(Calc.Add(916, 917)).IsEqualTo(1833);

    [Test]
    public async Task Case_917() => await Assert.That(Calc.Add(917, 918)).IsEqualTo(1835);

    [Test]
    public async Task Case_918() => await Assert.That(Calc.Add(918, 919)).IsEqualTo(1837);

    [Test]
    public async Task Case_919() => await Assert.That(Calc.Add(919, 920)).IsEqualTo(1839);

    [Test]
    public async Task Case_920() => await Assert.That(Calc.Add(920, 921)).IsEqualTo(1841);
}
#endif
