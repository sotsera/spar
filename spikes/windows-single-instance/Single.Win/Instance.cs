using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using static Spar.SingleInstance.Native;

namespace Spar.SingleInstance;

/// <summary>
/// The mechanism under test. The lock is an exclusive (FileShare.None) handle on <c>instance.lock</c> in the data
/// folder, which the OS releases when the process ends, crash included. The pipe's name comes from that lock file
/// (its path, or its file identity). With <c>--security spar</c> (the default) the server's DACL names the token's user
/// SID only, and the client checks the user of the server process; <c>--security dotnet</c> uses
/// <see cref="PipeOptions.CurrentUserOnly"/> on both ends instead, which follows the token's owner.
/// </summary>
internal static class Instance
{
    public const int Forwarded = 0;
    public const int NotResponding = 10;
    public const int StillClosing = 11;
    public const int OtherUser = 12;
    public const int PipeUnavailable = 13;

    private const int HeaderSize = 20;
    private static readonly ConcurrentQueue<string> s_received = new();

    public static int Run(Options options)
    {
        var mainEntry = Log.Ticks;
        Directory.CreateDirectory(options.Data);
        var lockPath = Path.Combine(options.Data, "instance.lock");
        var deadline = Log.Now + options.LockWaitMs;
        var attempt = 0;
        while (true)
        {
            attempt++;
            var lockFile = TryLock(lockPath);
            if (lockFile is not null)
            {
                Log.Mark($"lock taken {Log.Now - Log.Milliseconds(mainEntry):F1} ms after main, attempt {attempt}");
                return RunPrimary(options, lockPath, lockFile);
            }

            var name = PipeName(options.Naming, lockPath, null);
            var outcome = TrySend(options, name, mainEntry);
            if (outcome is not null)
            {
                return outcome.Value;
            }

            if (Log.Now > deadline)
            {
                Log.Mark($"the lock stays held and nobody serves {name} after {options.LockWaitMs} ms; giving up");
                return StillClosing;
            }

            Thread.Sleep(20);
        }
    }

    /// <summary>The name both ends derive from the lock file: its full path, or its volume and file id.</summary>
    public static string PipeName(string naming, string lockPath, SafeHandle? lockHandle)
    {
        string identity;
        if (naming == "path")
        {
            identity = Path.GetFullPath(lockPath).ToUpperInvariant();
        }
        else
        {
            identity = FileIdentity(lockPath, lockHandle);
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return "spar-" + Convert.ToHexStringLower(hash)[..32];
    }

    public static string Sddl(SafeHandle handle)
    {
        var status = GetSecurityInfo(
            handle, SeKernelObject, OwnerSecurityInformation | DaclSecurityInformation, out _, out _, out _, out _,
            out var descriptor);
        if (status != 0)
        {
            return $"(GetSecurityInfo {status})";
        }

        ConvertSecurityDescriptorToString(descriptor, 1, OwnerSecurityInformation | DaclSecurityInformation, out var text, out _);
        var result = Marshal.PtrToStringUni(text) ?? "";
        LocalFree(text);
        LocalFree(descriptor);
        return result;
    }

    private static string FileIdentity(string lockPath, SafeHandle? lockHandle)
    {
        // An open with no access rights is allowed while another process holds the file with FileShare.None.
        // A raw CreateFileW call gets no long-path handling from .NET: past MAX_PATH it needs the \\?\ prefix.
        using var queried = lockHandle is null
            ? CreateFile(@"\\?\" + Path.GetFullPath(lockPath), 0, FileShareAll, 0, OpenExisting, 0, 0)
            : null;
        var handle = lockHandle ?? queried!;
        if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var information))
        {
            throw new IOException($"Cannot read the identity of {lockPath}: error {Marshal.GetLastPInvokeError()}");
        }

        return $"{information.VolumeSerialNumber:x8}-{information.FileIndexHigh:x8}{information.FileIndexLow:x8}";
    }

    private static FileStream? TryLock(string lockPath)
    {
        try
        {
            return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1);
        }
        catch (IOException exception) when (exception.HResult == unchecked((int)0x80070020))
        {
            return null; // ERROR_SHARING_VIOLATION: another process holds the lock
        }
    }

    /// <summary>Forwarded when the primary acknowledged; a failure code; null when nobody serves the pipe.</summary>
    private static int? TrySend(Options options, string name, long mainEntry)
    {
        var dotnet = options.Security == "dotnet";
        var pipeOptions = dotnet ? PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous : PipeOptions.Asynchronous;
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, pipeOptions);
        var started = Log.Now;
        try
        {
            client.Connect(options.ConnectTimeoutMs);
        }
        catch (TimeoutException)
        {
            Log.Mark($"connect: no free instance of {name} within {options.ConnectTimeoutMs} ms");
            return null;
        }
        catch (UnauthorizedAccessException exception)
        {
            Log.Mark($"connect refused, {exception.GetType().Name}: {exception.Message}");
            return OtherUser;
        }

        GetNamedPipeServerProcessId(client.SafePipeHandle, out var serverId);
        GetNamedPipeServerSessionId(client.SafePipeHandle, out var serverSession);
        ProcessIdToSessionId((uint)Environment.ProcessId, out var ownSession);
        var serverUser = ProcessUser(serverId);
        Log.Mark($"server pid {serverId} user {serverUser} session {serverSession} (ours {CurrentUser} session {ownSession}); pipe {Sddl(client.SafePipeHandle)}");
        if (!dotnet && serverUser != CurrentUser)
        {
            Log.Mark($"connect refused: the server process runs as {serverUser}, not as this user");
            return OtherUser;
        }

        if (options.AllowForeground)
        {
            var allowed = AllowSetForegroundWindow(serverId);
            var error = allowed ? 0 : Marshal.GetLastPInvokeError();
            Log.Mark($"connected to pid {serverId} in {Log.Now - started:F1} ms; AllowSetForegroundWindow={allowed} error={error}; {ForegroundDescription()}");
        }
        else
        {
            Log.Mark($"connected to pid {serverId} in {Log.Now - started:F1} ms; AllowSetForegroundWindow skipped");
        }

        var payload = Encoding.UTF8.GetBytes(string.Join('\u001f', options.Forwarded));
        var message = new byte[HeaderSize + payload.Length];
        BinaryPrimitives.WriteInt64LittleEndian(message, options.LaunchTicks ?? mainEntry);
        BinaryPrimitives.WriteInt64LittleEndian(message.AsSpan(8), mainEntry);
        BinaryPrimitives.WriteInt32LittleEndian(message.AsSpan(16), payload.Length);
        payload.CopyTo(message, HeaderSize);

        using var timeout = new CancellationTokenSource(options.AckTimeoutMs);
        try
        {
            client.WriteAsync(message, timeout.Token).AsTask().GetAwaiter().GetResult();
            var ack = new byte[3];
            var read = client.ReadAsync(ack, timeout.Token).AsTask().GetAwaiter().GetResult();
            Log.Mark($"forwarded; ack '{Encoding.UTF8.GetString(ack, 0, read).Trim()}' {Log.Now - started:F1} ms after connecting");
            return read > 0 ? Forwarded : null;
        }
        catch (OperationCanceledException)
        {
            Log.Mark($"the primary (pid {serverId}) accepted but did not answer within {options.AckTimeoutMs} ms; not starting a second primary");
            return NotResponding;
        }
        catch (IOException exception)
        {
            Log.Mark($"the pipe broke ({exception.Message}); the primary is exiting");
            return null;
        }
    }

    private static int RunPrimary(Options options, string lockPath, FileStream lockFile)
    {
        var name = PipeName(options.Naming, lockPath, lockFile.SafeFileHandle);
        NamedPipeServerStream? server = null;
        try
        {
            server = options.Security == "dotnet" ? CreateDotnetServer(name) : CreateServer(name);
            Log.Mark($"primary serving {name} sddl={Sddl(server.SafePipeHandle)} args=[{string.Join(' ', options.Forwarded)}]");
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            Log.Mark($"primary without a pipe: {name} is taken ({exception.GetType().Name}: {exception.Message})");
        }

        using var stop = new CancellationTokenSource();
        var serving = server is null ? Task.CompletedTask : Task.Run(() => Serve(server, options, stop.Token));
        if (options.Window is not null)
        {
            AppWindow.Run($"spar-si-primary-{Environment.ProcessId}", options.Window, options.HoldMs, OnWindowMessage);
        }
        else
        {
            Thread.Sleep(options.HoldMs);
        }

        stop.Cancel();
        serving.GetAwaiter().GetResult();
        server?.Dispose();
        Log.Mark("primary stopped serving");
        if (options.SlowExitMs > 0)
        {
            Log.Mark($"primary exiting slowly ({options.SlowExitMs} ms) with the pipe closed and the lock held");
            Thread.Sleep(options.SlowExitMs);
        }

        lockFile.Dispose();
        Log.Mark("primary released the lock");
        return server is null ? PipeUnavailable : 0;
    }

    /// <summary>The token's user SID (TokenUser), which an elevated token keeps; its owner becomes Administrators.</summary>
    private static string CurrentUser { get; } = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;

    // One instance: .NET then asks for FILE_FLAG_FIRST_PIPE_INSTANCE, so a name taken by anyone fails here.
    private static NamedPipeServerStream CreateDotnetServer(string name) => new(
        name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 4096, 4096);

    /// <summary>The first and only instance, owned by and open only to the token's user, local clients only.</summary>
    private static unsafe NamedPipeServerStream CreateServer(string name)
    {
        var sddl = $"O:{CurrentUser}D:P(A;;GA;;;{CurrentUser})";
        if (!ConvertStringToSecurityDescriptor(sddl, 1, out var descriptor, 0))
        {
            throw new IOException($"Bad security descriptor {sddl}: error {Marshal.GetLastPInvokeError()}");
        }

        try
        {
            var attributes = new SecurityAttributes { Length = sizeof(SecurityAttributes), SecurityDescriptor = descriptor };
            var handle = CreateNamedPipe(
                @"\\.\pipe\" + name, PipeAccessDuplex | FileFlagFirstPipeInstance | FileFlagOverlapped,
                PipeRejectRemoteClients, 1, 4096, 4096, 0, &attributes);
            if (handle.IsInvalid)
            {
                var error = Marshal.GetLastPInvokeError();
                handle.Dispose();
                throw new IOException($"CreateNamedPipe failed, error {error}");
            }

            return new NamedPipeServerStream(PipeDirection.InOut, true, false, handle);
        }
        finally
        {
            LocalFree(descriptor);
        }
    }

    private static async Task Serve(NamedPipeServerStream server, Options options, CancellationToken stop)
    {
        var header = new byte[HeaderSize];
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await server.WaitForConnectionAsync(stop);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
                timeout.CancelAfter(1000);
                await server.ReadExactlyAsync(header, timeout.Token);
                var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(16));
                var payload = new byte[length];
                await server.ReadExactlyAsync(payload, timeout.Token);

                var now = Log.Now;
                var launch = Log.Milliseconds(BinaryPrimitives.ReadInt64LittleEndian(header));
                var main = Log.Milliseconds(BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(8)));
                var text = Encoding.UTF8.GetString(payload).Replace('\u001f', ' ');
                Log.Mark($"received args=[{text}] from_launch_ms={now - launch:F1} from_main_ms={now - main:F1}");
                s_received.Enqueue(text);
                if (options.Window is not null)
                {
                    PostMessage(AppWindow.Handle, WmApp, 0, 0);
                }

                await server.WriteAsync("ok\n"u8.ToArray(), timeout.Token);

                // Disconnecting discards unread data, so wait for the client to close its end first.
                var end = new byte[1];
                if (await server.ReadAsync(end, timeout.Token) != 0)
                {
                    Log.Mark("the client sent more than one message");
                }
            }
            catch (OperationCanceledException) when (!stop.IsCancellationRequested)
            {
                Log.Mark("a client sent nothing, or did not close, within 1 s");
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException exception)
            {
                Log.Mark($"the client is gone ({exception.Message}); its arguments, if any, were handled");
            }
            finally
            {
                try
                {
                    server.Disconnect();
                }
                catch (InvalidOperationException)
                {
                }
            }
        }
    }

    private static bool OnWindowMessage(nint window, uint message, nuint wParam, nint lParam)
    {
        if (message == WmApp)
        {
            s_received.TryDequeue(out var text);
            var iconic = IsIconic(window);
            var visible = IsWindowVisible(window);
            if (!visible)
            {
                ShowWindow(window, SwShow);
            }

            if (iconic)
            {
                ShowWindow(window, SwRestore);
            }

            var result = SetForegroundWindow(window);
            Log.Mark($"activate for [{text}]: iconic={iconic} visible={visible} SetForegroundWindow={result}; {ForegroundDescription()}");
            SetTimer(window, 2, 300, 0);
            return true;
        }

        if (message == WmTimer && wParam == 2)
        {
            KillTimer(window, 2);
            var foreground = GetForegroundWindow() == window;
            Log.Mark($"300 ms after activating: foreground={foreground}; {ForegroundDescription()}");
            return true;
        }

        return false;
    }
}
