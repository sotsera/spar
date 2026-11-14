#if SPIKE_SCALE
using Spike.Subject;

namespace Spike.XunitSuite.Scale;

public sealed class ScaleTests20
{
    [Fact]
    public void Case_2001() => Assert.Equal(4003, Calc.Add(2001, 2002));

    [Fact]
    public void Case_2002() => Assert.Equal(4005, Calc.Add(2002, 2003));

    [Fact]
    public void Case_2003() => Assert.Equal(4007, Calc.Add(2003, 2004));

    [Fact]
    public void Case_2004() => Assert.Equal(4009, Calc.Add(2004, 2005));

    [Fact]
    public void Case_2005() => Assert.Equal(4011, Calc.Add(2005, 2006));

    [Fact]
    public void Case_2006() => Assert.Equal(4013, Calc.Add(2006, 2007));

    [Fact]
    public void Case_2007() => Assert.Equal(4015, Calc.Add(2007, 2008));

    [Fact]
    public void Case_2008() => Assert.Equal(4017, Calc.Add(2008, 2009));

    [Fact]
    public void Case_2009() => Assert.Equal(4019, Calc.Add(2009, 2010));

    [Fact]
    public void Case_2010() => Assert.Equal(4021, Calc.Add(2010, 2011));

    [Fact]
    public void Case_2011() => Assert.Equal(4023, Calc.Add(2011, 2012));

    [Fact]
    public void Case_2012() => Assert.Equal(4025, Calc.Add(2012, 2013));

    [Fact]
    public void Case_2013() => Assert.Equal(4027, Calc.Add(2013, 2014));

    [Fact]
    public void Case_2014() => Assert.Equal(4029, Calc.Add(2014, 2015));

    [Fact]
    public void Case_2015() => Assert.Equal(4031, Calc.Add(2015, 2016));

    [Fact]
    public void Case_2016() => Assert.Equal(4033, Calc.Add(2016, 2017));

    [Fact]
    public void Case_2017() => Assert.Equal(4035, Calc.Add(2017, 2018));

    [Fact]
    public void Case_2018() => Assert.Equal(4037, Calc.Add(2018, 2019));

    [Fact]
    public void Case_2019() => Assert.Equal(4039, Calc.Add(2019, 2020));

    [Fact]
    public void Case_2020() => Assert.Equal(4041, Calc.Add(2020, 2021));
}
#endif
