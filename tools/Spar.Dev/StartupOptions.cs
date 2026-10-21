using System.Globalization;

namespace Spar.Dev;

internal sealed record StartupOptions(string Configuration, bool NoBuild, bool OwnerStdin, bool Debugger,
    bool Inspect, bool Probe, string? EventsFile, int? ProjectOwnerId, long ProjectOwnerTicks)
{
    internal const string Usage = "Usage: Spar.Dev <spar.dev.json> [--no-build] [--owner-stdin] [--debugger] "
        + "[--inspect] [--probe] [--events-file <path>]";

    internal static StartupOptions Parse(string[] arguments)
    {
        if (arguments.Length == 0 || string.IsNullOrWhiteSpace(arguments[0])
            || arguments[0].StartsWith("--", StringComparison.Ordinal) || arguments[0].Contains('\0'))
        {
            throw new StartupFailure("invalid_arguments", "Supply a session configuration file before the options.");
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? eventsFile = null;
        int? projectOwnerId = null;
        var projectOwnerTicks = 0L;
        for (var index = 1; index < arguments.Length; index++)
        {
            var option = arguments[index];
            if (option is not ("--no-build" or "--owner-stdin" or "--debugger" or "--inspect"
                or "--probe" or "--events-file" or "--project-owner"))
            {
                throw new StartupFailure("invalid_arguments", "Unknown option or extra argument. Run Spar.Dev without arguments for usage.");
            }
            if (!seen.Add(option))
            {
                throw new StartupFailure("invalid_arguments", $"Specify {option} only once.");
            }
            if (option == "--events-file")
            {
                eventsFile = ReadValue(arguments, ref index, "--events-file requires a new file path.");
            }
            else if (option == "--project-owner")
            {
                var id = ReadValue(arguments, ref index, "--project-owner requires an application PID and UTC creation ticks.");
                var ticks = ReadValue(arguments, ref index, "--project-owner requires an application PID and UTC creation ticks.");
                if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedId) || parsedId <= 0
                    || !long.TryParse(ticks, NumberStyles.None, CultureInfo.InvariantCulture, out projectOwnerTicks)
                    || projectOwnerTicks <= 0 || projectOwnerTicks > DateTime.MaxValue.Ticks)
                {
                    throw new StartupFailure("invalid_arguments", "--project-owner requires a positive PID and valid UTC creation ticks.");
                }
                projectOwnerId = parsedId;
            }
        }
        return new StartupOptions(arguments[0], seen.Contains("--no-build"), seen.Contains("--owner-stdin"),
            seen.Contains("--debugger"), seen.Contains("--inspect"), seen.Contains("--probe"), eventsFile,
            projectOwnerId, projectOwnerTicks);
    }

    private static string ReadValue(string[] arguments, ref int index, string message)
    {
        index++;
        if (index >= arguments.Length || string.IsNullOrWhiteSpace(arguments[index])
            || arguments[index].StartsWith("--", StringComparison.Ordinal) || arguments[index].Contains('\0'))
        {
            throw new StartupFailure("invalid_arguments", message);
        }
        return arguments[index];
    }
}
