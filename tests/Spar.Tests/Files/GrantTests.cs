using Spar.Files;

namespace Spar.Tests.Files;

/// <summary>
/// Grants in C# (decision "M3c's decide-first questions", items 2 and 3): one window's table, its limit and its close;
/// expiry; a folder grant's paths, which cannot leave the folder; and safe writes, which leave the old file whole when
/// they fail.
/// </summary>
public sealed class GrantTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void A_window_holds_at_most_its_capacity_and_refuses_the_next_without_revoking_one()
    {
        using var folder = new TestFolder();
        var file = folder.File("a.txt", "alpha");
        var table = new GrantTable(TimeProvider.System, capacity: 3);

        var grants = Enumerable.Range(0, 3).Select(_ => table.AddFile(file, canWrite: false, null)).ToList();
        var refused = table.AddFile(file, canWrite: false, null);

        Assert.All(grants, grant => Assert.True(grant!.IsValid));
        Assert.Null(refused);
        Assert.Equal(3, table.Count);
        Assert.False(table.HasRoom(1));
    }

    [Fact]
    public void Ids_are_128_random_bits_with_their_kind()
    {
        using var folder = new TestFolder();
        var table = new GrantTable(TimeProvider.System);

        var file = table.AddFile(folder.File("a.txt"), canWrite: false, null)!;
        var other = table.AddFile(folder.File("a.txt"), canWrite: false, null)!;
        var dir = table.AddFolder(folder.Path, FolderAccess.Read, null)!;

        Assert.Matches("^fg_[0-9a-f]{32}$", file.Id);
        Assert.Matches("^fd_[0-9a-f]{32}$", dir.Id);
        Assert.NotEqual(file.Id, other.Id);
        Assert.Same(file, table.Find(file.Id));
        Assert.Null(table.Find("fg_" + new string('0', 32)));
    }

    [Fact]
    public async Task Closing_the_window_revokes_every_grant()
    {
        using var folder = new TestFolder();
        var table = new GrantTable(TimeProvider.System);
        var file = table.AddFile(folder.File("a.txt", "alpha"), canWrite: true, null)!;
        var dir = table.AddFolder(folder.Path, FolderAccess.ReadWrite, null)!;

        table.Close();

        Assert.False(file.IsValid);
        Assert.False(dir.IsValid);
        Assert.Null(table.Find(file.Id));
        Assert.Null(table.AddFile(folder.File("b.txt"), canWrite: false, null));
        Assert.IsType<FileAccessError.GrantExpired>((await file.ReadTextAsync(Token)).Error);
        Assert.IsType<FileAccessError.GrantExpired>((await file.WriteTextAsync("x", Token)).Error);
        Assert.IsType<FileAccessError.GrantExpired>((await dir.ListAsync("", Token)).Error);
        Assert.Equal("alpha", File.ReadAllText(file.Path));
    }

    [Fact]
    public async Task A_grant_with_an_expiry_ends_when_it_passes()
    {
        using var folder = new TestFolder();
        var time = new ManualTime();
        var table = new GrantTable(time);
        var grant = table.AddFile(folder.File("a.txt", "alpha"), canWrite: false, TimeSpan.FromMinutes(5))!;

        time.Advance(TimeSpan.FromMinutes(4));
        var before = await grant.ReadTextAsync(Token);
        time.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal("alpha", before.Value);
        Assert.False(grant.IsValid);
        Assert.IsType<FileAccessError.GrantExpired>((await grant.ReadTextAsync(Token)).Error);
    }

    [Fact]
    public async Task An_open_dialogs_grant_cannot_write()
    {
        using var folder = new TestFolder();
        var grant = new GrantTable(TimeProvider.System).AddFile(folder.File("a.txt", "alpha"), canWrite: false, null)!;

        var written = await grant.WriteTextAsync("changed", Token);

        Assert.IsType<FileAccessError.ReadOnlyGrant>(written.Error);
        Assert.Equal("alpha", File.ReadAllText(grant.Path));
    }

    [Fact]
    public async Task A_safe_write_replaces_the_file_and_a_failed_one_leaves_it_whole()
    {
        using var folder = new TestFolder();
        var grant = new GrantTable(TimeProvider.System).AddFile(folder.File("a.txt", "alpha"), canWrite: true, null)!;

        var written = await grant.WriteTextAsync("beta", Token);
        var failing = grant.WriteAsync(async (stream, token) =>
        {
            await stream.WriteAsync("half"u8.ToArray(), token);
            throw new InvalidOperationException("the writer broke");
        }, Token);

        Assert.True(written.IsSuccess);
        await Assert.ThrowsAsync<InvalidOperationException>(() => failing);
        Assert.Equal("beta", File.ReadAllText(grant.Path));
        Assert.Equal(["a.txt"], Directory.GetFiles(folder.Path).Select(Path.GetFileName));
    }

    [Fact]
    public async Task A_save_dialogs_grant_writes_a_file_that_did_not_exist()
    {
        using var folder = new TestFolder();
        var target = Path.Combine(folder.Path, "new.md");
        var grant = new GrantTable(TimeProvider.System).AddFile(target, canWrite: true, null)!;

        var written = await grant.WriteBytesAsync("# new"u8.ToArray(), Token);

        Assert.True(written.IsSuccess);
        Assert.Equal(0, grant.Size);
        Assert.Equal("# new", File.ReadAllText(target));
    }

    [Fact]
    public async Task A_missing_file_reads_as_not_found()
    {
        using var folder = new TestFolder();
        var grant = new GrantTable(TimeProvider.System).AddFile(folder.File("a.txt"), canWrite: false, null)!;
        File.Delete(grant.Path);

        Assert.IsType<FileAccessError.NotFound>((await grant.ReadBytesAsync(Token)).Error);
        Assert.IsType<FileAccessError.NotFound>(grant.OpenRead().Error);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../outside.txt")]
    [InlineData("inner/../../outside.txt")]
    [InlineData("inner\\..\\..\\outside.txt")]
    public async Task A_folder_grant_refuses_a_path_that_leaves_it(string path)
    {
        using var folder = new TestFolder();
        var grant = new GrantTable(TimeProvider.System).AddFolder(folder.Path, FolderAccess.ReadWrite, null)!;

        var read = await grant.ReadTextAsync(path, Token);
        var written = await grant.WriteTextAsync(path, "x", Token);

        Assert.Equal(new FileAccessError.OutsideFolder(path), read.Error);
        Assert.Equal(new FileAccessError.OutsideFolder(path), written.Error);
    }

    [Fact]
    public void A_folder_grant_refuses_an_absolute_path()
    {
        using var folder = new TestFolder();
        var grant = new GrantTable(TimeProvider.System).AddFolder(folder.Path, FolderAccess.Read, null)!;
        var absolute = Path.Combine(folder.Path, "a.txt");

        Assert.IsType<FileAccessError.OutsideFolder>(grant.ResolvePath(absolute).Error);
    }

    [Theory]
    [InlineData("NUL")]
    [InlineData("con.txt")]
    [InlineData("inner/COM1")]
    [InlineData("a.txt:hidden")]
    public void On_Windows_a_folder_grant_refuses_devices_and_streams(string path)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows' device names and alternate data streams");
        using var folder = new TestFolder();
        var grant = new GrantTable(TimeProvider.System).AddFolder(folder.Path, FolderAccess.Read, null)!;

        Assert.IsType<FileAccessError.OutsideFolder>(grant.ResolvePath(path).Error);
    }

    [Fact]
    public async Task A_link_inside_the_folder_that_points_outside_is_refused()
    {
        using var outside = new TestFolder();
        using var folder = new TestFolder();
        var secret = outside.File("secret.txt", "secret");
        var link = Path.Combine(folder.Path, "link.txt");
        if (!TryLink(() => File.CreateSymbolicLink(link, secret)))
        {
            Assert.Skip("This account cannot create symbolic links (Windows without Developer Mode).");
        }

        var grant = new GrantTable(TimeProvider.System).AddFolder(folder.Path, FolderAccess.ReadWrite, null)!;

        Assert.IsType<FileAccessError.OutsideFolder>((await grant.ReadTextAsync("link.txt", Token)).Error);
        Assert.IsType<FileAccessError.OutsideFolder>((await grant.WriteTextAsync("link.txt", "x", Token)).Error);
        Assert.Equal("secret", File.ReadAllText(secret));
    }

    [Fact]
    public async Task A_folder_link_inside_that_points_outside_is_refused_below_it_too()
    {
        using var outside = new TestFolder();
        using var folder = new TestFolder();
        outside.File("secret.txt", "secret");
        var link = Path.Combine(folder.Path, "elsewhere");
        if (!TryLink(() => Directory.CreateSymbolicLink(link, outside.Path)))
        {
            Assert.Skip("This account cannot create symbolic links (Windows without Developer Mode).");
        }

        var grant = new GrantTable(TimeProvider.System).AddFolder(folder.Path, FolderAccess.Read, null)!;

        Assert.IsType<FileAccessError.OutsideFolder>((await grant.ReadTextAsync("elsewhere/secret.txt", Token)).Error);
        Assert.IsType<FileAccessError.OutsideFolder>((await grant.ListAsync("elsewhere", Token)).Error);
    }

    [Fact]
    public async Task A_folder_grant_lists_reads_writes_creates_and_deletes_inside_it()
    {
        using var folder = new TestFolder();
        folder.File("b.md", "# b");
        folder.File("inner/c.md", "# c");
        var grant = new GrantTable(TimeProvider.System).AddFolder(folder.Path, FolderAccess.ReadWrite, null)!;

        var created = await grant.CreateFolderAsync("new/deeper", Token);
        var written = await grant.WriteTextAsync("new/deeper/d.md", "# d", Token);
        var top = await grant.ListAsync("", Token);
        var inner = await grant.ListAsync("inner", Token);
        var read = await grant.ReadTextAsync("inner/c.md", Token);
        var deleted = await grant.DeleteAsync("new", Token);
        var root = await grant.DeleteAsync("", Token);

        Assert.True(created.IsSuccess);
        Assert.True(written.IsSuccess);
        Assert.Equal(["b.md", "inner", "new"], top.Value.Select(entry => entry.Name));
        Assert.Equal([false, true, true], top.Value.Select(entry => entry.IsFolder));
        Assert.Equal(["inner/c.md"], inner.Value.Select(entry => entry.Name));
        Assert.Equal("# c", read.Value);
        Assert.True(deleted.IsSuccess);
        Assert.False(Directory.Exists(Path.Combine(folder.Path, "new")));
        Assert.IsType<FileAccessError.OutsideFolder>(root.Error);
    }

    [Fact]
    public async Task A_read_folder_grant_lists_and_reads_but_changes_nothing()
    {
        using var folder = new TestFolder();
        folder.File("b.md", "# b");
        var grant = new GrantTable(TimeProvider.System).AddFolder(folder.Path, FolderAccess.Read, null)!;

        Assert.Equal("# b", (await grant.ReadTextAsync("b.md", Token)).Value);
        Assert.IsType<FileAccessError.ReadOnlyGrant>((await grant.WriteTextAsync("b.md", "x", Token)).Error);
        Assert.IsType<FileAccessError.ReadOnlyGrant>((await grant.CreateFolderAsync("x", Token)).Error);
        Assert.IsType<FileAccessError.ReadOnlyGrant>((await grant.DeleteAsync("b.md", Token)).Error);
        Assert.Equal("# b", File.ReadAllText(Path.Combine(folder.Path, "b.md")));
    }

    private static bool TryLink(Action create)
    {
        try
        {
            create();
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
