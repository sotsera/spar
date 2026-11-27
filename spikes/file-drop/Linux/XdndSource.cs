using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using static FileDrop.Native;

namespace FileDrop;

// A drag source of the probe's own on a second X connection, speaking XDND version 5 to the GTK window: Enter,
// Position (twice, as a pointer would), Drop, and the answers to the target's selection requests for text/uri-list.
// No pointer moves: XDND carries the position in its messages, so the drop is the X11 exchange a file manager makes.
internal sealed unsafe class XdndSource
{
    private const int ClientMessage = 33;
    private const int SelectionRequest = 30;
    private const int SelectionNotify = 31;

    private readonly nint _display;
    private readonly ulong _root;
    private readonly ulong _window;
    private readonly Stopwatch _clock;
    private readonly ulong _aware, _enter, _position, _status, _drop, _finished, _leave, _selection, _copy, _uriList,
        _targets, _atom;

    public XdndSource(Stopwatch clock)
    {
        _clock = clock;
        _display = XOpenDisplay(0);
        if (_display == 0)
        {
            throw new InvalidOperationException("XOpenDisplay failed.");
        }

        _root = XDefaultRootWindow(_display);
        _window = XCreateSimpleWindow(_display, _root, 0, 0, 1, 1, 0, 0, 0);
        _aware = XInternAtom(_display, "XdndAware", 0);
        _enter = XInternAtom(_display, "XdndEnter", 0);
        _position = XInternAtom(_display, "XdndPosition", 0);
        _status = XInternAtom(_display, "XdndStatus", 0);
        _drop = XInternAtom(_display, "XdndDrop", 0);
        _finished = XInternAtom(_display, "XdndFinished", 0);
        _leave = XInternAtom(_display, "XdndLeave", 0);
        _selection = XInternAtom(_display, "XdndSelection", 0);
        _copy = XInternAtom(_display, "XdndActionCopy", 0);
        _uriList = XInternAtom(_display, "text/uri-list", 0);
        _targets = XInternAtom(_display, "TARGETS", 0);
        _atom = 4; // XA_ATOM
    }

    private double _dragStarted;
    private long[]? _lastStatus;
    private int _statuses;

    public double DropSentAt { get; private set; }

    public JsonObject Drop(ulong target, int x, int y, string[] paths)
    {
        _dragStarted = _clock.Elapsed.TotalMilliseconds;
        var log = new JsonObject();
        var requests = new JsonArray();
        log["selectionRequests"] = requests;

        var builder = new StringBuilder();
        foreach (var path in paths)
        {
            builder.Append(new Uri(path).AbsoluteUri).Append("\r\n");
        }

        var data = Encoding.UTF8.GetBytes(builder.ToString());
        log["uriListBytes"] = data.Length;

        XTranslateCoordinates(_display, target, _root, x, y, out var rootX, out var rootY, out _);
        XSetSelectionOwner(_display, _selection, _window, 0);

        // Hover as a pointer would: a position every 16 ms for 400 ms, keeping the target's latest status.
        _lastStatus = null;
        _statuses = 0;
        Send(target, _enter, (long)_window, 5L << 24, (long)_uriList, 0, 0);
        var hoverEnd = _clock.Elapsed.TotalMilliseconds + 400;
        for (var step = 0; _clock.Elapsed.TotalMilliseconds < hoverEnd; step++)
        {
            var packed = ((long)rootX << 16) | (uint)((rootY + (step % 2)) & 0xffff);
            Send(target, _position, (long)_window, 0, packed, 0, (long)_copy);
            Pump(0, 16, data, requests);
        }

        var second = _lastStatus;
        log["statuses"] = _statuses;
        log["status"] = second is null ? "none" : $"accept={second[1] & 1} action={AtomText(second[4])}";

        if (second is null || (second[1] & 1) == 0)
        {
            Send(target, _leave, (long)_window, 0, 0, 0, 0);
            XFlush(_display);
            log["dropped"] = false;
            return log;
        }

        DropSentAt = _clock.Elapsed.TotalMilliseconds;
        Send(target, _drop, (long)_window, 0, 0, 0, 0);
        var finished = Pump(_finished, 8000, data, requests);
        log["dropped"] = true;
        log["finished"] = finished is null ? "none" : $"success={finished[1] & 1} action={AtomText(finished[2])}";
        log["finishedAfterMs"] = Math.Round(_clock.Elapsed.TotalMilliseconds - DropSentAt, 2);

        // Answer late requests for a moment: a reader that asks after Finished still gets the data.
        Pump(0, 300, data, requests);
        return log;
    }

    private string AtomText(long atom) => (ulong)atom == _copy ? "copy" : atom == 0 ? "none" : atom.ToString();

    private void Send(ulong target, ulong type, long l0, long l1, long l2, long l3, long l4)
    {
        var e = stackalloc long[24];
        e[0] = ClientMessage;
        e[2] = 1;
        e[3] = _display;
        e[4] = (long)target;
        e[5] = (long)type;
        e[6] = 32;
        e[7] = l0;
        e[8] = l1;
        e[9] = l2;
        e[10] = l3;
        e[11] = l4;
        XSendEvent(_display, target, 0, 0, e);
        XFlush(_display);
    }

    // Reads events until a client message of the given type arrives (0: none awaited), answering selection requests.
    private long[]? Pump(ulong awaited, int timeoutMs, byte[] data, JsonArray requests)
    {
        var deadline = _clock.Elapsed.TotalMilliseconds + timeoutMs;
        var e = stackalloc long[24];
        while (_clock.Elapsed.TotalMilliseconds < deadline)
        {
            if (XPending(_display) == 0)
            {
                Thread.Sleep(1);
                continue;
            }

            XNextEvent(_display, e);
            var type = (int)e[0];
            if (type == SelectionRequest)
            {
                requests.Add(Answer(e, data));
                continue;
            }

            if (type == ClientMessage && (ulong)e[5] == _status)
            {
                _lastStatus = [e[7], e[8], e[9], e[10], e[11]];
                _statuses++;
            }

            if (type == ClientMessage && awaited != 0 && (ulong)e[5] == awaited)
            {
                return [e[7], e[8], e[9], e[10], e[11]];
            }
        }

        return null;
    }

    private string Answer(long* request, byte[] data)
    {
        var requestor = (ulong)request[5];
        var selection = (ulong)request[6];
        var wanted = (ulong)request[7];
        var property = (ulong)request[8];
        var time = request[9];
        if (property == 0)
        {
            property = wanted;
        }

        string answered;
        if (wanted == _uriList)
        {
            fixed (byte* bytes = data)
            {
                XChangeProperty(_display, requestor, property, _uriList, 8, 0, bytes, data.Length);
            }

            answered = "text/uri-list";
        }
        else if (wanted == _targets)
        {
            var atoms = stackalloc long[] { (long)_targets, (long)_uriList };
            XChangeProperty(_display, requestor, property, _atom, 32, 0, (byte*)atoms, 2);
            answered = "TARGETS";
        }
        else
        {
            property = 0;
            answered = $"refused {wanted}";
        }

        var notify = stackalloc long[24];
        notify[0] = SelectionNotify;
        notify[2] = 1;
        notify[3] = _display;
        notify[4] = (long)requestor;
        notify[5] = (long)selection;
        notify[6] = (long)wanted;
        notify[7] = (long)property;
        notify[8] = time;
        XSendEvent(_display, requestor, 0, 0, notify);
        XFlush(_display);
        return $"{Math.Round(_clock.Elapsed.TotalMilliseconds - _dragStarted, 2)} ms after enter: {answered}";
    }
}
