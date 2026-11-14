using Spike.Subject;

namespace Spike.XunitSuite;

// Four probes in one class (one collection) and four in classes of their own. Each waits 250 ms.
[Trait("Category", "Probe")]
public sealed class ProbeTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Same_class_1() =>
        output.WriteLine($"max concurrent {await Concurrency.ProbeAsync(TestContext.Current.CancellationToken)}");

    [Fact]
    public async Task Same_class_2() =>
        output.WriteLine($"max concurrent {await Concurrency.ProbeAsync(TestContext.Current.CancellationToken)}");

    [Fact]
    public async Task Same_class_3() =>
        output.WriteLine($"max concurrent {await Concurrency.ProbeAsync(TestContext.Current.CancellationToken)}");

    [Fact]
    public async Task Same_class_4() =>
        output.WriteLine($"max concurrent {await Concurrency.ProbeAsync(TestContext.Current.CancellationToken)}");
}

[Trait("Category", "Probe")]
public sealed class ProbeOwnClass1(ITestOutputHelper output)
{
    [Fact]
    public async Task Own_class() =>
        output.WriteLine($"max concurrent {await Concurrency.ProbeAsync(TestContext.Current.CancellationToken)}");
}

[Trait("Category", "Probe")]
public sealed class ProbeOwnClass2(ITestOutputHelper output)
{
    [Fact]
    public async Task Own_class() =>
        output.WriteLine($"max concurrent {await Concurrency.ProbeAsync(TestContext.Current.CancellationToken)}");
}

[Trait("Category", "Probe")]
public sealed class ProbeOwnClass3(ITestOutputHelper output)
{
    [Fact]
    public async Task Own_class() =>
        output.WriteLine($"max concurrent {await Concurrency.ProbeAsync(TestContext.Current.CancellationToken)}");
}

[Trait("Category", "Probe")]
public sealed class ProbeOwnClass4(ITestOutputHelper output)
{
    [Fact]
    public async Task Own_class() =>
        output.WriteLine($"max concurrent {await Concurrency.ProbeAsync(TestContext.Current.CancellationToken)}");
}
