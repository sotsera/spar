namespace Spar;

/// <summary>What <c>Run</c> does outside a test host: per-user folders, file log, the platform's window.</summary>
internal static class SparProductionHost
{
    internal static int Run(SparBuilder builder)
    {
        // A black-box test of the exact Production binary still needs its data somewhere disposable.
        var dataRoot = ArgumentValue(builder.Args, "--spar-data-root")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SparTestHostSample");
        var folders = SparFolders.Under(Path.GetFullPath(dataRoot), builder.FolderOptions);
        var log = new FileSparLog(folders.Logs);
        SparApp.AddSparServices(builder.Services, folders, log);

        var runtime = SparRuntime.Build(builder, builder.Services, folders, log);
        try
        {
            if (OperatingSystem.IsWindowsVersionAtLeast(10))
            {
                return Windows.WindowsHost.RunUntilClosed(runtime);
            }

            if (OperatingSystem.IsLinux())
            {
                return Linux.LinuxHost.RunUntilClosed(runtime);
            }

            return 2;
        }
        finally
        {
            runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static string? ArgumentValue(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}

/// <summary>A window a test opened in its own process, driven through the page's script.</summary>
internal interface ISparTestWindow : IAsyncDisposable
{
    /// <summary>Process ids of the web engine processes this window started (all of them gone after dispose).</summary>
    IReadOnlyList<int> EngineProcessIds { get; }

    /// <summary>Runs the body of an async function in the page and returns its result as JSON.</summary>
    Task<string> EvaluateAsync(string asyncFunctionBody, CancellationToken cancellationToken);
}

internal static class SparTestWindows
{
    public static Task<ISparTestWindow> OpenAsync(SparRuntime runtime, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10))
        {
            return Windows.WebViewWindow.OpenForTestAsync(runtime, cancellationToken);
        }

        if (OperatingSystem.IsLinux())
        {
            return Linux.WebKitWindow.OpenForTestAsync(runtime, cancellationToken);
        }

        throw new PlatformNotSupportedException();
    }
}
