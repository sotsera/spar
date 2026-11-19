using System.Runtime.Versioning;
using Spar.Testing;

namespace Spar.WindowTests;

/// <summary>
/// Focus and the native close dialogs with real input, on WindowHost's Production executable in the guest's user
/// session (step 8c): the first click on an inactive window reaches the page, Tab moves into the page after the window
/// is activated, the close confirmation is answered with real keys and the mouse, and "Wait / Close anyway" for a silent
/// guard likewise. The window is activated on the guest's own screen; nothing reaches the host's desktop.
/// </summary>
[Trait(SandboxLane.Trait, "UserSession")]
[SupportedOSPlatform("windows10.0.17763")]
public sealed class FocusAndDialogTests
{
    /// <summary>The page's close guards, in the list's order (tests/apps/WindowHost/frontend/index.html).</summary>
    private static readonly string[] GuardLabels =
    [
        "No guard", "Close", "Keep", "Ask", "Keep after a second", "Keep after 5 seconds, ignoring the signal",
        "Never answer", "Ask in the page", "Throw",
    ];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    public async Task The_first_click_on_an_inactive_window_reaches_the_page()
    {
        SandboxLane.SkipUnlessUserSession();
        await using var app = await SparBlackBox.LaunchAsync(Projects.WindowHost, cancellationToken: Token);
        await app.Window.GetByText("Ready", new() { Exact = true }).WaitForAsync();

        using var other = OtherWindow.Show(Beside(await app.Window.PartsAsync()));
        RealInput.Click(other.Bounds.Center.X, other.Bounds.Center.Y);
        await UntilAsync(() => Task.FromResult(other.IsForeground), "the other window takes the foreground");
        Assert.False(await app.Window.IsForegroundAsync(), "The app's window is inactive before the click.");
        await SandboxLane.EvidenceAsync(app, "first-click-before");

        await app.Window.GetByRole(SparRole.Button, new() { Name = "Echo", Exact = true }).ClickAsync();

        await app.Window.GetByText("Echo: hello").WaitForAsync(new() { Timeout = 5_000 });
        Assert.True(await app.Window.IsForegroundAsync(), "The click activated the window.");
        await SandboxLane.EvidenceAsync(app, "first-click-after");
        Assert.Equal(0, await CloseByButtonAsync(app));
    }

    [Fact(Explicit = true)]
    public async Task Tab_moves_into_the_page_after_the_window_is_activated()
    {
        SandboxLane.SkipUnlessUserSession();
        await using var app = await SparBlackBox.LaunchAsync(Projects.WindowHost, cancellationToken: Token);
        await app.Window.GetByText("Ready", new() { Exact = true }).WaitForAsync();

        using var other = OtherWindow.Show(Beside(await app.Window.PartsAsync()));
        RealInput.Click(other.Bounds.Center.X, other.Bounds.Center.Y);
        await UntilAsync(() => Task.FromResult(other.IsForeground), "the other window takes the foreground");

        // Activation by the title bar: no click reaches the page, so only the host's focus forwarding gives it the keys.
        await app.Window.ActivateAsync();
        await UntilAsync(app.Window.IsForegroundAsync, "the app's window takes the foreground");
        await app.Window.PressAsync("Tab");
        await app.Window.PressAsync("Enter");

        // The page's first focusable element is "Start a long call".
        await app.Window.GetByText("Long call: running").WaitForAsync(new() { Timeout = 5_000 });
        await SandboxLane.EvidenceAsync(app, "tab-after-activation");
    }

    [Fact(Explicit = true)]
    public async Task The_close_confirmation_is_answered_with_real_keys_and_the_mouse()
    {
        SandboxLane.SkipUnlessUserSession();
        await using var app = await SparBlackBox.LaunchAsync(Projects.WindowHost, cancellationToken: Token);
        await PickGuardAsync(app, "Ask");

        // Keep open: the dialog's default button, with the Enter key.
        await CloseButton(app).ClickAsync();
        var first = await app.WaitForDialogAsync(TimeSpan.FromSeconds(10), Token);
        Assert.Contains("Close without saving?", first.Text, StringComparison.Ordinal);
        Assert.Equal(["Close anyway", "Keep open"], first.Buttons);
        await SandboxLane.EvidenceAsync(app, "close-confirmation");
        await app.Window.PressAsync("Enter");
        await UntilAsync(async () => await app.Automation.FindDialogAsync(app.ProcessId) is null, "the dialog closes");
        await Task.Delay(500, Token);
        Assert.False(app.HasExited, "Keep open kept the app open.");

        // Close anyway: a real click on the button.
        await CloseButton(app).ClickAsync();
        var second = await app.WaitForDialogAsync(TimeSpan.FromSeconds(10), Token);
        await second.ClickAsync("Close anyway");
        Assert.Equal(0, await app.WaitForExitAsync(Token));
    }

    [Fact(Explicit = true)]
    public async Task Wait_keeps_a_silent_guards_window_and_Close_anyway_closes_it()
    {
        SandboxLane.SkipUnlessUserSession();
        await using var app = await SparBlackBox.LaunchAsync(Projects.WindowHost, cancellationToken: Token);
        await PickGuardAsync(app, "Never answer");

        // The guard never answers: after about 5 s the host asks "Wait / Close anyway". Wait, with a real click.
        await CloseButton(app).ClickAsync();
        var asked = await app.WaitForDialogAsync(TimeSpan.FromSeconds(15), Token);
        Assert.Contains("Wait", asked.Buttons);
        Assert.Contains("Close anyway", asked.Buttons);
        await SandboxLane.EvidenceAsync(app, "wait-close-anyway");
        await asked.ClickAsync("Wait");
        await UntilAsync(async () => await app.Automation.FindDialogAsync(app.ProcessId) is null, "the dialog closes");
        await Task.Delay(1_000, Token);
        Assert.False(app.HasExited, "Wait kept the window open.");
        SandboxLane.Note("silent guard: Wait kept the window");

        // The same request asks again after 4 intervals (about 20 s): Close anyway, with the keyboard this time.
        var again = await app.WaitForDialogAsync(TimeSpan.FromSeconds(40), Token);
        Assert.Contains("Close anyway", again.Buttons);
        await again.ClickAsync("Close anyway");
        Assert.Equal(0, await app.WaitForExitAsync(Token));
    }

    /// <summary>The title bar's own Close button (a native element: no ARIA role).</summary>
    internal static SparBlackBoxLocator CloseButton(SparBlackBoxApp app) =>
        app.Window.GetByRole(SparRole.Button, new() { Name = "Close", Exact = true });

    /// <summary>Closes the window with a real click on its Close button and returns the exit code.</summary>
    internal static async Task<int> CloseByButtonAsync(SparBlackBoxApp app)
    {
        await CloseButton(app).ClickAsync();
        return await app.WaitForExitAsync(Token);
    }

    /// <summary>
    /// Picks the page's close guard with real arrow keys and reads each step back (the Value pattern): the list is
    /// focused through UI Automation before every key, the window activated by its title bar first when it is not in the
    /// foreground, and the walk goes up or down by the list's known order, so a key that went elsewhere, or arrived twice,
    /// is put right instead of failing the check (run 5 of 2026-11-19 read "No guard" after its keys). The choice only sets
    /// the scene: the dialogs the check is about are answered with real keys and clicks.
    /// </summary>
    internal static async Task PickGuardAsync(SparBlackBoxApp app, string label)
    {
        var target = Array.IndexOf(GuardLabels, label);
        Assert.True(target >= 0, $"'{label}' is not one of the page's close guards.");
        await app.Window.GetByText("Ready", new() { Exact = true }).WaitForAsync();
        await SandboxLane.EvidenceAsync(app, "window-host-start");
        if (!await app.Window.IsForegroundAsync())
        {
            await app.Window.ActivateAsync();
            await UntilAsync(app.Window.IsForegroundAsync, "the app's window takes the foreground");
        }

        var guard = app.Window.GetByRole(SparRole.Combobox, new() { Name = "Close guard", Exact = true });
        var steps = new List<string>();
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var current = Array.IndexOf(GuardLabels, await guard.InputValueAsync());
            if (current == target)
            {
                SandboxLane.Note($"close guard '{label}' picked: {string.Join(", ", steps)}");
                return;
            }

            // Focus, not a click: the list sits below the window's fold, where a click has nothing to hit (run 3).
            var key = current < target ? "ArrowDown" : "ArrowUp";
            await guard.FocusAsync();
            await app.Window.PressAsync(key);
            steps.Add(key);
            var limit = DateTime.UtcNow.AddSeconds(1);
            while (Array.IndexOf(GuardLabels, await guard.InputValueAsync()) == current && DateTime.UtcNow < limit)
            {
                await Task.Delay(50, Token);
            }
        }

        Assert.Fail($"The close guard did not reach '{label}' in 20 keys ({string.Join(", ", steps)}); it reads '{await guard.InputValueAsync()}'.");
    }

    /// <summary>
    /// A place for a second window beside the app's window, never over it: a window a background process shows can stay
    /// behind the foreground window, so a click on an overlap reached the app instead (run 3 of 2026-11-19). The guest's
    /// screen is 1520 px wide and the app's window is centred in it with about 200 px on each side.
    /// </summary>
    internal static ScreenRect Beside(WindowParts app)
    {
        var top = app.Bounds.Top + 100;
        return app.Bounds.Left >= 160
            ? new ScreenRect(Math.Max(0, app.Bounds.Left - 300), top, app.Bounds.Left - 4, top + 240)
            : new ScreenRect(app.Bounds.Right + 4, top, app.Bounds.Right + 300, top + 240);
    }

    internal static async Task UntilAsync(Func<Task<bool>> condition, string what, int seconds = 5)
    {
        var limit = DateTime.UtcNow.AddSeconds(seconds);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < limit, $"Timed out after {seconds} s waiting for {what}.");
            await Task.Delay(50, Token);
        }
    }
}
