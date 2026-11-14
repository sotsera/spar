#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.XunitSuite.Scale;

public sealed class ScaleTests51
{
    [Fact]
    public void Case_5101() => Assert.Equal(10203, Calc.Add(5101, 5102));

    [Fact]
    public void Case_5102() => Assert.Equal(10205, Calc.Add(5102, 5103));

    [Fact]
    public void Case_5103() => Assert.Equal(10207, Calc.Add(5103, 5104));

    [Fact]
    public void Case_5104() => Assert.Equal(10209, Calc.Add(5104, 5105));

    [Fact]
    public void Case_5105() => Assert.Equal(10211, Calc.Add(5105, 5106));

    [Fact]
    public void Case_5106() => Assert.Equal(10213, Calc.Add(5106, 5107));

    [Fact]
    public void Case_5107() => Assert.Equal(10215, Calc.Add(5107, 5108));

    [Fact]
    public void Case_5108() => Assert.Equal(10217, Calc.Add(5108, 5109));

    [Fact]
    public void Case_5109() => Assert.Equal(10219, Calc.Add(5109, 5110));

    [Fact]
    public void Case_5110() => Assert.Equal(10221, Calc.Add(5110, 5111));

    [Fact]
    public void Case_5111() => Assert.Equal(10223, Calc.Add(5111, 5112));

    [Fact]
    public void Case_5112() => Assert.Equal(10225, Calc.Add(5112, 5113));

    [Fact]
    public void Case_5113() => Assert.Equal(10227, Calc.Add(5113, 5114));

    [Fact]
    public void Case_5114() => Assert.Equal(10229, Calc.Add(5114, 5115));

    [Fact]
    public void Case_5115() => Assert.Equal(10231, Calc.Add(5115, 5116));

    [Fact]
    public void Case_5116() => Assert.Equal(10233, Calc.Add(5116, 5117));

    [Fact]
    public void Case_5117() => Assert.Equal(10235, Calc.Add(5117, 5118));

    [Fact]
    public void Case_5118() => Assert.Equal(10237, Calc.Add(5118, 5119));

    [Fact]
    public void Case_5119() => Assert.Equal(10239, Calc.Add(5119, 5120));

    [Fact]
    public void Case_5120() => Assert.Equal(10241, Calc.Add(5120, 5121));
}
#endif
