using System.Text.Json.Nodes;

namespace Spar;

/// <summary>
/// How one settings record is kept (decision "M3b's decide-first questions", item 4): its name, which names its file,
/// where that file is, its schema version and the steps that bring an older file up to it.
/// </summary>
public sealed class SettingsOptions
{
    /// <summary>The name of the app's first settings record, and of its file, <c>settings.json</c>.</summary>
    internal const string DefaultName = "settings";

    internal string SettingsName { get; private set; } = DefaultName;

    internal string? FilePath { get; private set; }

    internal bool NetworkAllowed { get; private set; }

    internal int CurrentVersion { get; private set; } = 1;

    internal Dictionary<int, Func<JsonObject, JsonObject>> Migrations { get; } = [];

    /// <summary>
    /// The record's name, which names its file (<c>&lt;name&gt;.json</c>); every record after the first takes one. Lower
    /// case letters, digits and hyphens.
    /// </summary>
    public SettingsOptions Name(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (!name.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-'))
        {
            throw new ArgumentException($"A settings name is lower case letters, digits and hyphens: '{name}' is not.", nameof(name));
        }

        SettingsName = name;
        return this;
    }

    /// <summary>
    /// Keeps this record's file at <paramref name="path"/>, an absolute path, instead of the app's data folder. A network
    /// path is refused unless <see cref="AllowNetworkFile"/>. A Development build adds <c>.Development</c> before the
    /// extension, so a debug run never writes the installed app's file; a test keeps it under its own root.
    /// </summary>
    public SettingsOptions File(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException($"A settings file needs an absolute path: '{path}' is relative.", nameof(path));
        }

        FilePath = path;
        return this;
    }

    /// <summary>
    /// Allows the file on a network share, for settings that follow a user between machines. What changes: whoever can
    /// read the share can read the file; the share may be unreachable, and the app then starts on the last copy Spar
    /// keeps in the local data folder, reports <see cref="SparSettingsSource.LocalCopy"/>, and fails updates until it is
    /// back; another machine may write the file, so each update reads it again just before writing and, when it changed,
    /// applies the change to the newer settings. Never put secrets in settings.
    /// </summary>
    public SettingsOptions AllowNetworkFile()
    {
        NetworkAllowed = true;
        return this;
    }

    /// <summary>The schema version this build writes, 1 unless set; an older file goes through <see cref="Migrate"/> steps.</summary>
    public SettingsOptions Version(int version)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        CurrentVersion = version;
        return this;
    }

    /// <summary>
    /// Rewrites the settings of a file at version <paramref name="from"/> into version <paramref name="from"/> + 1, as
    /// JSON, before the record is read: rename a property, change a value's shape. A missing step, or one that throws,
    /// makes the file unreadable.
    /// </summary>
    public SettingsOptions Migrate(int from, Func<JsonObject, JsonObject> step)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(from, 1);
        ArgumentNullException.ThrowIfNull(step);
        Migrations[from] = step;
        return this;
    }
}
