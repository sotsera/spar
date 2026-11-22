using System.Text.Json;

namespace Spar.Tool.Project;

/// <summary>
/// The <c>global.json</c> that applies to a folder (the nearest one at or above it, as the .NET host looks for it): the
/// .NET SDK it asks for and the <c>Spar.Sdk</c> version in <c>msbuild-sdks</c>.
/// </summary>
internal sealed record GlobalJson(string File, string? SdkVersion, string? RollForward, string? SparSdkVersion, string? Problem)
{
    public string Directory => Path.GetDirectoryName(File)!;

    public static GlobalJson? Find(string folder)
    {
        for (var directory = new DirectoryInfo(folder); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "global.json");
            if (System.IO.File.Exists(path))
            {
                return Read(path);
            }
        }

        return null;
    }

    public static GlobalJson Read(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(System.IO.File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = document.RootElement;
            string? version = null;
            string? rollForward = null;
            string? spar = null;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("sdk", out var sdk) && sdk.ValueKind == JsonValueKind.Object)
            {
                version = Text(sdk, "version");
                rollForward = Text(sdk, "rollForward");
            }

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("msbuild-sdks", out var sdks) && sdks.ValueKind == JsonValueKind.Object)
            {
                spar = Text(sdks, "Spar.Sdk");
            }

            return new GlobalJson(path, version, rollForward, spar, null);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return new GlobalJson(path, null, null, null, exception.Message);
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
