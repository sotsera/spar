using System.Runtime.Versioning;
using Spar.Windows;

namespace Spar.WindowTests;

/// <summary>
/// The Windows host's real clipboard and credential store (decision "M3c's decide-first questions", items 5, 6 and 7),
/// in Windows Sandbox only: a Credential Manager entry is a per-user registration and the clipboard is the user's, so
/// neither is touched on a developer's machine, where the test host's fakes stand in. Credential Manager runs in the
/// SYSTEM lane; the clipboard needs the guest's user session.
/// </summary>
[Trait(SandboxLane.Trait, "System")]
public sealed class CredentialManagerTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    [SupportedOSPlatform("windows")]
    public async Task Credential_Manager_keeps_a_secret_under_the_app_id_and_deletes_it()
    {
        SandboxLane.SkipUnlessGuest();
        var store = new WindowsCredentials("com.example.SparTests", roaming: false);

        var missing = await store.GetAsync("prod-sql", Token);
        var stored = await store.SetAsync("prod-sql", "p@ss wörd", Token);
        var read = await store.GetAsync("prod-sql", Token);
        var exists = await store.ExistsAsync("prod-sql", Token);
        var replaced = await store.SetAsync("prod-sql", "second", Token);
        var again = await store.GetAsync("prod-sql", Token);
        var tooLarge = await store.SetAsync("big", new string('x', ICredentialStore.MaxSecretBytes + 1), Token);
        var largest = await store.SetAsync("largest", new string('x', ICredentialStore.MaxSecretBytes), Token);
        var deleted = await store.DeleteAsync("prod-sql", Token);
        var deletedAgain = await store.DeleteAsync("prod-sql", Token);
        var gone = await store.ExistsAsync("prod-sql", Token);
        await store.DeleteAsync("largest", Token);

        Assert.IsType<CredentialError.NotFound>(missing.Error);
        Assert.True(stored.IsSuccess);
        Assert.Equal("p@ss wörd", read.Value);
        Assert.True(exists.Value);
        Assert.True(replaced.IsSuccess);
        Assert.Equal("second", again.Value);
        Assert.Equal(new CredentialError.TooLarge(ICredentialStore.MaxSecretBytes), tooLarge.Error);
        Assert.True(largest.IsSuccess);
        Assert.True(deleted.IsSuccess);
        Assert.True(deletedAgain.IsSuccess);
        Assert.False(gone.Value);
        SandboxLane.Note("Credential Manager: set, get, replace, the 2,560-byte limit and delete passed as " + Environment.UserName);
    }
}

[Trait(SandboxLane.Trait, "UserSession")]
public sealed class ClipboardTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    [SupportedOSPlatform("windows")]
    public async Task The_backends_clipboard_puts_text_there_and_reads_it_back()
    {
        SandboxLane.SkipUnlessUserSession();
        var clipboard = new WindowsClipboard();

        var set = await clipboard.SetTextAsync("SELECT 1;\r\nGO — ünïcödé", Token);
        var read = await clipboard.GetTextAsync(Token);
        var tooLarge = await clipboard.SetTextAsync(new string('x', IClipboard.MaxTextBytes + 1), Token);
        var unchanged = await clipboard.GetTextAsync(Token);

        Assert.True(set.IsSuccess);
        Assert.Equal("SELECT 1;\r\nGO — ünïcödé", read.Value);
        Assert.Equal(new ClipboardError.TooLarge(IClipboard.MaxTextBytes), tooLarge.Error);
        Assert.Equal(read.Value, unchanged.Value);
        SandboxLane.Note("Clipboard: set and read back through the clipboard thread in the user session");
    }
}
