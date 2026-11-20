using System.Diagnostics;

namespace Spike.BlackBoxAtSpi.Runs;

/// <summary>
/// An accessibility bus the test process starts itself: a plain <c>dbus-daemon</c> (the dbus-daemon package, which a
/// session bus needs anyway) with a configuration of the reader's own, no at-spi2-core. The app gets its address in
/// <c>AT_SPI_BUS_ADDRESS</c>, which GTK 4 reads before asking the session bus's <c>org.a11y.Bus</c>.
/// </summary>
internal sealed class PrivateBus : IDisposable
{
    private readonly Process _daemon;

    private PrivateBus(Process daemon, string address, double startMs)
    {
        _daemon = daemon;
        Address = address;
        StartMs = startMs;
    }

    public string Address { get; }

    public double StartMs { get; }

    public static PrivateBus Start()
    {
        var folder = Path.Combine(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR")!, "a11y");
        Directory.CreateDirectory(folder);
        var config = Path.Combine(folder, "a11y.conf");
        File.WriteAllText(config, $"""
            <!DOCTYPE busconfig PUBLIC "-//freedesktop//DTD D-Bus Bus Configuration 1.0//EN" "http://www.freedesktop.org/standards/dbus/1.0/busconfig.dtd">
            <busconfig>
              <type>accessibility</type>
              <listen>unix:dir={folder}</listen>
              <auth>EXTERNAL</auth>
              <policy context="default"><allow send_destination="*" eavesdrop="true"/><allow eavesdrop="true"/><allow own="*"/></policy>
            </busconfig>
            """);
        var clock = Stopwatch.StartNew();
        var info = new ProcessStartInfo("dbus-daemon", ["--config-file=" + config, "--print-address=1", "--nofork", "--nopidfile"])
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            KillOnParentExit = true,
        };
        var daemon = Process.Start(info)!;
        var address = daemon.StandardOutput.ReadLine() ?? throw new InvalidOperationException("dbus-daemon printed no address");
        return new PrivateBus(daemon, address.Trim(), clock.Elapsed.TotalMilliseconds);
    }

    public void Dispose()
    {
        if (!_daemon.HasExited)
        {
            _daemon.Kill();
            _daemon.WaitForExit(2_000);
        }

        _daemon.Dispose();
    }
}
