using Spike.Subject;

namespace Spike.TUnitSuite;

// Four probes in one class and four in classes of their own. Each waits 250 ms.
[Category("Probe")]
public sealed class ProbeTests
{
    [Test]
    public async Task Same_class_1(CancellationToken token) =>
        Console.WriteLine($"max concurrent {await Concurrency.ProbeAsync(token)}");

    [Test]
    public async Task Same_class_2(CancellationToken token) =>
        Console.WriteLine($"max concurrent {await Concurrency.ProbeAsync(token)}");

    [Test]
    public async Task Same_class_3(CancellationToken token) =>
        Console.WriteLine($"max concurrent {await Concurrency.ProbeAsync(token)}");

    [Test]
    public async Task Same_class_4(CancellationToken token) =>
        Console.WriteLine($"max concurrent {await Concurrency.ProbeAsync(token)}");
}

[Category("Probe")]
public sealed class ProbeOwnClass1
{
    [Test]
    public async Task Own_class(CancellationToken token) =>
        Console.WriteLine($"max concurrent {await Concurrency.ProbeAsync(token)}");
}

[Category("Probe")]
public sealed class ProbeOwnClass2
{
    [Test]
    public async Task Own_class(CancellationToken token) =>
        Console.WriteLine($"max concurrent {await Concurrency.ProbeAsync(token)}");
}

[Category("Probe")]
public sealed class ProbeOwnClass3
{
    [Test]
    public async Task Own_class(CancellationToken token) =>
        Console.WriteLine($"max concurrent {await Concurrency.ProbeAsync(token)}");
}

[Category("Probe")]
public sealed class ProbeOwnClass4
{
    [Test]
    public async Task Own_class(CancellationToken token) =>
        Console.WriteLine($"max concurrent {await Concurrency.ProbeAsync(token)}");
}
