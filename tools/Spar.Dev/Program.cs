using System.Text.Json;
using System.Threading.Channels;
using Spar.Dev;
using Sotsera.Spar.Development;

if (args.Length == 0)
{
    Console.Error.WriteLine(StartupOptions.Usage);
    return 1;
}

try
{
    return await RunAsync(StartupOptions.Parse(args));
}
catch (StartupFailure error)
{
    Console.Error.WriteLine($"Spar startup failed [{error.Code}]: {error.Message}");
    Console.WriteLine(JsonSerializer.Serialize(new StartupError("startup-error", error.Code, error.Message),
        StartupJsonContext.Default.StartupError));
    return 1;
}

static async Task<int> RunAsync(StartupOptions options)
{
    var settings = StartupPreparation.LoadSettings(options.Configuration);
    using var projectOwner = StartupPreparation.PrepareProjectOwner(options, settings);
    if (options.Debugger || projectOwner is not null)
    {
        if (settings.ContractFile is null)
        {
            throw new StartupFailure("contract_required", "Debugger mode requires the separately compiled contract source path. Set contractFile in the configuration.");
        }
        settings = settings with
        {
            WatchBackend = false,
            DebuggerBackend = true
        };
    }
    if (options.NoBuild && projectOwner is null && (settings.WatchBackend || settings.DebuggerBackend))
    {
        throw new StartupFailure("build_required", "Backend update modes require a selected full build. Omit --no-build.");
    }
    using var eventsFile = StartupPreparation.OpenEventsFile(options.EventsFile);
    StartupPreparation.PrepareSessionDirectory(settings.SessionDirectory);
    var commands = Channel.CreateBounded<DevelopmentCommand>(16);
    var sessionObserved = false;
    await using var session = new DevelopmentSession(settings,
        value =>
        {
            sessionObserved = true;
            var line = JsonSerializer.Serialize(value, DevelopmentJsonContext.Default.DevelopmentEvent);
            eventsFile?.WriteLine(line);
            Console.WriteLine(line);
            if (!settings.DebuggerBackend && SessionPresentation.Format(value) is { } progress)
            {
                Console.Error.WriteLine(progress);
            }
        },
        options.Probe,
        launch =>
        {
            // Private owner transport only. Never copy capabilities into the public event file.
            Console.WriteLine(JsonSerializer.Serialize(launch, DevelopmentJsonContext.Default.DevelopmentDebugLaunch));
            return Task.CompletedTask;
        }, projectOwner, options.Inspect);
    var interruptCount = 0;
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        session.RequestStop(Interlocked.Increment(ref interruptCount) > 1, "console_cancel");
    };
    var ownerStdin = projectOwner is not null || options.OwnerStdin;
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
    try
    {
        return await session.RunAsync(commands.Reader, projectOwner is not null || options.NoBuild);
    }
    catch (Exception error) when (!sessionObserved && error is IOException or UnauthorizedAccessException)
    {
        // The root can exist while denying creation of the first session subdirectory.
        throw new StartupFailure("session_directory_unavailable", "Cannot create the session output directory under "
            + settings.SessionDirectory + ". Check write access and the configured path.");
    }
}
