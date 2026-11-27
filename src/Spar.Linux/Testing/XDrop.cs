using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Spar.Linux.Testing;

/// <summary>
/// Files dropped on a test window (decision "The drop design, from the file-drop spike"): a drag source of the test's
/// own, on a second connection to its private display, speaking XDND version 5 as a file manager does: Enter, a
/// Position every 16 ms for at least 400 ms and until the window accepts (WebKit reads the drag's data as it enters,
/// and took a drop made at once only one time in two, in the spike), Drop, and the answers to the target's requests
/// for <c>text/uri-list</c>. No pointer
/// moves: XDND carries the position in its messages. Runs on a thread of its own, never the main loop, which answers
/// the target's side.
/// </summary>
internal static unsafe partial class XDrop
{
    private const string Xlib = "libX11.so.6";
    private const int ClientMessage = 33;
    private const int SelectionRequest = 30;
    private const int SelectionNotify = 31;
    private const int XdndVersion = 5;
    private const int HoverMilliseconds = 400;
    private const int AcceptMilliseconds = 3000;
    private const int PositionMilliseconds = 16;
    private const int FinishMilliseconds = 8000;

    /// <summary>Drops <paramref name="paths"/> on <paramref name="target"/> at the root position (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public static void Drop(string displayName, nuint target, Func<(int X, int Y)> where, IReadOnlyList<string> paths)
    {
        var display = XOpenDisplay(displayName);
        if (display == 0)
        {
            throw new InvalidOperationException($"The test's display {displayName} could not be opened for a drop.");
        }

        try
        {
            new Source(display, paths).Drop(target, where);
        }
        finally
        {
            XCloseDisplay(display);
        }
    }

    private sealed class Source
    {
        private readonly nint _display;
        private readonly nuint _window;
        private readonly byte[] _uriList;
        private readonly nuint _enter, _position, _status, _drop, _finished, _leave, _selection, _copy, _uriListAtom, _targets;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private bool _accepted;
        private int _statuses;
        private int _requests;

        public Source(nint display, IReadOnlyList<string> paths)
        {
            _display = display;
            _window = XCreateSimpleWindow(display, XDefaultRootWindow(display), 0, 0, 1, 1, 0, 0, 0);
            var list = new StringBuilder();
            foreach (var path in paths)
            {
                list.Append(new Uri(path).AbsoluteUri).Append("\r\n");
            }

            _uriList = Encoding.UTF8.GetBytes(list.ToString());
            _enter = Atom("XdndEnter");
            _position = Atom("XdndPosition");
            _status = Atom("XdndStatus");
            _drop = Atom("XdndDrop");
            _finished = Atom("XdndFinished");
            _leave = Atom("XdndLeave");
            _selection = Atom("XdndSelection");
            _copy = Atom("XdndActionCopy");
            _uriListAtom = Atom("text/uri-list");
            _targets = Atom("TARGETS");
        }

        /// <summary>
        /// One drop; a drag the window did not accept is left and made once more, as a hand that saw no copy cursor tries
        /// again (a drag through the display server is lost now and then, as a press is: about one in twenty here).
        /// </summary>
        public void Drop(nuint target, Func<(int X, int Y)> where)
        {
            try
            {
                DropOnce(target, where);
            }
            catch (DragRefusedException)
            {
                Thread.Sleep(200);
                DropOnce(target, where);
            }
        }

        private void DropOnce(nuint target, Func<(int X, int Y)> where)
        {
            _accepted = false;
            _statuses = 0;
            _requests = 0;
            var (x, y) = where();
            // The X server's time, as a file manager's drag carries it from the press that started it; GTK orders a drag's
            // events by it, and CurrentTime (0) is older than any event it has seen.
            var time = ServerTime();
            var started = _clock.ElapsedMilliseconds;
            nuint Now() => time + (nuint)(_clock.ElapsedMilliseconds - started);
            XSetSelectionOwner(_display, _selection, _window, time);
            Send(target, _enter, (long)_window, (long)XdndVersion << 24, (long)_uriListAtom, 0, 0);
            // At least the hover a hand gives, and on until the window accepts, as a person waits for the copy cursor: a
            // page answers the first drag later than the next ones (the Notebook's, in the CI image, after more than 400 ms).
            var hoverEnd = _clock.ElapsedMilliseconds + HoverMilliseconds;
            var acceptEnd = _clock.ElapsedMilliseconds + AcceptMilliseconds;
            for (var step = 0; _clock.ElapsedMilliseconds < hoverEnd || (!_accepted && _clock.ElapsedMilliseconds < acceptEnd); step++)
            {
                // A pixel of movement each time, as a hand holding the files gives. As XDND asks of a source, the next
                // position waits for the target's status to this one (GTK answers once the page has decided).
                // Where the point is now: a test window can still be settling into its place as the drag starts.
                if (step > 0 && step % 6 == 0)
                {
                    (x, y) = where();
                }

                var packed = ((long)x << 16) | (uint)((y + (step % 2)) & 0xffff);
                var sent = _clock.ElapsedMilliseconds;
                Send(target, _position, (long)_window, 0, packed, (long)Now(), (long)_copy);
                Pump(_status, (int)Math.Max(1, acceptEnd - sent));
                Pump(0, (int)Math.Max(0, PositionMilliseconds - (_clock.ElapsedMilliseconds - sent)));
            }

            if (!_accepted)
            {
                Send(target, _leave, (long)_window, 0, 0, 0, 0);
                throw new DragRefusedException(
                    $"The window did not accept the drag of files within {AcceptMilliseconds} ms ({_statuses} answers, {_requests} reads of the files): is the point over a drop area?");
            }

            Send(target, _drop, (long)_window, 0, (long)Now(), 0, 0);
            if (!Pump(_finished, FinishMilliseconds))
            {
                throw new TimeoutException($"The window did not finish the drop within {FinishMilliseconds} ms.");
            }

            // A reader that asks after the drop finished still gets the files, as from a file manager, which keeps them.
            Pump(0, 300);
        }

        /// <summary>
        /// An event's type: <c>XEvent</c>'s first field is an <c>int</c>, and the four bytes after it are padding Xlib never
        /// clears, so reading the first <c>long</c> whole compares whatever the heap left there.
        /// </summary>
        private static int TypeOf(long* xevent) => *(int*)xevent;

        private nuint Atom(string name) => XInternAtom(_display, name, 0);

        /// <summary>The server's time now: a property change on the source's window, and the time of the event it raises.</summary>
        private nuint ServerTime()
        {
            const int PropertyNotify = 28;
            XSelectInput(_display, _window, 1 << 22);
            var stamp = Atom("SPAR_DROP_TIME");
            byte none = 0;
            XChangeProperty(_display, _window, stamp, 31, 8, 2, &none, 0);
            XFlush(_display);
            var e = stackalloc long[24];
            var deadline = _clock.ElapsedMilliseconds + 2000;
            while (_clock.ElapsedMilliseconds < deadline)
            {
                if (XPending(_display) == 0)
                {
                    Thread.Sleep(1);
                    continue;
                }

                XNextEvent(_display, e);
                if (TypeOf(e) == PropertyNotify)
                {
                    return (nuint)e[6];
                }
            }

            throw new TimeoutException("The X server did not report the time of a property change.");
        }

        private void Send(nuint target, nuint type, long l0, long l1, long l2, long l3, long l4)
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

        /// <summary>Handles events for <paramref name="milliseconds"/>, or until a message of <paramref name="awaited"/> arrives.</summary>
        private bool Pump(nuint awaited, int milliseconds)
        {
            var deadline = _clock.ElapsedMilliseconds + milliseconds;
            var e = stackalloc long[24];
            while (_clock.ElapsedMilliseconds < deadline)
            {
                if (XPending(_display) == 0)
                {
                    Thread.Sleep(1);
                    continue;
                }

                XNextEvent(_display, e);
                if (TypeOf(e) == SelectionRequest)
                {
                    _requests++;
                    Answer(e);
                }
                else if (TypeOf(e) == ClientMessage && (nuint)e[5] == _status)
                {
                    _accepted = (e[8] & 1) != 0;
                    _statuses++;
                    if (awaited == _status)
                    {
                        return true;
                    }
                }
                else if (TypeOf(e) == ClientMessage && awaited != 0 && (nuint)e[5] == awaited)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>A target's request for the drag's data: the URI list, or the list of what it can have.</summary>
        private void Answer(long* request)
        {
            var requestor = (nuint)request[5];
            var wanted = (nuint)request[7];
            var property = (nuint)request[8] == 0 ? wanted : (nuint)request[8];
            if (wanted == _uriListAtom)
            {
                fixed (byte* bytes = _uriList)
                {
                    XChangeProperty(_display, requestor, property, _uriListAtom, 8, 0, bytes, _uriList.Length);
                }
            }
            else if (wanted == _targets)
            {
                var atoms = stackalloc long[] { (long)_targets, (long)_uriListAtom };
                XChangeProperty(_display, requestor, property, 4, 32, 0, (byte*)atoms, 2);
            }
            else
            {
                property = 0;
            }

            var notify = stackalloc long[24];
            notify[0] = SelectionNotify;
            notify[2] = 1;
            notify[3] = _display;
            notify[4] = (long)requestor;
            notify[5] = request[6];
            notify[6] = (long)wanted;
            notify[7] = (long)property;
            notify[8] = request[9];
            XSendEvent(_display, requestor, 0, 0, notify);
            XFlush(_display);
        }
    }

    [LibraryImport(Xlib, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint XOpenDisplay(string name);

    [LibraryImport(Xlib)]
    private static partial int XCloseDisplay(nint display);

    [LibraryImport(Xlib)]
    private static partial nuint XDefaultRootWindow(nint display);

    [LibraryImport(Xlib)]
    private static partial nuint XCreateSimpleWindow(nint display, nuint parent, int x, int y, uint width, uint height, uint border, nuint borderPixel, nuint background);

    [LibraryImport(Xlib, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nuint XInternAtom(nint display, string name, int onlyIfExists);

    [LibraryImport(Xlib)]
    private static partial int XSetSelectionOwner(nint display, nuint selection, nuint owner, nuint time);

    [LibraryImport(Xlib)]
    private static partial int XSendEvent(nint display, nuint window, int propagate, nint mask, long* xevent);

    [LibraryImport(Xlib)]
    private static partial int XFlush(nint display);

    [LibraryImport(Xlib)]
    private static partial int XPending(nint display);

    [LibraryImport(Xlib)]
    private static partial int XNextEvent(nint display, long* xevent);

    [LibraryImport(Xlib)]
    private static partial int XSelectInput(nint display, nuint window, nint mask);

    [LibraryImport(Xlib)]
    private static partial int XChangeProperty(nint display, nuint window, nuint property, nuint type, int format, int mode, byte* data, int elements);
}

/// <summary>A drag of files the test window did not accept.</summary>
internal sealed class DragRefusedException(string message) : InvalidOperationException(message);
