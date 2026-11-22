using System.Text;
using System.Text.Json;
using Spar.Bridge;
using Spar.HotReload;

namespace Spar.Tests;

/// <summary>
/// The Hot Reload signal's backend side (plan section 4): the update as the page receives it (its revision, time and
/// types, a compiler-generated type named by the type it belongs to), every listener reached in turn and a disposed
/// one no more, and the event a session sends a page that listed <c>event</c>. Whether the runtime calls the handler is
/// checked under <c>dotnet watch</c> (docs/evidence/m1d-hot-reload.md); in this process it never applies updates.
/// </summary>
public sealed class BackendUpdatesTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 22, 9, 30, 0, 123, TimeSpan.Zero);

    [Fact]
    public void A_compiler_generated_type_is_named_by_the_type_it_belongs_to_once()
    {
        var nested = typeof(Sample).GetNestedTypes(System.Reflection.BindingFlags.NonPublic);
        var stateMachine = nested.First(t => t.Name.Contains("<RunAsync>", StringComparison.Ordinal));
        var closure = nested.First(t => t.Name.StartsWith("<>c", StringComparison.Ordinal));

        var update = BackendUpdates.Describe(3, At, [stateMachine, closure, typeof(Sample), typeof(Sample.Inner), typeof(BackendUpdatesTests)]);

        Assert.Equal(["Spar.Tests.BackendUpdatesTests+Sample", "Spar.Tests.BackendUpdatesTests+Sample+Inner", "Spar.Tests.BackendUpdatesTests"], update.Types);
    }

    [Fact]
    public void Without_types_the_update_names_none()
    {
        var update = BackendUpdates.Describe(1, At, null);

        Assert.Empty(update.Types);
        Assert.Equal("no named type", update.TypeList);
    }

    [Fact]
    public void The_payload_is_the_revision_the_time_and_the_types()
    {
        var payload = BackendUpdates.Payload(3, At, ["Notebook.Pages.PagesService", "A \"quoted\" name"]);

        using var json = JsonDocument.Parse(payload);
        var root = json.RootElement;
        Assert.Equal(3, root.GetProperty("revision").GetInt64());
        Assert.Equal(At, root.GetProperty("time").GetDateTimeOffset());
        Assert.Equal(["Notebook.Pages.PagesService", "A \"quoted\" name"], root.GetProperty("types").EnumerateArray().Select(t => t.GetString()));
        Assert.StartsWith("{\"revision\":3,\"time\":\"2026-11-22T09:30:00.123+00:00\",\"types\":[", Encoding.UTF8.GetString(payload), StringComparison.Ordinal);
    }

    [Fact]
    public void Every_listener_gets_each_update_with_the_next_revision_until_disposed()
    {
        var first = new List<long>();
        var second = new List<long>();
        var stopFirst = BackendUpdates.Listen(update => first.Add(update.Revision));
        using var stopSecond = BackendUpdates.Listen(update => second.Add(update.Revision));

        var one = BackendUpdates.Publish([typeof(Sample)], At);
        stopFirst.Dispose();
        stopFirst.Dispose();
        var two = BackendUpdates.Publish([typeof(Sample)], At);

        // Other tests may publish in this process: only this test's two revisions are asserted, in order.
        Assert.Equal(one.Revision + 1, two.Revision);
        Assert.Equal([one.Revision], first);
        Assert.Equal([one.Revision, two.Revision], second.Where(r => r == one.Revision || r == two.Revision));
    }

    [Fact]
    public async Task A_host_sends_the_update_as_the_backend_updated_event()
    {
        await using var bridge = await TestBridge.OpenAsync();
        using var listening = BackendUpdates.Listen(update => bridge.Session.SendEvent(BackendUpdates.EventName, update.Payload));

        var update = BackendUpdates.Publish([typeof(Sample)], At);

        var sent = await bridge.Sent.NextAsync();
        Assert.Equal(BridgeMessageType.Event, sent.Type);
        Assert.Equal("backend-updated", sent.Name);
        Assert.Equal(update.Payload, sent.Payload.ToArray());
    }

    [Fact]
    public void A_build_without_the_development_switch_ignores_the_runtime_call()
    {
        var seen = 0;
        using var listening = BackendUpdates.Listen(_ => seen++);

        // This test process has neither Spar's development switch nor a runtime that applies updates.
        Assert.False(BackendUpdates.IsSupported);
        BackendUpdates.UpdateApplication([typeof(Sample)]);

        Assert.Equal(0, seen);
    }

    private sealed class Sample
    {
        public static async Task<int> RunAsync()
        {
            await Task.Yield();
            return 1;
        }

        public static Func<int, int> Twice() => value => value * 2;

        public sealed class Inner;
    }
}
