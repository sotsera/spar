#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests4
{
    [Test]
    public async Task Case_401() => await Assert.That(Calc.Add(401, 402)).IsEqualTo(803);

    [Test]
    public async Task Case_402() => await Assert.That(Calc.Add(402, 403)).IsEqualTo(805);

    [Test]
    public async Task Case_403() => await Assert.That(Calc.Add(403, 404)).IsEqualTo(807);

    [Test]
    public async Task Case_404() => await Assert.That(Calc.Add(404, 405)).IsEqualTo(809);

    [Test]
    public async Task Case_405() => await Assert.That(Calc.Add(405, 406)).IsEqualTo(811);

    [Test]
    public async Task Case_406() => await Assert.That(Calc.Add(406, 407)).IsEqualTo(813);

    [Test]
    public async Task Case_407() => await Assert.That(Calc.Add(407, 408)).IsEqualTo(815);

    [Test]
    public async Task Case_408() => await Assert.That(Calc.Add(408, 409)).IsEqualTo(817);

    [Test]
    public async Task Case_409() => await Assert.That(Calc.Add(409, 410)).IsEqualTo(819);

    [Test]
    public async Task Case_410() => await Assert.That(Calc.Add(410, 411)).IsEqualTo(821);

    [Test]
    public async Task Case_411() => await Assert.That(Calc.Add(411, 412)).IsEqualTo(823);

    [Test]
    public async Task Case_412() => await Assert.That(Calc.Add(412, 413)).IsEqualTo(825);

    [Test]
    public async Task Case_413() => await Assert.That(Calc.Add(413, 414)).IsEqualTo(827);

    [Test]
    public async Task Case_414() => await Assert.That(Calc.Add(414, 415)).IsEqualTo(829);

    [Test]
    public async Task Case_415() => await Assert.That(Calc.Add(415, 416)).IsEqualTo(831);

    [Test]
    public async Task Case_416() => await Assert.That(Calc.Add(416, 417)).IsEqualTo(833);

    [Test]
    public async Task Case_417() => await Assert.That(Calc.Add(417, 418)).IsEqualTo(835);

    [Test]
    public async Task Case_418() => await Assert.That(Calc.Add(418, 419)).IsEqualTo(837);

    [Test]
    public async Task Case_419() => await Assert.That(Calc.Add(419, 420)).IsEqualTo(839);

    [Test]
    public async Task Case_420() => await Assert.That(Calc.Add(420, 421)).IsEqualTo(841);
}
#endif
