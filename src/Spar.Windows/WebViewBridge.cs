using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Channels;
using Spar.Bridge;
using Spar.Files;
using Spar.Hosting;
using Spar.HotReload;
using Spar.Logging;
using Spar.Runtime;
using Spar.Windows.Interop;

namespace Spar.Windows;

/// <summary>
/// The bridge over WebView2 web messages (plan section 2c): one <see cref="BridgeSession"/> per document, strings only
/// (<c>PostWebMessageAsString</c>), and messages accepted only from the top frame of <c>https://app.localhost</c>.
/// A navigation starts a new document epoch: the old document's calls are cancelled at once and its late replies
/// dropped, and the new session opens when the new document starts loading, so a message still in flight from the old
/// page never reaches the new one. Messages are decoded and dispatched off the UI thread, in the order they arrived;
/// replies are posted back to it and checked against the current document before they are sent. Every document of the
/// app's origin carries the host's injected script (<see cref="PageScript"/>), which answers the heartbeat from the
/// document's creation and posts the page's Content Security Policy violations and errors, handed to the
/// <see cref="CspViolationLog"/> and the <see cref="PageErrorLog"/> off the UI thread.
/// </summary>
internal sealed class WebViewBridge
{
    // The IIDs of the [Guid] attributes, written out so nothing looks a type's GUID up at run time.
    private static readonly Guid MessageArgs2Iid = new("06fc7ab7-c90c-4297-9389-33ca01cf6d5e");
    private static readonly Guid FileIid = new("f2c19559-6bc1-4583-a757-90021be9afec");

    private readonly UiThread _ui;
    private readonly HostLog _log;
    private readonly SparDispatcher _dispatcher;
    private readonly string _fingerprint;
    private readonly bool _includeDetails;
    private readonly CspViolationLog? _violations;
    private readonly PageErrorLog _pageErrors;
    private readonly HostCommands _hostCommands;
    private readonly HostWindow _window;
    private readonly Channel<Incoming> _incoming = Channel.CreateUnbounded<Incoming>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _reader;
    private readonly IDisposable? _backendUpdates;
    private ICoreWebView2_22? _webView;
    private Document? _current;

    /// <param name="violations">Where the listener's batches go; null when the listener does not report in this build.</param>
    /// <param name="pageErrors">Where the page's error batches go.</param>
    /// <param name="hostCommands">The window's own commands every document may call, such as <c>setColorScheme</c>.</param>
    /// <param name="window">The window's grants and file dialogs, which every document of the window shares.</param>
    public WebViewBridge(
        UiThread ui,
        HostLog log,
        SparDispatcher dispatcher,
        string fingerprint,
        bool includeDetails,
        CspViolationLog? violations,
        PageErrorLog pageErrors,
        HostCommands hostCommands,
        HostWindow window)
    {
        _hostCommands = hostCommands;
        _window = window;
        _ui = ui;
        _log = log;
        _dispatcher = dispatcher;
        _fingerprint = fingerprint;
        _includeDetails = includeDetails;
        _violations = violations;
        _pageErrors = pageErrors;
        _reader = Task.Run(ReadAsync);
        if (BackendUpdates.IsSupported)
        {
            _backendUpdates = BackendUpdates.Listen(update => _ui.Post(() => SendBackendUpdate(update), "backend update"));
        }
    }

    /// <summary>Raised on the UI thread when the current document reports <c>ui-ready</c>.</summary>
    public event Action? DocumentReady;

    /// <summary>
    /// Raised on the UI thread when a new document is created, before any of its scripts runs: true when the host's
    /// injected script answers its heartbeat (plan section 3, "Hung pages"), false for an error page, which has none.
    /// </summary>
    public event Action<bool>? DocumentOpened;

    /// <summary>Raised on the UI thread when the current document's epoch ends (navigation, renderer exit, closing).</summary>
    public event Action? DocumentClosed;

    /// <summary>
    /// Raised on the UI thread with the <c>seq</c> of each <c>pong</c> from the current document, and whether the page
    /// said it shows a modal dialog of its own.
    /// </summary>
    public event Action<long, bool>? Answered;

    /// <summary>
    /// Raised on the UI thread with the <c>seq</c> of each <c>close-answer</c> from the current document, read, and why
    /// it could not be read (null when it could). An answer from a document that has since gone is dropped here.
    /// </summary>
    public event Action<long, PageCloseAnswer, string?>? CloseAnswered;

    /// <summary>Whether a document is open and reported <c>ui-ready</c>.</summary>
    public bool HasReadyDocument => _current is { IsReady: true, IsClosed: false };

    /// <summary>The current document's id, or null between documents (UI thread).</summary>
    public string? CurrentDocument => _current is { IsClosed: false } document ? document.Id : null;

    /// <summary>Where the page's errors go, for the Development build's console events.</summary>
    public PageErrorLog PageErrors => _pageErrors;

    /// <summary>The WebView whose messages this bridge carries; replaced when the browser is recreated.</summary>
    public void Attach(ICoreWebView2_22 webView) => _webView = webView;

    /// <summary>The page's zoom changed: the current document hears it (UI thread); one between documents gets it as it opens.</summary>
    public void SendZoom(double factor)
    {
        if (_current is { IsClosed: false } document)
        {
            document.Session.SendEvent(AppWindows.WindowCommands.ZoomEvent, AppWindows.WindowCommands.ZoomPayload(factor));
        }
    }

    /// <summary>The WebView is going away (recovery or shutdown): its document ends with it.</summary>
    public void Detach(string why)
    {
        CloseCurrent(why);
        _webView = null;
    }

    /// <summary>A navigation started: the current document's epoch ends (UI thread).</summary>
    public void OnNavigationStarting() => CloseCurrent("navigation");

    /// <summary>The page's renderer exited: its document is gone with it (UI thread).</summary>
    public void OnRendererExited() => CloseCurrent("renderer exited");

    /// <summary>
    /// The new document is loading: its session opens, before any of its scripts can send, and the heartbeat is armed
    /// for it unless it is an error page (UI thread).
    /// </summary>
    public void OnContentLoading(bool isErrorPage)
    {
        CloseCurrent("new document");
        var id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        var document = new Document(id);
        var session = new BridgeSession(_dispatcher, _fingerprint, id, message => Send(document, message), _includeDetails)
        {
            HostAnswersPing = !isErrorPage,
            HostCommands = _hostCommands,
            Window = _window,
        };
        session.Dropped += reason => _log.Warn($"Bridge, document {id}: dropped {reason}");
        session.Ready += () => _ui.Post(() => Ready(document), "bridge ui-ready");
        session.ClosingDone += () => document.ClosingDone.TrySetResult();
        session.Pong += (seq, dialog) => _ui.Post(() => Ponged(document, seq, dialog), "heartbeat answer");
        session.CloseAnswered += (seq, payload) =>
        {
            // Read on the reader task; only the small result crosses to the UI thread.
            var answer = PageCloseAnswer.Read(payload.Span, out var problem);
            _ui.Post(() => ClosingAnswered(document, seq, answer, problem), "close answer");
        };
        if (_violations is { } violations)
        {
            // On the reader task: the batch is copied and queued; it is parsed and logged on the log's own task.
            session.Violations += payload => violations.PostBatch(id, payload);
            violations.PostDocument(id);
        }

        // The same for the page's errors, which the injected script always reports.
        var pageErrors = _pageErrors;
        session.PageErrors += payload => pageErrors.PostBatch(id, payload);
        pageErrors.PostDocument(id);

        document.Session = session;
        _current = document;
        _log.Info($"Bridge: document {id} opened{(isErrorPage ? " (an error page)" : "")}.");
        DocumentOpened?.Invoke(!isErrorPage);
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
            _incoming.Writer.TryWrite(new Incoming(document, text, AttachedFiles(argsPointer)));
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

    /// <summary>
    /// Asks the current document's close guards (UI thread): false when there is no document, or its page did not list
    /// <c>close-request</c> (it has no SDK, or an older one). The answer arrives through <see cref="CloseAnswered"/>.
    /// </summary>
    public bool RequestClose(long seq, CloseReason reason, bool automated) =>
        _current is { IsClosed: false } document && document.Session.RequestClose(seq, PageCloseAnswer.RequestPayload(reason, automated));

    /// <summary>The close request <paramref name="seq"/> ended without the page's answer: its guards' signal is aborted (UI thread).</summary>
    public void EndClose(long seq)
    {
        if (_current is { IsClosed: false } document)
        {
            document.Session.EndClose(seq);
        }
    }

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
        _backendUpdates?.Dispose();
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

    /// <summary>
    /// A Hot Reload update was applied (Development builds only): the current document gets <c>backend-updated</c> (UI
    /// thread). A page between documents misses it; the next one runs the patched code anyway.
    /// </summary>
    private void SendBackendUpdate(BackendUpdate update)
    {
        var document = _current is { IsClosed: false } current ? current : null;
        document?.Session.SendEvent(BackendUpdates.EventName, update.Payload);
        var sent = document is null ? "no document open" : $"sent to document {document.Id}";
        _log.Info($"Hot Reload applied revision {update.Revision} ({update.TypeList}); {sent}.");
    }

    private void Ponged(Document document, long seq, bool dialog)
    {
        if (document == _current && !document.IsClosed)
        {
            Answered?.Invoke(seq, dialog);
        }
    }

    private void ClosingAnswered(Document document, long seq, PageCloseAnswer answer, string? problem)
    {
        if (document == _current && !document.IsClosed)
        {
            CloseAnswered?.Invoke(seq, answer, problem);
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
        await foreach (var (document, text, files) in _incoming.Reader.ReadAllAsync())
        {
            if (document.IsClosed)
            {
                continue;
            }

            try
            {
                if (files is null)
                {
                    document.Session.Receive(text);
                }
                else
                {
                    document.Session.Receive(text, files);
                }
            }
            catch (Exception exception)
            {
                _log.Error($"Bridge, document {document.Id}: a message could not be handled", exception);
            }
        }
    }

    /// <summary>
    /// The paths of the files a message carries (UI thread): only <c>postMessageWithAdditionalObjects</c> gives a
    /// message objects, and the browser accepts only files on disk that the user gave the page (a drop, a picker), never
    /// one the page made (the spike: "additional File object is not a file on the disk"). Null for a message without
    /// any; other objects, such as a directory handle, are skipped, since a folder's <c>File</c> carries its path.
    /// </summary>
    private static IReadOnlyList<string>? AttachedFiles(nint argsPointer)
    {
        var argsIid = MessageArgs2Iid;
        if (Marshal.QueryInterface(argsPointer, in argsIid, out var argsPointer2) < 0)
        {
            return null;
        }

        var args = Com.Take<ICoreWebView2WebMessageReceivedEventArgs2>(argsPointer2);
        try
        {
            if (args.GetAdditionalObjects(out var objectsPointer) < 0 || objectsPointer == 0)
            {
                return null;
            }

            var objects = Com.Take<ICoreWebView2ObjectCollectionView>(objectsPointer);
            try
            {
                return objects.GetCount(out var count) < 0 || count == 0 ? null : Paths(objects, count);
            }
            finally
            {
                Com.Release(objects);
            }
        }
        finally
        {
            Com.Release(args);
        }
    }

    private static List<string> Paths(ICoreWebView2ObjectCollectionView objects, uint count)
    {
        var fileIid = FileIid;
        var paths = new List<string>((int)count);
        for (var i = 0u; i < count; i++)
        {
            if (objects.GetValueAtIndex(i, out var value) < 0 || value == 0)
            {
                continue;
            }

            var isFile = Marshal.QueryInterface(value, in fileIid, out var filePointer) >= 0;
            Marshal.Release(value);
            if (!isFile)
            {
                continue;
            }

            var file = Com.Take<ICoreWebView2File>(filePointer);
            try
            {
                paths.Add(file.GetPath());
            }
            finally
            {
                Com.Release(file);
            }
        }

        return paths;
    }

    private readonly record struct Incoming(Document Document, string Text, IReadOnlyList<string>? Files);

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
