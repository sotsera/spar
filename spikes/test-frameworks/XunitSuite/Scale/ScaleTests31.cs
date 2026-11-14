#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.XunitSuite.Scale;

public sealed class ScaleTests31
{
    [Fact]
    public void Case_3101() => Assert.Equal(6203, Calc.Add(3101, 3102));

    [Fact]
    public void Case_3102() => Assert.Equal(6205, Calc.Add(3102, 3103));

    [Fact]
    public void Case_3103() => Assert.Equal(6207, Calc.Add(3103, 3104));

    [Fact]
    public void Case_3104() => Assert.Equal(6209, Calc.Add(3104, 3105));

    [Fact]
    public void Case_3105() => Assert.Equal(6211, Calc.Add(3105, 3106));

    [Fact]
    public void Case_3106() => Assert.Equal(6213, Calc.Add(3106, 3107));

    [Fact]
    public void Case_3107() => Assert.Equal(6215, Calc.Add(3107, 3108));

    [Fact]
    public void Case_3108() => Assert.Equal(6217, Calc.Add(3108, 3109));

    [Fact]
    public void Case_3109() => Assert.Equal(6219, Calc.Add(3109, 3110));

    [Fact]
    public void Case_3110() => Assert.Equal(6221, Calc.Add(3110, 3111));

    [Fact]
    public void Case_3111() => Assert.Equal(6223, Calc.Add(3111, 3112));

    [Fact]
    public void Case_3112() => Assert.Equal(6225, Calc.Add(3112, 3113));

    [Fact]
    public void Case_3113() => Assert.Equal(6227, Calc.Add(3113, 3114));

    [Fact]
    public void Case_3114() => Assert.Equal(6229, Calc.Add(3114, 3115));

    [Fact]
    public void Case_3115() => Assert.Equal(6231, Calc.Add(3115, 3116));

    [Fact]
    public void Case_3116() => Assert.Equal(6233, Calc.Add(3116, 3117));

    [Fact]
    public void Case_3117() => Assert.Equal(6235, Calc.Add(3117, 3118));

    [Fact]
    public void Case_3118() => Assert.Equal(6237, Calc.Add(3118, 3119));

    [Fact]
    public void Case_3119() => Assert.Equal(6239, Calc.Add(3119, 3120));

    [Fact]
    public void Case_3120() => Assert.Equal(6241, Calc.Add(3120, 3121));
}
#endif
