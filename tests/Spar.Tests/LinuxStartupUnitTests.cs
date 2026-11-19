using System.Runtime.Versioning;
using Spar.Hosting;
using Spar.Linux;

namespace Spar.Tests;

/// <summary>
/// The Linux start-up's decisions as plain functions, on every OS (plan M1c, build step 2): the display, WebKitGTK's and
/// GTK's versions, the helper folder, the trial sandbox run, the install hints, the software-GPU verdicts, the portal's
/// values and the saved theme choice, the option's shape, and single instance's lock and socket on Linux itself. The
/// real paths, in real processes, are <see cref="LinuxStartupTests"/>.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxStartupUnitTests : IDisposable
{
    private static readonly Distribution Ubuntu = Distribution.FromOsRelease("NAME=\"Ubuntu\"\nID=ubuntu\nID_LIKE=debian\n");

    private readonly string _root = Path.Combine(TestFolders.Artifacts(), "test-host", "linux-startup", Guid.NewGuid().ToString("N")[..12]);
    private readonly HostLog _log = new(standardError: false);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // artifacts/ is disposable.
        }
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData("", "", true)]
    [InlineData(":0", null, false)]
    [InlineData(null, "wayland-0", false)]
    public void A_session_names_its_display_or_the_app_exits_6(string? display, string? wayland, bool refused)
    {
        var problem = Prerequisites.CheckDisplay(name => name == "DISPLAY" ? display : name == "WAYLAND_DISPLAY" ? wayland : null);

        Assert.Equal(refused, problem is not null);
        if (problem is not null)
        {
            Assert.Equal((StartupProblemKind.NoGraphicalSession, 6), (problem.Kind, problem.ExitCode));
        }
    }

    [Theory]
    [InlineData("2.52.6", true)]
    [InlineData("2.48.0", true)]
    [InlineData("2.46.9", false)]
    [InlineData("2.44.0", false)]
    public void WebKitGTK_2_48_is_the_oldest_supported(string found, bool supported)
    {
        var problem = Prerequisites.DecideWebKit(Version.Parse(found), Version.Parse(Prerequisites.MinimumWebKit), Ubuntu);

        Assert.Equal(supported, problem is null);
        if (problem is not null)
        {
            Assert.Equal((StartupProblemKind.TooOld, 4, "WebKitGTK 6.0", found, "2.48.0"), (problem.Kind, problem.ExitCode, problem.Component, problem.Found, problem.Required));
            Assert.Contains("sudo apt update && sudo apt upgrade", problem.Details, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("4.14.5", true)]
    [InlineData("4.10.0", true)]
    [InlineData("4.8.3", false)]
    public void GTK_needs_4_10_for_the_hosts_dialogs(string found, bool supported)
    {
        var problem = Prerequisites.DecideGtk(Version.Parse(found), Ubuntu);

        Assert.Equal(supported, problem is null);
        Assert.Equal(supported ? null : (StartupProblemKind?)StartupProblemKind.TooOld, problem?.Kind);
    }

    [Fact]
    public void The_helpers_are_found_beside_the_loaded_library_or_in_libexec()
    {
        const string Debian = "7f00-7f10 r-xp 00000000 08:20 123 /usr/lib/x86_64-linux-gnu/libwebkitgtk-6.0.so.4.16.10\n";
        const string Fedora = "7f00-7f10 r-xp 00000000 08:20 123 /usr/lib64/libwebkitgtk-6.0.so.4.16.10\n";

        Assert.Equal("/usr/lib/x86_64-linux-gnu/webkitgtk-6.0", Prerequisites.HelperFolder(Debian, folder => folder == "/usr/lib/x86_64-linux-gnu/webkitgtk-6.0"));
        Assert.Equal("/usr/libexec/webkitgtk-6.0", Prerequisites.HelperFolder(Fedora, folder => folder == "/usr/libexec/webkitgtk-6.0"));
        Assert.Null(Prerequisites.HelperFolder(Debian, _ => false));
        Assert.Equal(
            (StartupProblemKind.Broken, 5),
            Prerequisites.CheckHelpers("", Ubuntu) is { } problem ? (problem.Kind, problem.ExitCode) : default);
    }

    [Fact]
    public void A_trial_sandbox_run_that_fails_is_broken_and_says_why()
    {
        var denied = Prerequisites.DecideTrial(new ChildResult(true, 1, "bwrap: No permissions to create new namespace", false, 1.5));
        var timedOut = Prerequisites.DecideTrial(new ChildResult(true, -1, "", true, 5_000));

        Assert.Null(Prerequisites.DecideTrial(new ChildResult(true, 0, "", false, 4)));
        Assert.NotNull(denied);
        Assert.Equal((StartupProblemKind.Broken, 5, "Web content sandbox"), (denied.Kind, denied.ExitCode, denied.Component));
        Assert.Contains("No permissions to create new namespace", denied.Details, StringComparison.Ordinal);
        Assert.Contains("did not finish within 5 seconds", timedOut!.Details, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ID=ubuntu\nID_LIKE=debian\n", "sudo apt install libwebkitgtk-6.0-4")]
    [InlineData("ID=debian\n", "sudo apt install libwebkitgtk-6.0-4")]
    [InlineData("ID=fedora\n", "sudo dnf install webkitgtk6.0")]
    [InlineData("ID=\"linuxmint\"\nID_LIKE=\"ubuntu debian\"\n", "sudo apt install libwebkitgtk-6.0-4")]
    [InlineData("ID=arch\n", "libwebkitgtk-6.0-4 on Debian and Ubuntu, webkitgtk6.0 on Fedora")]
    public void The_message_names_the_distributions_package(string osRelease, string hint)
    {
        Assert.Contains(hint, Distribution.FromOsRelease(osRelease).InstallHint, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("llvmpipe (LLVM 19.1.1, 256 bits)", true)]
    [InlineData("softpipe", true)]
    [InlineData("SwiftShader Device (Subzero) (0x0000C0DE)", true)]
    [InlineData("Microsoft Basic Render Driver", true)]
    [InlineData("D3D12 (Intel(R) UHD Graphics 770)", false)]
    [InlineData("Mesa Intel(R) UHD Graphics 770 (ADL-S GT1)", false)]
    [InlineData("AMD Radeon RX 6600 (radeonsi, navi23, LLVM 19.1.1, DRM 3.57)", false)]
    [InlineData("NVIDIA GeForce RTX 4070/PCIe/SSE2", false)]
    public void A_software_renderer_is_told_by_its_name(string renderer, bool software)
    {
        Assert.Equal(software, GpuCheck.IsSoftwareRenderer(renderer));
    }

    /// <summary>
    /// The EGL half of the check, which runs where a render node can be opened (none can in WSL as the user is set up):
    /// a surfaceless display and a GLES context name the renderer, and nothing is left current. Its cost is recorded.
    /// </summary>
    [Fact]
    public void The_EGL_check_names_the_renderer_and_tears_down()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() && File.Exists("/usr/lib/x86_64-linux-gnu/libEGL.so.1"), "Needs Linux with libEGL.");
        var first = System.Diagnostics.Stopwatch.StartNew();
        var (renderer, failure) = GpuCheck.Renderer();
        first.Stop();
        var again = System.Diagnostics.Stopwatch.StartNew();
        _ = GpuCheck.Renderer();
        again.Stop();

        Assert.True(renderer is not null, failure);
        var results = Path.Combine(TestFolders.Artifacts(), "test-results", "Spar.Tests");
        Directory.CreateDirectory(results);
        File.AppendAllLines(Path.Combine(results, "linux-startup.txt"), [string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{DateTimeOffset.Now:HH:mm:ss} EGL check (test process, CoreCLR): '{renderer}', software {GpuCheck.IsSoftwareRenderer(renderer!)}, first {first.Elapsed.TotalMilliseconds:0.0} ms, again {again.Elapsed.TotalMilliseconds:0.0} ms")]);
    }

    [Theory]
    [InlineData("vgem", true)]
    [InlineData("vkms", true)]
    [InlineData("i915", false)]
    [InlineData("amdgpu", false)]
    public void A_virtual_render_node_is_no_GPU(string driver, bool isVirtual)
    {
        Assert.Equal(isVirtual, GpuCheck.IsVirtualDriver(driver));
    }

    [Theory]
    [InlineData(0u, false)]
    [InlineData(1u, true)]
    [InlineData(2u, false)]
    [InlineData(7u, null)]
    public void The_portals_value_one_is_dark_and_no_preference_is_light(uint value, bool? dark)
    {
        Assert.Equal(dark, WindowTheme.DarkFromPortal(value));
    }

    [Theory]
    [InlineData("Adwaita-dark", true)]
    [InlineData("Yaru-dark", true)]
    [InlineData("Breeze:dark", true)]
    [InlineData("Adwaita", false)]
    [InlineData(null, false)]
    public void A_dark_GTK_theme_name_counts_as_dark(string? name, bool dark)
    {
        Assert.Equal(dark, WindowTheme.IsDarkThemeName(name));
    }

    [Fact]
    public void The_window_is_painted_by_a_class_rule_in_the_backgrounds_colour()
    {
        Assert.Equal("window.spar-window { background-color: #1d1f24; }", WindowTheme.Css(SparColor.Parse("#1d1f24", "dark")));
    }

    [Theory]
    [InlineData("dark", true)]
    [InlineData("light\n", false)]
    [InlineData("system", null)]
    [InlineData("purple", null)]
    [InlineData(null, null)]
    public void The_apps_saved_scheme_is_read_from_the_data_root(string? saved, bool? dark)
    {
        Directory.CreateDirectory(_root);
        if (saved is not null)
        {
            File.WriteAllText(Path.Combine(_root, SavedColorScheme.FileName), saved);
        }

        Assert.Equal(dark, SavedColorScheme.Read(_root));
    }

    [Fact]
    public void Hardware_acceleration_is_Auto_unless_the_app_says_otherwise_and_is_no_opt_in()
    {
        var builder = new SparBuilder([]);
        Assert.Equal(HardwareAcceleration.Auto, builder.BrowserOptions.Acceleration);

        builder.Browser(b => b.HardwareAcceleration(HardwareAcceleration.Never));

        Assert.Equal(HardwareAcceleration.Never, builder.BrowserOptions.Acceleration);
        Assert.Equal("", builder.BrowserOptions.Active());
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Browser(b => b.HardwareAcceleration((HardwareAcceleration)7)));
    }

    [Fact]
    public void A_later_launch_hands_its_arguments_and_token_to_the_first()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Linux single instance");
        var received = new List<(Relaunch Relaunch, string? Token)>();
        using var primary = Assert.IsType<SingleInstance>(SingleInstance.Claim(_root, Relaunch([]), null, _log).Instance);
        primary.Listen((relaunch, token) =>
        {
            lock (received)
            {
                received.Add((relaunch, token));
            }

            return true;
        }, _log);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var second = SingleInstance.Claim(_root, Relaunch(["--recipe", "nightly.sqlrecipe"]), "token-1", _log);
        clock.Stop();

        Assert.Null(second.Problem);
        Assert.Null(second.Instance);
        Assert.Equal((uint)Environment.ProcessId, second.ForwardedTo);
        var (relaunch, token) = Assert.Single(received);
        Assert.Equal(["--recipe", "nightly.sqlrecipe"], relaunch.Arguments);
        Assert.Equal("token-1", token);
        Assert.True(clock.ElapsedMilliseconds < 1_000, $"the hand-off took {clock.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void A_first_that_does_not_answer_is_exit_8_after_the_acknowledgement_wait()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Linux single instance");
        using var primary = SingleInstance.Claim(_root, Relaunch([]), null, _log).Instance!;
        using var release = new ManualResetEventSlim();
        primary.Listen((_, _) => release.Wait(TimeSpan.FromSeconds(5)), _log);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var second = SingleInstance.Claim(_root, Relaunch(["late"]), null, _log);
        clock.Stop();
        release.Set();

        Assert.NotNull(second.Problem);
        Assert.Equal((StartupProblemKind.NotResponding, 8), (second.Problem.Kind, second.Problem.ExitCode));
        Assert.InRange(clock.ElapsedMilliseconds, 1_900, 3_000);
    }

    [Fact]
    public void A_lock_nobody_serves_is_exit_8_after_the_lock_wait()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Linux single instance");
        using var primary = SingleInstance.Claim(_root, Relaunch([]), null, _log).Instance!;

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var second = SingleInstance.Claim(_root, Relaunch([]), null, _log);
        clock.Stop();

        Assert.Equal(StartupProblemKind.NotResponding, second.Problem?.Kind);
        Assert.InRange(clock.ElapsedMilliseconds, SingleInstance.LockWaitMilliseconds - 100, SingleInstance.LockWaitMilliseconds + 1_000);
    }

    [Fact]
    public void The_lock_is_free_again_once_the_first_is_disposed_and_its_socket_is_the_users_own()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Linux single instance");
        var first = SingleInstance.Claim(_root, Relaunch([]), null, _log).Instance!;
        first.Listen((_, _) => true, _log);
        var socket = SingleInstance.SocketName(Path.Combine(_root, SingleInstance.LockFileName));
        if (!socket.StartsWith('\0'))
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(socket));
        }

        first.Dispose();

        using var again = SingleInstance.Claim(_root, Relaunch([]), null, _log).Instance;
        Assert.NotNull(again);
        Assert.False(!socket.StartsWith('\0') && File.Exists(socket), "The socket's name outlived its instance.");
    }

    private static Relaunch Relaunch(string[] arguments) => new(arguments, Environment.CurrentDirectory);
}
