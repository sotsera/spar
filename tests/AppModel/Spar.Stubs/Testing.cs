// Stand-ins for the test host's Window surface, page API and close request that docs/app-model.md 6.12 uses (Spar.Testing).
namespace Spar.Testing;

public enum SparTestSurface
{
    None,
    Browser,
    Window,
}

public sealed class SparTestOptions
{
    public SparTestSurface Surface { get; set; }
}

public sealed class SparTestApp : IAsyncDisposable
{
    public SparPage Page { get; } = new();

    public SparWindow Window { get; } = new();

    public IReadOnlyList<string> Problems { get; } = [];

    public static Task<SparTestApp> StartAsync<TApp>(Action<SparTestOptions>? configure = null, CancellationToken cancellationToken = default)
    {
        configure?.Invoke(new SparTestOptions());
        _ = cancellationToken;
        return Task.FromResult(new SparTestApp());
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public enum SparRole
{
    Alertdialog,
    Button,
    List,
    Listitem,
    Status,
    Textbox,
}

public sealed class SparGetByRoleOptions
{
    public string? Name { get; set; }
}

public sealed class SparLocatorFilterOptions
{
    public string? HasText { get; set; }
}

public sealed class SparPage
{
    public IReadOnlyList<string> PageErrors { get; } = [];

    public SparLocator GetByRole(SparRole role, SparGetByRoleOptions? options = null) => new();
}

public sealed class SparLocator
{
    public SparLocator GetByRole(SparRole role, SparGetByRoleOptions? options = null) => this;

    public SparLocator Filter(SparLocatorFilterOptions options) => this;

    public Task ClickAsync() => Task.CompletedTask;

    public Task FillAsync(string value) => Task.CompletedTask;
}

public static class SparAssertions
{
    public static SparLocatorAssertions Expect(SparLocator locator) => new();
}

public sealed class SparLocatorAssertions
{
    public Task ToHaveCountAsync(int count) => Task.CompletedTask;

    public Task ToHaveTextAsync(string expected) => Task.CompletedTask;

    public Task ToHaveValueAsync(string expected) => Task.CompletedTask;
}

public enum CloseOutcome
{
    Closed,
    Kept,
}

public enum CloseAnswer
{
    CloseAnyway,
    KeepOpen,
}

public sealed class SparCloseOptions
{
    public bool Automated { get; set; } = true;

    public CloseAnswer? Answer { get; set; }

    public CloseReason Reason { get; set; } = CloseReason.User;

    public float? Timeout { get; set; }
}

public sealed class SparCloseResult
{
    public CloseOutcome Outcome { get; init; }

    public string? Question { get; init; }

    public bool PageDidNotAnswer { get; init; }
}

public sealed class SparWindow
{
    public Task<SparCloseResult> CloseAsync(SparCloseOptions? options = null) =>
        Task.FromResult(new SparCloseResult { Outcome = options?.Automated == false ? CloseOutcome.Kept : CloseOutcome.Closed });
}
