using System.Buffers.Binary;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;

namespace Spar.Devtools;

/// <summary>
/// The devtools' local socket (decision "M2's decide-first questions", item 3): no port, so no page in a browser can
/// reach it, and only this user can open it. Each accepted connection is a stream of newline-delimited JSON-RPC.
/// </summary>
internal abstract partial class DevtoolsListener : IAsyncDisposable
{
    /// <summary><c>pipe</c> or <c>unix</c>, as the discovery file names it.</summary>
    public abstract string Transport { get; }

    /// <summary>The pipe's name or the socket's path.</summary>
    public abstract string Address { get; }

    /// <summary>
    /// A new socket for <paramref name="run"/>, its name starting with <paramref name="prefix"/>: the devtools', or
    /// <c>spar-dev-</c> for <c>spar dev</c>'s own (the spar tool links this file).
    /// </summary>
    public static DevtoolsListener Start(string run, string prefix = "spar-devtools-") =>
        OperatingSystem.IsWindows() ? new PipeListener(prefix) : new UnixListener(run, prefix);

    /// <summary>The next client's connection; throws <see cref="OperationCanceledException"/> once stopped.</summary>
    public abstract ValueTask<Stream> AcceptAsync(CancellationToken cancellationToken);

    public abstract ValueTask DisposeAsync();

    /// <summary>
    /// A named pipe whose name holds 16 random bytes, so no other process can create it first, with a protected access
    /// list granting the current user alone (the token's user, never its owner: pitfall "CurrentUserOnly follows the
    /// token's owner"). .NET creates every instance with <c>PIPE_REJECT_REMOTE_CLIENTS</c>.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private sealed class PipeListener(string prefix) : DevtoolsListener
    {
        private readonly PipeSecurity _security = OwnerOnly();
        private NamedPipeServerStream? _waiting;
        private int _disposed;

        public override string Transport => "pipe";

        public override string Address { get; } = prefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

        public override async ValueTask<Stream> AcceptAsync(CancellationToken cancellationToken)
        {
            var server = NamedPipeServerStreamAcl.Create(
                Address, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous, 0, 0, _security);
            _waiting = server;
            try
            {
                await server.WaitForConnectionAsync(cancellationToken);
                _waiting = null;
                return server;
            }
            catch
            {
                _waiting = null;
                await server.DisposeAsync();
                throw;
            }
        }

        public override async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0 && _waiting is { } waiting)
            {
                await waiting.DisposeAsync();
            }
        }

        private static PipeSecurity OwnerOnly()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var user = identity.User!;
            var security = new PipeSecurity();
            security.SetOwner(user);
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
            return security;
        }
    }

    /// <summary>
    /// A Unix socket in the user's runtime folder (<c>XDG_RUNTIME_DIR</c>, mode 0700), or else in a new 0700 folder of
    /// its own under the temporary folder; the socket itself is 0600, and a peer of another user is refused
    /// (<c>SO_PEERCRED</c>).
    /// </summary>
    [UnsupportedOSPlatform("windows")]
    private sealed partial class UnixListener : DevtoolsListener
    {
        private const int SolSocket = 1;
        private const int SoPeerCred = 17;

        /// <summary>The longest path a Unix socket address takes, less its terminating zero.</summary>
        private const int MaxPath = 107;

        private readonly Socket _socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        private readonly string? _ownFolder;
        private int _disposed;

        public UnixListener(string run, string prefix)
        {
            var name = $"{prefix}{Environment.ProcessId}-{run}.sock";
            var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            if (runtime is { Length: > 0 } && Path.IsPathFullyQualified(runtime) && Directory.Exists(runtime)
                && Path.Combine(runtime, name).Length <= MaxPath)
            {
                Address = Path.Combine(runtime, name);
            }
            else
            {
                _ownFolder = Directory.CreateTempSubdirectory(prefix).FullName;
                Address = Path.Combine(_ownFolder, "socket");
            }

            File.Delete(Address);
            _socket.Bind(new UnixDomainSocketEndPoint(Address));
            File.SetUnixFileMode(Address, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            _socket.Listen(8);
        }

        public override string Transport => "unix";

        public override string Address { get; }

        public override async ValueTask<Stream> AcceptAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                var client = await _socket.AcceptAsync(cancellationToken);
                if (SameUser(client))
                {
                    return new NetworkStream(client, ownsSocket: true);
                }

                client.Dispose();
            }
        }

        public override ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _socket.Dispose();
                TryDelete(Address);
                if (_ownFolder is not null)
                {
                    try
                    {
                        Directory.Delete(_ownFolder);
                    }
                    catch (IOException)
                    {
                        // Not empty: something else was put there; it is left.
                    }
                }
            }

            return ValueTask.CompletedTask;
        }

        private static bool SameUser(Socket client)
        {
            // struct ucred { pid_t pid; uid_t uid; gid_t gid; }
            Span<byte> credentials = stackalloc byte[12];
            var read = client.GetRawSocketOption(SolSocket, SoPeerCred, credentials);
            return read == credentials.Length && BinaryPrimitives.ReadUInt32LittleEndian(credentials[4..]) == GetEffectiveUserId();
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Gone already, or the folder went first.
            }
        }

        [LibraryImport("libc", EntryPoint = "geteuid")]
        private static partial uint GetEffectiveUserId();
    }
}
