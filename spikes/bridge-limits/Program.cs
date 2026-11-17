using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Server.Kestrel.Core;

System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

// 1. What a call waiting for a slot or running holds besides its message: a linked token source, a task awaiting
//    its work, and its entry in the session's table.
Console.WriteLine("== Memory per pending call (100,000 pending, no payload)");
{
    var closing = new CancellationTokenSource();
    var gate = new TaskCompletionSource();
    var pending = new Dictionary<long, CancellationTokenSource>();
    var running = new List<Task>();
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    var before = GC.GetTotalMemory(true);
    for (long id = 1; id <= 100_000; id++)
    {
        var token = CancellationTokenSource.CreateLinkedTokenSource(closing.Token);
        pending[id] = token;
        running.Add(Task.Run(async () => await gate.Task.WaitAsync(token.Token)));
    }

    await Task.Delay(500);
    var after = GC.GetTotalMemory(true);
    Console.WriteLine($"{(after - before) / 100_000.0:0} bytes per pending call");
    gate.SetResult();
    await Task.WhenAll(running);
}

Console.WriteLine();
// 2. One message taken in the way Spar's bridge does: the host hands a .NET string, it becomes UTF-8, the JSON
//    header up to the TAB is read, and the payload ({"text":"..."}) is read to its end with Utf8JsonReader.
Console.WriteLine("== Taking in one call message (median of 7; allocated bytes of one run)");
Console.WriteLine("payload MiB | ms | allocated MiB | x payload");
foreach (var mib in new[] { 0.0625, 1, 4, 16, 64 })
{
    var text = new string('a', (int)(mib * 1024 * 1024) - 12);
    var message = "{\"type\":\"call\",\"id\":7,\"contract\":\"pages\",\"command\":\"save\"}\t{\"text\":\"" + text + "\"}";
    var times = new List<double>();
    long allocated = 0;
    for (var run = 0; run < 7; run++)
    {
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var clock = Stopwatch.StartNew();
        TakeIn(message);
        clock.Stop();
        allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        times.Add(clock.Elapsed.TotalMilliseconds);
    }

    times.Sort();
    Console.WriteLine($"{mib,11} | {times[3],6:0.00} | {allocated / 1048576.0,13:0.0} | {allocated / (mib * 1048576),9:0.0}");
}

// 3. What the loopback server already refuses.
Console.WriteLine();
Console.WriteLine("== Kestrel defaults");
var limits = new KestrelServerLimits();
Console.WriteLine($"MaxRequestBodySize: {limits.MaxRequestBodySize:N0} bytes ({limits.MaxRequestBodySize / 1048576.0:0.0} MiB)");
Console.WriteLine($"MaxRequestLineSize: {limits.MaxRequestLineSize:N0}; MaxRequestHeadersTotalSize: {limits.MaxRequestHeadersTotalSize:N0}");

static void TakeIn(string message)
{
    var bytes = Encoding.UTF8.GetBytes(message);
    var tab = Array.IndexOf(bytes, (byte)'\t');
    var header = new Utf8JsonReader(bytes.AsSpan(0, tab));
    while (header.Read())
    {
    }

    var payload = new Utf8JsonReader(bytes.AsSpan(tab + 1));
    while (payload.Read())
    {
        if (payload.TokenType == JsonTokenType.String)
        {
            _ = payload.GetString();
        }
    }
}
