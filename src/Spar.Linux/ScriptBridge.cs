using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Spar.Bridge;
using Spar.Files;
using Spar.Hosting;
using Spar.HotReload;
using Spar.Linux.Interop;

namespace Spar.Linux;

/// <summary>
/// The bridge over WebKitGTK's script messages (plan section 2c), through the relay in the top document's isolated
/// world (<c>BridgeRelay.js</c>): one <see cref="BridgeSession"/> per document, strings only. A navigation starts a new
/// document epoch: the current document's calls are cancelled when a load starts, and its late replies dropped; the new
/// document opens when its load is committed, before any of its scripts can send, and only on the app's origin. A
/// message between the two is dropped. Messages are decoded and dispatched off the main loop, in the order they arrived,
/// once the app's services exist (a page may connect while they are still being built); replies are encoded as script
/// literals where they are produced, and the main loop only checks that their document is still the current one and
/// hands what queued up to WebKit in one <c>evaluate_javascript</c> per turn. The heartbeat (plan section 3, "Hung pages")
/// goes the same way, with no session: the relay answers each ping itself, and its answer is read on the loop.
/// </summary>
internal sealed class ScriptBridge
{
    /// <summary>The isolated script world of the relay and the handler; the page's own world has neither.</summary>
    public const string World = "spar";

    /// <summary>The script message handler's name, in <see cref="World"/> only.</summary>
    public const string Handler = "spar";

    private const string RelayResource = "Spar.Linux.BridgeRelay.js";

    /// <summary>The longest message read on the loop as a heartbeat answer; anything longer goes to the reader.</summary>
    private const int PongLimit = 128;

    /// <summary>How the relay's answers start; nothing else the page sends starts like that.</summary>
    private const string PongStart = "{\"type\":\"pong\",";

    /// <summary>How the relay's violation batches start (<c>BridgeRelay.js</c>); the page can send the same itself.</summary>
    private const string CspStart = "{\"type\":\"csp\"}\t";

    /// <summary>
    /// The longest violation batch read, in characters: the relay's hold at most 100 groups of texts cut to 256
    /// characters, well under this; a longer one is dropped unread.
    /// </summary>
    private const int MaxBatchLength = 256 * 1024;

    private static ReadOnlySpan<byte> DeliverStart => "typeof sparRelay === \"object\" && sparRelay.deliver(["u8;

    private static ReadOnlySpan<byte> DeliverEnd => "]);"u8;

    private readonly MainLoop _ui;
    private readonly HostLog _log;
    private readonly Task<SparRunningApp> _app;
    private readonly bool _includeDetails;
    private readonly CspViolationLog? _violations;
    private readonly Channel<Incoming> _incoming = Channel.CreateUnbounded<Incoming>(new UnboundedChannelOptions { SingleReader = true });
    private readonly List<byte[]> _outgoing = [];
    private readonly Task _reader;
    private readonly IDisposable? _backendUpdates;
    private readonly Func<bool?, Task> _chooseScheme;
    private HostCommands? _hostCommands;
    private readonly Func<SparRunningApp, HostWindow> _makeWindow;
    private HostWindow? _window;
    private nint _view;
    private Document? _current;
    private bool _flushPosted;

    /// <param name="app">The app's services, being built: messages wait for them on the reader, never on the loop.</param>
    /// <param name="violations">Where the relay's violation batches go; null when the listener does not report in this build.</param>
    /// <param name="chooseScheme">The window taking the page's <c>setColorScheme</c>, completing once it has (any thread).</param>
    public ScriptBridge(
        MainLoop ui, HostLog log, Task<SparRunningApp> app, bool includeDetails, CspViolationLog? violations, Func<bool?, Task> chooseScheme,
        Func<SparRunningApp, HostWindow> window)
    {
        _makeWindow = window;
        _chooseScheme = chooseScheme;
        _ui = ui;
        _log = log;
        _app = app;
        _includeDetails = includeDetails;
        _violations = violations;
        _reader = Task.Run(ReadAsync);
        if (BackendUpdates.IsSupported)
        {
            _backendUpdates = BackendUpdates.Listen(update => _ui.Post(() => SendBackendUpdate(update), "backend update"));
        }
    }

    /// <summary>Raised on the loop when the current document reports <c>ui-ready</c>.</summary>
    /// <summary>The window's grants, dialogs and drops, made with its first document's session; null before it.</summary>
    public HostWindow? Window => Volatile.Read(ref _window);

    public event Action? DocumentReady;

    /// <summary>
    /// Raised on the loop when a new document is committed, before any of its scripts runs: true on the app's origin,
    /// where the relay answers the heartbeat (plan section 3, "Hung pages"), false for a document anywhere else.
    /// </summary>
    public event Action<bool>? DocumentOpened;

    /// <summary>Raised on the loop when the current document's epoch ends (a load, the web process ending, closing).</summary>
    public event Action? DocumentClosed;

    /// <summary>
    /// Raised on the loop with the <c>seq</c> of each <c>pong</c> from the current document, and whether the page shows
    /// a modal dialog of its own. The relay answers, so a document answers before its page connects.
    /// </summary>
    public event Action<long, bool>? Answered;

    /// <summary>
    /// Raised on the loop with the <c>seq</c> of each <c>close-answer</c> from the current document, read, and why it
    /// could not be read (null when it could). An answer from a document that has since gone is dropped here.
    /// </summary>
    public event Action<long, PageCloseAnswer, string?>? CloseAnswered;

    /// <summary>The current document's id, or null between documents (loop thread).</summary>
    public string? CurrentDocument => _current is { IsClosed: false } document ? document.Id : null;

    /// <summary>
    /// The relay's source (<c>BridgeRelay.js</c>, embedded), read from the executable's image, with the violation
    /// listener's settings written in: the same flood limits as the Windows host's script.
    /// </summary>
    /// <param name="reportViolations">Whether the listener reports (<c>csp.Reporting(listener: …)</c> for this build).</param>
    public static string RelayScript(bool reportViolations)
    {
        using var stream = typeof(ScriptBridge).Assembly.GetManifestResourceStream(RelayResource)
            ?? throw new InvalidOperationException($"The resource {RelayResource} is missing from Spar.Linux.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd()
            .Replace("__SPAR_REPORT_VIOLATIONS__", reportViolations ? "true" : "false", StringComparison.Ordinal)
            .Replace("__SPAR_REPORT_INTERVAL_MS__", Number(CspViolationLog.ListenerIntervalMilliseconds), StringComparison.Ordinal)
            .Replace("__SPAR_REPORT_FIRST_DELAY_MS__", Number(CspViolationLog.ListenerFirstDelayMilliseconds), StringComparison.Ordinal)
            .Replace("__SPAR_REPORT_MAX_GROUPS__", Number(CspViolationLog.ListenerMaxGroups), StringComparison.Ordinal)
            .Replace("__SPAR_REPORT_MAX_VIOLATIONS__", Number(CspViolationLog.ListenerMaxViolations), StringComparison.Ordinal);
    }

    /// <summary>The view whose messages this bridge carries (loop thread); 0 once it is gone.</summary>
    public void Attach(nint view) => _view = view;

    /// <summary>The view is going away (shutdown): its document ends with it (loop thread).</summary>
    public void Detach(string why)
    {
        CloseCurrent(why);
        _view = 0;
    }

    /// <summary>A load started (<c>WEBKIT_LOAD_STARTED</c>): the current document's epoch ends (loop thread).</summary>
    public void OnLoadStarted() => CloseCurrent("navigation");

    /// <summary>The web process ended: its document is gone with it (loop thread).</summary>
    public void OnWebProcessEnded() => CloseCurrent("web process ended");

    /// <summary>
    /// The new document is committed (<c>WEBKIT_LOAD_COMMITTED</c>): its epoch opens before any of its scripts runs, on the
    /// app's origin only; a document anywhere else has no relay and gets no session (loop thread).
    /// </summary>
    public void OnLoadCommitted(string address)
    {
        CloseCurrent("new document");
        if (!SchemeServer.IsAppOrigin(address))
        {
            _log.Warn("Bridge: a document outside the app's origin was committed; it has no bridge.");
            DocumentOpened?.Invoke(false);
            return;
        }

        var document = new Document(Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8)));
        _current = document;
        _violations?.PostDocument(document.Id);
        _log.Info($"Bridge: document {document.Id} opened.");
        DocumentOpened?.Invoke(true);
    }

    /// <summary>
    /// A script message from the relay (loop thread): the handler exists in the relay's world only, and the relay only
    /// in the top document of the app's origin, so a message is the current document's. A heartbeat answer is read here,
    /// before the document has a session and whatever the reader is waiting for; anything else is copied out and queued.
    /// </summary>
    public void OnMessage(string text)
    {
        if (_current is not { IsClosed: false } document)
        {
            _log.Warn("Bridge: a message arrived between documents and was ignored.");
            return;
        }

        if (text.Length <= PongLimit && text.StartsWith(PongStart, StringComparison.Ordinal) && Pong(text))
        {
            return;
        }

        _log.Phase("first bridge message");
        _incoming.Writer.TryWrite(new Incoming(document, text));
    }

    /// <summary>
    /// The heartbeat's ping to the current document (loop thread), which the relay answers: false when there is none.
    /// It needs no session, so a page that hangs before it connects is pinged too.
    /// </summary>
    public bool Ping(long seq)
    {
        if (_current is not { IsClosed: false } document)
        {
            return false;
        }

        Deliver(document, Literal(BridgeCodec.EncodeToString(new BridgeMessage(BridgeMessageType.Ping) { Seq = seq })));
        return true;
    }

    /// <summary>
    /// Asks the current document's close guards (loop thread): false when there is no document, it has not connected
    /// yet, or its page did not list <c>close-request</c>. The answer arrives through <see cref="CloseAnswered"/>.
    /// </summary>
    public bool RequestClose(long seq, CloseReason reason, bool automated) =>
        SessionOfCurrent() is { } session && session.RequestClose(seq, PageCloseAnswer.RequestPayload(reason, automated));

    /// <summary>The close request <paramref name="seq"/> ended without the page's answer: its guards' signal is aborted (loop thread).</summary>
    public void EndClose(long seq) => SessionOfCurrent()?.EndClose(seq);

    /// <summary>
    /// The page's close work (the <c>closing</c> message): the page runs it and answers <c>closing-done</c>; the host
    /// waits at most <paramref name="limit"/>, keeping the loop running (loop thread).
    /// </summary>
    public async Task RunPageCloseWorkAsync(TimeSpan limit)
    {
        if (_current is not { IsReady: true, IsClosed: false, Session: { } session } document)
        {
            return;
        }

        session.RequestClosing();
        var done = await Task.WhenAny(document.ClosingDone.Task, Task.Delay(limit));
        _log.Step(done == document.ClosingDone.Task ? "page close work done" : "page close work timed out");
    }

    /// <summary>Stops admitting calls: the current document's session closes, which cancels its calls (loop thread).</summary>
    public Task CloseAsync(string why)
    {
        var running = _current?.Session?.WhenIdleAsync() ?? Task.CompletedTask;
        CloseCurrent(why);
        return running;
    }

    /// <summary>Ends the reader once every queued message was handled; the host calls it last.</summary>
    public async Task StopAsync()
    {
        _backendUpdates?.Dispose();
        _incoming.Writer.TryComplete();
        await _reader;
    }

    /// <summary>
    /// One host message as a JavaScript string literal in UTF-8, followed by a comma: double quotes, with backslash,
    /// quote, control characters, U+2028, U+2029 and lone surrogates escaped, so the literal is valid UTF-8 and reads
    /// back as exactly <paramref name="message"/>. Done where the message is produced, off the loop.
    /// </summary>
    internal static byte[] Literal(string message)
    {
        var text = new StringBuilder(message.Length + 8);
        text.Append('"');
        for (var i = 0; i < message.Length; i++)
        {
            var c = message[i];
            switch (c)
            {
                case '"':
                    text.Append("\\\"");
                    break;
                case '\\':
                    text.Append("\\\\");
                    break;
                case < ' ' or (char)0x2028 or (char)0x2029:
                    AppendEscape(text, c);
                    break;
                default:
                    if (char.IsHighSurrogate(c) && i + 1 < message.Length && char.IsLowSurrogate(message[i + 1]))
                    {
                        text.Append(c).Append(message[++i]);
                    }
                    else if (char.IsSurrogate(c))
                    {
                        AppendEscape(text, c);
                    }
                    else
                    {
                        text.Append(c);
                    }

                    break;
            }
        }

        text.Append("\",");
        return Encoding.UTF8.GetBytes(text.ToString());
    }

    /// <summary>The script that hands <paramref name="literals"/> to the relay, in order, as one UTF-8 buffer.</summary>
    internal static byte[] DeliverScript(IReadOnlyList<byte[]> literals)
    {
        var length = DeliverStart.Length + DeliverEnd.Length;
        foreach (var literal in literals)
        {
            length += literal.Length;
        }

        var script = new byte[length];
        var at = 0;
        DeliverStart.CopyTo(script);
        at += DeliverStart.Length;
        foreach (var literal in literals)
        {
            literal.CopyTo(script, at);
            at += literal.Length;
        }

        DeliverEnd.CopyTo(script.AsSpan(at));
        return script;
    }

    private static string Number(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static void AppendEscape(StringBuilder text, char c) =>
        text.Append("\\u").Append(((int)c).ToString("x4", System.Globalization.CultureInfo.InvariantCulture));

    private void CloseCurrent(string why)
    {
        if (_current is not { } document)
        {
            return;
        }

        _current = null;
        BridgeSession? session;
        lock (document.Gate)
        {
            document.IsClosed = true;
            session = document.Session;
        }

        session?.Close();
        document.ClosingDone.TrySetResult();
        _log.Info($"Bridge: document {document.Id} closed ({why}).");
        DocumentClosed?.Invoke();
    }

    /// <summary>A heartbeat answer read on the loop; false when it is not one after all, so the session refuses it.</summary>
    private bool Pong(string text)
    {
        if (!BridgeCodec.TryDecode(text, out var decoded, out _) || decoded is not { Type: BridgeMessageType.Pong, Seq: { } seq })
        {
            return false;
        }

        Answered?.Invoke(seq, BridgeSession.ShowsDialog(decoded.Payload.Span));
        return true;
    }

    /// <summary>
    /// A Hot Reload update was applied (Development builds only): the current document gets <c>backend-updated</c>
    /// (loop thread). A page that has not connected yet misses it; it runs the patched code anyway.
    /// </summary>
    private void SendBackendUpdate(BackendUpdate update)
    {
        var session = SessionOfCurrent();
        session?.SendEvent(BackendUpdates.EventName, update.Payload);
        var sent = session is null ? "no document connected" : $"sent to document {_current?.Id}";
        _log.Info($"Hot Reload applied revision {update.Revision} ({update.TypeList}); {sent}.");
    }

    /// <summary>The current document's session, if it has connected (loop thread).</summary>
    private BridgeSession? SessionOfCurrent()
    {
        if (_current is not { IsClosed: false } document)
        {
            return null;
        }

        lock (document.Gate)
        {
            return document.Session;
        }
    }

    private void ClosingAnswered(Document document, long seq, PageCloseAnswer answer, string? problem)
    {
        if (document == _current && !document.IsClosed)
        {
            CloseAnswered?.Invoke(seq, answer, problem);
        }
    }

    /// <summary>The document's session, created by the reader for its first message once the services exist.</summary>
    private BridgeSession? SessionOf(Document document, SparRunningApp app)
    {
        lock (document.Gate)
        {
            if (document.IsClosed)
            {
                return null;
            }

            if (document.Session is { } existing)
            {
                return existing;
            }

            // One for the window, so the page's colour scheme choices are taken in the order sent, across documents.
            _hostCommands ??= new HostCommands(app.Folders.DataRoot, _chooseScheme, _log.Logger);
            _window ??= _makeWindow(app);
            var session = new BridgeSession(
                app.Dispatcher, app.Catalog.Fingerprint, document.Id, message => Send(document, message), _includeDetails)
            {
                HostCommands = _hostCommands,
                Window = _window,
            };
            session.Dropped += reason => _log.Warn($"Bridge, document {document.Id}: dropped {reason}");
            session.Ready += () => _ui.Post(() => Ready(document), "bridge ui-ready");
            session.ClosingDone += () => document.ClosingDone.TrySetResult();
            session.CloseAnswered += (seq, payload) =>
            {
                // Read on the reader task; only the small result crosses to the loop.
                var answer = PageCloseAnswer.Read(payload.Span, out var problem);
                _ui.Post(() => ClosingAnswered(document, seq, answer, problem), "close answer");
            };
            document.Session = session;
            return session;
        }
    }

    private void Ready(Document document)
    {
        if (document != _current || document.IsClosed)
        {
            return;
        }

        document.IsReady = true;
        _log.Phase(HostPhases.UiReady);
        _log.Info($"Bridge: document {document.Id} reported ui-ready.");
        DocumentReady?.Invoke();
    }

    /// <summary>The session's send function, called on any thread: encoded here, then checked and queued on the loop.</summary>
    private void Send(Document document, string message)
    {
        var literal = Literal(message);
        _ui.Post(() => Deliver(document, literal), "bridge reply");
    }

    /// <summary>A reply reaches the page only if its document is still the current one (late replies are dropped).</summary>
    private void Deliver(Document document, byte[] literal)
    {
        if (document.IsClosed || document != _current || _view == 0)
        {
            return;
        }

        _outgoing.Add(literal);
        if (!_flushPosted)
        {
            // After the replies already queued on the loop: one script carries the whole burst.
            _flushPosted = true;
            _ui.Post(Flush, "bridge flush");
        }
    }

    private unsafe void Flush()
    {
        _flushPosted = false;
        if (_outgoing.Count == 0 || _view == 0)
        {
            _outgoing.Clear();
            return;
        }

        var script = DeliverScript(_outgoing);
        _outgoing.Clear();
        fixed (byte* text = script)
        {
            // WebKit copies the script before this returns.
            WebKit.webkit_web_view_evaluate_javascript(_view, text, script.Length, World, 0, 0, 0, 0);
        }

        _log.Phase("first bridge reply");
    }

    /// <summary>
    /// A violation batch of the relay's listener (reader task), as Windows' session hands its <c>csp</c> messages on:
    /// decoded, its payload copied to the <see cref="CspViolationLog"/>, which parses it untrusted on its own task.
    /// Dropped when this build's listener does not report, or when it is too long to be one.
    /// </summary>
    private void Violations(Document document, string text)
    {
        if (_violations is null || text.Length > MaxBatchLength)
        {
            return;
        }

        if (BridgeCodec.TryDecode(text, out var decoded, out _) && decoded is { Type: BridgeMessageType.Csp })
        {
            _violations.PostBatch(document.Id, decoded.Payload);
        }
    }

    /// <summary>The reader: waits for the app's services, then decodes and admits each message off the loop, in order.</summary>
    private async Task ReadAsync()
    {
        SparRunningApp? app = null;
        await foreach (var (document, text) in _incoming.Reader.ReadAllAsync())
        {
            if (document.IsClosed)
            {
                continue;
            }

            try
            {
                if (text.StartsWith(CspStart, StringComparison.Ordinal))
                {
                    // Needs no session, so a page that never connects still reports; read on this task, never the loop.
                    Violations(document, text);
                    continue;
                }

                app ??= await _app;
                SessionOf(document, app)?.Receive(text);
            }
            catch (Exception exception)
            {
                _log.Error($"Bridge, document {document.Id}: a message could not be handled", exception);
            }
        }
    }

    private readonly record struct Incoming(Document Document, string Text);

    /// <summary>One document's epoch: its session once it has one, and whether it is still the page's.</summary>
    private sealed class Document(string id)
    {
        private volatile bool _closed;

        public string Id { get; } = id;

        /// <summary>Guards the session's creation by the reader against the document's close on the loop.</summary>
        public Lock Gate { get; } = new();

        public BridgeSession? Session { get; set; }

        public bool IsClosed
        {
            get => _closed;
            set => _closed = value;
        }

        public bool IsReady { get; set; }

        public TaskCompletionSource ClosingDone { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
