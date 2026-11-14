#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests49
{
    [Test]
    public async Task Case_4901() => await Assert.That(Calc.Add(4901, 4902)).IsEqualTo(9803);

    [Test]
    public async Task Case_4902() => await Assert.That(Calc.Add(4902, 4903)).IsEqualTo(9805);

    [Test]
    public async Task Case_4903() => await Assert.That(Calc.Add(4903, 4904)).IsEqualTo(9807);

    [Test]
    public async Task Case_4904() => await Assert.That(Calc.Add(4904, 4905)).IsEqualTo(9809);

    [Test]
    public async Task Case_4905() => await Assert.That(Calc.Add(4905, 4906)).IsEqualTo(9811);

    [Test]
    public async Task Case_4906() => await Assert.That(Calc.Add(4906, 4907)).IsEqualTo(9813);

    [Test]
    public async Task Case_4907() => await Assert.That(Calc.Add(4907, 4908)).IsEqualTo(9815);

    [Test]
    public async Task Case_4908() => await Assert.That(Calc.Add(4908, 4909)).IsEqualTo(9817);

    [Test]
    public async Task Case_4909() => await Assert.That(Calc.Add(4909, 4910)).IsEqualTo(9819);

    [Test]
    public async Task Case_4910() => await Assert.That(Calc.Add(4910, 4911)).IsEqualTo(9821);

    [Test]
    public async Task Case_4911() => await Assert.That(Calc.Add(4911, 4912)).IsEqualTo(9823);

    [Test]
    public async Task Case_4912() => await Assert.That(Calc.Add(4912, 4913)).IsEqualTo(9825);

    [Test]
    public async Task Case_4913() => await Assert.That(Calc.Add(4913, 4914)).IsEqualTo(9827);

    [Test]
    public async Task Case_4914() => await Assert.That(Calc.Add(4914, 4915)).IsEqualTo(9829);

    [Test]
    public async Task Case_4915() => await Assert.That(Calc.Add(4915, 4916)).IsEqualTo(9831);

    [Test]
    public async Task Case_4916() => await Assert.That(Calc.Add(4916, 4917)).IsEqualTo(9833);

    [Test]
    public async Task Case_4917() => await Assert.That(Calc.Add(4917, 4918)).IsEqualTo(9835);

    [Test]
    public async Task Case_4918() => await Assert.That(Calc.Add(4918, 4919)).IsEqualTo(9837);

    [Test]
    public async Task Case_4919() => await Assert.That(Calc.Add(4919, 4920)).IsEqualTo(9839);

    [Test]
    public async Task Case_4920() => await Assert.That(Calc.Add(4920, 4921)).IsEqualTo(9841);
}
#endif
