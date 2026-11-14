#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.XunitSuite.Scale;

public sealed class ScaleTests81
{
    [Fact]
    public void Case_8101() => Assert.Equal(16203, Calc.Add(8101, 8102));

    [Fact]
    public void Case_8102() => Assert.Equal(16205, Calc.Add(8102, 8103));

    [Fact]
    public void Case_8103() => Assert.Equal(16207, Calc.Add(8103, 8104));

    [Fact]
    public void Case_8104() => Assert.Equal(16209, Calc.Add(8104, 8105));

    [Fact]
    public void Case_8105() => Assert.Equal(16211, Calc.Add(8105, 8106));

    [Fact]
    public void Case_8106() => Assert.Equal(16213, Calc.Add(8106, 8107));

    [Fact]
    public void Case_8107() => Assert.Equal(16215, Calc.Add(8107, 8108));

    [Fact]
    public void Case_8108() => Assert.Equal(16217, Calc.Add(8108, 8109));

    [Fact]
    public void Case_8109() => Assert.Equal(16219, Calc.Add(8109, 8110));

    [Fact]
    public void Case_8110() => Assert.Equal(16221, Calc.Add(8110, 8111));

    [Fact]
    public void Case_8111() => Assert.Equal(16223, Calc.Add(8111, 8112));

    [Fact]
    public void Case_8112() => Assert.Equal(16225, Calc.Add(8112, 8113));

    [Fact]
    public void Case_8113() => Assert.Equal(16227, Calc.Add(8113, 8114));

    [Fact]
    public void Case_8114() => Assert.Equal(16229, Calc.Add(8114, 8115));

    [Fact]
    public void Case_8115() => Assert.Equal(16231, Calc.Add(8115, 8116));

    [Fact]
    public void Case_8116() => Assert.Equal(16233, Calc.Add(8116, 8117));

    [Fact]
    public void Case_8117() => Assert.Equal(16235, Calc.Add(8117, 8118));

    [Fact]
    public void Case_8118() => Assert.Equal(16237, Calc.Add(8118, 8119));

    [Fact]
    public void Case_8119() => Assert.Equal(16239, Calc.Add(8119, 8120));

    [Fact]
    public void Case_8120() => Assert.Equal(16241, Calc.Add(8120, 8121));
}
#endif
