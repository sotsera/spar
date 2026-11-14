#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests51
{
    [Test]
    public async Task Case_5101() => await Assert.That(Calc.Add(5101, 5102)).IsEqualTo(10203);

    [Test]
    public async Task Case_5102() => await Assert.That(Calc.Add(5102, 5103)).IsEqualTo(10205);

    [Test]
    public async Task Case_5103() => await Assert.That(Calc.Add(5103, 5104)).IsEqualTo(10207);

    [Test]
    public async Task Case_5104() => await Assert.That(Calc.Add(5104, 5105)).IsEqualTo(10209);

    [Test]
    public async Task Case_5105() => await Assert.That(Calc.Add(5105, 5106)).IsEqualTo(10211);

    [Test]
    public async Task Case_5106() => await Assert.That(Calc.Add(5106, 5107)).IsEqualTo(10213);

    [Test]
    public async Task Case_5107() => await Assert.That(Calc.Add(5107, 5108)).IsEqualTo(10215);

    [Test]
    public async Task Case_5108() => await Assert.That(Calc.Add(5108, 5109)).IsEqualTo(10217);

    [Test]
    public async Task Case_5109() => await Assert.That(Calc.Add(5109, 5110)).IsEqualTo(10219);

    [Test]
    public async Task Case_5110() => await Assert.That(Calc.Add(5110, 5111)).IsEqualTo(10221);

    [Test]
    public async Task Case_5111() => await Assert.That(Calc.Add(5111, 5112)).IsEqualTo(10223);

    [Test]
    public async Task Case_5112() => await Assert.That(Calc.Add(5112, 5113)).IsEqualTo(10225);

    [Test]
    public async Task Case_5113() => await Assert.That(Calc.Add(5113, 5114)).IsEqualTo(10227);

    [Test]
    public async Task Case_5114() => await Assert.That(Calc.Add(5114, 5115)).IsEqualTo(10229);

    [Test]
    public async Task Case_5115() => await Assert.That(Calc.Add(5115, 5116)).IsEqualTo(10231);

    [Test]
    public async Task Case_5116() => await Assert.That(Calc.Add(5116, 5117)).IsEqualTo(10233);

    [Test]
    public async Task Case_5117() => await Assert.That(Calc.Add(5117, 5118)).IsEqualTo(10235);

    [Test]
    public async Task Case_5118() => await Assert.That(Calc.Add(5118, 5119)).IsEqualTo(10237);

    [Test]
    public async Task Case_5119() => await Assert.That(Calc.Add(5119, 5120)).IsEqualTo(10239);

    [Test]
    public async Task Case_5120() => await Assert.That(Calc.Add(5120, 5121)).IsEqualTo(10241);
}
#endif
