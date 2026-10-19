using System.Text.Json;

namespace Sotsera.Spar.Development;

public sealed record FrontendCommand
{
    public string FileName { get; set; } = "cmd.exe";
    public string[] Arguments { get; set; } = ["/d", "/c", "npm", "run", "dev"];
    public string WorkingDirectory { get; set; } = "frontend";
    public Dictionary<string, string> Environment { get; set; } = [];
    public int Port
    {
        get; set;
    }
}

public sealed record DevelopmentSettings
{
    public string Project { get; set; } = "";
    public string Application { get; set; } = "";
    public string GeneratedClient { get; set; } = "frontend/src/generated/client.js";
    public string SessionDirectory { get; set; } = "../../artifacts/development";
    public FrontendCommand Frontend { get; set; } = new();
    public int StartupTimeoutSeconds { get; set; } = 30;
    public int ShutdownTimeoutSeconds { get; set; } = 10;
    public bool WatchBackend
    {
        get; set;
    }
    public string? ContractFile
    {
        get; set;
    }

    public static DevelopmentSettings Load(string path)
    {
        var file = new FileInfo(Path.GetFullPath(path));
        if (file.Length > 64 * 1024)
        {
            throw new InvalidDataException("Session configuration exceeds 64 KiB.");
        }
        var settings = JsonSerializer.Deserialize(File.ReadAllText(file.FullName), DevelopmentJsonContext.Default.DevelopmentSettings)
            ?? throw new InvalidDataException("Missing session configuration.");
        var directory = file.DirectoryName!;
        if (settings.WatchBackend && string.IsNullOrWhiteSpace(settings.ContractFile))
        {
            throw new InvalidDataException("Watch mode requires the separately compiled contract source path.");
        }
        if (settings.Frontend is null || settings.Frontend.Arguments is null || settings.Frontend.Environment is null)
        {
            throw new InvalidDataException("Frontend command, arguments, and environment must not be null.");
        }

        var required = new[] { settings.Project, settings.Application, settings.GeneratedClient,
            settings.SessionDirectory, settings.Frontend.FileName, settings.Frontend.WorkingDirectory };
        if (required.Any(value => string.IsNullOrWhiteSpace(value) || value.Contains('\0'))
            || settings.Frontend.Arguments.Any(value => value is null || value.Contains('\0'))
            || settings.Frontend.Environment.Any(entry => string.IsNullOrWhiteSpace(entry.Key)
                || entry.Key.Contains('=') || entry.Key.Contains('\0') || entry.Value is null || entry.Value.Contains('\0')))
        {
            throw new InvalidDataException("Session paths and commands must be present and contain no NUL characters; environment names cannot contain '='.");
        }

        if (settings.StartupTimeoutSeconds is < 1 or > 300 || settings.ShutdownTimeoutSeconds is < 1 or > 60
            || settings.Frontend.Port is < 0 or > 65535 || settings.Frontend.Arguments.Length > 64
            || settings.Frontend.Environment.Count > 32)
        {
            throw new InvalidDataException($"Invalid session limits: startup {settings.StartupTimeoutSeconds}s, " +
                $"shutdown {settings.ShutdownTimeoutSeconds}s, port {settings.Frontend.Port}, " +
                $"arguments {settings.Frontend.Arguments.Length}, environment entries {settings.Frontend.Environment.Count}.");
        }

        return settings with
        {
            Project = Path.GetFullPath(settings.Project, directory),
            Application = Path.GetFullPath(settings.Application, directory),
            GeneratedClient = Path.GetFullPath(settings.GeneratedClient, directory),
            SessionDirectory = Path.GetFullPath(settings.SessionDirectory, directory),
            ContractFile = settings.ContractFile is null ? null : Path.GetFullPath(settings.ContractFile, directory),
            Frontend = settings.Frontend with
            {
                WorkingDirectory = Path.GetFullPath(settings.Frontend.WorkingDirectory, directory)
            }
        };
    }
}
