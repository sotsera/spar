using System.Globalization;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Spar.Hosting;

/// <summary>
/// Turns the page's Content Security Policy violations into log lines that cannot flood the log (plan section 3b,
/// "Violations become diagnostics"; decision "CSP reporting: both channels, with flood limits"). Both channels feed it:
/// the host-injected listener's batches and the browser's <c>report-uri</c> reports. Violations are grouped by
/// directive, blocked address and source location, per document. The first of a group is logged at once, in
/// Development with the C# that would allow it; later ones only raise its count, written in one summary at most every
/// <see cref="SummaryInterval"/>. A violation both channels report is one violation: each group keeps a count per
/// channel and reports the larger. Past <see cref="MaxGroupsPerDocument"/> groups, one line says so and the rest are
/// only counted. Hosts post work from any thread, the window's UI thread included; it is parsed and logged on a task of
/// its own, from a bounded queue that drops what does not fit.
/// </summary>
internal sealed class CspViolationLog : IAsyncDisposable
{
    /// <summary>The most distinct groups logged per document.</summary>
    public const int MaxGroupsPerDocument = 100;

    /// <summary>How many reports and batches may wait to be read; more are dropped and counted.</summary>
    public const int QueueLength = 256;

    /// <summary>How many groups one summary line names; the rest are counted.</summary>
    private const int GroupsPerSummary = 10;

    // The messages take strings only: each value-type argument list is generic code of its own in a NativeAOT image, in
    // every logger the app has, and these arities of strings are already there.
    private static readonly Action<ILogger, string, string, string, Exception?> Violation = LoggerMessage.Define<string, string, string>(
        LogLevel.Warning, new EventId(1, "CspViolation"), "Content Security Policy: {Directive} blocked {Blocked} at {Where}");

    private static readonly Action<ILogger, int, Exception?> TooMany = LoggerMessage.Define<int>(
        LogLevel.Warning, new EventId(2, "CspTooManyViolations"),
        "Content Security Policy: more than {Groups} different violations in this document; further ones are counted, not logged");

    private static readonly Action<ILogger, string, string, Exception?> Summary = LoggerMessage.Define<string, string>(
        LogLevel.Information, new EventId(3, "CspViolationSummary"), "Content Security Policy violations in the last {Seconds} s: {Groups}");

    private static readonly Action<ILogger, string, Exception?> Unreadable = LoggerMessage.Define<string>(
        LogLevel.Debug, new EventId(4, "CspReportUnreadable"), "A Content Security Policy report could not be read ({Channel}); it was dropped");

    private static readonly Action<ILogger, Exception?> ListenerStopped = LoggerMessage.Define(
        LogLevel.Information, new EventId(6, "CspListenerStopped"),
        "Content Security Policy: the page raised so many violations that Spar's listener stopped counting them in this document; report-uri still reports, rate-limited");

    private static readonly Action<ILogger, string, Exception?> Totals = LoggerMessage.Define<string>(
        LogLevel.Information, new EventId(5, "CspReportTotals"), "Content Security Policy reports in this run: {Counts}");

    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly Func<CspViolation, string>? _advice;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Group> _groups = new(StringComparer.Ordinal);
    private readonly List<CspViolation> _batch = [];
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
    private long _reports;
    private long _refused;
    private long _lastSummary;

    /// <param name="advice">
    /// What a new group's line says would allow it (<see cref="CspAdvice.For"/>), in Development; null in Production.
    /// Hosts pass it behind <see cref="SparDevelopment.IsSupported"/>, so a Production image carries none of it.
    /// </param>
    public CspViolationLog(ILogger logger, TimeProvider time, Func<CspViolation, string>? advice)
    {
        _logger = logger;
        _time = time;
        _advice = advice;
        _lastSummary = time.GetTimestamp();
        _reader = Task.Run(ReadAsync);
        _timer = time.CreateTimer(_ => _queue.Writer.TryWrite(Work.Tick), null, SummaryInterval, SummaryInterval);
    }

    /// <summary>The most often a summary line is written.</summary>
    public static TimeSpan SummaryInterval { get; } = TimeSpan.FromSeconds(10);

    /// <summary>Reports and batches dropped because the queue was full, since the start.</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>A <c>report-uri</c> report the host answered without reading it, past its rate or size limit (any thread).</summary>
    public void NoteRefused() => Interlocked.Increment(ref _refused);

    /// <summary>A new document's bridge opened: its groups start afresh (any thread).</summary>
    public void PostDocument(string document) => Post(new Work(WorkKind.Document, document, default));

    /// <summary>A batch from the injected listener of <paramref name="document"/> (any thread; the payload is copied).</summary>
    public void PostBatch(string document, ReadOnlyMemory<byte> payload) =>
        Post(new Work(WorkKind.Batch, document, payload.ToArray()));

    /// <summary>A <c>report-uri</c> report's body (any thread; the array is the log's from now on).</summary>
    public void PostReport(byte[] body) => Post(new Work(WorkKind.Report, null, body));

    /// <summary>Writes what is still counted and ends the reader.</summary>
    public async ValueTask DisposeAsync()
    {
        await _timer.DisposeAsync();
        _queue.Writer.TryComplete();
        await _reader;
        Summarize();
        var batches = Interlocked.Read(ref _batches);
        var reports = Interlocked.Read(ref _reports);
        var refused = Interlocked.Read(ref _refused);
        if (batches + reports + refused + Dropped > 0)
        {
            var counts = string.Create(
                CultureInfo.InvariantCulture,
                $"{batches} listener batches, {reports} report-uri reports read, {refused} refused unread (rate or size), {Dropped} dropped (queue full)");
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

    /// <summary>Reads and records a listener batch from <paramref name="document"/>; a late one from an older document is ignored.</summary>
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
            if (!CspReportReader.TryReadBatch(payload, _batch, out var overflow, out var stopped))
            {
                Unreadable(_logger, "listener", null);
                return;
            }

            foreach (var violation in _batch)
            {
                Record(violation, fromListener: true);
            }

            _overflow += overflow;
            if (stopped && !_stoppedLogged)
            {
                _stoppedLogged = true;
                ListenerStopped(_logger, null);
            }
        }
    }

    /// <summary>Reads and records a <c>report-uri</c> report, for the current document.</summary>
    public void Report(ReadOnlySpan<byte> body)
    {
        lock (_gate)
        {
            _reports++;
            if (!CspReportReader.TryReadReport(body, out var violation))
            {
                Unreadable(_logger, "report-uri", null);
                return;
            }

            Record(violation, fromListener: false);
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
                switch (work.Kind)
                {
                    case WorkKind.Document:
                        OpenDocument(work.Document!);
                        break;
                    case WorkKind.Batch:
                        Batch(work.Document!, work.Body.Span);
                        break;
                    case WorkKind.Report:
                        Report(work.Body.Span);
                        break;
                    default:
                        Summarize();
                        break;
                }
            }
            catch (Exception exception)
            {
                // A log line must never stop the reader; the next report is read as usual.
                Unreadable(_logger, exception.GetType().Name, exception);
            }
        }
    }

    private void Record(CspViolation violation, bool fromListener)
    {
        if (_groups.TryGetValue(violation.Key, out var group))
        {
            group.Add(violation.Count, fromListener);
            return;
        }

        if (_groups.Count >= MaxGroupsPerDocument)
        {
            _overflow += violation.Count;
            if (!_tooManyLogged)
            {
                _tooManyLogged = true;
                TooMany(_logger, MaxGroupsPerDocument, null);
            }

            return;
        }

        group = new Group(violation);
        group.Add(violation.Count, fromListener);
        group.Logged = 1;
        _groups.Add(violation.Key, group);
        var advice = _advice is null ? "" : " " + _advice(violation);
        var sample = violation.Sample.Length > 0 ? $" (\"{violation.Sample}\")" : "";
        Violation(_logger, violation.Directive, Describe(violation.Blocked) + sample, violation.Where + advice, null);
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
                Separate(text).Append(group.Directive).Append(' ').Append(Describe(group.Blocked)).Append(": ").Append(group.More).Append(" more");
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

    private static string Describe(string blocked) => blocked.Length == 0 ? "(nothing named)" : blocked;

    private enum WorkKind
    {
        Document,
        Batch,
        Report,
        Tick,
    }

    /// <summary>A class, not a struct: the channel's code is then shared with the other channels of reference types.</summary>
    private sealed record Work(WorkKind Kind, string? Document, ReadOnlyMemory<byte> Body)
    {
        public static Work Tick { get; } = new(WorkKind.Tick, null, default);
    }

    /// <summary>One group's counts per channel; the larger is the group's, since both channels see the same violations.</summary>
    private sealed class Group(CspViolation first)
    {
        private long _listener;
        private long _endpoint;

        public string Directive { get; } = first.Directive;

        public string Blocked { get; } = first.Blocked;

        public long Count => Math.Max(_listener, _endpoint);

        /// <summary>The count already written, by the group's first line or a summary.</summary>
        public long Logged { get; set; }

        /// <summary>How much the count grew since the last summary, while one is written.</summary>
        public long More { get; set; }

        public void Add(long count, bool fromListener)
        {
            if (fromListener)
            {
                _listener += count;
            }
            else
            {
                _endpoint += count;
            }
        }
    }
}
