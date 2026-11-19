using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Spar.Bridge;
using Spar.Hosting;
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
/// hands what queued up to WebKit in one <c>evaluate_javascript</c> per turn.
/// </summary>
internal sealed class ScriptBridge
{
    /// <summary>The isolated script world of the relay and the handler; the page's own world has neither.</summary>
    public const string World = "spar";

    /// <summary>The script message handler's name, in <see cref="World"/> only.</summary>
    public const string Handler = "spar";

    private const string RelayResource = "Spar.Linux.BridgeRelay.js";

    private static ReadOnlySpan<byte> DeliverStart => "typeof sparRelay === \"object\" && sparRelay.deliver(["u8;

    private static ReadOnlySpan<byte> DeliverEnd => "]);"u8;

    private readonly MainLoop _ui;
    private readonly HostLog _log;
    private readonly Task<SparRunningApp> _app;
    private readonly bool _includeDetails;
    private readonly Channel<Incoming> _incoming = Channel.CreateUnbounded<Incoming>(new UnboundedChannelOptions { SingleReader = true });
    private readonly List<byte[]> _outgoing = [];
    private readonly Task _reader;
    private nint _view;
    private Document? _current;
    private bool _flushPosted;

    /// <param name="app">The app's services, being built: messages wait for them on the reader, never on the loop.</param>
    public ScriptBridge(MainLoop ui, HostLog log, Task<SparRunningApp> app, bool includeDetails)
    {
        _ui = ui;
        _log = log;
        _app = app;
        _includeDetails = includeDetails;
        _reader = Task.Run(ReadAsync);
    }

    /// <summary>Raised on the loop when the current document reports <c>ui-ready</c>.</summary>
    public event Action? DocumentReady;

    /// <summary>The current document's id, or null between documents (loop thread).</summary>
    public string? CurrentDocument => _current is { IsClosed: false } document ? document.Id : null;

    /// <summary>The relay's source (<c>BridgeRelay.js</c>, embedded): read from the executable's image, in memory.</summary>
    public static string RelayScript()
    {
        using var stream = typeof(ScriptBridge).Assembly.GetManifestResourceStream(RelayResource)
            ?? throw new InvalidOperationException($"The resource {RelayResource} is missing from Spar.Linux.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
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
            return;
        }

        var document = new Document(Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8)));
        _current = document;
        _log.Info($"Bridge: document {document.Id} opened.");
    }

    /// <summary>
    /// A script message from the relay (loop thread): the handler exists in the relay's world only, and the relay only
    /// in the top document of the app's origin, so a message is the current document's. Copied out and queued.
    /// </summary>
    public void OnMessage(string text)
    {
        if (_current is not { IsClosed: false } document)
        {
            _log.Warn("Bridge: a message arrived between documents and was ignored.");
            return;
        }

        _log.Phase("first bridge message");
        _incoming.Writer.TryWrite(new Incoming(document, text));
    }

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

            var session = new BridgeSession(
                app.Dispatcher, app.Catalog.Fingerprint, document.Id, message => Send(document, message), _includeDetails);
            session.Dropped += reason => _log.Warn($"Bridge, document {document.Id}: dropped {reason}");
            session.Ready += () => _ui.Post(() => Ready(document), "bridge ui-ready");
            session.ClosingDone += () => document.ClosingDone.TrySetResult();
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
