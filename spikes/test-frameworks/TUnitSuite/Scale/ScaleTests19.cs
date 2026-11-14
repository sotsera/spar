#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests19
{
    [Test]
    public async Task Case_1901() => await Assert.That(Calc.Add(1901, 1902)).IsEqualTo(3803);

    [Test]
    public async Task Case_1902() => await Assert.That(Calc.Add(1902, 1903)).IsEqualTo(3805);

    [Test]
    public async Task Case_1903() => await Assert.That(Calc.Add(1903, 1904)).IsEqualTo(3807);

    [Test]
    public async Task Case_1904() => await Assert.That(Calc.Add(1904, 1905)).IsEqualTo(3809);

    [Test]
    public async Task Case_1905() => await Assert.That(Calc.Add(1905, 1906)).IsEqualTo(3811);

    [Test]
    public async Task Case_1906() => await Assert.That(Calc.Add(1906, 1907)).IsEqualTo(3813);

    [Test]
    public async Task Case_1907() => await Assert.That(Calc.Add(1907, 1908)).IsEqualTo(3815);

    [Test]
    public async Task Case_1908() => await Assert.That(Calc.Add(1908, 1909)).IsEqualTo(3817);

    [Test]
    public async Task Case_1909() => await Assert.That(Calc.Add(1909, 1910)).IsEqualTo(3819);

    [Test]
    public async Task Case_1910() => await Assert.That(Calc.Add(1910, 1911)).IsEqualTo(3821);

    [Test]
    public async Task Case_1911() => await Assert.That(Calc.Add(1911, 1912)).IsEqualTo(3823);

    [Test]
    public async Task Case_1912() => await Assert.That(Calc.Add(1912, 1913)).IsEqualTo(3825);

    [Test]
    public async Task Case_1913() => await Assert.That(Calc.Add(1913, 1914)).IsEqualTo(3827);

    [Test]
    public async Task Case_1914() => await Assert.That(Calc.Add(1914, 1915)).IsEqualTo(3829);

    [Test]
    public async Task Case_1915() => await Assert.That(Calc.Add(1915, 1916)).IsEqualTo(3831);

    [Test]
    public async Task Case_1916() => await Assert.That(Calc.Add(1916, 1917)).IsEqualTo(3833);

    [Test]
    public async Task Case_1917() => await Assert.That(Calc.Add(1917, 1918)).IsEqualTo(3835);

    [Test]
    public async Task Case_1918() => await Assert.That(Calc.Add(1918, 1919)).IsEqualTo(3837);

    [Test]
    public async Task Case_1919() => await Assert.That(Calc.Add(1919, 1920)).IsEqualTo(3839);

    [Test]
    public async Task Case_1920() => await Assert.That(Calc.Add(1920, 1921)).IsEqualTo(3841);
}
#endif
