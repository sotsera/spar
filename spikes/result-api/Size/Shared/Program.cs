// Runs the 20-command sample through its dispatch: checks the JSON, then measures allocations and time per call.
// The same file runs in every variant, under the JIT (dotnet run -c Release) and as a NativeAOT executable.
using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Sample;

const int WarmUp = 500;
const int Rounds = 2000;

var service = new Service();
var buffer = new ArrayBufferWriter<byte>(4096);
var success = new Req("id-1", Fail: false, Yield: false);
var notFound = new Req("id-1", Fail: true, Yield: false);
var conflict = new Req("id-22", Fail: true, Yield: false);

// 1. The JSON of every command, as a hash, so the variants can be compared; commands 1 and 15 in full.
var hash = 14695981039346656037UL;
foreach (var command in Enumerable.Range(1, Dispatch.CommandCount))
{
    foreach (var request in new[] { success, notFound, conflict })
    {
        buffer.ResetWrittenCount();
        await Dispatch.RunAsync(service, command, request, buffer);
        foreach (var b in buffer.WrittenSpan)
        {
            hash = (hash ^ b) * 1099511628211UL;
        }

        if (command is 1 or 15)
        {
            Console.WriteLine($"json {command} fail={request.Fail}: {Encoding.UTF8.GetString(buffer.WrittenSpan)}");
        }
    }
}

Console.WriteLine($"json hash: {hash:x16}");

// 2. Allocated bytes per call when commands complete synchronously, split by Task and ValueTask commands.
Console.WriteLine($"bytes/call sync success, Task commands: {await MeasureSync(success, odd: true)}");
Console.WriteLine($"bytes/call sync success, ValueTask commands: {await MeasureSync(success, odd: false)}");
Console.WriteLine($"bytes/call sync error, Task commands: {await MeasureSync(notFound, odd: true)}");
Console.WriteLine($"bytes/call sync error, ValueTask commands: {await MeasureSync(notFound, odd: false)}");

// 3. The same when every command yields once, so it completes asynchronously (all threads counted).
Console.WriteLine($"bytes/call async success, all commands: {await MeasureAsync(success with { Yield = true })}");
Console.WriteLine($"bytes/call async error, all commands: {await MeasureAsync(notFound with { Yield = true })}");

// 4. Time per synchronous dispatch, best of five runs.
Console.WriteLine($"ns/call sync success: {await Time(success)}");
Console.WriteLine($"ns/call sync error: {await Time(notFound)}");
return 0;

async Task<string> MeasureSync(Req request, bool odd)
{
    var commands = Enumerable.Range(1, Dispatch.CommandCount).Where(c => (c % 2 == 1) == odd).ToArray();
    await Loop(request, commands, WarmUp);

    var before = GC.GetAllocatedBytesForCurrentThread();
    await Loop(request, commands, Rounds);
    var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    return Format((double)allocated / (Rounds * commands.Length));
}

async Task<string> MeasureAsync(Req request)
{
    var commands = Enumerable.Range(1, Dispatch.CommandCount).ToArray();
    const int asyncRounds = 200;
    await Loop(request, commands, asyncRounds);

    var before = GC.GetTotalAllocatedBytes(precise: true);
    await Loop(request, commands, asyncRounds);
    var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
    return Format((double)allocated / (asyncRounds * commands.Length));
}

async Task<string> Time(Req request)
{
    var commands = Enumerable.Range(1, Dispatch.CommandCount).ToArray();
    var best = double.MaxValue;
    for (var run = 0; run < 5; run++)
    {
        var watch = Stopwatch.StartNew();
        await Loop(request, commands, Rounds);
        best = Math.Min(best, watch.Elapsed.TotalNanoseconds / (Rounds * commands.Length));
    }

    return Format(best);
}

async Task Loop(Req request, int[] commands, int rounds)
{
    for (var round = 0; round < rounds; round++)
    {
        foreach (var command in commands)
        {
            buffer.ResetWrittenCount();
            await Dispatch.RunAsync(service, command, request, buffer);
        }
    }
}

static string Format(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);
