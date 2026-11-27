using System.Text.Json;

namespace Spar;

/// <summary>
/// The installation file (decision "M3b's decide-first questions", item 6): <c>&lt;app&gt;.spar.json</c> beside the
/// executable, which an installation's owner edits to set deployment choices without rebuilding the app. Spar reads it
/// only when <c>Program.cs</c> calls <c>spar.InstallationFile()</c>, and it sets deployment choices only: the folders and
/// their removal at exit, browser data kept in memory, and where each settings record lives; never the page's policy,
/// permissions or the browser's defaults. Environment variables (<c>%USERNAME%</c>) are expanded; an unknown key, a
/// wrong value or a key the code locked stops the start with a message that names it.
/// </summary>
public sealed class InstallationOptions
{
    internal HashSet<string> LockedKeys { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Keeps a key as the code set it: the file may not set it. Keys are dotted paths, as <c>folders.data</c>,
    /// <c>folders.removeOnExit</c>, <c>settings.file</c> or <c>settings.layout.allowNetworkFile</c>.
    /// </summary>
    public InstallationOptions Locked(params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!InstallationFile.IsKnownKey(key))
            {
                throw new ArgumentException($"'{key}' is not a key of the installation file.", nameof(keys));
            }

            LockedKeys.Add(key);
        }

        return this;
    }
}

/// <summary>What an installation file set, parsed and checked.</summary>
internal sealed class InstallationFile
{
    private static readonly string[] FolderKeys = ["data", "logs", "profile", "ephemeralBrowserData", "removeOnExit"];

    private static readonly string[] SettingsKeys = ["file", "allowNetworkFile"];

    private static readonly JsonDocumentOptions DocumentOptions = new() { MaxDepth = 8 };

    private InstallationFile(string path)
    {
        Path = path;
    }

    /// <summary>Where it was read from.</summary>
    public string Path { get; }

    public string? DataFolder { get; private set; }

    public string? LogsFolder { get; private set; }

    public string? ProfileFolder { get; private set; }

    public bool? EphemeralBrowserData { get; private set; }

    public IReadOnlyList<SparFolder>? RemoveOnExit { get; private set; }

    /// <summary>Each settings record's place, by the record's name (<c>settings</c> for the first).</summary>
    public Dictionary<string, (string? File, bool? AllowNetworkFile)> Settings { get; } = new(StringComparer.Ordinal);

    /// <summary>Every key the file set, as a dotted path, for the start-up log and the locked keys.</summary>
    public List<string> Keys { get; } = [];

    /// <summary>Sets the folder choices the file made, over the code's.</summary>
    public void ApplyTo(FolderOptions folders)
    {
        if (DataFolder is not null)
        {
            folders.Data(DataFolder);
        }

        if (LogsFolder is not null)
        {
            folders.Logs(LogsFolder);
        }

        if (ProfileFolder is not null)
        {
            folders.Profile(ProfileFolder);
        }

        if (EphemeralBrowserData == true)
        {
            folders.EphemeralBrowserData();
        }

        if (RemoveOnExit is not null)
        {
            folders.RemoveOnExit([.. RemoveOnExit]);
        }
    }

    /// <summary>The installation file beside the running executable: <c>&lt;executable name&gt;.spar.json</c>.</summary>
    public static string BesideExecutable()
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("The app's executable path is unknown.");
        return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(executable)!, System.IO.Path.GetFileNameWithoutExtension(executable) + ".spar.json");
    }

    /// <summary>Whether <paramref name="key"/> names a setting the file may hold.</summary>
    public static bool IsKnownKey(string key)
    {
        var parts = key.Split('.');
        return parts switch
        {
            ["folders", var name] => FolderKeys.Contains(name),
            ["settings", var name] => SettingsKeys.Contains(name),
            ["settings", _, var name] => SettingsKeys.Contains(name),
            _ => false,
        };
    }

    /// <summary>
    /// The file at <paramref name="path"/>, or null when there is none. A file that cannot be used throws a
    /// <see cref="InvalidOperationException"/> naming the file and the key.
    /// </summary>
    public static InstallationFile? Read(string path, InstallationOptions options)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }

        var file = new InstallationFile(path);
        try
        {
            using var document = JsonDocument.Parse(bytes, DocumentOptions);
            file.ReadRoot(document.RootElement);
        }
        catch (JsonException exception)
        {
            throw file.Problem($"it is not valid JSON: {exception.Message}");
        }

        foreach (var key in file.Keys.Where(options.LockedKeys.Contains))
        {
            throw file.Problem($"it sets '{key}', which this app does not let an installation change");
        }

        return file;
    }

    private void ReadRoot(JsonElement root)
    {
        Expect(root, JsonValueKind.Object, "the file");
        foreach (var section in root.EnumerateObject())
        {
            if (section.Name == "folders")
            {
                ReadFolders(section.Value);
            }
            else if (section.Name == "settings")
            {
                ReadSettings(section.Value, "settings", SettingsOptions.DefaultName);
            }
            else if (section.Name.StartsWith("settings.", StringComparison.Ordinal) && section.Name.Length > "settings.".Length)
            {
                ReadSettings(section.Value, section.Name, section.Name["settings.".Length..]);
            }
            else
            {
                throw Problem($"'{section.Name}' is not a key of the installation file (it may hold folders, settings and settings.<name>)");
            }
        }
    }

    private void ReadFolders(JsonElement folders)
    {
        Expect(folders, JsonValueKind.Object, "folders");
        foreach (var entry in folders.EnumerateObject())
        {
            var key = "folders." + entry.Name;
            switch (entry.Name)
            {
                case "data":
                    DataFolder = AbsolutePath(entry.Value, key);
                    break;
                case "logs":
                    LogsFolder = AbsolutePath(entry.Value, key);
                    break;
                case "profile":
                    ProfileFolder = AbsolutePath(entry.Value, key);
                    break;
                case "ephemeralBrowserData":
                    EphemeralBrowserData = Boolean(entry.Value, key);
                    break;
                case "removeOnExit":
                    RemoveOnExit = Folders(entry.Value, key);
                    break;
                default:
                    throw Problem($"'{key}' is not a key of the installation file (folders may hold {string.Join(", ", FolderKeys)})");
            }

            Keys.Add(key);
        }
    }

    private void ReadSettings(JsonElement settings, string section, string name)
    {
        Expect(settings, JsonValueKind.Object, section);
        string? file = null;
        bool? network = null;
        foreach (var entry in settings.EnumerateObject())
        {
            var key = $"{section}.{entry.Name}";
            switch (entry.Name)
            {
                case "file":
                    file = AbsolutePath(entry.Value, key);
                    break;
                case "allowNetworkFile":
                    network = Boolean(entry.Value, key);
                    break;
                default:
                    throw Problem($"'{key}' is not a key of the installation file ({section} may hold {string.Join(", ", SettingsKeys)})");
            }

            Keys.Add(key);
        }

        Settings[name] = (file, network);
    }

    private string AbsolutePath(JsonElement value, string key)
    {
        Expect(value, JsonValueKind.String, key);
        var path = Environment.ExpandEnvironmentVariables(value.GetString()!);
        if (!System.IO.Path.IsPathFullyQualified(path))
        {
            throw Problem($"'{key}' must be an absolute path once its variables are expanded, not '{path}'");
        }

        return path;
    }

    private bool Boolean(JsonElement value, string key)
    {
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw Problem($"'{key}' must be true or false");
        }

        return value.GetBoolean();
    }

    private List<SparFolder> Folders(JsonElement value, string key)
    {
        Expect(value, JsonValueKind.Array, key);
        var folders = new List<SparFolder>();
        foreach (var item in value.EnumerateArray())
        {
            folders.Add(item.ValueKind == JsonValueKind.String ? item.GetString() switch
            {
                "profile" => SparFolder.Profile,
                "cache" => SparFolder.Cache,
                "data" => SparFolder.Data,
                "logs" => SparFolder.Logs,
                _ => throw Problem($"'{key}' lists '{item.GetString()}'; it may list profile, cache, data and logs"),
            } : throw Problem($"'{key}' must list folder names"));
        }

        return folders;
    }

    private void Expect(JsonElement value, JsonValueKind kind, string key)
    {
        if (value.ValueKind != kind)
        {
            throw Problem($"'{key}' must be {(kind == JsonValueKind.Object ? "an object" : kind == JsonValueKind.Array ? "a list" : "a string")}");
        }
    }

    private InvalidOperationException Problem(string reason) =>
        new($"The installation file {Path} cannot be used: {reason}.");
}
