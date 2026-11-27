using Spar;
using Spar.Testing;
using WireShapes;

namespace WireShapes.Tests;

/// <summary>
/// Grants, the clipboard and credentials through the generated code and the typed test clients (decision "M3c's
/// decide-first questions", items 2, 5, 6 and 7): the test answers each dialog, a grant a command returned goes back as
/// a parameter and is resolved to the same window, and the clipboard and credentials are the test host's fakes.
/// </summary>
public sealed class GrantsClientTests : IAsyncLifetime
{
    private SparTestApp _app = null!;
    private string _folder = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private GrantsClient Grants => new(_app);

    public async ValueTask InitializeAsync()
    {
        _app = await SparTestApp.StartAsync<Program>(cancellationToken: Token);
        _folder = Directory.CreateDirectory(Path.Combine(_app.Folders.DataRoot, "picked")).FullName;
    }

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    [Fact]
    public async Task A_dialogs_grant_comes_back_and_goes_in_again_as_a_parameter()
    {
        var path = Write("notes.md", "# notes");
        _app.FileDialogs.Choose(path);

        var picked = await Grants.PickAsync("Pick notes", Token);
        var length = await Grants.LengthAsync(picked.Value, Token);

        Assert.Equal(("notes.md", 7L, false), (picked.Value.Name, picked.Value.Size, picked.Value.CanWrite));
        Assert.Equal(7L, length);
        var shown = Assert.Single(_app.FileDialogs.Shown);
        Assert.Equal(("open", "Pick notes"), (shown.Kind, shown.Title));
        Assert.Equal(new FileFilter("Text", "*.txt;*.md"), Assert.Single(shown.Filters));
    }

    [Fact]
    public async Task A_grant_in_a_record_is_resolved_too_and_a_save_dialogs_grant_writes()
    {
        var target = Path.Combine(_folder, "out.md");
        _app.FileDialogs.Choose(target);

        var saved = await Grants.SaveAsync("out.md", Token);
        File.WriteAllText(target, "a");
        var appended = await Grants.AppendAsync(new AppendRequest(saved.Value, "b"), Token);

        Assert.True(appended.IsSuccess);
        Assert.Equal("ab", File.ReadAllText(target));
        Assert.Equal("out.md", Assert.Single(_app.FileDialogs.Shown).SuggestedName);
    }

    [Fact]
    public async Task An_open_dialogs_grant_cannot_be_written_through()
    {
        var path = Write("a.md", "a");
        _app.FileDialogs.Choose(path);
        var picked = await Grants.PickAsync("Pick", Token);

        var appended = await Grants.AppendAsync(new AppendRequest(picked.Value, "b"), Token);

        Assert.IsType<FileAccessError.ReadOnlyGrant>(appended.Error);
        Assert.Equal("a", File.ReadAllText(path));
    }

    [Fact]
    public async Task A_record_with_a_file_and_an_optional_folder_comes_back_whole()
    {
        Write("a.md", "a");
        Write("b.md", "b");
        _app.FileDialogs.Choose(Path.Combine(_folder, "a.md")).Choose(_folder);

        var picked = await Grants.PickBothAsync(Token);
        var count = await Grants.CountAsync(picked.Value.Folder!, Token);

        Assert.Equal(("a.md", "picked"), (picked.Value.File.Name, picked.Value.Folder!.Name));
        Assert.Equal(2, count.Value);
    }

    [Fact]
    public async Task A_dismissed_dialog_is_a_declared_error()
    {
        _app.FileDialogs.Dismiss();

        var picked = await Grants.PickAsync("Pick", Token);

        Assert.IsType<FileDialogError.Dismissed>(picked.Error);
    }

    [Fact]
    public async Task A_dialog_with_no_answer_queued_is_unavailable_and_a_problem()
    {
        var picked = await Grants.PickAsync("Pick", Token);

        Assert.IsType<FileDialogError.Unavailable>(picked.Error);
        Assert.Contains(_app.Problems, problem => problem.Contains("queued no answer", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Another_apps_grant_is_refused_before_the_command_runs()
    {
        var path = Write("a.md", "a");
        await using var other = await SparTestApp.StartAsync<Program>(cancellationToken: Token);
        other.FileDialogs.Choose(path);
        var theirs = await new GrantsClient(other).PickAsync("Pick", Token);

        var failure = await Assert.ThrowsAsync<SparCallFailedException>(() => Grants.LengthAsync(theirs.Value, Token));

        Assert.Equal(("rejected", "invalid-request"), (failure.Failure.Kind, failure.Failure.Code));
    }

    [Fact]
    public async Task The_backends_clipboard_is_the_test_hosts_text()
    {
        _app.Clipboard.Text = "from another program";

        var pasted = await Grants.PasteAsync(Token);
        var copied = await Grants.CopyAsync("SELECT 1;", Token);
        _app.Clipboard.FailNext(new ClipboardError.Busy());
        var busy = await Grants.CopyAsync("SELECT 2;", Token);

        Assert.Equal("from another program", pasted.Value);
        Assert.True(copied.IsSuccess);
        Assert.IsType<ClipboardError.Busy>(busy.Error);
        Assert.Equal("SELECT 1;", _app.Clipboard.Text);
    }

    [Fact]
    public async Task A_secret_is_kept_by_the_test_hosts_store_and_never_comes_back()
    {
        var remembered = await Grants.RememberAsync(new SecretRequest("prod-sql", "p@ss"), Token);
        var known = await Grants.KnowsAsync("prod-sql", Token);
        var tooLarge = await Grants.RememberAsync(new SecretRequest("big", new string('x', ICredentialStore.MaxSecretBytes + 1)), Token);
        _app.Credentials.Unavailable("no Secret Service");
        var unavailable = await Grants.KnowsAsync("prod-sql", Token);

        Assert.True(remembered.IsSuccess);
        Assert.True(known.Value);
        Assert.Equal("p@ss", _app.Credentials.Stored["prod-sql"]);
        Assert.Equal(new CredentialError.TooLarge(ICredentialStore.MaxSecretBytes), tooLarge.Error);
        Assert.Equal(new CredentialError.Unavailable("no Secret Service"), unavailable.Error);
    }

    private string Write(string name, string text)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, text);
        return path;
    }
}
