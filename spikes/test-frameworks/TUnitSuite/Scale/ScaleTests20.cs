#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests20
{
    [Test]
    public async Task Case_2001() => await Assert.That(Calc.Add(2001, 2002)).IsEqualTo(4003);

    [Test]
    public async Task Case_2002() => await Assert.That(Calc.Add(2002, 2003)).IsEqualTo(4005);

    [Test]
    public async Task Case_2003() => await Assert.That(Calc.Add(2003, 2004)).IsEqualTo(4007);

    [Test]
    public async Task Case_2004() => await Assert.That(Calc.Add(2004, 2005)).IsEqualTo(4009);

    [Test]
    public async Task Case_2005() => await Assert.That(Calc.Add(2005, 2006)).IsEqualTo(4011);

    [Test]
    public async Task Case_2006() => await Assert.That(Calc.Add(2006, 2007)).IsEqualTo(4013);

    [Test]
    public async Task Case_2007() => await Assert.That(Calc.Add(2007, 2008)).IsEqualTo(4015);

    [Test]
    public async Task Case_2008() => await Assert.That(Calc.Add(2008, 2009)).IsEqualTo(4017);

    [Test]
    public async Task Case_2009() => await Assert.That(Calc.Add(2009, 2010)).IsEqualTo(4019);

    [Test]
    public async Task Case_2010() => await Assert.That(Calc.Add(2010, 2011)).IsEqualTo(4021);

    [Test]
    public async Task Case_2011() => await Assert.That(Calc.Add(2011, 2012)).IsEqualTo(4023);

    [Test]
    public async Task Case_2012() => await Assert.That(Calc.Add(2012, 2013)).IsEqualTo(4025);

    [Test]
    public async Task Case_2013() => await Assert.That(Calc.Add(2013, 2014)).IsEqualTo(4027);

    [Test]
    public async Task Case_2014() => await Assert.That(Calc.Add(2014, 2015)).IsEqualTo(4029);

    [Test]
    public async Task Case_2015() => await Assert.That(Calc.Add(2015, 2016)).IsEqualTo(4031);

    [Test]
    public async Task Case_2016() => await Assert.That(Calc.Add(2016, 2017)).IsEqualTo(4033);

    [Test]
    public async Task Case_2017() => await Assert.That(Calc.Add(2017, 2018)).IsEqualTo(4035);

    [Test]
    public async Task Case_2018() => await Assert.That(Calc.Add(2018, 2019)).IsEqualTo(4037);

    [Test]
    public async Task Case_2019() => await Assert.That(Calc.Add(2019, 2020)).IsEqualTo(4039);

    [Test]
    public async Task Case_2020() => await Assert.That(Calc.Add(2020, 2021)).IsEqualTo(4041);
}
#endif
