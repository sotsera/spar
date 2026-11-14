#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.TUnitSuite.Scale;

public sealed class ScaleTests38
{
    [Test]
    public async Task Case_3801() => await Assert.That(Calc.Add(3801, 3802)).IsEqualTo(7603);

    [Test]
    public async Task Case_3802() => await Assert.That(Calc.Add(3802, 3803)).IsEqualTo(7605);

    [Test]
    public async Task Case_3803() => await Assert.That(Calc.Add(3803, 3804)).IsEqualTo(7607);

    [Test]
    public async Task Case_3804() => await Assert.That(Calc.Add(3804, 3805)).IsEqualTo(7609);

    [Test]
    public async Task Case_3805() => await Assert.That(Calc.Add(3805, 3806)).IsEqualTo(7611);

    [Test]
    public async Task Case_3806() => await Assert.That(Calc.Add(3806, 3807)).IsEqualTo(7613);

    [Test]
    public async Task Case_3807() => await Assert.That(Calc.Add(3807, 3808)).IsEqualTo(7615);

    [Test]
    public async Task Case_3808() => await Assert.That(Calc.Add(3808, 3809)).IsEqualTo(7617);

    [Test]
    public async Task Case_3809() => await Assert.That(Calc.Add(3809, 3810)).IsEqualTo(7619);

    [Test]
    public async Task Case_3810() => await Assert.That(Calc.Add(3810, 3811)).IsEqualTo(7621);

    [Test]
    public async Task Case_3811() => await Assert.That(Calc.Add(3811, 3812)).IsEqualTo(7623);

    [Test]
    public async Task Case_3812() => await Assert.That(Calc.Add(3812, 3813)).IsEqualTo(7625);

    [Test]
    public async Task Case_3813() => await Assert.That(Calc.Add(3813, 3814)).IsEqualTo(7627);

    [Test]
    public async Task Case_3814() => await Assert.That(Calc.Add(3814, 3815)).IsEqualTo(7629);

    [Test]
    public async Task Case_3815() => await Assert.That(Calc.Add(3815, 3816)).IsEqualTo(7631);

    [Test]
    public async Task Case_3816() => await Assert.That(Calc.Add(3816, 3817)).IsEqualTo(7633);

    [Test]
    public async Task Case_3817() => await Assert.That(Calc.Add(3817, 3818)).IsEqualTo(7635);

    [Test]
    public async Task Case_3818() => await Assert.That(Calc.Add(3818, 3819)).IsEqualTo(7637);

    [Test]
    public async Task Case_3819() => await Assert.That(Calc.Add(3819, 3820)).IsEqualTo(7639);

    [Test]
    public async Task Case_3820() => await Assert.That(Calc.Add(3820, 3821)).IsEqualTo(7641);
}
#endif
