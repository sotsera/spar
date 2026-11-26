using System.ComponentModel;

namespace Spar.Testing;

/// <summary>
/// The scenes of a test project (decision "M2c's decide-first questions", items 1 and 2), registered by the code Spar's
/// generator writes for each <see cref="SparSceneAttribute"/> method, with no reflection, and each also a generated
/// window test that runs the scene and its capture. <c>spar snap</c> lists them by starting the test executable with
/// <see cref="ListArgument"/>, answered by the generated module initializer before the test framework starts; it
/// captures one by running the executable's own xUnit runner on that scene's test (<c>-trait "SparScene=name"</c>) with
/// <see cref="SceneVariable"/> and <see cref="OutVariable"/> set, so the test writes the capture's files. A scene cannot
/// run inside the module initializer: until it returns, no other thread may touch the test assembly's types.
/// </summary>
public static class SparScenes
{
    /// <summary>The argument that lists the scenes, one name a line, and ends the process.</summary>
    public const string ListArgument = "--spar-scene-list";

    /// <summary>The variable naming the scene whose capture is written (set by <c>spar snap</c>).</summary>
    public const string SceneVariable = "SPAR_SNAP_SCENE";

    /// <summary>The variable naming the folder the capture is written into (set by <c>spar snap</c>).</summary>
    public const string OutVariable = "SPAR_SNAP_OUT";

    private static readonly Lock Gate = new();
    private static readonly SortedDictionary<string, Func<SparScene, Task>> Scenes = new(StringComparer.Ordinal);

    /// <summary>The registered scenes' names, in order.</summary>
    public static IReadOnlyList<string> Names
    {
        get
        {
            lock (Gate)
            {
                return [.. Scenes.Keys];
            }
        }
    }

    /// <summary>For generated code: adds the scene <paramref name="name"/>, run by <paramref name="run"/>.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void Register(string name, Func<SparScene, Task> run)
    {
        lock (Gate)
        {
            if (!Scenes.TryAdd(name, run))
            {
                throw new InvalidOperationException($"Two scenes are named '{name}'.");
            }
        }
    }

    /// <summary>
    /// For generated code, in the module initializer: when the process was started with <see cref="ListArgument"/>,
    /// writes the scenes' names and ends the process (exit code 0); otherwise returns at once.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void RunIfAsked()
    {
        if (!Environment.GetCommandLineArgs().Contains(ListArgument, StringComparer.Ordinal))
        {
            return;
        }

        foreach (var name in Names)
        {
            Console.Out.WriteLine(name);
        }

        Console.Out.Flush();
        Environment.Exit(0);
    }

    /// <summary>
    /// For the generated test of a scene: runs it and its capture, and writes the capture's files when <c>spar snap</c>
    /// named this scene and a folder; a failure fails the test.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static async Task RunAsTestAsync(string name, CancellationToken cancellationToken)
    {
        var capture = await CaptureAsync(name, cancellationToken);
        if (Environment.GetEnvironmentVariable(SceneVariable) == name && Environment.GetEnvironmentVariable(OutVariable) is { Length: > 0 } folder)
        {
            await SceneCapture.WriteAsync(capture, folder);
        }
    }

    /// <summary>Runs the scene <paramref name="name"/> and returns its capture (Spar's own tests compare two of them).</summary>
    internal static async Task<SceneCaptureResult> CaptureAsync(string name, CancellationToken cancellationToken)
    {
        var run = Find(name) ?? throw new ArgumentException($"There is no scene '{name}'; the scenes: {Listed()}.", nameof(name));
        await using var scene = new SparScene(name, cancellationToken);
        await run(scene);
        return await SceneCapture.CaptureAsync(scene, cancellationToken);
    }

    private static Func<SparScene, Task>? Find(string name)
    {
        lock (Gate)
        {
            return Scenes.GetValueOrDefault(name);
        }
    }

    private static string Listed() => Names.Count == 0 ? "none" : string.Join(", ", Names);
}
