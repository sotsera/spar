using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Spar.Tool.Doctor;
using Spar.Tool.Machine;
using Spar.Tool.Project;

namespace Spar.Tool.Tests;

/// <summary>The .NET SDK and Spar.Sdk, and Windows' and Linux's own checks, each against a fake machine.</summary>
public sealed class PlatformCheckTests
{
    private static readonly string Folder = Path.GetFullPath("app");

    [Fact]
    public void The_SDK_the_host_resolves_for_the_folder_is_reported()
    {
        var machine = new FakeMachine().WithProgram("dotnet", "--version", FakeMachine.Says("11.0.100\n"));
        var globalJson = new GlobalJson("/app/global.json", "11.0.100", "latestFeature", "0.1.0-dev.1", null);

        var finding = DotnetChecks.Sdk(machine, Folder, globalJson);

        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Equal(".NET SDK 11.0.100 (global.json asks for 11.0.100, rollForward latestFeature).", finding.Message);
    }

    [Fact]
    public void An_SDK_global_json_asks_for_and_the_machine_lacks_is_an_error_with_the_download()
    {
        var machine = new FakeMachine().WithProgram("dotnet", "--version",
            new ProcessResult(true, 145, "", "A compatible .NET SDK was not found.\nRequested SDK version: 11.0.200\n"));
        var globalJson = new GlobalJson("/app/global.json", "11.0.200", "latestPatch", null, null);

        var finding = DotnetChecks.Sdk(machine, Folder, globalJson);

        Assert.Equal(Severity.Error, finding.Severity);
        Assert.Contains("A compatible .NET SDK was not found.", finding.Message, StringComparison.Ordinal);
        Assert.Contains("https://dotnet.microsoft.com/download/dotnet/11.0", finding.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void Spar_Sdk_in_the_package_folder_at_the_tools_version_is_fine_and_another_version_is_a_warning()
    {
        var machine = new FakeMachine();
        machine.Variables["NUGET_PACKAGES"] = "/nuget";
        machine.WithFile("/nuget/spar.sdk/0.1.0-dev.7/Sdk/Sdk.props");
        var globalJson = new GlobalJson("/app/global.json", "11.0.100", null, "0.1.0-dev.7", null);

        var same = DotnetChecks.SparSdk(machine, Folder, globalJson, "0.1.0-dev.7+4f2c").ToList();
        var other = DotnetChecks.SparSdk(machine, Folder, globalJson, "0.1.0-dev.6").ToList();

        var resolved = Assert.Single(same);
        Assert.Equal((FindingIds.SparSdk, Severity.Info), (resolved.Id, resolved.Severity));
        Assert.Contains(other, f => f.Id == FindingIds.ToolVersion && f.Severity == Severity.Warning && f.Fix!.Contains("--version 0.1.0-dev.7", StringComparison.Ordinal));
    }

    [Fact]
    public void A_global_json_without_Spar_Sdk_is_an_error_and_one_not_yet_restored_is_information()
    {
        var machine = new FakeMachine();
        machine.Variables["NUGET_PACKAGES"] = "/nuget";

        var missing = DotnetChecks.SparSdk(machine, Folder, new GlobalJson("/app/global.json", "11.0.100", null, null, null), "0.1.0-dev.7").Single();
        var notRestored = DotnetChecks.SparSdk(machine, Folder, new GlobalJson("/app/global.json", "11.0.100", null, "0.1.0-dev.7", null), "0.1.0-dev.7").Single();

        Assert.Equal(Severity.Error, missing.Severity);
        Assert.Equal(Severity.Info, notRestored.Severity);
        Assert.Contains("downloads it", notRestored.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(unchecked((int)0x80070002), null, null, "Error", "not installed")]
    [InlineData(0, "138.0.3351.65", null, "Error", "older than Spar's minimum")]
    [InlineData(0, "139.0.3405.78", null, "Info", "WebView2 Runtime 139.0.3405.78.")]
    [InlineData(0, "157.0.4341.60 beta", null, "Info", "157.0.4341.60 beta")]
    [InlineData(-1, null, "Unable to load DLL 'WebView2Loader'", "Error", "loader could not be loaded")]
    [InlineData(unchecked((int)0x80004005), null, null, "Error", "could not read the Runtime")]
    public void The_WebView2_Runtime_is_judged_from_the_loaders_answer(int status, string? version, string? loaderProblem, string expected, string says)
    {
        var finding = WindowsChecks.WebView2(new WebView2Answer(status, version, loaderProblem));

        Assert.Equal(expected, finding.Severity.ToString());
        Assert.Contains(says, finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeAOT_on_Windows_needs_vswhere_to_find_the_C_build_tools_for_the_machine_s_architecture()
    {
        var vswhere = @"C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe";
        var none = new FakeMachine { Platform = Platform.Windows };
        var noTools = new FakeMachine { Platform = Platform.Windows }.WithProgram(vswhere, "-latest", FakeMachine.Says(""));
        var tools = new FakeMachine { Platform = Platform.Windows, Architecture = Architecture.Arm64 }
            .WithProgram(vswhere, "-latest", FakeMachine.Says(@"C:\VS\18\Community" + "\r\n"))
            .WithFile(@"C:\VS\18\Community\VC\Auxiliary\Build\vcvarsall.bat");

        Assert.Equal(Severity.Warning, WindowsChecks.NativeAot(none).Severity);
        Assert.Equal(Severity.Warning, WindowsChecks.NativeAot(noTools).Severity);
        Assert.Equal(Severity.Info, WindowsChecks.NativeAot(tools).Severity);
        Assert.Contains("Microsoft.VisualStudio.Component.VC.Tools.ARM64", Assert.Single(tools.Ran), StringComparison.Ordinal);
    }

    [Fact]
    public void Long_paths_are_information_either_way()
    {
        Assert.Equal(Severity.Info, WindowsChecks.LongPaths(1).Severity);
        Assert.Contains("260 characters", WindowsChecks.LongPaths(null).Message, StringComparison.Ordinal);
        Assert.Equal(Severity.Info, WindowsChecks.LongPaths(0).Severity);
    }

    [Fact]
    public void On_Linux_a_missing_or_old_web_engine_is_an_error_with_the_distributions_install_command()
    {
        var machine = new FakeMachine();
        machine.Texts["/etc/os-release"] = "NAME=\"Ubuntu\"\nID=ubuntu\nID_LIKE=debian\n";
        machine.Libraries["libgtk-4.so.1"] = new LibraryAnswer(true, new Version(4, 8, 3));

        var findings = LinuxChecks.Check(machine).ToDictionary(f => f.Id);

        Assert.Equal(Severity.Error, findings[FindingIds.Gtk].Severity);
        Assert.Contains("older than Spar's minimum, 4.10.0", findings[FindingIds.Gtk].Message, StringComparison.Ordinal);
        Assert.Equal("Install it with: sudo apt install libwebkitgtk-6.0-4", findings[FindingIds.WebKitGtk].Fix);
        Assert.Equal("Install it with: sudo apt install bubblewrap xdg-dbus-proxy", findings[FindingIds.Sandbox].Fix);
        Assert.Equal(Severity.Warning, findings[FindingIds.Clang].Severity);
        Assert.Equal("Install it with: sudo apt install zlib1g-dev", findings[FindingIds.Zlib].Fix);
        Assert.Equal("Install it with: sudo apt install libicu-dev", findings[FindingIds.Icu].Fix);
    }

    [Fact]
    public void On_Linux_a_complete_machine_has_only_information()
    {
        var machine = new FakeMachine();
        machine.Variables["PATH"] = "/usr/bin";
        machine.Texts["/etc/os-release"] = "ID=fedora\n";
        machine.Libraries["libgtk-4.so.1"] = new LibraryAnswer(true, new Version(4, 14, 2));
        machine.Libraries["libwebkitgtk-6.0.so.4"] = new LibraryAnswer(true, new Version(2, 50, 1));
        machine.Libraries["libssl.so.3"] = new LibraryAnswer(true, null);
        machine.WithFile("/usr/bin/bwrap", "/usr/bin/xdg-dbus-proxy", "/usr/bin/clang", "/usr/lib64/libz.so", "/usr/lib64/libicuuc.so.74");

        var findings = LinuxChecks.Check(machine).ToList();

        Assert.All(findings, finding => Assert.Equal(Severity.Info, finding.Severity));
        Assert.Contains(findings, f => f.Message == "WebKitGTK 6.0 2.50.1.");
        Assert.Equal("Install it with: sudo dnf install zlib-devel", LinuxChecks.Install(LinuxChecks.Family("ID=fedora"), LinuxChecks.Package.Zlib));
        Assert.StartsWith("Install the distribution's package", LinuxChecks.Install(LinuxChecks.Family("ID=arch"), LinuxChecks.Package.Gtk), StringComparison.Ordinal);
    }

    [Fact]
    public void The_tools_minimums_are_the_hosts()
    {
        var repository = Repository.Root;
        var windows = File.ReadAllText(Path.Combine(repository, "src", "Spar.Windows", "Prerequisites.cs"));
        var linux = File.ReadAllText(Path.Combine(repository, "src", "Spar.Linux", "Prerequisites.cs"));

        Assert.Equal(WindowsChecks.MinimumRuntime, Constant(windows, "MinimumRuntime"));
        Assert.Equal(LinuxChecks.MinimumGtk, Constant(linux, "MinimumGtk"));
        Assert.Equal(LinuxChecks.MinimumWebKit, Constant(linux, "MinimumWebKit"));
    }

    private static string Constant(string source, string name) =>
        Regex.Match(source, $@"const string {name} = ""(?<value>[^""]+)""").Groups["value"].Value;
}
