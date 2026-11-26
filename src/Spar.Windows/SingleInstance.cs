using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using Spar.Hosting;
using Spar.Windows.Interop;

namespace Spar.Windows;

/// <summary>
/// Single instance on Windows (decision "Windows single instance: a lock file plus a pipe";
/// <c>docs/evidence/windows-single-instance</c>), claimed before the WebView environment exists. The first process holds
/// <c>instance.lock</c> in the
/// app's data root with no sharing, which covers exactly the folder a second instance would corrupt and which Windows
/// releases when the process ends. It serves one named pipe, named from that file's identity (volume serial number and
/// file index, so a junction or a short name reaches the same instance), with a DACL for the token's user only,
/// <c>FILE_FLAG_FIRST_PIPE_INSTANCE</c> and <c>PIPE_REJECT_REMOTE_CLIENTS</c>. A later process finds the lock held,
/// connects, checks that the server process runs as the same user, lets it take the foreground, hands over its
/// arguments and working directory, and exits once they are acknowledged (queued, never replayed).
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    /// <summary>How long a later process waits for the acknowledgement before reporting "not responding" (exit 8).</summary>
    public const int AcknowledgeMilliseconds = 2_000;

    /// <summary>How long a later process waits for a lock that nobody serves (a closing app) before giving up.</summary>
    public const int LockWaitMilliseconds = 5_000;

    /// <summary>The lock file's name in the data root.</summary>
    public const string LockFileName = "instance.lock";

    private const uint ConnectMilliseconds = 100;
    private const int RetryMilliseconds = 20;
    private const byte Accepted = 1;
    private const byte Refused = 0;

    /// <summary>A frame starts with the message's length (4 bytes) and the sender's tick count when it sent it (8 bytes).</summary>
    private const int FrameHeaderBytes = 12;

    /// <summary><c>HRESULT_FROM_WIN32(ERROR_SHARING_VIOLATION)</c>: another process holds the lock.</summary>
    private const int SharingViolation = unchecked((int)0x80070020);

    private readonly FileStream _lock;
    private readonly CancellationTokenSource _stopping = new();
    private NamedPipeServerStream? _server;
    private Task _serving = Task.CompletedTask;

    private SingleInstance(FileStream lockFile, string pipeName)
    {
        _lock = lockFile;
        PipeName = pipeName;
    }

    /// <summary>The pipe's name, without <c>\\.\pipe\</c>.</summary>
    public string PipeName { get; }

    /// <summary>
    /// Claims the data root: the primary's lock, or the relaunch handed to the running instance, or the problem that
    /// stopped the hand-off (exit 8 or 9). Blocks for at most about <see cref="LockWaitMilliseconds"/> plus the
    /// acknowledgement's wait; it runs on the main thread before any window exists.
    /// </summary>
    public static InstanceClaim Claim(string dataRoot, Relaunch relaunch, HostLog log)
    {
        Directory.CreateDirectory(dataRoot);
        var lockPath = Path.Combine(dataRoot, LockFileName);
        var deadline = Stopwatch.StartNew();
        var message = RelaunchMessage.Encode(relaunch);

        while (true)
        {
            if (TryLock(lockPath) is { } held)
            {
                return InstanceClaim.Primary(new SingleInstance(held, NameFrom(held.SafeFileHandle)));
            }

            if (TryHandOff(lockPath, message, log) is { } handedOff)
            {
                return handedOff;
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
    /// Starts serving relaunches on a task of its own. <paramref name="onRelaunch"/> queues one and answers whether it
    /// was accepted; it must not block. A pipe whose name is taken leaves the app running without relaunches, logged.
    /// </summary>
    public void Listen(Func<Relaunch, bool> onRelaunch, HostLog log)
    {
        try
        {
            _server = CreateServer(PipeName);
        }
        catch (IOException exception)
        {
            log.Warn($"Single instance: the relaunch pipe could not be created ({exception.Message}); later launches will report that the app is not responding.");
            return;
        }

        _serving = Task.Run(() => ServeAsync(_server, onRelaunch, log, _stopping.Token));
    }

    /// <summary>
    /// Closes the pipe, so a launch from now on waits for the lock instead of being acknowledged. It does not wait for
    /// the serving loop, which ends on its own, so the window's UI thread can call it at the start of the teardown.
    /// </summary>
    public void StopListening()
    {
        if (_stopping.IsCancellationRequested)
        {
            return;
        }

        _stopping.Cancel();
        _server?.Dispose();
    }

    /// <summary>Closes the pipe, then releases the lock: in that order, so no launch is acknowledged by a closing app.</summary>
    public void Dispose()
    {
        StopListening();
        try
        {
            // At process exit, on the main thread: the loop ends at once on the cancelled token and the closed pipe.
            _serving.Wait(AcknowledgeMilliseconds);
        }
        catch (AggregateException)
        {
            // Its failures were logged by the loop.
        }

        _lock.Dispose();
        _stopping.Dispose();
    }

    private static FileStream? TryLock(string path)
    {
        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.None);
        }
        catch (IOException exception) when (exception.HResult == SharingViolation)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            // Another user's lock file: the pipe tells who serves it.
            return null;
        }
    }

    /// <summary>One attempt to reach the running instance; null when nobody serves the pipe yet (or any more).</summary>
    private static InstanceClaim? TryHandOff(string lockPath, byte[] message, HostLog log)
    {
        string name;
        try
        {
            using var identity = Native.CreateFile(LongPath(lockPath), 0, Native.ShareAll, 0, Native.OpenExisting, 0, 0);
            if (identity.IsInvalid)
            {
                return null;
            }

            name = NameFrom(identity);
        }
        catch (IOException)
        {
            return null;
        }

        var pipe = $@"\\.\pipe\{name}";
        using var handle = Native.CreateFile(pipe, Native.GenericRead | Native.GenericWrite, 0, 0, Native.OpenExisting, Native.FileFlagOverlapped, 0);
        if (handle.IsInvalid)
        {
            var error = System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
            if (error == Native.ErrorAccessDenied)
            {
                return InstanceClaim.Failed(OtherUser());
            }

            // No pipe yet or any more (the primary is starting or closing), or a busy one: wait briefly, try the lock again.
            if (error == Native.ErrorPipeBusy)
            {
                _ = Native.WaitNamedPipe(pipe, ConnectMilliseconds);
            }

            return null;
        }

        if (!Native.GetNamedPipeServerProcessId(handle, out var serverProcess) || !RunsAsThisUser(serverProcess))
        {
            return InstanceClaim.Failed(OtherUser());
        }

        using var client = new FileStream(handle, FileAccess.ReadWrite, 1, isAsync: true);

        // The running instance may take the foreground only if this process, started by the user, lets it.
        _ = global::Windows.Win32.PInvoke.AllowSetForegroundWindow(serverProcess);
        return Send(client, message, serverProcess, log);
    }

    private static InstanceClaim Send(Stream client, byte[] message, uint serverProcess, HostLog log)
    {
        // This is the main thread of a process that has no window, so waiting here blocks nothing that draws.
        using var timeout = new CancellationTokenSource(AcknowledgeMilliseconds);
        byte answer;
        try
        {
            answer = ExchangeAsync(client, message, timeout.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            return InstanceClaim.Failed(NotResponding(
                $"The app is already running but did not answer within {AcknowledgeMilliseconds / 1000} seconds. Wait a moment, then start it again, or end it in Task Manager."));
        }
        catch (IOException exception)
        {
            return InstanceClaim.Failed(NotResponding($"The app is already running but the hand-over failed ({exception.Message}). Start it again."));
        }

        if (answer != Accepted)
        {
            return InstanceClaim.Failed(NotResponding("The app is already running and is closing. Wait until it has closed, then start it again."));
        }

        log.Info($"Handed this launch to the running instance (process {serverProcess}).");
        return InstanceClaim.Forwarded(serverProcess);
    }

    /// <summary>Sends the framed message and reads the one-byte answer; the end of the pipe reads as a refusal.</summary>
    private static async Task<byte> ExchangeAsync(Stream client, byte[] message, CancellationToken cancellationToken)
    {
        var header = new byte[FrameHeaderBytes];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header, message.Length);
        System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(4), Environment.TickCount64);
        await client.WriteAsync(header, cancellationToken);
        await client.WriteAsync(message, cancellationToken);
        await client.FlushAsync(cancellationToken);

        var answer = new byte[1];
        return await client.ReadAsync(answer, cancellationToken) == 1 ? answer[0] : Refused;
    }

    /// <summary>Whether a process runs as this process's user: a check of the server, not of the pipe's owner.</summary>
    private static bool RunsAsThisUser(uint processId)
    {
        var process = Native.OpenProcess(Native.ProcessQueryLimitedInformation, false, processId);
        if (process == 0)
        {
            return false;
        }

        try
        {
            return UserOf(process) is { } user && user == UserOf(Native.GetCurrentProcess());
        }
        finally
        {
            Native.CloseHandle(process);
        }
    }

    /// <summary>
    /// The user SID of a process's token, as text (the token's user, never its owner, which is Administrators for an
    /// elevated token; pitfall "Single instance and named pipes"); null when it cannot be read.
    /// </summary>
    private static unsafe string? UserOf(nint process)
    {
        if (!Native.OpenProcessToken(process, Native.TokenQuery, out var token))
        {
            return null;
        }

        try
        {
            _ = Native.GetTokenInformation(token, Native.TokenUser, null, 0, out var length);
            if (length == 0 || length > 1024)
            {
                return null;
            }

            var buffer = stackalloc byte[(int)length];
            if (!Native.GetTokenInformation(token, Native.TokenUser, buffer, length, out _))
            {
                return null;
            }

            // TOKEN_USER starts with SID_AND_ATTRIBUTES, which starts with the SID's pointer (into the buffer).
            if (!Native.ConvertSidToStringSid(*(nint*)buffer, out var text))
            {
                return null;
            }

            try
            {
                return System.Runtime.InteropServices.Marshal.PtrToStringUni(text);
            }
            finally
            {
                Native.LocalFree(text);
            }
        }
        finally
        {
            Native.CloseHandle(token);
        }
    }

    /// <summary>The only instance of the pipe, readable and writable by this token's user alone, local clients only.</summary>
    private static NamedPipeServerStream CreateServer(string name)
    {
        var user = UserOf(Native.GetCurrentProcess()) ?? throw new IOException("The process's user cannot be read from its token.");
        if (!Native.ConvertStringSecurityDescriptorToSecurityDescriptor($"O:{user}D:P(A;;FA;;;{user})", Native.SddlRevision1, out var descriptor, 0))
        {
            throw new IOException($"The pipe's security descriptor could not be built (error {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()}).");
        }

        try
        {
            var attributes = new SecurityAttributes
            {
                Length = (uint)System.Runtime.InteropServices.Marshal.SizeOf<SecurityAttributes>(),
                SecurityDescriptor = descriptor,
            };
            var handle = Native.CreateNamedPipe($@"\\.\pipe\{name}", Native.PipeServerOpenMode, Native.PipeServerMode, 1, 4096, 4096, 0, attributes);
            if (handle.IsInvalid)
            {
                var error = System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
                handle.Dispose();
                throw new IOException($"CreateNamedPipe failed with error {error}.");
            }

            return new NamedPipeServerStream(PipeDirection.InOut, isAsync: true, isConnected: false, handle);
        }
        finally
        {
            Native.LocalFree(descriptor);
        }
    }

    /// <summary>
    /// One connection at a time on the one pipe instance: read the message, queue it, answer, wait for the client to
    /// close its end (disconnecting earlier discards an answer it has not read), then disconnect for the next. A message
    /// that waited in the pipe longer than its sender waits for the answer (a primary that was suspended, then resumed)
    /// is dropped: its sender already reported "not responding", so acting on it now would surprise the user.
    /// </summary>
    private static async Task ServeAsync(NamedPipeServerStream server, Func<Relaunch, bool> onRelaunch, HostLog log, CancellationToken stopping)
    {
        var failuresInARow = 0;
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                await server.WaitForConnectionAsync(stopping);
                using var exchange = CancellationTokenSource.CreateLinkedTokenSource(stopping);
                exchange.CancelAfter(AcknowledgeMilliseconds);

                var (relaunch, waited) = await ReadAsync(server, exchange.Token);
                var accepted = false;
                if (relaunch is null)
                {
                    log.Warn("Single instance: a malformed relaunch was refused.");
                }
                else if (waited > AcknowledgeMilliseconds)
                {
                    log.Warn($"Single instance: a relaunch that waited {waited} ms, longer than its sender waits, was dropped.");
                }
                else
                {
                    accepted = onRelaunch(relaunch);
                }

                await server.WriteAsync(new[] { accepted ? Accepted : Refused }, exchange.Token);
                await server.FlushAsync(exchange.Token);
                await WaitForCloseAsync(server, exchange.Token);
                failuresInARow = 0;
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException)
            {
                if (stopping.IsCancellationRequested)
                {
                    return;
                }

                log.Warn($"Single instance: a relaunch's connection failed ({exception.Message}).");
                failuresInARow++;
            }

            if (!Disconnect(server, stopping))
            {
                return;
            }

            // A pipe that keeps failing at once must not spin: wait a little before serving it again.
            if (failuresInARow > 1)
            {
                await Task.Delay(Math.Min(failuresInARow * 100, 2_000), CancellationToken.None);
            }
        }
    }

    /// <summary>Ends the current client, whatever state it left the pipe in; false when the pipe is gone (stopping).</summary>
    private static bool Disconnect(NamedPipeServerStream server, CancellationToken stopping)
    {
        try
        {
            server.Disconnect();
            return true;
        }
        catch (InvalidOperationException)
        {
            // Nobody was connected.
            return !stopping.IsCancellationRequested;
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            return !stopping.IsCancellationRequested && exception is IOException;
        }
    }

    /// <summary>The frame: the message's length, the sender's <c>GetTickCount64</c> when it sent it, then the message.</summary>
    private static async Task<(Relaunch? Relaunch, long WaitedMilliseconds)> ReadAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        var header = new byte[FrameHeaderBytes];
        await server.ReadExactlyAsync(header, cancellationToken);
        var length = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(header);
        var sentAt = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(4));
        if (length <= 0 || length > RelaunchMessage.MaximumBytes)
        {
            return (null, 0);
        }

        var body = new byte[length];
        await server.ReadExactlyAsync(body, cancellationToken);
        var waited = Environment.TickCount64 - sentAt;
        return (RelaunchMessage.TryDecode(body, out var relaunch) ? relaunch : null, waited);
    }

    private static async Task WaitForCloseAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        var rest = new byte[64];
        while (await server.ReadAsync(rest, cancellationToken) > 0)
        {
            // A well-behaved client sends nothing more; anything else is read and ignored until it closes.
        }
    }

    /// <summary>The pipe's name from the lock file's identity: <c>spar-</c> and 32 hex characters of SHA-256.</summary>
    private static string NameFrom(SafeFileHandle file)
    {
        if (!Native.GetFileInformationByHandle(file, out var info))
        {
            throw new IOException($"The lock file's identity cannot be read (error {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()}).");
        }

        Span<byte> identity = stackalloc byte[12];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(identity, info.VolumeSerialNumber);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(identity[4..], info.FileIndexHigh);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(identity[8..], info.FileIndexLow);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(identity, hash);
        return "spar-" + Convert.ToHexStringLower(hash[..16]);
    }

    /// <summary>Raw Win32 path calls need the <c>\\?\</c> prefix past <c>MAX_PATH</c>; .NET's own file APIs add it.</summary>
    private static string LongPath(string path) =>
        path.Length < 260 || path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path : @"\\?\" + path;

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
