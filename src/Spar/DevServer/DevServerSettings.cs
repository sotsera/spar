using System.Diagnostics.CodeAnalysis;

namespace Spar.DevServer;

/// <summary>
/// Where a Debug build's page comes from (plan section 4; decision "M1d step 1's shape: Debug through the app's own
/// origin"): the Vite development server, which the app starts with the frontend's own <c>npm run dev</c>, or reuses, or,
/// with <see cref="UrlVariable"/> in the environment or <c>SparDevServerUrl</c> in the project, one it did not start.
/// Spar.Sdk turns the feature switch on only in a Development build that has a frontend and no
/// <c>SparDevServer=false</c>, and off, with <c>Trim="true"</c>, everywhere else, so no other build carries any of this
/// folder's code. The folders and the address the build knew reach the run as the runtime configuration's values.
/// </summary>
/// <param name="FrontendDirectory">The frontend's folder (<c>SparFrontendPath</c>), where <c>npm run dev</c> runs.</param>
/// <param name="StateFile">
/// <c>obj/spar-dev-server.json</c>: the address the Spar Vite plugin records once the server listens, so a later run of
/// the app finds and reuses it.
/// </param>
/// <param name="OutputFile"><c>obj/spar-dev-server.log</c>: the server's output, which every run that uses it reads.</param>
/// <param name="Url">The address of a server Spar does not start, when one was given; null otherwise.</param>
/// <param name="UrlSource">Where <paramref name="Url"/> came from, for messages.</param>
internal sealed record DevServerSettings(string FrontendDirectory, string StateFile, string OutputFile, string? Url, string? UrlSource)
{
    /// <summary>The variable that names a development server Spar did not start (an orchestrator, a developer's own Vite).</summary>
    public const string UrlVariable = "SPAR_DEV_SERVER_URL";

    private const string Switch = "Spar.DevServer.IsSupported";

    /// <summary>Whether this build serves its page from the development server.</summary>
    [FeatureSwitchDefinition(Switch)]
    internal static bool IsSupported { get; } = AppContext.TryGetSwitch(Switch, out var on) && on;

    /// <summary>
    /// The build's settings and the environment's address; null when the build gave no frontend folder (a catalog built
    /// without Spar's build steps).
    /// </summary>
    public static DevServerSettings? Read() =>
        Read(AppContext.GetData, Environment.GetEnvironmentVariable(UrlVariable));

    /// <summary>The settings from <paramref name="data"/> (the runtime configuration's values) and the environment's address.</summary>
    internal static DevServerSettings? Read(Func<string, object?> data, string? environmentUrl)
    {
        if (data("Spar.DevServer.Frontend") is not string { Length: > 0 } frontend
            || data("Spar.DevServer.Intermediate") is not string { Length: > 0 } intermediate)
        {
            return null;
        }

        var stateFile = Path.Combine(intermediate, "spar-dev-server.json");
        var outputFile = Path.Combine(intermediate, "spar-dev-server.log");
        if (environmentUrl is { Length: > 0 })
        {
            return new(frontend, stateFile, outputFile, environmentUrl, UrlVariable);
        }

        var built = data("Spar.DevServer.Url") as string;
        return new(frontend, stateFile, outputFile, built is { Length: > 0 } ? built : null, built is { Length: > 0 } ? "SparDevServerUrl" : null);
    }
}
