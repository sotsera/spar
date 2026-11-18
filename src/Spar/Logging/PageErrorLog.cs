using System.Globalization;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Spar.Logging;

/// <summary>
/// Turns the page's errors into log lines that cannot flood the log (decision "Logging's public shape"), under the
/// category <see cref="Category"/>: console errors, uncaught exceptions and unhandled rejections, as the host-injected
/// script groups and batches them per document. The first of a group is logged at once, as an error with its stack;
/// later ones only raise its count, written in one summary at most every <see cref="SummaryInterval"/>. Past
/// <see cref="MaxGroupsPerDocument"/> groups, one line says so and the rest are only counted. Hosts post batches from
/// any thread; they are parsed and logged on a task of its own, from a bounded queue that drops what does not fit. The
/// same rules as <see cref="Hosting.CspViolationLog"/>, for a channel the page alone feeds.
/// </summary>
internal sealed class PageErrorLog : IAsyncDisposable
{
    /// <summary>The category of the page's lines.</summary>
    public const string Category = "Spar.Page";

    /// <summary>The most distinct groups logged per document.</summary>
    public const int MaxGroupsPerDocument = 100;

    /// <summary>How many batches may wait to be read; more are dropped and counted.</summary>
    public const int QueueLength = 256;

    /// <summary>How many groups one summary line names; the rest are counted.</summary>
    private const int GroupsPerSummary = 10;

    /// <summary>How much of a group's text a summary repeats.</summary>
    private const int SummaryText = 80;

    // Strings only: each value-type argument list is generic code of its own in a NativeAOT image.
    private static readonly Action<ILogger, string, string, string, Exception?> Reported = LoggerMessage.Define<string, string, string>(
        LogLevel.Error, new EventId(1, "PageError"), "{Kind} at {Where}: {Message}");

    private static readonly Action<ILogger, string, Exception?> TooMany = LoggerMessage.Define<string>(
        LogLevel.Warning, new EventId(2, "PageTooManyErrors"),
        "The page: more than {Groups} different errors in this document; further ones are counted, not logged");

    private static readonly Action<ILogger, string, string, Exception?> Summary = LoggerMessage.Define<string, string>(
        LogLevel.Information, new EventId(3, "PageErrorSummary"), "The page's errors in the last {Seconds} s: {Groups}");

    private static readonly Action<ILogger, string, Exception?> Unreadable = LoggerMessage.Define<string>(
        LogLevel.Debug, new EventId(4, "PageErrorsUnreadable"), "A batch of the page's errors could not be read ({Why}); it was dropped");

    private static readonly Action<ILogger, string, Exception?> Totals = LoggerMessage.Define<string>(
        LogLevel.Information, new EventId(5, "PageErrorTotals"), "The page's error reports in this run: {Counts}");

    private static readonly Action<ILogger, Exception?> Stopped = LoggerMessage.Define(
        LogLevel.Warning, new EventId(6, "PageErrorsStopped"),
        "The page raised so many errors that Spar's script stopped reporting them in this document");

    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Group> _groups = new(StringComparer.Ordinal);
    private readonly List<PageError> _batch = [];
    private readonly Channel<Work> _queue = Channel.CreateBounded<Work>(
        new BoundedChannelOptions(QueueLength) { SingleReader = true, FullMode = BoundedChannelFullMode.DropWrite });
    private readonly Task _reader;
    private readonly ITimer _timer;
    private string? _document;
    private long _overflow;
    private long _overflowLogged;
    private bool _tooManyLogged;
    private bool _stoppedLogged;
    private long _dropped;
    private long _batches;
    private long _errors;
    private long _lastSummary;

    public PageErrorLog(ILogger logger, TimeProvider time)
    {
        _logger = logger;
        _time = time;
        _lastSummary = time.GetTimestamp();
        _reader = Task.Run(ReadAsync);
        _timer = time.CreateTimer(_ => _queue.Writer.TryWrite(Work.Tick), null, SummaryInterval, SummaryInterval);
    }

    /// <summary>The most often a summary line is written.</summary>
    public static TimeSpan SummaryInterval { get; } = TimeSpan.FromSeconds(10);

    /// <summary>Batches dropped because the queue was full, since the start.</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>A new document's bridge opened: its groups start afresh (any thread).</summary>
    public void PostDocument(string document) => Post(new Work(document, isDocument: true, default));

    /// <summary>A batch from the injected script of <paramref name="document"/> (any thread; the payload is copied).</summary>
    public void PostBatch(string document, ReadOnlyMemory<byte> payload) => Post(new Work(document, isDocument: false, payload.ToArray()));

    /// <summary>Writes what is still counted and ends the reader.</summary>
    public async ValueTask DisposeAsync()
    {
        await _timer.DisposeAsync();
        _queue.Writer.TryComplete();
        await _reader;
        Summarize();
        var batches = Interlocked.Read(ref _batches);
        if (batches + Dropped > 0)
        {
            var counts = string.Create(
                CultureInfo.InvariantCulture,
                $"{batches} batches, {Interlocked.Read(ref _errors)} errors, {Dropped} batches dropped (queue full)");
            Totals(_logger, counts, null);
        }
    }

    /// <summary>A new document: what the old one still counted is written, then its groups are forgotten.</summary>
    public void OpenDocument(string document)
    {
        lock (_gate)
        {
            SummarizeLocked();
            _document = document;
            _groups.Clear();
            _overflow = 0;
            _overflowLogged = 0;
            _tooManyLogged = false;
            _stoppedLogged = false;
        }
    }

    /// <summary>Reads and records a batch from <paramref name="document"/>; a late one from an older document is ignored.</summary>
    public void Batch(string document, ReadOnlySpan<byte> payload)
    {
        lock (_gate)
        {
            if (document != _document)
            {
                return;
            }

            _batches++;
            _batch.Clear();
            if (!PageErrorReader.TryReadBatch(payload, _batch, out var overflow, out var stopped))
            {
                Unreadable(_logger, "malformed", null);
                return;
            }

            foreach (var error in _batch)
            {
                Record(error);
            }

            _overflow += overflow;
            _errors += overflow;
            if (stopped && !_stoppedLogged)
            {
                _stoppedLogged = true;
                Stopped(_logger, null);
            }
        }
    }

    /// <summary>Writes one summary of the counts that grew since the last one, if any did.</summary>
    public void Summarize()
    {
        lock (_gate)
        {
            SummarizeLocked();
        }
    }

    private void Post(Work work)
    {
        if (!_queue.Writer.TryWrite(work))
        {
            Interlocked.Increment(ref _dropped);
        }
    }

    private async Task ReadAsync()
    {
        await foreach (var work in _queue.Reader.ReadAllAsync())
        {
            try
            {
                if (ReferenceEquals(work, Work.Tick))
                {
                    Summarize();
                }
                else if (work.IsDocument)
                {
                    OpenDocument(work.Document!);
                }
                else
                {
                    Batch(work.Document!, work.Body.Span);
                }
            }
            catch (Exception exception)
            {
                // A log line must never stop the reader; the next batch is read as usual.
                Unreadable(_logger, exception.GetType().Name, exception);
            }
        }
    }

    private void Record(PageError error)
    {
        _errors += error.Count;
        if (_groups.TryGetValue(error.Key, out var group))
        {
            group.Count += error.Count;
            return;
        }

        if (_groups.Count >= MaxGroupsPerDocument)
        {
            _overflow += error.Count;
            if (!_tooManyLogged)
            {
                _tooManyLogged = true;
                TooMany(_logger, MaxGroupsPerDocument.ToString(CultureInfo.InvariantCulture), null);
            }

            return;
        }

        group = new Group(error) { Count = error.Count, Logged = 1 };
        _groups.Add(error.Key, group);
        var message = error.Message.Length == 0 ? "(no message)" : error.Message;
        if (error.Stack.Length > 0)
        {
            message += "\n" + error.Stack;
        }

        Reported(_logger, error.KindName, error.Where, message, null);
    }

    private void SummarizeLocked()
    {
        var now = _time.GetTimestamp();
        var grown = new List<Group>();
        foreach (var group in _groups.Values)
        {
            group.More = group.Count - group.Logged;
            if (group.More > 0)
            {
                grown.Add(group);
                group.Logged = group.Count;
            }
        }

        var overflow = _overflow - _overflowLogged;
        _overflowLogged = _overflow;
        if (grown.Count == 0 && overflow == 0)
        {
            _lastSummary = now;
            return;
        }

        grown.Sort(static (left, right) => right.More.CompareTo(left.More));
        var text = new StringBuilder();
        long others = 0;
        for (var index = 0; index < grown.Count; index++)
        {
            var group = grown[index];
            if (index < GroupsPerSummary)
            {
                Separate(text).Append(group.Name).Append(": ").Append(group.More).Append(" more");
            }
            else
            {
                others += group.More;
            }
        }

        if (grown.Count > GroupsPerSummary)
        {
            Separate(text).Append(grown.Count - GroupsPerSummary).Append(" other groups: ").Append(others).Append(" more");
        }

        if (overflow > 0)
        {
            Separate(text).Append("past the group limit: ").Append(overflow);
        }

        var seconds = Math.Round(_time.GetElapsedTime(_lastSummary, now).TotalSeconds, 1);
        _lastSummary = now;
        Summary(_logger, seconds.ToString(CultureInfo.InvariantCulture), text.ToString(), null);
    }

    private static StringBuilder Separate(StringBuilder text) => text.Length > 0 ? text.Append("; ") : text;

    /// <summary>A new document, or a batch of one; a class, so the channel's code is shared with the other channels.</summary>
    private sealed class Work(string? document, bool isDocument, ReadOnlyMemory<byte> body)
    {
        public static Work Tick { get; } = new(null, isDocument: false, default);

        public string? Document { get; } = document;

        public bool IsDocument { get; } = isDocument;

        public ReadOnlyMemory<byte> Body { get; } = body;
    }

    /// <summary>One group's count, and what the log has written of it.</summary>
    private sealed class Group(PageError first)
    {
        /// <summary>The kind and the start of the text, for a summary.</summary>
        public string Name { get; } = first.KindName + " \"" +
            (first.Message.Length <= SummaryText ? first.Message : first.Message[..SummaryText] + "…") + "\"";

        public long Count { get; set; }

        /// <summary>The count already written, by the group's first line or a summary.</summary>
        public long Logged { get; set; }

        /// <summary>How much the count grew since the last summary, while one is written.</summary>
        public long More { get; set; }
    }
}
