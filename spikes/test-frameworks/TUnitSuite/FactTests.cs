using Spike.Subject;

namespace Spike.TUnitSuite;

public sealed class FactTests1
{
    [Test]
    public async Task Case_101() => await Assert.That(Calc.Add(101, 102)).IsEqualTo(203);

    [Test]
    public async Task Case_102() => await Assert.That(Calc.Add(102, 103)).IsEqualTo(205);

    [Test]
    public async Task Case_103() => await Assert.That(Calc.Add(103, 104)).IsEqualTo(207);

    [Test]
    public async Task Case_104() => await Assert.That(Calc.Add(104, 105)).IsEqualTo(209);

    [Test]
    public async Task Case_105() => await Assert.That(Calc.Add(105, 106)).IsEqualTo(211);

    [Test]
    public async Task Case_106() => await Assert.That(Calc.Add(106, 107)).IsEqualTo(213);

    [Test]
    public async Task Case_107() => await Assert.That(Calc.Add(107, 108)).IsEqualTo(215);

    [Test]
    public async Task Case_108() => await Assert.That(Calc.Add(108, 109)).IsEqualTo(217);

    [Test]
    public async Task Case_109() => await Assert.That(Calc.Add(109, 110)).IsEqualTo(219);

    [Test]
    public async Task Case_110() => await Assert.That(Calc.Add(110, 111)).IsEqualTo(221);
}

public sealed class FactTests2
{
    [Test]
    public async Task Case_201() => await Assert.That(Calc.Add(201, 202)).IsEqualTo(403);

    [Test]
    public async Task Case_202() => await Assert.That(Calc.Add(202, 203)).IsEqualTo(405);

    [Test]
    public async Task Case_203() => await Assert.That(Calc.Add(203, 204)).IsEqualTo(407);

    [Test]
    public async Task Case_204() => await Assert.That(Calc.Add(204, 205)).IsEqualTo(409);

    [Test]
    public async Task Case_205() => await Assert.That(Calc.Add(205, 206)).IsEqualTo(411);

    [Test]
    public async Task Case_206() => await Assert.That(Calc.Add(206, 207)).IsEqualTo(413);

    [Test]
    public async Task Case_207() => await Assert.That(Calc.Add(207, 208)).IsEqualTo(415);

    [Test]
    public async Task Case_208() => await Assert.That(Calc.Add(208, 209)).IsEqualTo(417);

    [Test]
    public async Task Case_209() => await Assert.That(Calc.Add(209, 210)).IsEqualTo(419);

    [Test]
    public async Task Case_210() => await Assert.That(Calc.Add(210, 211)).IsEqualTo(421);
}

public sealed class FactTests3
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
}

public sealed class FactTests4
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
}
