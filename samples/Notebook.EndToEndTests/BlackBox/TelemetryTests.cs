using Spar;
using Spar.Testing;

namespace Notebook.EndToEndTests;

/// <summary>
/// The Notebook's Production executable with its OpenTelemetry layer on (section 6b; decision "M2d's decide-first
/// questions"): pointed at an OTLP endpoint of the test's own, the page's spans come through the host's relay and the
/// backend's straight from .NET's exporter, in one trace per call.
/// </summary>
[Trait("Surface", "BlackBox")]
public sealed class TelemetryTests
{
    private const string PageService = "com.example.Notebook-page";
    private const string BackendService = "com.example.Notebook";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_page_call_and_its_backend_span_share_one_trace()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows() || OperatingSystem.IsLinux(), "The black-box fixture runs on Windows and Linux.");
        await using var receiver = new OtlpReceiver();
        var root = await SeededRootAsync();
        try
        {
            await using (var app = await SparBlackBox.LaunchAsync(Projects.Notebook, o =>
            {
                o.DataRoot = root;
                o.Environment["OTEL_EXPORTER_OTLP_ENDPOINT"] = receiver.Endpoint;
                o.Environment["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf";
                o.Environment["OTEL_BSP_SCHEDULE_DELAY"] = "200";
            }, Token))
            {
                // The page's telemetry loads after its start: wait for its first span before the call it should trace.
                await receiver.WaitForSpansAsync(s => s.Any(span => span.Service == PageService), TimeSpan.FromSeconds(20), Token);
                // An invoke right after the button appears can be lost before the page's accessibility tree has caught up
                // (pitfalls, "Windows host"): invoke again until the call is made, and say how many it took.
                var invokes = 0;
                do
                {
                    invokes++;
                    await app.Window.GetByRole(SparRole.Button, new() { Name = "Welcome" }).InvokeAsync();
                }
                while (!HasLinkedGet(await receiver.WaitForSpansAsync(HasLinkedGet, TimeSpan.FromSeconds(5), Token)) && invokes < 4);

                TestContext.Current.TestOutputHelper?.WriteLine($"Invokes until the call: {invokes}");
                Assert.Equal(0, await app.CloseAsync(Token));
                Assert.True(await app.WaitForProcessTreeExitAsync(), $"Processes left: {string.Join(", ", app.ProcessIds)}");
            }

            var spans = await receiver.WaitForSpansAsync(HasLinkedGet, TimeSpan.FromSeconds(10), Token);
            foreach (var span in spans)
            {
                TestContext.Current.TestOutputHelper?.WriteLine($"{span.Service} {span.Name} kind={span.Kind} trace={span.TraceId} span={span.SpanId} parent={span.ParentSpanId}");
            }

            var page = spans.Where(s => s.Service == PageService).ToList();
            var backend = spans.Where(s => s.Service == BackendService).ToList();
            Assert.Contains(page, s => s.Name == "documentLoad");

            var call = Assert.Single(page, s => s.Name == "pages/get");
            Assert.Equal(ReceivedSpan.Client, call.Kind);
            var served = Assert.Single(backend, s => s.Name == "pages/get");
            Assert.Equal(ReceivedSpan.Server, served.Kind);
            Assert.Equal(call.TraceId, served.TraceId);
            Assert.Equal(call.SpanId, served.ParentSpanId);

            // The calls before the page's telemetry loaded have backend spans of their own, each the root of its trace.
            Assert.Contains(backend, s => s.Name == "pages/list");
            Assert.NotEmpty(receiver.Bodies("/v1/logs"));
            Assert.NotEmpty(receiver.Bodies("/v1/metrics"));
        }
        finally
        {
            await DeleteAsync(root);
        }
    }

    /// <summary>The data root, once the browser has let go of its profile (it can outlive its app by a moment).</summary>
    private static async Task DeleteAsync(string root)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Delete(root, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 50)
            {
                await Task.Delay(200);
            }
        }
    }

    private static bool HasLinkedGet(IReadOnlyList<ReceivedSpan> spans) =>
        spans.Any(s => s.Service == PageService && s.Name == "pages/get")
        && spans.Any(s => s.Service == BackendService && s.Name == "pages/get");

    /// <summary>A data root whose notebook has one page, made by a first run that keeps its data.</summary>
    private static async Task<string> SeededRootAsync()
    {
        string root, data;
        await using (var first = await SparBlackBox.LaunchAsync(Projects.Notebook, o => o.KeepData = true, Token))
        {
            Assert.Equal(0, await first.CloseAsync(Token));
            Assert.True(await first.WaitForProcessTreeExitAsync());
            root = first.DataRoot;
            data = first.DataFolder;
        }

        var pages = Directory.CreateDirectory(Path.Combine(data, "pages")).FullName;
        await File.WriteAllTextAsync(
            Path.Combine(pages, "welcome.md"),
            "---\ntitle: Welcome\nrevision: 1\nupdated: 2026-11-01T09:00:00.0000000+00:00\n---\n# Hello from the test\n",
            Token);
        return root;
    }
}
