using System.Diagnostics;

namespace Spar.Testing;

/// <summary>
/// Real input on a launched app's window (step 8c): internal, for Spar's own checks in Windows Sandbox, until M3's
/// app-facing layer makes it public. Every action refuses outside the user session of a guest Spar's runner started
/// (<see cref="SandboxGuest.EnsureUserSession"/>) before it reads or sends anything, so it never reaches a developer's
/// desktop. The app runs with <see cref="SparBlackBoxPlacement.Activated"/> there, on the guest's input desktop.
/// </summary>
public sealed partial class SparBlackBoxWindow
{
    /// <summary>Activates the window as a user does, with a real click on its title bar (away from its buttons).</summary>
    internal async Task ActivateAsync()
    {
        var parts = await PartsAsync();
        if (parts.TitleBar is not { IsEmpty: false } titleBar)
        {
            throw new InvalidOperationException($"The app's window has no title bar to click ({parts.Bounds}).");
        }

        if (!BlackBoxPlatform.IsWindows)
        {
            throw BlackBoxPlatform.NoRealInput();
        }

        RealInput.Click(titleBar.Left + Math.Min(120, titleBar.Width / 4), titleBar.Center.Y);
    }

    /// <summary>Presses a key or a combination (<c>"F12"</c>, <c>"Control+Shift+I"</c>) into whatever has the keyboard focus.</summary>
    internal Task PressAsync(string combination)
    {
        SandboxGuest.EnsureUserSession("send real input");
        if (!BlackBoxPlatform.IsWindows)
        {
            throw BlackBoxPlatform.NoRealInput();
        }

        RealInput.Press(combination);
        return Task.CompletedTask;
    }

    /// <summary>Whether the main window is the foreground window now.</summary>
    internal async Task<bool> IsForegroundAsync() => (await PartsAsync()).IsForeground;

    /// <summary>The main window's handle, rectangle and title bar.</summary>
    internal async Task<WindowParts> PartsAsync()
    {
        SandboxGuest.EnsureUserSession("read the window for real input");
        return await App.Automation.WindowPartsAsync(App.ProcessId)
            ?? throw new InvalidOperationException($"The app (process {App.ProcessId}) has no window.");
    }

    /// <summary>Every visible top-level window of the app and the processes it started (the browser's popups and windows).</summary>
    internal Task<IReadOnlyList<TopLevelWindow>> TopLevelWindowsAsync() => App.Automation.TopLevelWindowsAsync(App.ProcessIds);

    /// <summary>A text dump of the app's windows and their elements, for the evidence.</summary>
    internal Task<string> DumpAsync() => App.Automation.DumpAsync(App.ProcessIds);
}

/// <summary>Real input on one element, found as the locator's other actions find it (strict, waited for).</summary>
public sealed partial class SparBlackBoxLocator
{
    /// <summary>Clicks the middle of the element with a real mouse, once there is exactly one, enabled and on screen.</summary>
    internal Task ClickAsync(RealMouseButton button = RealMouseButton.Left, SparActionOptions? options = null)
    {
        SandboxGuest.EnsureUserSession("send real input");
        return RetryAsync(Limit(options?.Timeout), "click", async () =>
        {
            var outcome = await App.Automation.BoundsAsync(App.ProcessId, _target);
            if (outcome is not { Status: ElementStatus.Done, Bounds: { } bounds })
            {
                return Fail<bool>(outcome);
            }

            if (!BlackBoxPlatform.IsWindows)
            {
                throw BlackBoxPlatform.NoRealInput();
            }

            var (x, y) = bounds.Center;
            RealInput.Click(x, y, button);
            return true;
        });
    }

    /// <summary>The element's rectangle on the screen, once there is exactly one, enabled and on screen.</summary>
    internal Task<ScreenRect> BoundsAsync(SparActionOptions? options = null) =>
        RetryAsync(Limit(options?.Timeout), "read the rectangle of", async () =>
        {
            var outcome = await App.Automation.BoundsAsync(App.ProcessId, _target);
            return outcome is { Status: ElementStatus.Done, Bounds: { } bounds } ? bounds : Fail<ScreenRect>(outcome);
        });

    /// <summary>Gives the element the keyboard focus through UI Automation (no input), then presses the key or combination.</summary>
    internal async Task PressAsync(string combination, SparActionOptions? options = null)
    {
        await FocusAsync(options);
        if (!BlackBoxPlatform.IsWindows)
        {
            throw BlackBoxPlatform.NoRealInput();
        }

        RealInput.Press(combination);
    }

    /// <summary>Gives the element the keyboard focus through UI Automation (no input), then types the text as real key presses.</summary>
    internal async Task TypeAsync(string text, SparActionOptions? options = null)
    {
        await FocusAsync(options);
        if (!BlackBoxPlatform.IsWindows)
        {
            throw BlackBoxPlatform.NoRealInput();
        }

        RealInput.Type(text);
    }

    /// <summary>Gives the element the keyboard focus through UI Automation, once there is exactly one and it is enabled.</summary>
    internal async Task FocusAsync(SparActionOptions? options = null)
    {
        SandboxGuest.EnsureUserSession("send real input");

        // A focus reaches the page through the browser's accessibility tree, as an invoke does: it waits as an invoke does.
        var limit = Limit(options?.Timeout);
        var clock = Stopwatch.StartNew();
        await App.WaitForSettledPageAsync(limit, $"focus {this}");
        await RetryAsync(Remaining(limit, clock), "focus", async () =>
        {
            var outcome = await App.Automation.FocusAsync(App.ProcessId, _target);
            return outcome.Status == ElementStatus.Done || Fail<bool>(outcome);
        });
    }
}
