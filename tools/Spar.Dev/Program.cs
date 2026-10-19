using System.Text.Json;
using System.Threading.Channels;
using Sotsera.Spar.Development;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: Spar.Dev <spar.dev.json> [--no-build] [--owner-stdin] [--probe] [--events-file <path>]");
    return 1;
}

var settings = DevelopmentSettings.Load(args[0]);
var eventsIndex = Array.IndexOf(args, "--events-file");
if (eventsIndex >= 0 && eventsIndex + 1 >= args.Length)
{
    Console.Error.WriteLine("--events-file requires a path.");
    return 1;
}

using var eventsFile = eventsIndex < 0 ? null : new StreamWriter(new FileStream(
    Path.GetFullPath(args[eventsIndex + 1]), FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite))
{
    AutoFlush = true
};
var commands = Channel.CreateBounded<DevelopmentCommand>(16);
await using var session = new DevelopmentSession(settings,
    value =>
    {
        var line = JsonSerializer.Serialize(value, DevelopmentJsonContext.Default.DevelopmentEvent);
        eventsFile?.WriteLine(line);
        Console.WriteLine(line);
    },
    args.Contains("--probe", StringComparer.Ordinal));
var interruptCount = 0;
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    session.RequestStop(Interlocked.Increment(ref interruptCount) > 1, "console_cancel");
};
var ownerStdin = args.Contains("--owner-stdin", StringComparer.Ordinal);
_ = Task.Run(async () =>
{
    try
    {
        var input = new BoundedLineReader(Console.OpenStandardInput());
        while (await input.ReadAsync() is { } line)
        {
            var command = line.StartsWith('{')
                ? JsonSerializer.Deserialize(line, DevelopmentJsonContext.Default.DevelopmentCommand)
                : new DevelopmentCommand(line.Trim());
            if (command is null)
            {
                continue;
            }
            if (command.Command == "stop")
            {
                session.RequestStop(command.Force, correlation: command.Id);
            }
            else if (!commands.Writer.TryWrite(command))
            {
                // Keep reading so an explicit force/stop can still interrupt a pending graceful restart.
                Console.Error.WriteLine("Command queue is full; this request was not accepted.");
            }
        }

        if (ownerStdin)
        {
            session.RequestStop(true, "owner_disconnected");
        }
    }
    catch (Exception error) when (error is IOException or JsonException or System.Text.DecoderFallbackException)
    {
        session.RequestStop(ownerStdin, "owner_input_failed");
    }
    finally { commands.Writer.TryComplete(); }
});
return await session.RunAsync(commands.Reader, args.Contains("--no-build", StringComparer.Ordinal));
