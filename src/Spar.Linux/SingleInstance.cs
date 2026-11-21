using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Spar.Hosting;
using Spar.Linux.Interop;

namespace Spar.Linux;

/// <summary>
/// Single instance on Linux (decided, user, 2026-11-14: a lock file plus a Unix socket; plan section 3, "Single
/// instance"), claimed before the network session exists: a second instance on the same WebKit folders silently loses
/// data (pitfall). The first process holds an exclusive <c>flock</c> on <c>instance.lock</c> in the app's data root,
/// which the kernel releases when the process ends, and serves one Unix-domain socket named from that file's device
/// and inode, in <c>XDG_RUNTIME_DIR</c> (the user's own 0700 folder) or, without one, the abstract namespace. Both ends
/// check the other's user (<c>SO_PEERCRED</c>). A later process finds the lock held, connects, hands over its arguments,
/// working directory and activation token in the frame Windows uses (<see cref="RelaunchMessage"/> after the send
/// time), and exits once they are acknowledged (queued, never replayed).
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    /// <summary>How long a later process waits for the acknowledgement before reporting "not responding" (exit 8).</summary>
    public const int AcknowledgeMilliseconds = 2_000;

    /// <summary>How long a later process waits for a lock that nobody serves (a starting or closing app).</summary>
    public const int LockWaitMilliseconds = 5_000;

    /// <summary>The lock file's name in the data root, as on Windows.</summary>
    public const string LockFileName = "instance.lock";

    private const int RetryMilliseconds = 20;
    private const byte Accepted = 1;
    private const byte Refused = 0;

    /// <summary>A frame starts with the message's length (4 bytes) and the sender's monotonic time in ms (8 bytes).</summary>
    private const int FrameHeaderBytes = 12;

    /// <summary>The longest activation token taken (they are short random strings).</summary>
    private const int TokenLimitBytes = 4096;

    private readonly FileStream _lock;
    private readonly string _socketName;
    private int _listener = -1;
    private Thread? _serving;
    private volatile bool _stopping;

    private SingleInstance(FileStream lockFile, string socketName)
    {
        _lock = lockFile;
        _socketName = socketName;
    }

    /// <summary>
    /// Claims the data root: the primary's lock, or the relaunch handed to the running instance, or the problem that
    /// stopped the hand-off (exit 8 or 9). Blocks for at most about <see cref="LockWaitMilliseconds"/> plus the
    /// acknowledgement's wait; it runs on the main thread before any window exists.
    /// </summary>
    public static InstanceClaim Claim(string dataRoot, Relaunch relaunch, string? activationToken, HostLog log)
    {
        Directory.CreateDirectory(dataRoot);
        var lockPath = Path.Combine(dataRoot, LockFileName);
        var deadline = Stopwatch.StartNew();
        var frame = Frame(RelaunchMessage.Encode(relaunch), activationToken);

        while (true)
        {
            InstanceClaim? claim;
            try
            {
                claim = TryLock(lockPath) is { } held
                    ? InstanceClaim.Primary(new SingleInstance(held, SocketName(lockPath)))
                    : TryHandOff(lockPath, frame, log);
            }
            catch (UnauthorizedAccessException)
            {
                // Another user's lock file in this data root: that user's app runs on these folders.
                claim = InstanceClaim.Failed(OtherUser());
            }

            if (claim is { } decided)
            {
                return decided;
            }

            if (deadline.ElapsedMilliseconds >= LockWaitMilliseconds)
            {
                return InstanceClaim.Failed(NotResponding(
                    $"The app is still running or closing, and did not let another start within {LockWaitMilliseconds / 1000} seconds. Wait a moment, then start it again."));
            }

            Thread.Sleep(RetryMilliseconds);
        }
    }

    /// <summary>
    /// Starts serving relaunches on a thread of its own. <paramref name="onRelaunch"/> queues one and answers whether it
    /// was accepted; it must not block. A socket that cannot be created leaves the app running without relaunches, logged.
    /// </summary>
    public unsafe void Listen(Func<Relaunch, string?, bool> onRelaunch, HostLog log)
    {
        var listener = Libc.Socket(Libc.AfUnix, Libc.SockStream | Libc.SockCloexec, 0);
        if (listener < 0)
        {
            log.Warn($"Single instance: no socket (errno {Marshal.GetLastPInvokeError()}); later launches will report that the app is not responding.");
            return;
        }

        var (address, length) = Address(_socketName);
        if (!_socketName.StartsWith('\0'))
        {
            // The lock's holder owns the name: a socket file left by a killed primary goes.
            Libc.Unlink(_socketName);
        }

        if (Libc.Bind(listener, &address, length) != 0 || Libc.Listen(listener, 16) != 0)
        {
            log.Warn($"Single instance: the socket could not be bound (errno {Marshal.GetLastPInvokeError()}); later launches will report that the app is not responding.");
            Libc.Close(listener);
            return;
        }

        if (!_socketName.StartsWith('\0'))
        {
            Libc.Chmod(_socketName, 0x180);
        }

        _listener = listener;
        _serving = new Thread(() => Serve(listener, onRelaunch, log)) { Name = "Spar single instance", IsBackground = true };
        _serving.Start();
    }

    /// <summary>
    /// Stops accepting, so a launch from now on waits for the lock instead of being acknowledged. It does not wait for
    /// the serving thread, so the window's UI thread can call it at the start of the teardown.
    /// </summary>
    public void StopListening()
    {
        if (_stopping)
        {
            return;
        }

        _stopping = true;
        if (_listener >= 0)
        {
            // Wakes the blocked accept on Linux. The socket stays open until Dispose, so its number is never reused
            // while the serving thread might still use it.
            Libc.Shutdown(_listener, Libc.ShutReadWrite);
        }
    }

    /// <summary>Stops serving, removes the socket's name, then releases the lock, in that order.</summary>
    public void Dispose()
    {
        StopListening();
        if (_listener >= 0)
        {
            // At process exit, on the main thread: the thread ends at once, or within one exchange's timeout.
            if (_serving?.Join(AcknowledgeMilliseconds * 2) != false)
            {
                Libc.Close(_listener);
            }

            if (!_socketName.StartsWith('\0'))
            {
                Libc.Unlink(_socketName);
            }
        }

        _lock.Dispose();
    }

    /// <summary>
    /// The exclusive lock, or null when another process holds it. .NET takes <c>flock(LOCK_EX | LOCK_NB)</c> for
    /// <see cref="FileShare.None"/> and reports a held lock as an <see cref="IOException"/> with <c>EWOULDBLOCK</c>; the
    /// host takes the same lock itself as well, so the lock holds even where .NET's file locking is turned off.
    /// </summary>
    private static FileStream? TryLock(string path)
    {
        FileStream file;
        try
        {
            file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.None);
        }
        catch (IOException exception) when (exception.HResult == Libc.EAgain)
        {
            return null;
        }

        if (Libc.Flock((int)file.SafeFileHandle.DangerousGetHandle(), Libc.LockExclusive | Libc.LockNonBlocking) != 0)
        {
            file.Dispose();
            return null;
        }

        return file;
    }

    /// <summary>One attempt to reach the running instance; null when nobody serves the socket yet (or any more).</summary>
    private static unsafe InstanceClaim? TryHandOff(string lockPath, byte[] frame, HostLog log)
    {
        var client = Libc.Socket(Libc.AfUnix, Libc.SockStream | Libc.SockCloexec, 0);
        if (client < 0)
        {
            return null;
        }

        try
        {
            // The timeouts first: they also bound a connect to a primary whose queue is full.
            SetTimeouts(client, AcknowledgeMilliseconds);
            var (address, length) = Address(SocketName(lockPath));
            if (Libc.Connect(client, &address, length) != 0)
            {
                // No socket yet or any more (the primary is starting or closing): try the lock again.
                return null;
            }

            if (PeerOf(client) is not { } server || server.UserId != Libc.GetEffectiveUserId())
            {
                return InstanceClaim.Failed(OtherUser());
            }

            if (!SendAll(client, frame))
            {
                return InstanceClaim.Failed(NotResponding("The app is already running but the hand-over failed. Start it again."));
            }

            byte answer;
            var read = Libc.Receive(client, &answer, 1, 0);
            if (read != 1)
            {
                var error = Marshal.GetLastPInvokeError();
                return InstanceClaim.Failed(read < 0 && error == Libc.EAgain
                    ? NotResponding($"The app is already running but did not answer within {AcknowledgeMilliseconds / 1000} seconds. Wait a moment, then start it again, or end it in the system monitor.")
                    : NotResponding("The app is already running and is closing. Wait until it has closed, then start it again."));
            }

            if (answer != Accepted)
            {
                return InstanceClaim.Failed(NotResponding("The app is already running and is closing. Wait until it has closed, then start it again."));
            }

            log.Info($"Handed this launch to the running instance (process {server.ProcessId}).");
            return InstanceClaim.Forwarded((uint)server.ProcessId);
        }
        finally
        {
            Libc.Close(client);
        }
    }

    /// <summary>
    /// One connection at a time: the peer's user checked, the frame read, the relaunch queued, the answer sent. A
    /// relaunch that waited longer than its sender waits (a primary that was stopped, then resumed) is dropped: its
    /// sender already reported "not responding".
    /// </summary>
    private unsafe void Serve(int listener, Func<Relaunch, string?, bool> onRelaunch, HostLog log)
    {
        while (!_stopping)
        {
            var connection = Libc.Accept4(listener, null, null, Libc.SockCloexec);
            if (connection < 0)
            {
                if (_stopping)
                {
                    return;
                }

                var error = Marshal.GetLastPInvokeError();
                if (error != Libc.EIntr)
                {
                    log.Warn($"Single instance: accept failed (errno {error}).");
                    Thread.Sleep(100);
                }

                continue;
            }

            try
            {
                Answer(connection, onRelaunch, log);
            }
            finally
            {
                Libc.Close(connection);
            }
        }
    }

    private unsafe void Answer(int connection, Func<Relaunch, string?, bool> onRelaunch, HostLog log)
    {
        if (PeerOf(connection) is not { } peer || peer.UserId != Libc.GetEffectiveUserId())
        {
            log.Warn("Single instance: a connection from another user was refused.");
            return;
        }

        SetTimeouts(connection, AcknowledgeMilliseconds);
        var header = new byte[FrameHeaderBytes];
        if (!ReceiveAll(connection, header))
        {
            log.Warn("Single instance: a relaunch's connection ended early.");
            return;
        }

        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        var sentAt = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(4));
        var accepted = false;
        if (length <= 0 || length > RelaunchMessage.MaximumBytes)
        {
            log.Warn("Single instance: a malformed relaunch was refused.");
        }
        else
        {
            var body = new byte[length];
            var tokenLength = new byte[4];
            if (!ReceiveAll(connection, body) || !ReceiveAll(connection, tokenLength))
            {
                log.Warn("Single instance: a relaunch's connection ended early.");
                return;
            }

            var token = ReadToken(connection, BinaryPrimitives.ReadInt32LittleEndian(tokenLength));
            var waited = Libc.MonotonicMilliseconds() - sentAt;
            if (!RelaunchMessage.TryDecode(body, out var relaunch) || relaunch is null)
            {
                log.Warn("Single instance: a malformed relaunch was refused.");
            }
            else if (waited > AcknowledgeMilliseconds)
            {
                log.Warn($"Single instance: a relaunch that waited {waited} ms, longer than its sender waits, was dropped.");
            }
            else
            {
                accepted = onRelaunch(relaunch, token);
            }
        }

        var answer = accepted ? Accepted : Refused;
        Libc.Send(connection, &answer, 1, Libc.MsgNoSignal);
    }

    private static string? ReadToken(int connection, int length)
    {
        if (length <= 0 || length > TokenLimitBytes)
        {
            return null;
        }

        var bytes = new byte[length];
        if (!ReceiveAll(connection, bytes))
        {
            return null;
        }

        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    /// <summary>The frame: length, send time, the relaunch message, then the activation token's length and bytes.</summary>
    private static byte[] Frame(byte[] message, string? activationToken)
    {
        var token = activationToken is { Length: > 0 } text ? Encoding.UTF8.GetBytes(text) : [];
        if (token.Length > TokenLimitBytes)
        {
            token = [];
        }

        var frame = new byte[FrameHeaderBytes + message.Length + 4 + token.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, message.Length);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(4), Libc.MonotonicMilliseconds());
        message.CopyTo(frame, FrameHeaderBytes);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(FrameHeaderBytes + message.Length), token.Length);
        token.CopyTo(frame, FrameHeaderBytes + message.Length + 4);
        return frame;
    }

    /// <summary>
    /// The socket's name from the lock file's identity (device and inode, so a symbolic link to the data root reaches
    /// the same instance): a path in <c>XDG_RUNTIME_DIR</c> when it is a folder of this user's alone, else an abstract
    /// name (a leading zero byte), which any user can see but the peer check guards. The two numbers themselves, in
    /// hexadecimal, are the name: no hash, since .NET's hashes on Linux need OpenSSL, which the executable must not.
    /// </summary>
    internal static unsafe string SocketName(string lockPath)
    {
        var info = stackalloc byte[256];

        // Zero only when the file went away meanwhile: no socket has that name, and the next attempt finds the file.
        var (device, inode) = Libc.Stat(lockPath, info) == 0 ? (*(ulong*)info, *(ulong*)(info + 8)) : (0UL, 0UL);
        var name = $"spar-{device:x}-{inode:x}.sock";
        return RuntimeFolder() is { } folder ? Path.Combine(folder, name) : "\0" + name;
    }

    /// <summary><c>XDG_RUNTIME_DIR</c> when it is set, absolute, short enough and only this user's; else null.</summary>
    private static string? RuntimeFolder()
    {
        var folder = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (string.IsNullOrEmpty(folder) || !Path.IsPathFullyQualified(folder) || folder.Length > 60)
        {
            return null;
        }

        try
        {
            var mode = File.GetUnixFileMode(folder);
            var others = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
            return Directory.Exists(folder) && (mode & others) == 0 ? folder : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static unsafe (Libc.UnixAddress Address, uint Length) Address(string name)
    {
        var address = new Libc.UnixAddress { Family = Libc.AfUnix };
        var bytes = Encoding.UTF8.GetBytes(name);
        var count = Math.Min(bytes.Length, Libc.UnixAddress.PathBytes - 1);
        bytes.AsSpan(0, count).CopyTo(new Span<byte>(address.Path, Libc.UnixAddress.PathBytes));

        // A path is zero-terminated within the structure; an abstract name is exactly as long as the length says.
        var length = (uint)(sizeof(ushort) + count + (name.StartsWith('\0') ? 0 : 1));
        return (address, length);
    }

    private static unsafe Libc.PeerCredentials? PeerOf(int socket)
    {
        Libc.PeerCredentials peer;
        var size = (uint)sizeof(Libc.PeerCredentials);
        return Libc.GetSocketOption(socket, Libc.SolSocket, Libc.SoPeerCred, &peer, &size) == 0 ? peer : null;
    }

    private static unsafe void SetTimeouts(int socket, int milliseconds)
    {
        var timeout = new Libc.Timeval { Seconds = milliseconds / 1000, Microseconds = milliseconds % 1000 * 1000 };
        Libc.SetSocketOption(socket, Libc.SolSocket, Libc.SoReceiveTimeout, &timeout, (uint)sizeof(Libc.Timeval));
        Libc.SetSocketOption(socket, Libc.SolSocket, Libc.SoSendTimeout, &timeout, (uint)sizeof(Libc.Timeval));
    }

    private static unsafe bool SendAll(int socket, byte[] data)
    {
        fixed (byte* start = data)
        {
            var sent = 0;
            while (sent < data.Length)
            {
                var written = Libc.Send(socket, start + sent, (nuint)(data.Length - sent), Libc.MsgNoSignal);
                if (written < 0 && Marshal.GetLastPInvokeError() == Libc.EIntr)
                {
                    continue;
                }

                if (written <= 0)
                {
                    return false;
                }

                sent += (int)written;
            }
        }

        return true;
    }

    private static unsafe bool ReceiveAll(int socket, byte[] data)
    {
        fixed (byte* start = data)
        {
            var received = 0;
            while (received < data.Length)
            {
                var read = Libc.Receive(socket, start + received, (nuint)(data.Length - received), 0);
                if (read < 0 && Marshal.GetLastPInvokeError() == Libc.EIntr)
                {
                    continue;
                }

                if (read <= 0)
                {
                    return false;
                }

                received += (int)read;
            }
        }

        return true;
    }

    private static StartupProblem NotResponding(string details) =>
        new(StartupProblemKind.NotResponding, "The running app", null, null, details, null);

    private static StartupProblem OtherUser() => new(
        StartupProblemKind.OtherUser,
        "The running app",
        null,
        null,
        "The app is already running for another user of this computer, with the same data folder. Only one person can use it at a time.",
        null);
}

/// <summary>The outcome of <see cref="SingleInstance.Claim"/>: exactly one of the three is set.</summary>
internal readonly record struct InstanceClaim(SingleInstance? Instance, uint ForwardedTo, StartupProblem? Problem)
{
    public static InstanceClaim Primary(SingleInstance instance) => new(instance, 0, null);

    public static InstanceClaim Forwarded(uint process) => new(null, process, null);

    public static InstanceClaim Failed(StartupProblem problem) => new(null, 0, problem);
}
