#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.XunitSuite.Scale;

public sealed class ScaleTests85
{
    [Fact]
    public void Case_8501() => Assert.Equal(17003, Calc.Add(8501, 8502));

    [Fact]
    public void Case_8502() => Assert.Equal(17005, Calc.Add(8502, 8503));

    [Fact]
    public void Case_8503() => Assert.Equal(17007, Calc.Add(8503, 8504));

    [Fact]
    public void Case_8504() => Assert.Equal(17009, Calc.Add(8504, 8505));

    [Fact]
    public void Case_8505() => Assert.Equal(17011, Calc.Add(8505, 8506));

    [Fact]
    public void Case_8506() => Assert.Equal(17013, Calc.Add(8506, 8507));

    [Fact]
    public void Case_8507() => Assert.Equal(17015, Calc.Add(8507, 8508));

    [Fact]
    public void Case_8508() => Assert.Equal(17017, Calc.Add(8508, 8509));

    [Fact]
    public void Case_8509() => Assert.Equal(17019, Calc.Add(8509, 8510));

    [Fact]
    public void Case_8510() => Assert.Equal(17021, Calc.Add(8510, 8511));

    [Fact]
    public void Case_8511() => Assert.Equal(17023, Calc.Add(8511, 8512));

    [Fact]
    public void Case_8512() => Assert.Equal(17025, Calc.Add(8512, 8513));

    [Fact]
    public void Case_8513() => Assert.Equal(17027, Calc.Add(8513, 8514));

    [Fact]
    public void Case_8514() => Assert.Equal(17029, Calc.Add(8514, 8515));

    [Fact]
    public void Case_8515() => Assert.Equal(17031, Calc.Add(8515, 8516));

    [Fact]
    public void Case_8516() => Assert.Equal(17033, Calc.Add(8516, 8517));

    [Fact]
    public void Case_8517() => Assert.Equal(17035, Calc.Add(8517, 8518));

    [Fact]
    public void Case_8518() => Assert.Equal(17037, Calc.Add(8518, 8519));

    [Fact]
    public void Case_8519() => Assert.Equal(17039, Calc.Add(8519, 8520));

    [Fact]
    public void Case_8520() => Assert.Equal(17041, Calc.Add(8520, 8521));
}
#endif
