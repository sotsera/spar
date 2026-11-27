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

/// <summary>
/// What an installation file set, parsed and checked. This part reads the file only, so <c>spar doctor</c> compiles it
/// too; applying it to the app's folders is in <c>InstallationFile.Apply.cs</c>.
/// </summary>
internal sealed partial class InstallationFile
{
    /// <summary>The first settings record's name, as <c>SettingsOptions.DefaultName</c> has it.</summary>
    private const string DefaultSettingsName = "settings";

    private static readonly string[] FolderKeys = ["data", "logs", "profile", "ephemeralBrowserData", "removeOnExit"];

    private static readonly string[] SettingsKeys = ["file", "allowNetworkFile"];

    /// <summary>The level names <c>Microsoft.Extensions.Logging.LogLevel</c> has, as <c>logging.json</c> takes them (any case).</summary>
    private static readonly string[] LevelNames = ["Trace", "Debug", "Information", "Warning", "Error", "Critical", "None"];

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

    /// <summary>The folders to remove at exit, by their names in the file: profile, cache, data, logs.</summary>
    public IReadOnlyList<string>? RemoveOnExit { get; private set; }

    /// <summary>The log file's default level the file sets, by its name; null for none.</summary>
    public string? LogLevelName { get; private set; }

    /// <summary>The category levels the file sets, in its order, by their level names.</summary>
    public List<KeyValuePair<string, string>> LogCategories { get; } = [];

    /// <summary>Each settings record's place, by the record's name (<c>settings</c> for the first).</summary>
    public Dictionary<string, (string? File, bool? AllowNetworkFile)> Settings { get; } = new(StringComparer.Ordinal);

    /// <summary>Every key the file set, as a dotted path, for the start-up log and the locked keys.</summary>
    public List<string> Keys { get; } = [];

    /// <summary>Each key with the value used, environment variables expanded, as the start-up log and <c>spar doctor</c> show it.</summary>
    public List<KeyValuePair<string, string>> Values { get; } = [];

    /// <summary>What it sets, as the start-up log and <c>spar doctor</c> show it: <c>key = value</c>, one after another.</summary>
    public string Describe() => string.Join("; ", Values.Select(value => $"{value.Key} = {value.Value}"));

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
            ["logging", "level" or "categories"] => true,
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
            else if (section.Name == "logging")
            {
                ReadLogging(section.Value);
            }
            else if (section.Name == "settings")
            {
                ReadSettings(section.Value, "settings", DefaultSettingsName);
            }
            else if (section.Name.StartsWith("settings.", StringComparison.Ordinal) && section.Name.Length > "settings.".Length)
            {
                ReadSettings(section.Value, section.Name, section.Name["settings.".Length..]);
            }
            else
            {
                throw Problem($"'{section.Name}' is not a key of the installation file (it may hold folders, logging, settings and settings.<name>)");
            }
        }
    }

    private void ReadFolders(JsonElement folders)
    {
        Expect(folders, JsonValueKind.Object, "folders");
        foreach (var entry in folders.EnumerateObject())
        {
            var key = "folders." + entry.Name;
            var shown = entry.Name switch
            {
                "data" => DataFolder = AbsolutePath(entry.Value, key),
                "logs" => LogsFolder = AbsolutePath(entry.Value, key),
                "profile" => ProfileFolder = AbsolutePath(entry.Value, key),
                "ephemeralBrowserData" => Shown(EphemeralBrowserData = Boolean(entry.Value, key)),
                "removeOnExit" => string.Join(", ", RemoveOnExit = Folders(entry.Value, key)),
                _ => throw Problem($"'{key}' is not a key of the installation file (folders may hold {string.Join(", ", FolderKeys)})"),
            };

            Keys.Add(key);
            Values.Add(new(key, shown));
        }
    }

    /// <summary>
    /// <c>logging</c>: the shape of <c>logging.json</c>, <c>{"level":"Warning","categories":{"SqlTool.Copy":"Debug"}}</c>,
    /// over the code's levels; a user's <c>logging.json</c> still wins over it.
    /// </summary>
    private void ReadLogging(JsonElement logging)
    {
        Expect(logging, JsonValueKind.Object, "logging");
        foreach (var entry in logging.EnumerateObject())
        {
            var key = "logging." + entry.Name;
            if (entry.Name == "level")
            {
                LogLevelName = Level(entry.Value, key);
                Values.Add(new(key, LogLevelName));
            }
            else if (entry.Name == "categories")
            {
                Expect(entry.Value, JsonValueKind.Object, key);
                foreach (var category in entry.Value.EnumerateObject())
                {
                    var level = Level(category.Value, $"{key}.{category.Name}");
                    LogCategories.Add(new(category.Name, level));
                    Values.Add(new($"{key}.{category.Name}", level));
                }
            }
            else
            {
                throw Problem($"'{key}' is not a key of the installation file (logging may hold level and categories)");
            }

            Keys.Add(key);
        }
    }

    private string Level(JsonElement value, string key)
    {
        Expect(value, JsonValueKind.String, key);
        var name = value.GetString()!;
        return LevelNames.FirstOrDefault(level => string.Equals(level, name, StringComparison.OrdinalIgnoreCase))
            ?? throw Problem($"'{key}' must be one of {string.Join(", ", LevelNames)}, not '{name}'");
    }

    private void ReadSettings(JsonElement settings, string section, string name)
    {
        Expect(settings, JsonValueKind.Object, section);
        string? file = null;
        bool? network = null;
        foreach (var entry in settings.EnumerateObject())
        {
            var key = $"{section}.{entry.Name}";
            var shown = entry.Name switch
            {
                "file" => file = AbsolutePath(entry.Value, key),
                "allowNetworkFile" => Shown(network = Boolean(entry.Value, key)),
                _ => throw Problem($"'{key}' is not a key of the installation file ({section} may hold {string.Join(", ", SettingsKeys)})"),
            };

            Keys.Add(key);
            Values.Add(new(key, shown));
        }

        Settings[name] = (file, network);
    }

    private static string Shown(bool? value) => value == true ? "true" : "false";

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

    private List<string> Folders(JsonElement value, string key)
    {
        Expect(value, JsonValueKind.Array, key);
        var folders = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            var name = item.ValueKind == JsonValueKind.String ? item.GetString()! : throw Problem($"'{key}' must list folder names");
            if (name is not ("profile" or "cache" or "data" or "logs"))
            {
                throw Problem($"'{key}' lists '{name}'; it may list profile, cache, data and logs");
            }

            folders.Add(name);
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
