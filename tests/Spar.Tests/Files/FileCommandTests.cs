using System.Text;
using System.Text.Json;
using Spar.Files;

namespace Spar.Tests.Files;

/// <summary>
/// The page's own file commands over the bridge (decisions "M3c's decide-first questions", items 2 and 3, and "The drop
/// design, from the file-drop spike"): dialogs give grants with names only; reads and writes stay within 4 MiB; a
/// grant id the window does not hold is refused before anything runs; drops become grants only from the files the host
/// read itself.
/// </summary>
public sealed class FileCommandTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_open_dialog_gives_the_page_a_grant_with_the_name_and_never_the_path()
    {
        using var folder = new TestFolder();
        var file = folder.File("notes.md", "# notes");
        var dialogs = new QueuedDialogs().Choose(file);
        await using var bridge = await OpenAsync(dialogs);

        var grant = await CallAsync(bridge, 1, "openFile", """{"title":"Open notes","filters":[{"name":"Markdown","extensions":["md","markdown"]}]}""");

        Assert.True(grant.Ok);
        var json = Payload(grant);
        Assert.Matches("^fg_[0-9a-f]{32}$", json.GetProperty("id").GetString());
        Assert.Equal("notes.md", json.GetProperty("name").GetString());
        Assert.Equal(7, json.GetProperty("size").GetInt64());
        Assert.Equal("read", json.GetProperty("access").GetString());
        Assert.DoesNotContain(folder.Path, Encoding.UTF8.GetString(grant.Payload.Span), StringComparison.OrdinalIgnoreCase);
        var asked = Assert.Single(dialogs.Asked);
        Assert.Equal((FileDialogKind.Open, "Open notes"), (asked.Kind, asked.Title));
        Assert.Equal(new FileFilter("Markdown", "*.md;*.markdown"), Assert.Single(asked.Filters));
    }

    [Fact]
    public async Task A_closed_dialog_is_dismissed_and_a_missing_portal_unavailable()
    {
        var dialogs = new QueuedDialogs().Answer(new FileDialogError.Unavailable("no file-chooser portal"));
        await using var bridge = await OpenAsync(dialogs);

        var unavailable = await CallAsync(bridge, 1, "openFolder", "{}");
        var dismissed = await CallAsync(bridge, 2, "openFile", "{}");

        Assert.Equal("dismissed", ErrorCode(dismissed));
        Assert.Equal("unavailable", ErrorCode(unavailable));
        Assert.Equal("no file-chooser portal", Payload(unavailable).GetProperty("reason").GetString());
    }

    [Fact]
    public async Task A_second_dialog_while_one_is_open_is_busy()
    {
        var dialogs = new QueuedDialogs { Hold = new TaskCompletionSource() };
        await using var bridge = await OpenAsync(dialogs);

        bridge.Session.Receive(TestBridge.Call(1, "spar", "openFile", "{}"));
        while (dialogs.Asked.IsEmpty)
        {
            await Task.Delay(5, Token);
        }

        var second = await CallAsync(bridge, 2, "saveFile", "{}");
        dialogs.Hold.SetResult();
        var first = await bridge.Sent.NextAsync();

        Assert.Equal("busy", ErrorCode(second));
        Assert.Equal("dismissed", ErrorCode(first));
    }

    [Fact]
    public async Task The_page_reads_and_writes_a_grant_within_four_mebibytes()
    {
        using var folder = new TestFolder();
        var source = folder.File("in.txt", "héllo");
        var big = folder.File("big.bin");
        File.WriteAllBytes(big, new byte[FileCommands.PageLimit + 1]);
        var target = Path.Combine(folder.Path, "out.md");
        await using var bridge = await OpenAsync(new QueuedDialogs().Choose(source).Choose(big).Choose(target));
        var input = Id(await CallAsync(bridge, 1, "openFile", "{}"));
        var large = Id(await CallAsync(bridge, 2, "openFile", "{}"));
        var output = Id(await CallAsync(bridge, 3, "saveFile", """{"suggestedName":"out.md"}"""));

        var text = await CallAsync(bridge, 4, "readFile", $$"""{"grant":"{{input}}","as":"text"}""");
        var bytes = await CallAsync(bridge, 5, "readFile", $$"""{"grant":"{{input}}","as":"bytes"}""");
        var tooLarge = await CallAsync(bridge, 6, "readFile", $$"""{"grant":"{{large}}","as":"bytes"}""");
        var written = await CallAsync(bridge, 7, "writeFile", $$"""{"grant":"{{output}}","text":"# out"}""");
        var refused = await CallAsync(bridge, 8, "writeFile", $$"""{"grant":"{{input}}","text":"x"}""");

        Assert.Equal("héllo", Payload(text).GetString());
        Assert.Equal("héllo"u8.ToArray(), Payload(bytes).GetBytesFromBase64());
        Assert.Equal("tooLarge", ErrorCode(tooLarge));
        Assert.Equal(FileCommands.PageLimit, Payload(tooLarge).GetProperty("limit").GetInt32());
        Assert.True(written.Ok);
        Assert.Equal("# out", File.ReadAllText(target));
        Assert.Equal("readOnlyGrant", ErrorCode(refused));
        Assert.Equal("héllo", File.ReadAllText(source));
    }

    [Fact]
    public async Task A_grant_id_the_window_does_not_hold_is_refused_before_anything_runs()
    {
        await using var bridge = await OpenAsync(new QueuedDialogs());

        var madeUp = await CallAsync(bridge, 1, "readFile", $$"""{"grant":"fg_{{new string('a', 32)}}","as":"text"}""");
        var command = await CallAsync(bridge, 2, "size", $$"""{"id":"fg_{{new string('a', 32)}}"}""", contract: "grants");

        Assert.Equal(("rejected", "invalid-request", "notStarted"), (madeUp.Kind, madeUp.Code, madeUp.Execution));
        Assert.Equal(("rejected", "invalid-request", "notStarted"), (command.Kind, command.Code, command.Execution));
    }

    [Fact]
    public async Task Another_windows_grant_is_refused_in_this_one()
    {
        using var folder = new TestFolder();
        var file = folder.File("a.txt", "alpha");
        await using var first = await OpenAsync(new QueuedDialogs().Choose(file));
        await using var second = await OpenAsync(new QueuedDialogs());
        var id = Id(await CallAsync(first, 1, "openFile", "{}"));

        var here = await CallAsync(first, 2, "size", $$"""{"id":"{{id}}"}""", contract: "grants");
        var there = await CallAsync(second, 1, "size", $$"""{"id":"{{id}}"}""", contract: "grants");

        Assert.Equal("5", Encoding.UTF8.GetString(here.Payload.Span));
        Assert.Equal(("rejected", "invalid-request"), (there.Kind, there.Code));
    }

    [Fact]
    public async Task A_grant_outlives_the_document_and_ends_with_the_window()
    {
        using var folder = new TestFolder();
        var file = folder.File("a.txt", "alpha");
        var window = new HostWindow(TimeProvider.System, new QueuedDialogs().Choose(file));
        string id;
        await using (var first = await TestBridge.OpenAsync(window: window))
        {
            id = Id(await CallAsync(first, 1, "openFile", "{}"));
        }

        await using var reloaded = await TestBridge.OpenAsync(window: window);
        var read = await CallAsync(reloaded, 1, "readFile", $$"""{"grant":"{{id}}","as":"text"}""");
        window.Close();
        var closed = await CallAsync(reloaded, 2, "readFile", $$"""{"grant":"{{id}}","as":"text"}""");

        Assert.Equal("alpha", Payload(read).GetString());
        Assert.Equal(("rejected", "invalid-request"), (closed.Kind, closed.Code));
    }

    [Fact]
    public async Task A_files_blob_is_a_handle_the_origin_serves()
    {
        using var folder = new TestFolder();
        var file = folder.File("photo.png", "not really a png");
        await using var bridge = await OpenAsync(new QueuedDialogs().Choose(file));
        var id = Id(await CallAsync(bridge, 1, "openFile", "{}"));

        var handle = Payload(await CallAsync(bridge, 2, "fileBlob", $$"""{"grant":"{{id}}"}"""));

        Assert.Equal("image/png", handle.GetProperty("contentType").GetString());
        Assert.Equal("photo.png", handle.GetProperty("name").GetString());
        Assert.Equal(16, handle.GetProperty("size").GetInt64());
        Assert.Equal(1, bridge.Session.Blobs.Count);
    }

    [Fact]
    public async Task A_folders_commands_reach_inside_it_only()
    {
        using var folder = new TestFolder();
        folder.File("a.md", "# a");
        await using var bridge = await OpenAsync(new QueuedDialogs().Choose(folder.Path));
        var id = Id(await CallAsync(bridge, 1, "openFolder", """{"access":"readWrite"}"""));

        var created = await CallAsync(bridge, 2, "createFolder", $$"""{"grant":"{{id}}","path":"notes"}""");
        var written = await CallAsync(bridge, 3, "writeFile", $$"""{"grant":"{{id}}","path":"notes/b.md","text":"# b"}""");
        var listed = Payload(await CallAsync(bridge, 4, "listFolder", $$"""{"grant":"{{id}}"}"""));
        var outside = await CallAsync(bridge, 5, "readFile", $$"""{"grant":"{{id}}","path":"../x","as":"text"}""");
        var deleted = await CallAsync(bridge, 6, "deleteEntry", $$"""{"grant":"{{id}}","path":"a.md"}""");

        Assert.True(created.Ok);
        Assert.True(written.Ok);
        Assert.Equal(["a.md", "notes"], listed.EnumerateArray().Select(entry => entry.GetProperty("name").GetString()));
        Assert.Equal("outsideFolder", ErrorCode(outside));
        Assert.True(deleted.Ok);
        Assert.Equal("# b", File.ReadAllText(Path.Combine(folder.Path, "notes", "b.md")));
        Assert.False(File.Exists(Path.Combine(folder.Path, "a.md")));
    }

    [Fact]
    public async Task A_drop_with_the_hosts_files_becomes_read_only_grants()
    {
        using var folder = new TestFolder();
        var a = folder.File("a.csv", "1,2");
        var b = folder.File("b.CSV", "3,4");
        await using var bridge = await OpenAsync(new QueuedDialogs());

        bridge.Session.Receive(TestBridge.Call(1, "spar", "dropped", """{"accept":[".csv"],"limit":10}"""), [a, b]);
        var dropped = Payload(await bridge.Sent.NextAsync());

        var files = dropped.GetProperty("files").EnumerateArray().ToList();
        Assert.Equal(["a.csv", "b.CSV"], files.Select(file => file.GetProperty("name").GetString()));
        Assert.All(files, file => Assert.Equal("read", file.GetProperty("access").GetString()));
        Assert.Empty(dropped.GetProperty("folders").EnumerateArray());
    }

    [Fact]
    public async Task A_drop_the_area_does_not_take_says_why()
    {
        using var folder = new TestFolder();
        var text = folder.File("a.txt", "x");
        var csv = folder.File("b.csv", "x");
        await using var bridge = await OpenAsync(new QueuedDialogs());

        bridge.Session.Receive(TestBridge.Call(1, "spar", "dropped", """{"accept":["csv"]}"""), [csv, text]);
        var wrongType = await bridge.Sent.NextAsync();
        bridge.Session.Receive(TestBridge.Call(2, "spar", "dropped", """{"accept":["csv"]}"""), [folder.Path]);
        var aFolder = await bridge.Sent.NextAsync();
        bridge.Session.Receive(TestBridge.Call(3, "spar", "dropped", """{"limit":1}"""), [csv, text]);
        var tooMany = await bridge.Sent.NextAsync();

        Assert.Equal("notAccepted", ErrorCode(wrongType));
        Assert.Equal("a.txt", Payload(wrongType).GetProperty("name").GetString());
        Assert.Equal("notAccepted", ErrorCode(aFolder));
        Assert.Equal("tooMany", ErrorCode(tooMany));
        Assert.Equal(1, Payload(tooMany).GetProperty("limit").GetInt32());
    }

    [Fact]
    public async Task No_dialog_or_drop_grants_a_file_in_the_browser_profile_the_requested_one_or_the_one_in_use()
    {
        using var folder = new TestFolder();
        var profile = new Spar.Hosting.BrowserProfile(Path.Combine(folder.Path, "WebView2"));
        profile.RecordInUse(Path.Combine(folder.Path, "Moved"));
        var cookies = folder.File(Path.Combine("WebView2", "Default", "Cookies"), "secret");
        var moved = folder.File(Path.Combine("Moved", "Local State"), "{}");
        var notes = folder.File("notes.csv", "1,2");
        var dialogs = new QueuedDialogs().Choose(cookies).Choose(moved);
        await using var bridge = await TestBridge.OpenAsync(window: new HostWindow(TimeProvider.System, dialogs, profile.Contains));

        var requested = await CallAsync(bridge, 1, "openFile", "{}");
        var inUse = await CallAsync(bridge, 2, "openFile", "{}");
        bridge.Session.Receive(TestBridge.Call(3, "spar", "dropped", """{"accept":[],"limit":10}"""), [notes, cookies]);
        var dropped = await bridge.Sent.NextAsync().WaitAsync(TimeSpan.FromSeconds(10), Token);

        Assert.Equal(("accessDenied", cookies), (ErrorCode(requested), Payload(requested).GetProperty("path").GetString()));
        Assert.Equal(("accessDenied", moved), (ErrorCode(inUse), Payload(inUse).GetProperty("path").GetString()));
        Assert.Equal(("notAccepted", "Cookies"), (ErrorCode(dropped), Payload(dropped).GetProperty("name").GetString()));
    }

    [Fact]
    public async Task A_claim_without_a_drop_gets_nothing()
    {
        await using var bridge = await OpenAsync(new QueuedDialogs());

        // The page's own message, with no files the host read: on WebView2 no objects, on Linux no trusted drop.
        var claim = Payload(await CallAsync(bridge, 1, "dropped", "{}"));

        Assert.Empty(claim.GetProperty("files").EnumerateArray());
        Assert.Empty(claim.GetProperty("folders").EnumerateArray());
    }

    [Fact]
    public async Task On_Linux_only_a_drag_that_ended_in_a_trusted_drop_is_claimed_and_only_once()
    {
        using var folder = new TestFolder();
        var file = folder.File("a.md", "# a");
        var window = new HostWindow(TimeProvider.System, new QueuedDialogs());
        await using var bridge = await TestBridge.OpenAsync(window: window);

        // A drag that passed over the window and left: its files were read, but no trusted drop came.
        window.Drops.DragEntered(Task.FromResult<IReadOnlyList<string>>([file]));
        var passedOver = Payload(await CallAsync(bridge, 1, "dropped", "{}"));
        window.Drops.Dropped();
        var dropped = Payload(await CallAsync(bridge, 2, "dropped", "{}"));
        var again = Payload(await CallAsync(bridge, 3, "dropped", "{}"));

        Assert.Empty(passedOver.GetProperty("files").EnumerateArray());
        Assert.Equal("a.md", Assert.Single(dropped.GetProperty("files").EnumerateArray()).GetProperty("name").GetString());
        Assert.Empty(again.GetProperty("files").EnumerateArray());
    }

    [Fact]
    public async Task Files_attached_to_another_command_are_ignored()
    {
        using var folder = new TestFolder();
        var file = folder.File("a.md", "# a");
        await using var bridge = await OpenAsync(new QueuedDialogs());

        bridge.Session.Receive(TestBridge.Call(1, "spar", "openFile", "{}"), [file]);
        var opened = await bridge.Sent.NextAsync();

        Assert.Equal("dismissed", ErrorCode(opened));
    }

    [Fact]
    public async Task A_request_that_is_not_the_sdks_is_refused()
    {
        await using var bridge = await OpenAsync(new QueuedDialogs());

        var notAnObject = await CallAsync(bridge, 1, "openFile", "[]");
        var badAccess = await CallAsync(bridge, 2, "openFolder", """{"access":"everything"}""");
        var noAs = await CallAsync(bridge, 3, "readFile", """{"grant":"fg_00"}""");

        Assert.All([notAnObject, badAccess, noAs], refused => Assert.Equal(("rejected", "invalid-request"), (refused.Kind, refused.Code)));
    }

    private static Task<TestBridge> OpenAsync(QueuedDialogs dialogs) =>
        TestBridge.OpenAsync(window: new HostWindow(TimeProvider.System, dialogs));

    private static async Task<Spar.Bridge.BridgeMessage> CallAsync(TestBridge bridge, long id, string command, string payload, string contract = "spar")
    {
        bridge.Session.Receive(TestBridge.Call(id, contract, command, payload));
        return await bridge.Sent.NextAsync().WaitAsync(TimeSpan.FromSeconds(10), Token);
    }

    private static JsonElement Payload(Spar.Bridge.BridgeMessage message) => JsonDocument.Parse(message.Payload).RootElement;

    private static string? ErrorCode(Spar.Bridge.BridgeMessage message)
    {
        Assert.False(message.Ok, $"Expected a declared error, got {message.Type} {Encoding.UTF8.GetString(message.Payload.Span)}");
        return Payload(message).GetProperty("code").GetString();
    }

    private static string Id(Spar.Bridge.BridgeMessage message)
    {
        Assert.True(message.Ok, $"Expected a grant, got {message.Type} {message.Kind}/{message.Code} {Encoding.UTF8.GetString(message.Payload.Span)}");
        return Payload(message).GetProperty("id").GetString()!;
    }
}
