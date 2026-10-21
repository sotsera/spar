using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Sotsera.Spar.Development;

/// <summary>Debug-only bootstrap for ordinary IDE project launch. The IDE builds and owns managed updates.</summary>
public sealed class DevelopmentProjectLaunch : IDisposable
{
    private readonly Process _coordinator;
    private int _disposed;

    public string EventsFile
    {
        get;
    }

    private DevelopmentProjectLaunch(Process coordinator, string eventsFile)
    {
        _coordinator = coordinator;
        EventsFile = eventsFile;
    }

    public static DevelopmentProjectLaunch Start(string settingsPath, string coordinatorPath)
    {
        var settings = LoadSettings(settingsPath);
        using var application = Process.GetCurrentProcess();
        if (!string.Equals(application.MainModule?.FileName, settings.Application, StringComparison.OrdinalIgnoreCase))
        {
            throw StartupFailure("project_owner_mismatch", "Project launch must run the configured application executable.");
        }
        if (Environment.GetEnvironmentVariable("SPAR_DEV_APP_PIPE") is not null)
        {
            throw StartupFailure("owner_already_present", "The application already has a development owner.");
        }

        try
        {
            Directory.CreateDirectory(settings.SessionDirectory);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw StartupFailure("session_directory_unavailable", "Cannot create the session directory. Check write access to "
                + settings.SessionDirectory);
        }
        var eventsFile = Path.Combine(settings.SessionDirectory, "project-" + Guid.NewGuid().ToString("N") + ".jsonl");
        var start = new ProcessStartInfo(Path.GetFullPath(coordinatorPath))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(settings.Project)!
        };
        foreach (var argument in new[] { Path.GetFullPath(settingsPath), "--project-owner",
            application.Id.ToString(CultureInfo.InvariantCulture), application.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),
            "--owner-stdin", "--events-file", eventsFile })
        {
            start.ArgumentList.Add(argument);
        }
        // A debugger-launched process can carry startup hooks belonging to the IDE. The tool is not a debug target.
        foreach (var variable in new[] { "DOTNET_STARTUP_HOOKS", "DOTNET_MODIFIABLE_ASSEMBLIES", "SPAR_DEV_PROJECT", "SPAR_DEV_COORDINATOR" })
        {
            start.Environment.Remove(variable);
        }
        var coordinator = StartCoordinator(start);
        var owner = new DevelopmentProjectLaunch(coordinator, eventsFile);
        var launch = new TaskCompletionSource<DevelopmentDebugLaunch>(TaskCreationOptions.RunContinuationsAsynchronously);
        var output = owner.ReadOutputAsync(launch);
        var errors = owner.ReadErrorsAsync(eventsFile + ".stderr.log");
        _ = owner.ReleaseAfterExitAsync(output, errors);
        try
        {
            var configuration = launch.Task.WaitAsync(TimeSpan.FromSeconds(settings.StartupTimeoutSeconds + 15))
                .GetAwaiter().GetResult();
            if (!string.Equals(configuration.Application, settings.Application, StringComparison.OrdinalIgnoreCase)
                || configuration.Generation != 1)
            {
                throw new InvalidDataException("Unexpected project launch handoff.");
            }
            foreach (var variable in configuration.Environment)
            {
                Environment.SetEnvironmentVariable(variable.Key, variable.Value);
            }
            Debug.WriteLine("Spar session events: " + eventsFile);
            return owner;
        }
        catch
        {
            owner.CloseInput(); // Owner loss requests forced cleanup, including a partially admitted app.
            throw;
        }
    }

    /// <summary>Call only after native work and the application connection have completed normally.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }
        try
        {
            _coordinator.StandardInput.WriteLine("project-completed");
            _coordinator.StandardInput.Flush();
            _coordinator.StandardInput.Close();
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException or InvalidOperationException) { }
        // The coordinator waits for this process's actual exit; waiting here would deadlock its retirement.
    }

    private static DevelopmentSettings LoadSettings(string path)
    {
        try
        {
            return DevelopmentSettings.Load(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException
            or ArgumentException or InvalidDataException or NotSupportedException)
        {
            var code = error is FileNotFoundException or DirectoryNotFoundException
                ? "configuration_not_found" : "configuration_invalid";
            throw StartupFailure(code, "Cannot load session configuration: " + path
                + ". Check the path, JSON, settings and read access.");
        }
    }

    private static Process StartCoordinator(ProcessStartInfo start)
    {
        try
        {
            return Process.Start(start) ?? throw StartupFailure("coordinator_unavailable", "Coordinator did not start.");
        }
        catch (Exception error) when (error is Win32Exception or IOException or UnauthorizedAccessException)
        {
            throw StartupFailure("coordinator_unavailable", "Cannot start the coordinator: " + start.FileName
                + ". Build the development tool and check its configured path.");
        }
    }

    private static InvalidOperationException StartupFailure(string code, string message)
    {
        var text = new string(message.Select(value => char.IsControl(value) || value is '\u2028' or '\u2029'
            ? ' ' : value).ToArray());
        if (text.Length > 1024)
        {
            text = text[..1021] + "...";
        }
        var failure = $"Spar startup failed [{code}]: {text}";
        Debug.WriteLine(failure);
        return new InvalidOperationException(failure);
    }

    private async Task ReadOutputAsync(TaskCompletionSource<DevelopmentDebugLaunch> launch)
    {
        try
        {
            var reader = new BoundedLineReader(_coordinator.StandardOutput.BaseStream);
            while (await reader.ReadAsync().ConfigureAwait(false) is { } line)
            {
                using var document = JsonDocument.Parse(line);
                if (document.RootElement.GetProperty("kind").GetString() == "startup-error")
                {
                    var code = document.RootElement.GetProperty("code").GetString();
                    var message = document.RootElement.GetProperty("message").GetString();
                    if (string.IsNullOrWhiteSpace(code) || code.Length > 64
                        || !code.All(value => value is >= 'a' and <= 'z' or '_') || string.IsNullOrWhiteSpace(message)
                        || message.Length > 1024)
                    {
                        throw new InvalidDataException("Invalid coordinator startup error.");
                    }
                    throw StartupFailure(code, message);
                }
                if (document.RootElement.GetProperty("kind").GetString() == "debug-launch")
                {
                    var value = JsonSerializer.Deserialize(line, DevelopmentJsonContext.Default.DevelopmentDebugLaunch)
                        ?? throw new InvalidDataException("Empty project launch handoff.");
                    if (!launch.TrySetResult(value))
                    {
                        throw new InvalidDataException("A project session cannot launch another application.");
                    }
                }
                else
                {
                    Debug.WriteLine("Spar: " + line);
                }
            }
            launch.TrySetException(new IOException("Coordinator exited before project admission. Inspect " + EventsFile));
        }
        catch (Exception error)
        {
            launch.TrySetException(error);
            CloseInput();
        }
    }

    private void CloseInput()
    {
        try
        {
            _coordinator.StandardInput.Close();
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException or InvalidOperationException) { }
    }

    private async Task ReadErrorsAsync(string path)
    {
        using var log = new StreamWriter(path);
        var reader = new BoundedLineReader(_coordinator.StandardError.BaseStream);
        while (await reader.ReadAsync().ConfigureAwait(false) is { } line)
        {
            await log.WriteLineAsync(line).ConfigureAwait(false);
            await log.FlushAsync().ConfigureAwait(false);
            Debug.WriteLine("Spar: " + line);
        }
    }

    private async Task ReleaseAfterExitAsync(Task output, Task errors)
    {
        try
        {
            await _coordinator.WaitForExitAsync().ConfigureAwait(false);
            await Task.WhenAll(output, errors).ConfigureAwait(false);
        }
        finally
        {
            _coordinator.Dispose();
        }
    }
}
