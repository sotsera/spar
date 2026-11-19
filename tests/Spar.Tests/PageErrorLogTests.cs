using System.Text;
using Microsoft.Extensions.Logging;
using Spar.Logging;

namespace Spar.Tests;

/// <summary>
/// The page's errors into the log (plan M1b, build step 7): grouping, the bounds per document, the summaries, and
/// untrusted input, as the host-injected script batches them.
/// </summary>
public sealed class PageErrorLogTests : IAsyncLifetime
{
    private readonly CapturedLog _log = new();
    private readonly ManualTime _time = new();
    private PageErrorLog _errors = null!;

    public ValueTask InitializeAsync()
    {
        _errors = new PageErrorLog(_log, _time);
        _errors.OpenDocument("doc-1");
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _errors.DisposeAsync();

    [Fact]
    public void The_first_of_a_group_is_an_error_line_with_its_place_and_stack_and_the_rest_one_summary()
    {
        var stack = "TypeError: x is not a function\n    at load (https://app.localhost/assets/index.js:3:14)";
        _errors.Batch("doc-1", Batch(Group("error", "Uncaught TypeError: x is not a function", stack: stack, count: 100_000)));
        _errors.Batch("doc-1", Batch(Group("error", "Uncaught TypeError: x is not a function", stack: stack, count: 4)));
        _time.Advance(TimeSpan.FromSeconds(10));
        _errors.Summarize();
        _errors.Summarize();

        Assert.Equal(
            [
                (LogLevel.Error, "Uncaught exception at https://app.localhost/assets/index.js:3:14: Uncaught TypeError: x is not a function\n" + stack),
                (LogLevel.Information, "The page's errors in the last 10 s: Uncaught exception \"Uncaught TypeError: x is not a function\": 100003 more"),
            ],
            _log.Entries.Select(e => (e.Level, e.Message)));
    }

    [Fact]
    public void Each_kind_is_named_and_a_group_without_a_place_is_the_page()
    {
        _errors.Batch("doc-1", Batch(
            Group("console", "Saving failed: Error: disk full", count: 1),
            Group("rejection", "plain text", count: 1),
            Group("worker", "an unknown kind is dropped", count: 1)));

        Assert.Equal(
            ["console.error at https://app.localhost/assets/index.js:3:14: Saving failed: Error: disk full",
             "Unhandled rejection at https://app.localhost/assets/index.js:3:14: plain text"],
            _log.Entries.Select(e => e.Message));

        _errors.Batch("doc-1", Encoding.UTF8.GetBytes("""{"groups":[{"kind":"error","message":"","count":1}],"overflow":0}"""));
        Assert.Equal("Uncaught exception at the page: (no message)", _log.Entries[^1].Message);
    }

    [Fact]
    public void Past_the_bound_of_groups_one_line_says_so_and_the_rest_are_counted()
    {
        var groups = Enumerable.Range(0, PageErrorLog.MaxGroupsPerDocument + 20).Select(i => Group("console", $"error {i}", count: 1)).ToArray();
        foreach (var chunk in groups.Chunk(PageErrorReader.MaxGroupsPerBatch))
        {
            _errors.Batch("doc-1", Batch(chunk));
        }

        _errors.Batch("doc-1", Encoding.UTF8.GetBytes("""{"groups":[],"overflow":30,"stopped":true}"""));
        _errors.Summarize();

        var lines = _log.Entries.Select(e => e.Message).ToList();
        Assert.Equal(PageErrorLog.MaxGroupsPerDocument, lines.Count(line => line.StartsWith("console.error", StringComparison.Ordinal)));
        Assert.Single(lines, line => line.Contains("more than 100 different errors", StringComparison.Ordinal));
        Assert.Single(lines, line => line.Contains("stopped reporting them", StringComparison.Ordinal));
        Assert.EndsWith("past the group limit: 50", lines[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void A_new_document_starts_afresh_and_a_late_batch_of_the_old_one_is_ignored()
    {
        _errors.Batch("doc-1", Batch(Group("console", "same", count: 1)));
        _errors.OpenDocument("doc-2");
        _errors.Batch("doc-1", Batch(Group("console", "late", count: 1)));
        _errors.Batch("doc-2", Batch(Group("console", "same", count: 1)));

        Assert.Equal(2, _log.Entries.Count(e => e.Message.EndsWith(": same", StringComparison.Ordinal)));
        Assert.DoesNotContain(_log.Entries, e => e.Message.Contains("late", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("""not json""")]
    [InlineData("""{"groups":{}}""")]
    [InlineData("""{"groups":[1]}""")]
    [InlineData("""{"groups":[]} trailing""")]
    public void A_malformed_batch_is_refused_whole(string json)
    {
        var groups = new List<PageError>();

        Assert.False(PageErrorReader.TryReadBatch(Encoding.UTF8.GetBytes(json), groups, out _, out _));
        Assert.Empty(groups);
    }

    [Fact]
    public void Texts_are_cut_and_numbers_clamped_whatever_the_page_sends()
    {
        var json = $$$"""{"groups":[{"kind":"console","message":"{{{new string('m', 2_000)}}}","source":"{{{new string('s', 1_000)}}}","line":-3,"column":"x","stack":"{{{new string('t', 10_000)}}}","count":99999999999,"extra":{"a":1}}],"overflow":-5}""";
        var groups = new List<PageError>();

        Assert.True(PageErrorReader.TryReadBatch(Encoding.UTF8.GetBytes(json), groups, out var overflow, out _));
        var group = Assert.Single(groups);
        Assert.Equal(
            (PageErrorReader.MaxMessage, PageErrorReader.MaxSource, PageErrorReader.MaxStack, 0, 0, 1_000_000_000L, 0L),
            (group.Message.Length, group.Source.Length, group.Stack.Length, group.Line, group.Column, group.Count, overflow));
    }

    [Fact]
    public async Task Batches_posted_from_any_thread_are_read_on_the_logs_task_and_totals_written_at_the_end()
    {
        var log = new CapturedLog();
        var errors = new PageErrorLog(log, _time);
        errors.PostDocument("doc-9");
        errors.PostBatch("doc-9", Batch(Group("console", "posted", count: 3)));
        await errors.DisposeAsync();

        Assert.Equal(
            ["console.error at https://app.localhost/assets/index.js:3:14: posted",
             "The page's errors in the last 0 s: console.error \"posted\": 2 more",
             "The page's error reports in this run: 1 batches, 3 errors, 0 batches dropped (queue full)"],
            log.Entries.Select(e => e.Message));
    }

    private static string Group(string kind, string message, string stack = "", long count = 1) =>
        $$"""{"kind":"{{kind}}","message":"{{System.Text.Json.JsonEncodedText.Encode(message)}}","source":"https://app.localhost/assets/index.js","line":3,"column":14,"stack":"{{System.Text.Json.JsonEncodedText.Encode(stack)}}","count":{{count}}}""";

    private static byte[] Batch(params string[] groups) => Encoding.UTF8.GetBytes($$"""{"groups":[{{string.Join(",", groups)}}],"overflow":0}""");
}
