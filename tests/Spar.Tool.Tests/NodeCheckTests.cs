using Spar.Tool.Doctor;
using Spar.Tool.Machine;

namespace Spar.Tool.Tests;

/// <summary>Node.js and npm on PATH, and a Node.js a version manager installed that this shell does not see.</summary>
public sealed class NodeCheckTests
{
    [Fact]
    public void Node_and_npm_on_PATH_are_reported_with_their_versions()
    {
        var machine = new FakeMachine();
        machine.Variables["PATH"] = "/usr/local/bin:/usr/bin";
        machine.WithProgram("/usr/bin/node", "--version", FakeMachine.Says("v24.21.0\n"))
            .WithProgram("/usr/bin/npm", "--version", FakeMachine.Says("11.19.0\n"));

        var findings = NodeChecks.Check(machine).ToList();

        Assert.All(findings, finding => Assert.Equal(Severity.Info, finding.Severity));
        Assert.Contains(findings, f => f.Id == FindingIds.Node && f.Message.Contains("24.21.0", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Id == FindingIds.Npm && f.Message.Contains("11.19.0", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("v18.20.4", "Error")]
    [InlineData("v20.18.0", "Error")]
    [InlineData("v20.19.0", "Warning")]
    [InlineData("v22.11.0", "Error")]
    [InlineData("v22.12.0", "Warning")]
    [InlineData("v24.0.0", "Info")]
    [InlineData("v26.1.0", "Info")]
    public void A_Node_older_than_Vite_8_runs_on_is_an_error_and_one_before_24_a_warning(string version, string expected)
    {
        var machine = new FakeMachine().WithProgram("/usr/bin/node", "--version", FakeMachine.Says(version));

        var finding = NodeChecks.CheckVersion(machine, "/usr/bin/node");

        Assert.Equal(expected, finding.Severity.ToString());
    }

    [Fact]
    public void Node_on_PATH_without_npm_is_an_error()
    {
        var machine = new FakeMachine();
        machine.Variables["PATH"] = "/opt/node/bin";
        machine.WithProgram("/opt/node/bin/node", "--version", FakeMachine.Says("v24.1.0"));

        var npm = Assert.Single(NodeChecks.Check(machine), f => f.Id == FindingIds.Npm);

        Assert.Equal(Severity.Error, npm.Severity);
    }

    [Fact]
    public void Without_Node_anywhere_the_fix_is_installing_it()
    {
        var machine = new FakeMachine();
        machine.Variables["PATH"] = "/usr/bin";

        var finding = Assert.Single(NodeChecks.Check(machine));

        Assert.Equal((FindingIds.Node, Severity.Error), (finding.Id, finding.Severity));
        Assert.Contains("nodejs.org", finding.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void Nvm_s_Node_off_PATH_is_named_with_nvm_s_fix_and_the_newest_version()
    {
        var machine = new FakeMachine { Home = "/home/user" };
        machine.Variables["PATH"] = "/usr/bin";
        machine.WithFile("/home/user/.nvm/versions/node/v22.12.0/bin/node", "/home/user/.nvm/versions/node/v24.11.1/bin/node", "/home/user/.nvm/versions/node/not-a-version/bin/node");

        var findings = NodeChecks.Check(machine).ToList();

        var node = Assert.Single(findings, f => f.Id == FindingIds.Node);
        Assert.Equal(Severity.Error, node.Severity);
        Assert.Contains("nvm installed Node.js 24.11.1", node.Message, StringComparison.Ordinal);
        Assert.Contains("nvm alias default 24.11.1", node.Fix, StringComparison.Ordinal);
        Assert.Equal(2, findings.Count(f => f.Id == FindingIds.NodeVersionManager));
    }

    [Fact]
    public void Each_version_manager_s_folders_are_searched_from_its_variables_and_defaults()
    {
        var machine = new FakeMachine { Home = "/home/user" };
        machine.Variables["PATH"] = "/usr/bin";
        machine.Variables["XDG_DATA_HOME"] = "/home/user/.data";
        machine.Variables["ASDF_DATA_DIR"] = "/opt/asdf";
        machine.WithFile(
            "/home/user/.data/fnm/node-versions/v24.1.0/installation/bin/node",
            "/home/user/.volta/tools/image/node/24.2.0/bin/node",
            "/home/user/.data/mise/installs/node/24.3.0/bin/node",
            "/opt/asdf/installs/nodejs/24.4.0/bin/node");

        var managers = VersionManagers.Find(machine).Select(i => (i.Manager, i.Version.ToString(3))).OrderBy(i => i.Manager, StringComparer.Ordinal).ToList();

        Assert.Equal([("Volta", "24.2.0"), ("asdf", "24.4.0"), ("fnm", "24.1.0"), ("mise", "24.3.0")], managers);
        var fixes = VersionManagers.Find(machine).ToDictionary(i => i.Manager, i => i.Fix);
        Assert.Contains("fnm default 24.1.0", fixes["fnm"], StringComparison.Ordinal);
        Assert.Contains("volta install node@24.2.0", fixes["Volta"], StringComparison.Ordinal);
        Assert.Contains("mise use -g node@24.3.0", fixes["mise"], StringComparison.Ordinal);
        Assert.Contains("asdf set --home nodejs 24.4.0", fixes["asdf"], StringComparison.Ordinal);
    }

    [Fact]
    public void On_Windows_nvm_windows_fnm_Volta_and_mise_are_searched_in_their_Windows_folders()
    {
        var machine = new FakeMachine { Platform = Platform.Windows, Home = @"C:\Users\user" };
        machine.Variables["PATH"] = @"C:\Windows\System32";
        machine.Variables["APPDATA"] = @"C:\Users\user\AppData\Roaming";
        machine.Variables["LOCALAPPDATA"] = @"C:\Users\user\AppData\Local";
        machine.Variables["NVM_SYMLINK"] = @"C:\nvm4w\nodejs";
        machine.WithFile(
            @"C:\Users\user\AppData\Roaming\nvm\v24.1.0\node.exe",
            @"C:\Users\user\AppData\Roaming\fnm\node-versions\v24.2.0\installation\node.exe",
            @"C:\Users\user\AppData\Local\Volta\tools\image\node\24.3.0\node.exe",
            @"C:\Users\user\AppData\Local\mise\installs\node\24.4.0\node.exe");

        var found = VersionManagers.Find(machine).ToDictionary(i => i.Manager);

        Assert.Equal(["Volta", "fnm", "mise", "nvm-windows"], found.Keys.Order(StringComparer.Ordinal));
        Assert.Contains(@"nvm use 24.1.0 (it links the version at C:\nvm4w\nodejs", found["nvm-windows"].Fix, StringComparison.Ordinal);
        Assert.Contains("--shell powershell", found["fnm"].Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void On_Windows_node_exe_and_npm_cmd_are_looked_for_on_PATH()
    {
        var machine = new FakeMachine { Platform = Platform.Windows };
        machine.Variables["PATH"] = @"C:\Windows;""C:\Program Files\nodejs""";
        machine.WithProgram(@"C:\Program Files\nodejs\node.exe", "--version", FakeMachine.Says("v24.21.0"))
            .WithProgram(@"C:\Program Files\nodejs\npm.cmd", "--version", FakeMachine.Says("11.19.0"));

        var findings = NodeChecks.Check(machine).ToList();

        Assert.All(findings, finding => Assert.Equal(Severity.Info, finding.Severity));
        Assert.Equal(2, findings.Count);
    }
}
