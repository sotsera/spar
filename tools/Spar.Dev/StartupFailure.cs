using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sotsera.Spar.Development;

namespace Spar.Dev;

internal sealed class StartupFailure(string code, string message) : Exception(Bound(message))
{
    internal string Code { get; } = code;

    private static string Bound(string message)
    {
        var text = new string(message.Select(value => char.IsControl(value) || value is '\u2028' or '\u2029'
            ? ' ' : value).ToArray());
        return text.Length > 1024 ? text[..1021] + "..." : text;
    }
}

internal static class StartupPreparation
{
    internal static DevelopmentSettings LoadSettings(string path)
    {
        try
        {
            return DevelopmentSettings.Load(path);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new StartupFailure("configuration_not_found", $"Configuration file not found: {path}. Check the supplied path.");
        }
        catch (JsonException error)
        {
            throw new StartupFailure("configuration_invalid", $"Invalid configuration JSON at line {error.LineNumber}, "
                + $"byte {error.BytePositionInLine}. Check {path} against the session guide.");
        }
        catch (InvalidDataException error)
        {
            throw new StartupFailure("configuration_invalid", error.Message + $" Check {path}.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new StartupFailure("configuration_unavailable", $"Cannot read configuration: {path}. Check its path and read access.");
        }
    }

    internal static Process? OpenProjectOwner(StartupOptions options, DevelopmentSettings settings)
    {
        if (options.ProjectOwnerId is not { } id)
        {
            return null;
        }
        Process? process = null;
        try
        {
            process = Process.GetProcessById(id);
            _ = process.Handle;
            if (process.StartTime.ToUniversalTime().Ticks != options.ProjectOwnerTicks
                || !string.Equals(process.MainModule?.FileName, settings.Application, StringComparison.OrdinalIgnoreCase))
            {
                throw new StartupFailure("project_owner_mismatch", "Project owner does not match the configured application instance. Rebuild and launch through the IDE.");
            }
            return process;
        }
        catch
        {
            process?.Dispose();
            throw;
        }
    }

    internal static Process? PrepareProjectOwner(StartupOptions options, DevelopmentSettings settings)
    {
        try
        {
            return OpenProjectOwner(options, settings);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or Win32Exception)
        {
            throw new StartupFailure("project_owner_unavailable", "Cannot inspect the IDE application process. Rebuild and launch through the IDE.");
        }
    }

    internal static StreamWriter? OpenEventsFile(string? path)
    {
        if (path is null)
        {
            return null;
        }
        try
        {
            return new StreamWriter(new FileStream(Path.GetFullPath(path), FileMode.CreateNew,
                FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new StartupFailure("events_file_unavailable", $"Cannot create event file: {path}. Choose a new path in a writable directory.");
        }
    }

    internal static void PrepareSessionDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new StartupFailure("session_directory_unavailable", $"Cannot create session directory: {path}. Check write access and the configured path.");
        }
    }
}

internal sealed record StartupError(string Kind, string Code, string Message);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, MaxDepth = 4)]
[JsonSerializable(typeof(StartupError))]
internal sealed partial class StartupJsonContext : JsonSerializerContext;
