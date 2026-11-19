using System.Text;
using Microsoft.Extensions.Logging;
using Spar.Hosting;

namespace Spar.Tests;

/// <summary>
/// Both violation channels into the log (plan section 3b; decision "CSP reporting: both channels, with flood limits"):
/// grouping, the duplicates the two channels make, the bounds per document, the summaries, and untrusted input.
/// </summary>
public sealed class CspViolationLogTests : IAsyncLifetime
{
    private const string Report =
        "{\"csp-report\":{\"document-uri\":\"https://app.localhost/\",\"referrer\":\"\",\"violated-directive\":\"img-src\"," +
        "\"effective-directive\":\"img-src\",\"original-policy\":\"default-src 'none'\",\"disposition\":\"enforce\"," +
        "\"blocked-uri\":\"https://cdn.example.com/a.png\",\"line-number\":12,\"column-number\":5," +
        "\"source-file\":\"https://app.localhost/assets/index.js\",\"status-code\":200,\"script-sample\":\"\"}}";

    private readonly CapturedLog _log = new();
    private readonly ManualTime _time = new();
    private CspViolationLog _violations = null!;

    public ValueTask InitializeAsync()
    {
        _violations = new CspViolationLog(_log, _time, advice: null);
        _violations.OpenDocument("doc-1");
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _violations.DisposeAsync();

    [Fact]
    public void The_first_of_a_group_is_logged_at_once_and_the_rest_only_counted_in_a_summary()
    {
        _violations.Batch("doc-1", Batch(Group("img-src", "https://cdn.example.com/a.png", count: 100_000)));
        _violations.Batch("doc-1", Batch(Group("img-src", "https://cdn.example.com/a.png", count: 5)));
        _time.Advance(TimeSpan.FromSeconds(10));
        _violations.Summarize();
        _violations.Summarize();

        Assert.Equal(
            [
                (LogLevel.Warning, "Content Security Policy: img-src blocked https://cdn.example.com/a.png at https://app.localhost/assets/index.js:12:5"),
                (LogLevel.Information, "Content Security Policy violations in the last 10 s: img-src https://cdn.example.com/a.png: 100004 more"),
            ],
            _log.Entries.Select(e => (e.Level, e.Message)));
    }

    [Fact]
    public void A_violation_both_channels_report_is_one_violation()
    {
        _violations.Report(Encoding.UTF8.GetBytes(Report));
        _violations.Batch("doc-1", Batch(Group("img-src", "https://cdn.example.com/a.png", count: 3)));
        _violations.Report(Encoding.UTF8.GetBytes(Report));
        _violations.Summarize();

        // One line for the group, then the larger channel's count: the listener's 3, not 3 + 2.
        Assert.Equal(2, _log.Entries.Count);
        Assert.EndsWith(": 2 more", _log.Entries[1].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void What_only_report_uri_sees_is_still_logged_and_counted()
    {
        for (var i = 0; i < 4; i++)
        {
            _violations.Report(Encoding.UTF8.GetBytes(Report));
        }

        _violations.Summarize();

        Assert.Equal(2, _log.Entries.Count);
        Assert.EndsWith(": 3 more", _log.Entries[1].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Past_the_bound_of_groups_one_line_says_so_and_the_rest_are_counted()
    {
        var groups = Enumerable.Range(0, CspViolationLog.MaxGroupsPerDocument + 50)
            .Select(i => Group("img-src", $"https://cdn.example.com/{i}.png", count: 1))
            .ToArray();
        foreach (var chunk in groups.Chunk(50))
        {
            _violations.Batch("doc-1", Batch(chunk));
        }

        _violations.Summarize();

        var warnings = _log.Entries.Where(e => e.Level == LogLevel.Warning).ToList();
        Assert.Equal(CspViolationLog.MaxGroupsPerDocument + 1, warnings.Count);
        Assert.Contains("more than 100 different violations", warnings[^1].Message, StringComparison.Ordinal);
        Assert.EndsWith("past the group limit: 50", _log.Entries[^1].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_listeners_own_overflow_is_counted_too()
    {
        _violations.Batch("doc-1", Encoding.UTF8.GetBytes("{\"groups\":[],\"overflow\":250}"));
        _violations.Summarize();

        Assert.Equal("Content Security Policy violations in the last 0 s: past the group limit: 250", Assert.Single(_log.Entries).Message);
    }

    [Fact]
    public void A_listener_that_stopped_is_said_once_per_document()
    {
        _violations.Batch("doc-1", Encoding.UTF8.GetBytes("{\"groups\":[],\"overflow\":0,\"stopped\":true}"));
        _violations.Batch("doc-1", Encoding.UTF8.GetBytes("{\"groups\":[],\"overflow\":0,\"stopped\":true}"));

        Assert.Contains("listener stopped counting", Assert.Single(_log.Entries).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_summary_names_the_largest_groups_and_counts_the_rest()
    {
        var groups = Enumerable.Range(0, 15).Select(i => Group("img-src", $"https://cdn.example.com/{i}.png", count: i + 2)).ToArray();
        _violations.Batch("doc-1", Batch(groups));
        _violations.Summarize();

        var summary = _log.Entries[^1].Message;
        Assert.StartsWith("Content Security Policy violations in the last 0 s: img-src https://cdn.example.com/14.png: 15 more; ", summary, StringComparison.Ordinal);
        Assert.EndsWith("; 5 other groups: 15 more", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void A_new_document_starts_afresh_and_a_late_batch_of_the_old_one_is_ignored()
    {
        _violations.Batch("doc-1", Batch(Group("img-src", "https://cdn.example.com/a.png", count: 1)));
        _violations.OpenDocument("doc-2");
        _violations.Batch("doc-1", Batch(Group("img-src", "https://cdn.example.com/late.png", count: 1)));
        _violations.Batch("doc-2", Batch(Group("img-src", "https://cdn.example.com/a.png", count: 1)));

        Assert.Equal(2, _log.Entries.Count(e => e.Level == LogLevel.Warning));
        Assert.DoesNotContain(_log.Entries, e => e.Message.Contains("late.png", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Development_lines_say_what_would_allow_each_violation()
    {
        await using var development = new CspViolationLog(_log, _time, CspAdvice.For);
        development.OpenDocument("doc-1");

        development.Batch("doc-1", Batch(
            Group("font-src", "https://fonts.gstatic.com/s/x.woff2", count: 1),
            Group("style-src-attr", "inline", count: 1),
            Group("script-src", "eval", count: 1),
            Group("require-trusted-types-for", "trusted-types-sink", count: 1),
            Group("frame-src", "https://frames.example/x", count: 1),
            Group("base-uri", "https://evil.example/", count: 1)));

        var lines = _log.Entries.Select(e => e.Message).ToList();
        Assert.Contains("Allow it with csp.FontSrc(\"https://fonts.gstatic.com\").", lines[0], StringComparison.Ordinal);
        Assert.Contains("csp.StyleNonce()", lines[1], StringComparison.Ordinal);
        Assert.Contains("csp.AllowEval()", lines[2], StringComparison.Ordinal);
        Assert.Contains("csp.WithoutTrustedTypes()", lines[3], StringComparison.Ordinal);
        Assert.Contains("csp.FrameSrc(\"https://frames.example\")", lines[4], StringComparison.Ordinal);
        Assert.Contains("Spar keeps base-uri closed", lines[5], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{\"groups\":{}}")]
    [InlineData("{\"groups\":[1]}")]
    [InlineData("{\"groups\":[]} trailing")]
    [InlineData("{\"groups\":[{\"directive\":\"img-src\"}]")]
    public void A_malformed_batch_is_dropped_whole(string json)
    {
        var groups = new List<CspViolation>();

        Assert.False(CspReportReader.TryReadBatch(Encoding.UTF8.GetBytes(json), groups, out _));
        Assert.Empty(groups);
    }

    [Fact]
    public void Untrusted_values_are_cut_and_clamped()
    {
        var groups = new List<CspViolation>();
        var json = $"{{\"groups\":[{{\"directive\":\"img-src\",\"blocked\":\"{new string('a', 5_000)}\",\"line\":-3,\"count\":1e30,\"extra\":{{\"deep\":[1,2]}}}}],\"overflow\":-5}}";

        Assert.True(CspReportReader.TryReadBatch(Encoding.UTF8.GetBytes(json), groups, out var overflow));

        var group = Assert.Single(groups);
        Assert.Equal((CspReportReader.MaxText, 0, 1L, 0L), (group.Blocked.Length, group.Line, group.Count, overflow));
    }

    [Fact]
    public void A_batch_of_more_groups_than_the_listener_sends_is_cut_to_the_bound()
    {
        var groups = new List<CspViolation>();
        var json = Encoding.UTF8.GetString(Batch([.. Enumerable.Range(0, 500).Select(i => Group("img-src", $"{i}", count: 1))]));

        Assert.True(CspReportReader.TryReadBatch(Encoding.UTF8.GetBytes(json), groups, out _));
        Assert.Equal(CspReportReader.MaxGroupsPerBatch, groups.Count);
    }

    [Fact]
    public void A_report_of_an_older_engine_names_only_the_violated_directive()
    {
        var older = "{\"csp-report\":{\"violated-directive\":\"img-src 'self'\",\"blocked-uri\":\"data\"}}";

        Assert.True(CspReportReader.TryReadReport(Encoding.UTF8.GetBytes(older), out var violation));
        Assert.Equal(("img-src", "data"), (violation.Directive, violation.Blocked));
        Assert.False(CspReportReader.TryReadReport("{\"csp-report\":{}}"u8, out _));
        Assert.False(CspReportReader.TryReadReport("{\"other\":{}}"u8, out _));
    }

    [Fact]
    public async Task Posted_work_is_read_off_the_callers_thread_and_what_cannot_be_queued_is_dropped_and_counted()
    {
        var queued = new CspViolationLog(_log, TimeProvider.System, advice: null);
        queued.PostDocument("doc-9");
        queued.PostBatch("doc-9", Batch(Group("img-src", "https://cdn.example.com/q.png", count: 1)));
        for (var i = 0; i < 1_000; i++)
        {
            queued.PostReport(Encoding.UTF8.GetBytes(Report));
        }

        await queued.DisposeAsync();
        queued.PostReport(Encoding.UTF8.GetBytes(Report));

        Assert.Contains(_log.Entries, e => e.Message.Contains("q.png", StringComparison.Ordinal));
        Assert.Contains(_log.Entries, e => e.Message.Contains("cdn.example.com/a.png", StringComparison.Ordinal));
        Assert.True(queued.Dropped >= 1);
    }

    private static string Group(string directive, string blocked, long count) =>
        $"{{\"directive\":\"{directive}\",\"blocked\":\"{blocked}\",\"source\":\"https://app.localhost/assets/index.js\"," +
        $"\"line\":12,\"column\":5,\"sample\":\"\",\"count\":{count}}}";

    private static byte[] Batch(params string[] groups) => Encoding.UTF8.GetBytes($"{{\"groups\":[{string.Join(',', groups)}],\"overflow\":0}}");
}
