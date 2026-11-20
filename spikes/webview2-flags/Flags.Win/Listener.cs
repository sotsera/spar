using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;

namespace WebView2Flags;

/// <summary>
/// Accepts on 127.0.0.1 and [::1], ports 443 and 80, while the driver runs. Each connection is recorded with its time,
/// the owning process of the client end (from the TCP table, looked up while the connection is still open), and the TLS
/// SNI (443) or first request line (80). It never answers: it reads the first bytes, then closes.
/// </summary>
internal sealed class Listener : IDisposable
{
    private readonly List<TcpListener> _listeners = [];
    private readonly Lock _lock = new();
    private readonly List<JsonObject> _connections = [];
    private readonly CancellationTokenSource _stop = new();

    public Listener()
    {
        foreach (var address in new[] { IPAddress.Loopback, IPAddress.IPv6Loopback })
        {
            foreach (var port in new[] { 443, 80 })
            {
                var listener = new TcpListener(address, port);
                listener.Start();
                _listeners.Add(listener);
                _ = AcceptAsync(listener);
            }
        }
    }

    public List<JsonObject> Between(DateTime start, DateTime end)
    {
        lock (_lock)
        {
            return [.. _connections.Where(c => (DateTime)c["utc"]! >= start && (DateTime)c["utc"]! <= end)];
        }
    }

    public List<JsonObject> All()
    {
        lock (_lock)
        {
            return [.. _connections];
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        foreach (var l in _listeners)
        {
            l.Stop();
        }
    }

    private async Task AcceptAsync(TcpListener listener)
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch
            {
                return;
            }

            _ = HandleAsync(client);
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            var local = (IPEndPoint)client.Client.LocalEndPoint!;
            var remote = (IPEndPoint)client.Client.RemoteEndPoint!;
            var record = new JsonObject
            {
                ["utc"] = DateTime.UtcNow,
                ["local"] = local.ToString(),
                ["remote"] = remote.ToString(),
                ["pid"] = OwnerPid(remote, local),
            };
            try
            {
                var buffer = new byte[4096];
                using var timeout = new CancellationTokenSource(1500);
                var read = await client.GetStream().ReadAsync(buffer, timeout.Token);
                record["first"] = local.Port == 443 ? "SNI " + (Sni(buffer.AsSpan(0, read)) ?? "?") : FirstLine(buffer.AsSpan(0, read));
            }
            catch (Exception e)
            {
                record["first"] = "no data: " + e.GetType().Name;
            }

            lock (_lock)
            {
                _connections.Add(record);
            }
        }
    }

    private static string FirstLine(ReadOnlySpan<byte> data)
    {
        var text = Encoding.ASCII.GetString(data);
        var end = text.IndexOf('\r');
        return end < 0 ? text : text[..end];
    }

    /// <summary>The server name of a TLS ClientHello, or null.</summary>
    private static string? Sni(ReadOnlySpan<byte> d)
    {
        try
        {
            if (d.Length < 43 || d[0] != 0x16 || d[5] != 0x01)
            {
                return null;
            }

            var p = 9 + 2 + 32; // record header 5, handshake header 4, version 2, random 32
            p += 1 + d[p]; // session id
            p += 2 + ((d[p] << 8) | d[p + 1]); // cipher suites
            p += 1 + d[p]; // compression
            var end = p + 2 + ((d[p] << 8) | d[p + 1]);
            p += 2;
            while (p + 4 <= end && p + 4 <= d.Length)
            {
                int type = (d[p] << 8) | d[p + 1], length = (d[p + 2] << 8) | d[p + 3];
                if (type == 0)
                {
                    var nameLength = (d[p + 7] << 8) | d[p + 8];
                    return Encoding.ASCII.GetString(d.Slice(p + 9, nameLength));
                }

                p += 4 + length;
            }
        }
        catch (IndexOutOfRangeException)
        {
        }
        catch (ArgumentOutOfRangeException)
        {
        }

        return null;
    }

    /// <summary>The process owning the client end of a loopback connection (GetExtendedTcpTable, TCP_TABLE_OWNER_PID_ALL).</summary>
    private static unsafe uint? OwnerPid(IPEndPoint client, IPEndPoint server)
    {
        var v6 = client.AddressFamily == AddressFamily.InterNetworkV6;
        uint size = 0;
        Native.GetExtendedTcpTable(null, ref size, false, v6 ? 23u : 2u, 5, 0);
        var buffer = new byte[size + 4096];
        size = (uint)buffer.Length;
        fixed (byte* table = buffer)
        {
            if (Native.GetExtendedTcpTable(table, ref size, false, v6 ? 23u : 2u, 5, 0) != 0)
            {
                return null;
            }

            var count = *(uint*)table;
            var row = table + 4;
            var rowSize = v6 ? 56 : 24;
            for (var i = 0; i < count; i++, row += rowSize)
            {
                // MIB_TCPROW_OWNER_PID: state, localAddr, localPort, remoteAddr, remotePort, pid.
                // MIB_TCP6ROW_OWNER_PID: localAddr[16], localScope, localPort, remoteAddr[16], remoteScope, remotePort, state, pid.
                int localPort = v6 ? Port(*(uint*)(row + 20)) : Port(*(uint*)(row + 8));
                int remotePort = v6 ? Port(*(uint*)(row + 44)) : Port(*(uint*)(row + 16));
                if (localPort == client.Port && remotePort == server.Port)
                {
                    return v6 ? *(uint*)(row + 52) : *(uint*)(row + 20);
                }
            }
        }

        return null;
    }

    private static int Port(uint networkOrder) => (int)(((networkOrder & 0xFF) << 8) | ((networkOrder >> 8) & 0xFF));
}
