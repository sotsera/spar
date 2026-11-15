using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Spar;

/// <summary>The app's one composition: <c>var app = SparApplication.Create(args, spar => …); return app.Run();</c></summary>
public static class SparApplication
{
    public static SparApp Create(string[] args, Action<SparBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new SparBuilder(args);
        configure(builder);
        if (builder.Catalog is null)
        {
            throw new InvalidOperationException("No contracts were registered (the generated registration is missing).");
        }

        // A test host is present only in non-Production builds; the feature switch trims this branch away otherwise.
        var session = SparTestSession.IsSupported ? SparTestSession.Current : null;
        return new SparApp(builder, session);
    }
}

/// <summary>What <c>Program.cs</c> configures.</summary>
public sealed class SparBuilder
{
    internal SparBuilder(string[] args)
    {
        Args = args;
    }

    public string[] Args { get; }

    public IServiceCollection Services { get; } = new ServiceCollection();

    internal WindowOptions WindowOptions { get; } = new();

    internal FolderOptions FolderOptions { get; } = new();

    internal SparContractCatalog? Catalog { get; private set; }

    public SparBuilder Window(Action<WindowOptions> configure)
    {
        configure(WindowOptions);
        return this;
    }

    public SparBuilder Folders(Action<FolderOptions> configure)
    {
        configure(FolderOptions);
        return this;
    }

    /// <summary>Stand-in for the registration the contract generator emits.</summary>
    public SparBuilder Contracts(SparContractCatalog catalog)
    {
        Catalog = catalog;
        return this;
    }
}

public sealed class WindowOptions
{
    public string Title { get; private set; } = "Spar app";

    public int Width { get; private set; } = 1000;

    public int Height { get; private set; } = 700;

    public WindowOptions Titled(string title)
    {
        Title = title;
        return this;
    }

    public WindowOptions Size(int width, int height)
    {
        (Width, Height) = (width, height);
        return this;
    }
}

/// <summary>The built app. <see cref="Run"/> opens the window and returns the exit code when it closes.</summary>
public sealed class SparApp
{
    private readonly SparBuilder _builder;
    private readonly SparTestSession? _session;

    internal SparApp(SparBuilder builder, SparTestSession? session)
    {
        _builder = builder;
        _session = session;
    }

    public int Run()
    {
        if (SparTestSession.IsSupported && _session is not null)
        {
            return _session.Run(_builder);
        }

        return SparProductionHost.Run(_builder);
    }

    /// <summary>Spar's own services, added after the app's, so an app registration (or a test host's fake) wins.</summary>
    internal static void AddSparServices(IServiceCollection services, SparFolders folders, ISparLog log)
    {
        services.TryAddSingleton(folders);
        services.TryAddSingleton(log);
        services.TryAddSingleton<IClipboard, SystemClipboard>();
    }
}
