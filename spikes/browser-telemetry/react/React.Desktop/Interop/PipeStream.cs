using System.Runtime.InteropServices.Marshalling;

namespace React.Desktop.Interop;

/// <summary>
/// A response body for <c>WebResourceRequested</c> that is written while WebView2 reads it. The writer waits while
/// the buffered bytes exceed the capacity (back-pressure); <see cref="Read"/> blocks until bytes arrive or the writer
/// completes. Spike instrumentation records which methods WebView2 calls and on which thread.
/// </summary>
[GeneratedComClass]
internal sealed partial class PipeStream(long capacity, int staThreadId) : IStream
{
    private const int SOk = 0;
    private const int SFalse = 1;
    private const int ENotImpl = unchecked((int)0x80004001);
    private const int StgEInvalidFunction = unchecked((int)0x80030001);
    private const int EPending = unchecked((int)0x8000000A);

    private static readonly Lock StatsGate = new();

    // Monitor.Wait/PulseAll need a plain object, not System.Threading.Lock.
    private readonly object _gate = new();
    private readonly Queue<ReadOnlyMemory<byte>> _segments = new();
    private ReadOnlyMemory<byte> _current;
    private long _buffered;
    private long _position;
    private bool _completed;
    private TaskCompletionSource? _spaceWaiter;

    /// <summary>Calls by method name, and reads made on the STA thread (spike instrumentation, all streams).</summary>
    public static Dictionary<string, int> Calls { get; } = [];

    public static int ReadsOnSta { get; private set; }

    public static int BlockedReads { get; private set; }

    /// <summary>Probe (--istream-pending): return E_PENDING when no data is buffered, instead of blocking.</summary>
    public static bool ReturnPending { get; set; }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        while (true)
        {
            Task wait;
            lock (_gate)
            {
                if (_buffered < capacity)
                {
                    _segments.Enqueue(data);
                    _buffered += data.Length;
                    Monitor.PulseAll(_gate);
                    return;
                }

                _spaceWaiter ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                wait = _spaceWaiter.Task;
            }

            await wait.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public void Complete()
    {
        lock (_gate)
        {
            _completed = true;
            Monitor.PulseAll(_gate);
        }
    }

    public unsafe int Read(nint buffer, uint size, nint read)
    {
        Count(nameof(Read), Environment.CurrentManagedThreadId == staThreadId);
        var copied = 0;
        TaskCompletionSource? space = null;
        lock (_gate)
        {
            if (_current.IsEmpty && _segments.Count == 0 && !_completed)
            {
                lock (StatsGate)
                {
                    BlockedReads++;
                }
            }

            if (ReturnPending && _current.IsEmpty && _segments.Count == 0 && !_completed)
            {
                // Probe: tell the reader "no data yet" instead of blocking its thread.
                if (read != 0)
                {
                    *(uint*)read = 0;
                }

                return EPending;
            }

            while (_current.IsEmpty && _segments.Count == 0 && !_completed)
            {
                Monitor.Wait(_gate);
            }

            var target = new Span<byte>((void*)buffer, (int)size);
            while (copied < target.Length)
            {
                if (_current.IsEmpty && !_segments.TryDequeue(out _current))
                {
                    break;
                }

                var take = Math.Min(_current.Length, target.Length - copied);
                _current.Span[..take].CopyTo(target[copied..]);
                _current = _current[take..];
                copied += take;
            }

            _buffered -= copied;
            _position += copied;
            if (_buffered < capacity)
            {
                space = _spaceWaiter;
                _spaceWaiter = null;
            }
        }

        space?.TrySetResult();
        if (read != 0)
        {
            *(uint*)read = (uint)copied;
        }

        // A short read is not the end; only a read of zero bytes after completion is.
        return copied == 0 ? SFalse : SOk;
    }

    public int Write(nint buffer, uint size, nint written) => Count(nameof(Write), ENotImpl);

    public unsafe int Seek(long move, uint origin, nint newPosition)
    {
        // Only "where am I", and a rewind to the start before anything was read (WebView2 asks for both).
        if (move == 0 && (origin == 1 || (origin == 0 && Interlocked.Read(ref _position) == 0)))
        {
            Count($"Seek({origin},0)", 0);
            if (newPosition != 0)
            {
                lock (_gate)
                {
                    *(ulong*)newPosition = (ulong)_position;
                }
            }

            return SOk;
        }

        return Count($"Seek({origin},{move})", StgEInvalidFunction);
    }

    public int SetSize(ulong size) => Count(nameof(SetSize), ENotImpl);

    public int CopyTo(nint stream, ulong count, nint read, nint written) => Count(nameof(CopyTo), ENotImpl);

    public int Commit(uint flags) => Count(nameof(Commit), ENotImpl);

    public int Revert() => Count(nameof(Revert), ENotImpl);

    public int LockRegion(ulong offset, ulong count, uint type) => Count(nameof(LockRegion), ENotImpl);

    public int UnlockRegion(ulong offset, ulong count, uint type) => Count(nameof(UnlockRegion), ENotImpl);

    public int Stat(nint statstg, uint flags) => Count(nameof(Stat), ENotImpl);

    public int Clone(nint stream) => Count(nameof(Clone), ENotImpl);

    private static int Count(string method, int result)
    {
        lock (StatsGate)
        {
            Calls[method] = Calls.GetValueOrDefault(method) + 1;
        }

        return result;
    }

    private static void Count(string method, bool onSta)
    {
        lock (StatsGate)
        {
            Calls[method] = Calls.GetValueOrDefault(method) + 1;
            if (onSta)
            {
                ReadsOnSta++;
            }
        }
    }
}
