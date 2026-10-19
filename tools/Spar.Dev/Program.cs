using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Threading.Channels;
using Sotsera.Spar.Development;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: Spar.Dev <spar.dev.json> [--no-build] [--owner-stdin] [--debugger] [--inspect] [--probe] [--events-file <path>]");
    return 1;
}

var settings = DevelopmentSettings.Load(args[0]);
var projectOwnerIndex = Array.IndexOf(args, "--project-owner");
if (projectOwnerIndex >= 0 && projectOwnerIndex + 2 >= args.Length)
{
    Console.Error.WriteLine("--project-owner requires an application PID and UTC creation ticks.");
    return 1;
}
using var projectOwner = projectOwnerIndex < 0 ? null : Process.GetProcessById(
    int.Parse(args[projectOwnerIndex + 1], CultureInfo.InvariantCulture));
if (projectOwner is not null)
{
    _ = projectOwner.Handle;
    if (projectOwner.StartTime.ToUniversalTime().Ticks != long.Parse(args[projectOwnerIndex + 2], CultureInfo.InvariantCulture)
        || !string.Equals(projectOwner.MainModule?.FileName, settings.Application, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidDataException("Project owner does not match the configured application instance.");
    }
}
if (args.Contains("--debugger", StringComparer.Ordinal) || projectOwner is not null)
{
    if (settings.ContractFile is null)
    {
        throw new InvalidDataException("Debugger mode requires the separately compiled contract source path.");
    }
    settings = settings with
    {
        WatchBackend = false,
        DebuggerBackend = true
    };
}
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
    args.Contains("--probe", StringComparer.Ordinal),
    launch =>
    {
        // Private owner transport only. Never copy capabilities into the public event file.
        Console.WriteLine(JsonSerializer.Serialize(launch, DevelopmentJsonContext.Default.DevelopmentDebugLaunch));
        return Task.CompletedTask;
    }, projectOwner, args.Contains("--inspect", StringComparer.Ordinal));
var interruptCount = 0;
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    session.RequestStop(Interlocked.Increment(ref interruptCount) > 1, "console_cancel");
};
var ownerStdin = projectOwner is not null || args.Contains("--owner-stdin", StringComparer.Ordinal);
var projectCompleted = false;
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
            if (command.Command == "project-completed" && projectOwner is not null)
            {
                projectCompleted = true;
                session.RequestStop(reason: "application_closed");
            }
            else if (command.Command == "stop")
            {
                session.RequestStop(command.Force, correlation: command.Id);
            }
            else if (command.Command == "debugger-state" && command.Detail is not null)
            {
                session.ReportDebuggerState(command.Generation, command.Detail);
            }
            else if (!commands.Writer.TryWrite(command))
            {
                // Keep reading so an explicit force/stop can still interrupt a pending graceful restart.
                session.RejectCommand(command, "command_queue_full");
            }
        }

        if (ownerStdin && !projectCompleted)
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
return await session.RunAsync(commands.Reader, projectOwner is not null || args.Contains("--no-build", StringComparer.Ordinal));
