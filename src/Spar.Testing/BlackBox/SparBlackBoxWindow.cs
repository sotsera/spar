using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Spar.Testing;

/// <summary>
/// A launched app's main window through the accessibility API (<c>app.Window</c>; UI Automation on Windows, AT-SPI on
/// Linux): its title, and locators named as in the window tests' page API (<c>GetByRole</c>, <c>GetByText</c>), resolved
/// against the window's accessibility tree, the page's elements included, which the browser exposes with their ARIA
/// roles and accessible names. Actions go through
/// UI Automation's patterns (<see cref="SparBlackBoxLocator.InvokeAsync"/>), never through input: nothing reaches the
/// user's mouse or keyboard. Every call reads the window as it is now.
/// </summary>
public sealed partial class SparBlackBoxWindow
{
    /// <summary>How long an action or a wait takes at most, in milliseconds, unless its options say otherwise.</summary>
    public float DefaultTimeout { get; set; } = 10_000;

    internal SparBlackBoxApp App { get; }

    internal SparBlackBoxWindow(SparBlackBoxApp app)
    {
        App = app;
    }

    /// <summary>The window's title (its UI Automation name).</summary>
    public async Task<string> TitleAsync() =>
        await App.Automation.TitleAsync(App.ProcessId) ?? throw new InvalidOperationException($"The app (process {App.ProcessId}) has no window.");

    /// <summary>
    /// Elements with <paramref name="role"/> (a page element by its ARIA role, a native one by its control type) and the
    /// accessible name in the options: a case-insensitive substring, or the whole name with <c>Exact</c>, or
    /// <c>NameRegex</c>; and <c>Disabled</c>. The other options are not read through UI Automation.
    /// </summary>
    public SparBlackBoxLocator GetByRole(SparRole role, SparGetByRoleOptions? options = null) =>
        new(this, BlackBoxQuery.ByRole(role, options), null);

    /// <summary>Text elements whose name contains <paramref name="text"/> (case-insensitive), or equals it with <c>Exact</c>.</summary>
    public SparBlackBoxLocator GetByText(string text, SparGetByTextOptions? options = null) =>
        new(this, BlackBoxQuery.ByText(text, options), null);
}

/// <summary>Elements a <see cref="SparBlackBoxWindow"/> query finds, read again at every call.</summary>
public sealed partial class SparBlackBoxLocator
{
    private readonly SparBlackBoxWindow _window;
    private readonly ElementTarget _target;

    /// <summary>The first element found, so an action takes it even when there are several.</summary>
    public SparBlackBoxLocator First => new(_window, _target.Query, 0);

    /// <summary>The last element found.</summary>
    public SparBlackBoxLocator Last => new(_window, _target.Query, -1);

    private SparBlackBoxApp App => _window.App;

    internal SparBlackBoxLocator(SparBlackBoxWindow window, BlackBoxQuery query, int? index)
    {
        _window = window;
        _target = new ElementTarget(query, index);
    }

    /// <summary>The element at <paramref name="index"/> (from 0) in the order UI Automation lists them.</summary>
    public SparBlackBoxLocator Nth(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return new(_window, _target.Query, index);
    }

    /// <summary>How many elements match now.</summary>
    public async Task<int> CountAsync() => (await FindAsync()).Count;

    /// <summary>Whether a matching element exists now and is not off screen.</summary>
    public async Task<bool> IsVisibleAsync() => await FindAsync() is [var first, ..] && !first.IsOffscreen;

    /// <summary>The accessible names of every matching element, in order.</summary>
    public async Task<IReadOnlyList<string>> AllNamesAsync() => [.. (await FindAsync()).Select(element => element.Name)];

    /// <summary>The element's accessible name, once there is exactly one.</summary>
    public async Task<string> NameAsync(SparActionOptions? options = null)
    {
        var limit = Limit(options?.Timeout);
        return await RetryAsync(limit, "read the name of", async () =>
        {
            var found = await FindAsync();
            var status = found.Count == 0 ? ElementStatus.NotFound : ElementStatus.Ambiguous;
            return found.Count == 1 ? found[0].Name : Fail<string>(new ElementOutcome(status, Names: [.. found.Select(f => f.Name)]));
        });
    }

    /// <summary>The element's value (UI Automation's Value pattern: a text field's text), once there is exactly one.</summary>
    public Task<string> InputValueAsync(SparActionOptions? options = null) =>
        RetryAsync(Limit(options?.Timeout), "read the value of", async () =>
        {
            var outcome = await App.Automation.ValueAsync(App.ProcessId, _target);
            return outcome.Status == ElementStatus.Done ? outcome.Value! : Fail<string>(outcome);
        });

    /// <summary>
    /// Invokes the element through the accessibility API's default action (UI Automation's Invoke pattern, AT-SPI's
    /// first action: a button's press), once there is exactly one and it is enabled. It is no input: no pointer moves and
    /// no key is sent. An app's first action waits until its page has settled in the accessibility tree. Returns when the
    /// page has taken the press: on Windows once the browser confirms it (UI Automation's Invoked event, or the element
    /// gone from the tree when the press removed it). A press the browser does not confirm within the timeout fails,
    /// and is never pressed again, since it may still reach the app.
    /// </summary>
    public async Task InvokeAsync(SparActionOptions? options = null)
    {
        var limit = Limit(options?.Timeout);
        var clock = Stopwatch.StartNew();
        await App.WaitForSettledPageAsync(limit, $"invoke {this}");
        var confirmation = await RetryAsync(Remaining(limit, clock), "invoke", async () =>
        {
            var outcome = await App.Automation.InvokeAsync(App.ProcessId, _target);
            return outcome.Status == ElementStatus.Done ? outcome.Confirmation : Fail<IInvokeConfirmation?>(outcome);
        });
        if (confirmation is null)
        {
            return;
        }

        await using (confirmation)
        {
            await ConfirmAsync(confirmation, limit);
        }
    }

    /// <summary>Waits until a matching element is visible (by default), attached, hidden or detached.</summary>
    public Task WaitForAsync(SparWaitForOptions? options = null)
    {
        var state = options?.State ?? SparWaitForState.Visible;
        var name = state.ToString().ToLowerInvariant();
        return RetryAsync(Limit(options?.Timeout), $"wait for {name}:", async () =>
        {
            var found = await FindAsync();
            var met = state switch
            {
                SparWaitForState.Attached => found.Count > 0,
                SparWaitForState.Detached => found.Count == 0,
                SparWaitForState.Visible => found is [var first, ..] && !first.IsOffscreen,
                _ => found is [] || found[0].IsOffscreen,
            };
            return met ? true : throw new BlackBoxNotYetException($"it is not {name} yet");
        });
    }

    public override string ToString() => _target.Index switch
    {
        null => _target.Query.Description,
        -1 => _target.Query.Description + ".Last",
        { } index => $"{_target.Query.Description}.Nth({index})",
    };

    /// <summary>
    /// Waits until the press is confirmed: the browser's Invoked event, or the element gone from the tree (the press
    /// removed it: no event comes then), or the app gone (a Quit button). Fails after <paramref name="limit"/>.
    /// </summary>
    private async Task ConfirmAsync(IInvokeConfirmation confirmation, TimeSpan limit)
    {
        var clock = Stopwatch.StartNew();
        while (!confirmation.Invoked.IsCompleted && !App.HasExited)
        {
            if (await confirmation.IsGoneAsync())
            {
                return;
            }

            if (clock.Elapsed >= limit)
            {
                throw new TimeoutException(
                    $"{this} was invoked, but the browser did not confirm within {limit.TotalMilliseconds:0} ms that the page " +
                    "took the press (no Invoked event, and the element is still there). It was not pressed again.");
            }

            await Task.WhenAny(confirmation.Invoked, App.Process.Exited, Task.Delay(50, CancellationToken.None));
        }
    }

    private Task<IReadOnlyList<FoundElement>> FindAsync() => App.Automation.FindAsync(App.ProcessId, _target);

    private TimeSpan Limit(float? timeout) => TimeSpan.FromMilliseconds(timeout ?? _window.DefaultTimeout);

    /// <summary>What is left of <paramref name="limit"/>; with nothing left, the retry still makes its one attempt.</summary>
    private static TimeSpan Remaining(TimeSpan limit, Stopwatch clock) => limit > clock.Elapsed ? limit - clock.Elapsed : TimeSpan.Zero;

    /// <summary>Not found, disabled: not yet. Several, or no such pattern: a mistake in the test, at once (strict, as the page API).</summary>
    private T Fail<T>(ElementOutcome outcome) => outcome.Status switch
    {
        ElementStatus.NotFound => throw new BlackBoxNotYetException("it was not found"),
        ElementStatus.Disabled => throw new BlackBoxNotYetException("it is disabled"),
        ElementStatus.Ambiguous => throw new InvalidOperationException(
            $"{this} found {outcome.Names!.Count} elements ({string.Join(", ", outcome.Names.Take(5).Select(n => $"'{n}'"))}): " +
            "use First, Last or Nth, or a narrower name."),
        _ => throw new InvalidOperationException($"{this} has no {outcome.Value}."),
    };

    private async Task<T> RetryAsync<T>(TimeSpan limit, string action, Func<Task<T>> attempt)
    {
        var started = DateTime.UtcNow;
        var last = "";
        while (true)
        {
            try
            {
                return await attempt();
            }
            catch (BlackBoxNotYetException notYet)
            {
                last = notYet.Message;
            }
            catch (COMException exception)
            {
                // The element went away between finding and reading it: look again.
                last = exception.Message;
            }
            catch (DBusException exception) when (exception.IsGone)
            {
                // The same on Linux: its node went between two AT-SPI calls.
                last = exception.Message;
            }

            if (App.HasExited)
            {
                throw new InvalidOperationException($"Could not {action} {this}: the app exited with {App.ExitCode}.");
            }

            if (DateTime.UtcNow - started >= limit)
            {
                throw new TimeoutException($"Could not {action} {this} within {limit.TotalMilliseconds:0} ms: {last}.");
            }

            await Task.Delay(50, CancellationToken.None);
        }
    }
}

/// <summary>Not there yet, or not ready yet: the locator looks again until its deadline.</summary>
internal sealed class BlackBoxNotYetException(string message) : Exception(message);
