using System.Security.Cryptography;
using System.Threading.Channels;
using Spar.Bridge;
using Spar.Runtime;
using Spar.Windows.Interop;

namespace Spar.Windows;

/// <summary>
/// The bridge over WebView2 web messages (plan section 2c): one <see cref="BridgeSession"/> per document, strings only
/// (<c>PostWebMessageAsString</c>), and messages accepted only from the top frame of <c>https://app.localhost</c>.
/// A navigation starts a new document epoch: the old document's calls are cancelled at once and its late replies
/// dropped, and the new session opens when the new document starts loading, so a message still in flight from the old
/// page never reaches the new one. Messages are decoded and dispatched off the UI thread, in the order they arrived;
/// replies are posted back to it and checked against the current document before they are sent.
/// </summary>
internal sealed class WebViewBridge
{
    private readonly UiThread _ui;
    private readonly HostLog _log;
    private readonly SparDispatcher _dispatcher;
    private readonly string _fingerprint;
    private readonly bool _includeDetails;
    private readonly Channel<Incoming> _incoming = Channel.CreateUnbounded<Incoming>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _reader;
    private ICoreWebView2_22? _webView;
    private Document? _current;

    public WebViewBridge(UiThread ui, HostLog log, SparDispatcher dispatcher, string fingerprint, bool includeDetails)
    {
        _ui = ui;
        _log = log;
        _dispatcher = dispatcher;
        _fingerprint = fingerprint;
        _includeDetails = includeDetails;
        _reader = Task.Run(ReadAsync);
    }

    /// <summary>Raised on the UI thread when the current document reports <c>ui-ready</c>.</summary>
    public event Action? DocumentReady;

    /// <summary>
    /// Raised on the UI thread when the current document's page said <c>hello</c>: true when it answers the heartbeat's
    /// <c>ping</c> (plan section 3, "Hung pages"). This is the first thing the page's start-up script does.
    /// </summary>
    public event Action<bool>? DocumentOpened;

    /// <summary>Raised on the UI thread when the current document's epoch ends (navigation, renderer exit, closing).</summary>
    public event Action? DocumentClosed;

    /// <summary>Raised on the UI thread with the <c>seq</c> of each <c>pong</c> from the current document.</summary>
    public event Action<long>? Answered;

    /// <summary>Whether a document is open and reported <c>ui-ready</c>.</summary>
    public bool HasReadyDocument => _current is { IsReady: true, IsClosed: false };

    /// <summary>The WebView whose messages this bridge carries; replaced when the browser is recreated.</summary>
    public void Attach(ICoreWebView2_22 webView) => _webView = webView;

    /// <summary>The WebView is going away (recovery or shutdown): its document ends with it.</summary>
    public void Detach(string why)
    {
        CloseCurrent(why);
        _webView = null;
    }

    /// <summary>A navigation started: the current document's epoch ends (UI thread).</summary>
    public void OnNavigationStarting() => CloseCurrent("navigation");

    /// <summary>The new document is loading: its session opens, before any of its scripts can send (UI thread).</summary>
    public void OnContentLoading()
    {
        CloseCurrent("new document");
        var id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        var document = new Document(id);
        var session = new BridgeSession(_dispatcher, _fingerprint, id, message => Send(document, message), _includeDetails);
        session.Dropped += reason => _log.Warn($"Bridge, document {id}: dropped {reason}");
        session.Ready += () => _ui.Post(() => Ready(document), "bridge ui-ready");
        session.ClosingDone += () => document.ClosingDone.TrySetResult();
        session.Opened += ping => _ui.Post(() => Opened(document, ping), "bridge open");
        session.Pong += seq => _ui.Post(() => Ponged(document, seq), "heartbeat answer");
        document.Session = session;
        _current = document;
        _log.Info($"Bridge: document {id} opened.");
    }

    /// <summary>A web message from the page (UI thread): checked, copied out, and queued for the reader.</summary>
    public void OnMessage(nint argsPointer)
    {
        var args = Com.Borrow<ICoreWebView2WebMessageReceivedEventArgs>(argsPointer);
        try
        {
            // The top frame's document only: frames post through their own events, which the host never subscribes to.
            var source = args.GetSource();
            if (!PageServer.IsAppOrigin(source))
            {
                _log.Warn("Bridge: a message from outside the app's origin was ignored.");
                return;
            }

            if (args.TryGetWebMessageAsString(out var text) < 0 || text is null)
            {
                _log.Warn("Bridge: a message that is not a string, or has a lone surrogate, was ignored.");
                return;
            }

            if (_current is not { IsClosed: false } document)
            {
                _log.Warn("Bridge: a message arrived between documents and was ignored.");
                return;
            }

            _log.Phase("first bridge message");
            _incoming.Writer.TryWrite(new Incoming(document, text));
        }
        finally
        {
            Com.Release(args);
        }
    }

    /// <summary>
    /// The page's close work (the <c>closing</c> message): the page runs it and answers <c>closing-done</c>; the host
    /// waits at most <paramref name="limit"/>, keeping its thread pumping (UI thread).
    /// </summary>
    public async Task RunPageCloseWorkAsync(TimeSpan limit)
    {
        if (_current is not { IsReady: true, IsClosed: false } document)
        {
            return;
        }

        document.Session.RequestClosing();
        var done = await Task.WhenAny(document.ClosingDone.Task, Task.Delay(limit));
        _log.Step(done == document.ClosingDone.Task ? "page close work done" : "page close work timed out");
    }

    /// <summary>
    /// The heartbeat's ping to the current document (UI thread); false when there is none, or its page does not answer
    /// pings. The ping is encoded here and posted with the next drain of the UI thread's queue.
    /// </summary>
    public bool Ping(long seq) => _current is { IsClosed: false } document && document.Session.Ping(seq);

    /// <summary>Stops admitting calls: the current document's session closes, which cancels its calls (UI thread).</summary>
    public Task CloseAsync(string why)
    {
        var running = _current?.Session.WhenIdleAsync() ?? Task.CompletedTask;
        CloseCurrent(why);
        return running;
    }

    /// <summary>Ends the reader once every queued message was handled; the host calls it last.</summary>
    public async Task StopAsync()
    {
        _incoming.Writer.TryComplete();
        await _reader;
    }

    private void CloseCurrent(string why)
    {
        if (_current is not { } document)
        {
            return;
        }

        _current = null;
        document.IsClosed = true;
        document.Session.Close();
        document.ClosingDone.TrySetResult();
        _log.Info($"Bridge: document {document.Id} closed ({why}).");
        DocumentClosed?.Invoke();
    }

    private void Opened(Document document, bool answersPing)
    {
        if (document == _current && !document.IsClosed)
        {
            DocumentOpened?.Invoke(answersPing);
        }
    }

    private void Ponged(Document document, long seq)
    {
        if (document == _current && !document.IsClosed)
        {
            Answered?.Invoke(seq);
        }
    }

    private void Ready(Document document)
    {
        if (document != _current || document.IsClosed)
        {
            return;
        }

        document.IsReady = true;
        _log.Phase("ui-ready");
        _log.Info($"Bridge: document {document.Id} reported ui-ready.");
        DocumentReady?.Invoke();
    }

    /// <summary>The session's send function, called on any thread: the reply goes to the UI thread, to be checked there.</summary>
    private void Send(Document document, string message) => _ui.Post(() => Deliver(document, message), "bridge reply");

    /// <summary>A reply reaches the page only if its document is still the current one (late replies are dropped).</summary>
    private void Deliver(Document document, string message)
    {
        if (document.IsClosed || document != _current || _webView is null)
        {
            return;
        }

        try
        {
            _webView.PostWebMessageAsString(message);
            _log.Phase("first bridge reply");
        }
        catch (Exception exception)
        {
            _log.Warn($"Bridge: a message to document {document.Id} could not be posted ({exception.Message}).");
        }
    }

    /// <summary>The reader: decodes and admits each message off the UI thread, in arrival order.</summary>
    private async Task ReadAsync()
    {
        await foreach (var (document, text) in _incoming.Reader.ReadAllAsync())
        {
            if (document.IsClosed)
            {
                continue;
            }

            try
            {
                document.Session.Receive(text);
            }
            catch (Exception exception)
            {
                _log.Error($"Bridge, document {document.Id}: a message could not be handled", exception);
            }
        }
    }

    private readonly record struct Incoming(Document Document, string Text);

    /// <summary>One document's epoch: its session, and whether it is still the page's.</summary>
    private sealed class Document(string id)
    {
        private volatile bool _closed;

        public string Id { get; } = id;

        public BridgeSession Session { get; set; } = null!;

        public bool IsClosed
        {
            get => _closed;
            set => _closed = value;
        }

        public bool IsReady { get; set; }

        public TaskCompletionSource ClosingDone { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
