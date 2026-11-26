using System.Text.Json;

namespace Spar.Tool.Dev;

/// <summary>
/// A launch profile of <c>Properties/launchSettings.json</c>, as <c>dotnet run</c> and the IDEs read it (decision
/// "M2b's decide-first questions", item 5): only profiles that run the project (<c>"commandName": "Project"</c>), with
/// their variables, arguments and working folder. <c>spar dev</c> starts the built executable itself with them.
/// </summary>
internal sealed record LaunchProfile(string Name, IReadOnlyDictionary<string, string> Environment, IReadOnlyList<string> Arguments, string? WorkingDirectory)
{
    /// <summary>When the project has no launch settings: no variables, no arguments.</summary>
    public static readonly LaunchProfile None = new("(none)", new Dictionary<string, string>(), [], null);

    /// <summary>
    /// The profile named <paramref name="name"/>, or the first that runs the project when it is null; the project's
    /// <see cref="None"/> profile when it has no launch settings and no name was asked for. Null, with the reason, when
    /// the named profile is missing or the file cannot be read.
    /// </summary>
    public static LaunchProfile? Find(string projectDirectory, string? name, out string problem)
    {
        problem = "";
        var file = Path.Combine(projectDirectory, "Properties", "launchSettings.json");
        if (!File.Exists(file))
        {
            if (name is null)
            {
                return None;
            }

            problem = $"There is no {file}, so there is no launch profile '{name}'.";
            return null;
        }

        try
        {
            return Parse(File.ReadAllBytes(file), name, file, out problem);
        }
        catch (IOException exception)
        {
            problem = $"{file} cannot be read: {exception.Message}";
            return null;
        }
    }

    public static LaunchProfile? Parse(ReadOnlySpan<byte> json, string? name, string file, out string problem)
    {
        problem = "";
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json.ToArray(), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException exception)
        {
            problem = $"{file} is not valid JSON: {exception.Message}";
            return null;
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("profiles", out var profiles) || profiles.ValueKind != JsonValueKind.Object)
            {
                problem = $"{file} has no profiles.";
                return name is null ? None : null;
            }

            var runnable = new List<string>();
            foreach (var profile in profiles.EnumerateObject())
            {
                if (profile.Value.ValueKind != JsonValueKind.Object || Text(profile.Value, "commandName") != "Project")
                {
                    continue;
                }

                runnable.Add(profile.Name);
                if (name is null || string.Equals(profile.Name, name, StringComparison.Ordinal))
                {
                    return Read(profile.Name, profile.Value);
                }
            }

            problem = name is null
                ? $"{file} has no profile that runs the project (\"commandName\": \"Project\")."
                : $"{file} has no profile '{name}' that runs the project; it has: {(runnable.Count == 0 ? "none" : string.Join(", ", runnable))}.";
            return null;
        }
    }

    /// <summary>The arguments of <c>commandLineArgs</c>, split as Windows and the shells split a simple command line.</summary>
    internal static IReadOnlyList<string> SplitArguments(string? text)
    {
        var arguments = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return arguments;
        }

        var current = new System.Text.StringBuilder();
        var quoted = false;
        var any = false;
        foreach (var character in text)
        {
            if (character == '"')
            {
                quoted = !quoted;
                any = true;
            }
            else if (char.IsWhiteSpace(character) && !quoted)
            {
                if (any)
                {
                    arguments.Add(current.ToString());
                    current.Clear();
                    any = false;
                }
            }
            else
            {
                current.Append(character);
                any = true;
            }
        }

        if (any)
        {
            arguments.Add(current.ToString());
        }

        return arguments;
    }

    private static LaunchProfile Read(string name, JsonElement profile)
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        if (profile.TryGetProperty("environmentVariables", out var variables) && variables.ValueKind == JsonValueKind.Object)
        {
            foreach (var variable in variables.EnumerateObject())
            {
                environment[variable.Name] = variable.Value.ValueKind == JsonValueKind.String ? variable.Value.GetString() ?? "" : variable.Value.GetRawText();
            }
        }

        return new LaunchProfile(name, environment, SplitArguments(Text(profile, "commandLineArgs")), Text(profile, "workingDirectory"));
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
