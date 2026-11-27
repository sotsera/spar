using System.Diagnostics;
using System.Runtime.Versioning;
using Spar.Files;
using Spar.Hosting;
using Spar.Linux;

namespace Spar.WindowTests;

/// <summary>
/// The Linux host's file dialog and credential store against the session bus of the window tests' private session
/// (decisions "M3c's decide-first questions", items 4, 6 and 7, and "The drop design, from the file-drop spike", item
/// 4): with no file-chooser portal the dialog is unavailable and GTK's own chooser never opens; where no Secret Service can
/// start a credential is unavailable, never a file; with GNOME Keyring's daemon on the bus, a secret is stored, read,
/// replaced and deleted. A window opens first, so the private session (Xvfb, its D-Bus, the private home) exists.
/// </summary>
[SupportedOSPlatform("linux")]
[Trait("Surface", "Window")]
public sealed class LinuxNativeServicesTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Without_a_portal_the_file_dialog_is_unavailable_and_shows_nothing()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The Linux host's file dialog.");
        await using var host = await HostPage.OpenAsync();
        var dialogs = new FileDialogHost(null!, () => 0, new HostLog(standardError: false));

        var shown = await dialogs.ShowAsync(new FileDialogRequest(FileDialogKind.Open, "Pick", [], null), Token);

        var unavailable = Assert.IsType<FileDialogError.Unavailable>(shown.Error);
        Assert.Contains("file-chooser portal", unavailable.Reason, StringComparison.Ordinal);
        Assert.Contains("xdg-desktop-portal", unavailable.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_Secret_Service_keeps_a_secret_and_without_one_a_credential_is_unavailable()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The Linux host's credential store.");
        await using var host = await HostPage.OpenAsync();
        var store = new LinuxCredentials("com.example.SparTests");

        // Where no Secret Service can start, unavailable with the bus's reason. Where GNOME Keyring is installed, the bus
        // starts its daemon on the first call (D-Bus activation), so the call finds a service.
        var daemon = WhereIs("gnome-keyring-daemon");
        if (daemon is null)
        {
            var before = await store.GetAsync("prod-sql", Token);
            var refused = Assert.IsType<CredentialError.Unavailable>(before.Error);
            Assert.Contains("Secret Service", refused.Reason, StringComparison.Ordinal);
            Assert.SkipWhen(Environment.GetEnvironmentVariable("CI") != "true", "gnome-keyring-daemon is not installed here; Spar's Linux CI image has it.");
            Assert.Fail("gnome-keyring-daemon is not on PATH in the CI image.");
        }

        using var keyring = StartKeyring(daemon);
        try
        {
            await WaitForServiceAsync(store);
            var missing = await store.GetAsync("prod-sql", Token);
            var stored = await store.SetAsync("prod-sql", "p@ss wörd", Token);
            var read = await store.GetAsync("prod-sql", Token);
            var replaced = await store.SetAsync("prod-sql", "second", Token);
            var again = await store.GetAsync("prod-sql", Token);
            var tooLarge = await store.SetAsync("big", new string('x', ICredentialStore.MaxSecretBytes + 1), Token);
            var deleted = await store.DeleteAsync("prod-sql", Token);
            var deletedAgain = await store.DeleteAsync("prod-sql", Token);
            var gone = await store.ExistsAsync("prod-sql", Token);

            Assert.True(missing.IsError && missing.Error is CredentialError.NotFound, $"Expected NotFound, got {(missing.IsError ? missing.Error : "a secret")}.");
            Assert.True(stored.IsSuccess, stored.IsError ? stored.Error.ToString() : "");
            Assert.Equal("p@ss wörd", read.Value);
            Assert.True(replaced.IsSuccess);
            Assert.Equal("second", again.Value);
            Assert.Equal(new CredentialError.TooLarge(ICredentialStore.MaxSecretBytes), tooLarge.Error);
            Assert.True(deleted.IsSuccess);
            Assert.True(deletedAgain.IsSuccess);
            Assert.False(gone.Value);
        }
        finally
        {
            keyring.Kill(entireProcessTree: true);
            await keyring.WaitForExitAsync(CancellationToken.None);
        }
    }

    /// <summary>The keyring's daemon on the private session's bus, its keyring unlocked with an empty password.</summary>
    private static Process StartKeyring(string daemon)
    {
        // Started before the store's first call, so the bus never starts one of its own, whose keyring would be locked.
        var start = new ProcessStartInfo(daemon, ["--foreground", "--unlock", "--components=secrets"])
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var name in new[] { "DBUS_SESSION_BUS_ADDRESS", "HOME", "XDG_RUNTIME_DIR", "XDG_DATA_HOME", "XDG_CONFIG_HOME", "XDG_CACHE_HOME" })
        {
            if (Environment.GetEnvironmentVariable(name) is { } value)
            {
                start.Environment[name] = value;
            }
        }

        var process = Process.Start(start) ?? throw new InvalidOperationException("gnome-keyring-daemon did not start.");
        process.StandardInput.Write("\n");
        process.StandardInput.Close();
        return process;
    }

    /// <summary>Waits until the daemon answers on the bus: its first answer to a lookup is no longer unavailable.</summary>
    private static async Task WaitForServiceAsync(LinuxCredentials store)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var probe = await store.ExistsAsync("probe", Token);
            if (probe.IsSuccess)
            {
                return;
            }

            Assert.True(DateTime.UtcNow < until, $"The Secret Service did not answer within 10 s: {probe.Error}.");
            await Task.Delay(100, Token);
        }
    }

    private static string? WhereIs(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':')
            .Select(folder => Path.Combine(folder, name))
            .FirstOrDefault(File.Exists);
}
