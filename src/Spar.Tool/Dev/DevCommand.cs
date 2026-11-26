using System.Runtime.InteropServices;
using System.Text.Json;
using Spar.Tool.Devtools;
using Spar.Tool.Project;

namespace Spar.Tool.Dev;

/// <summary>
/// <c>spar dev</c>, <c>spar dev --stop</c> and <c>spar reload</c> (plan, M2b; decision "M2b's decide-first questions"):
/// the supervisor in the foreground, and the two requests to it through <c>obj/spar-dev.json</c>.
/// </summary>
internal static class DevCommand
{
    public const string DevUsage = """
        Usage: spar dev [--project <path>] [-lp|--launch-profile <name>] [--private]
               spar dev --stop [--project <path>]

        Builds the app's Development build, starts it, and runs until Ctrl+C or spar dev --stop. spar reload builds
        again: a failed build answers with its errors at file and line and keeps the running app; a good one restarts
        the app in its window's place, without taking the focus, and answers once the page is ready.

        Options:
          --project <path>          The app's project file or folder (default: the current folder).
          -lp, --launch-profile     A profile of Properties/launchSettings.json (default: the first that runs the project).
          --private                 Run the app where you cannot see it (a desktop of its own on Windows, a private Xvfb
                                    on Linux); the devtools still work.
          --stop                    End the project's running spar dev, its app and everything it started.
        """;

    public const string ReloadUsage = """
        Usage: spar reload [--project <path>] [--json]

        Asks the project's running spar dev to build again and restart the app. Answers with the build's errors and
        warnings at file, line and column, and the new run once its page reports ui-ready (60 s at most).
        Exit codes: 0 ready, 1 ready with warnings, 2 the build or the start failed, 64 a wrong command line.
        """;

    public static int RunDev(string[] args, TextWriter output, TextWriter error, string workingDirectory)
    {
        string? project = null;
        string? profileName = null;
        var isPrivate = false;
        var stop = false;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "-h" or "--help":
                    output.WriteLine(DevUsage);
                    return ExitCodes.Success;
                case "--project" when index + 1 < args.Length:
                    project = args[++index];
                    break;
                case "-lp" or "--launch-profile" when index + 1 < args.Length:
                    profileName = args[++index];
                    break;
                case "--private":
                    isPrivate = true;
                    break;
                case "--stop":
                    stop = true;
                    break;
                default:
                    error.WriteLine($"spar dev: unknown or incomplete option '{args[index]}'.");
                    error.WriteLine(DevUsage);
                    return ExitCodes.Usage;
            }
        }

        if (AppProject.Find(Path.Combine(workingDirectory, project ?? "."), out var problem) is not { } app)
        {
            error.WriteLine($"spar dev: {problem}");
            return ExitCodes.Errors;
        }

        if (stop)
        {
            return Request(app, "stop", json: false, output, error, "dev --stop");
        }

        if (LaunchProfile.Find(app.Directory, profileName, out var noProfile) is not { } profile)
        {
            error.WriteLine($"spar dev: {noProfile}");
            return ExitCodes.Usage;
        }

        return RunSupervisorAsync(app, profile, isPrivate, output, error).GetAwaiter().GetResult();
    }

    public static int RunReload(string[] args, TextWriter output, TextWriter error, string workingDirectory)
    {
        string? project = null;
        var json = false;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "-h" or "--help":
                    output.WriteLine(ReloadUsage);
                    return ExitCodes.Success;
                case "--project" when index + 1 < args.Length:
                    project = args[++index];
                    break;
                case "--json":
                    json = true;
                    break;
                default:
                    error.WriteLine($"spar reload: unknown or incomplete option '{args[index]}'.");
                    error.WriteLine(ReloadUsage);
                    return ExitCodes.Usage;
            }
        }

        if (AppProject.Find(Path.Combine(workingDirectory, project ?? "."), out var problem) is not { } app)
        {
            error.WriteLine($"spar reload: {problem}");
            return ExitCodes.Errors;
        }

        return Request(app, "reload", json, output, error, "reload");
    }

    private static async Task<int> RunSupervisorAsync(AppProject app, LaunchProfile profile, bool isPrivate, TextWriter output, TextWriter error)
    {
        var (supervisor, problem) = await DevSupervisor.StartAsync(app, profile, isPrivate, output);
        if (supervisor is null)
        {
            error.WriteLine($"spar dev: {problem}");
            return ExitCodes.Usage;
        }

        await using (supervisor)
        {
            void Stop(PosixSignalContext context)
            {
                context.Cancel = true;
                supervisor.RequestEnd();
            }

            using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, Stop);
            using var terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, Stop);
            output.WriteLine(supervisor.Describe());
            var first = supervisor.ReloadAsync();
            var done = await Task.WhenAny(first, supervisor.Ended);
            if (done == first)
            {
                output.WriteLine(Text(await first));
                await supervisor.Ended;
            }
        }

        return ExitCodes.Success;
    }

    /// <summary>One request to the project's running <c>spar dev</c>; its answer printed, its exit code returned.</summary>
    private static int Request(AppProject app, string method, bool json, TextWriter output, TextWriter error, string command)
    {
        var file = DevFile.PathFor(app.Directory);
        if (DevFile.Read(file) is not { } dev || !Running(dev.ProcessId))
        {
            error.WriteLine($"spar {command}: no spar dev runs for {app.Name} ({file} is missing or its process ended). Start one with spar dev.");
            return ExitCodes.Errors;
        }

        var target = new DevtoolsTarget(dev.ProcessId, "", "spar-dev", dev.Transport, dev.Address, dev.Token, app.Name);
        try
        {
            return RequestAsync(target, method, json, output).GetAwaiter().GetResult();
        }
        catch (DevtoolsCallException exception)
        {
            error.WriteLine($"spar {command}: {exception.Message}");
            return ExitCodes.Errors;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or TimeoutException or System.Net.Sockets.SocketException)
        {
            error.WriteLine($"spar {command}: spar dev did not answer: {exception.Message}");
            return ExitCodes.Errors;
        }
    }

    private static async Task<int> RequestAsync(DevtoolsTarget target, string method, bool json, TextWriter output)
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        await using var connection = await DevtoolsConnection.OpenAsync(new RealDevtoolsTransport(), target, limit.Token);
        using var answer = await connection.RequestAsync(method, _ => { }, limit.Token);
        if (answer.RootElement.TryGetProperty("error", out var refusal))
        {
            throw new DevtoolsCallException(refusal.Clone());
        }

        var result = answer.RootElement.GetProperty("result");
        if (json)
        {
            output.WriteLine(JsonText.Indented(result));
        }
        else
        {
            output.WriteLine(method == "reload" ? ReloadResult.Describe(result) : "spar dev is ending.");
        }

        return method == "reload" ? ExitCode(result) : ExitCodes.Success;
    }

    private static int ExitCode(JsonElement result)
    {
        var build = result.GetProperty("build");
        var state = result.GetProperty("app").GetProperty("state").GetString();
        if (!build.GetProperty("succeeded").GetBoolean() || state != ReloadResult.Ready)
        {
            return ExitCodes.Errors;
        }

        return build.GetProperty("warnings").GetArrayLength() > 0 ? ExitCodes.Warnings : ExitCodes.Success;
    }

    private static string Text(ReloadResult result)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            result.Write(writer);
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return ReloadResult.Describe(document.RootElement);
    }

    private static bool Running(int processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }
}
