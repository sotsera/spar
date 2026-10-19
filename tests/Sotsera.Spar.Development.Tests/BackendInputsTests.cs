using AwesomeAssertions;
using Sotsera.Spar.Development;

namespace Sotsera.Spar.Development.Tests;

public sealed class BackendInputsTests
{
    [Fact]
    public async Task BuildOutputsFrontendAndSessionSettingsDoNotInvalidateSelection()
    {
        var settings = CreateSettings();
        using var inputs = new BackendInputs(settings);
        var before = await CaptureAsync(inputs);
        foreach (var directory in new[] { "bin", "obj", "frontend", "sessions" })
        {
            var output = Path.Combine(Path.GetDirectoryName(settings.Project)!, directory);
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "Generated.cs"), "generated output");
        }
        File.WriteAllText(settings.ConfigurationFile!, "{}");
        await Task.Delay(200, TestContext.Current.CancellationToken);
        (await CaptureAsync(inputs)).Should().Be(before);
    }

    [Fact]
    public async Task EditThenRevertStillInvalidatesAnInFlightBuild()
    {
        var settings = CreateSettings();
        using var inputs = new BackendInputs(settings);
        var before = await CaptureAsync(inputs);
        var source = Path.Combine(Path.GetDirectoryName(settings.Project)!, "Backend.cs");
        File.WriteAllText(source, "class Changed;");
        await WaitForChangeAsync(inputs, before);
        File.WriteAllText(source, "class Backend;");
        await Task.Delay(200, TestContext.Current.CancellationToken);
        var after = await CaptureAsync(inputs);
        after.Hash.Should().Be(before.Hash);
        after.Revision.Should().BeGreaterThan(before.Revision);
    }

    [Fact]
    public async Task StartupAndDependencyInputsRequestRestartButMethodSourceDoesNot()
    {
        var settings = CreateSettings();
        using var inputs = new BackendInputs(settings);
        var before = await CaptureAsync(inputs);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(settings.Project)!, "Backend.cs"), "class Changed;");
        var method = await WaitForChangeAsync(inputs, before);
        method.RestartHash.Should().Be(before.RestartHash);
        File.WriteAllText(settings.RestartFiles[0], "class StartupChanged;");
        var startup = await WaitForChangeAsync(inputs, method);
        startup.RestartHash.Should().NotBe(method.RestartHash);
        File.WriteAllText(settings.Project, "<Project><ItemGroup /></Project>");
        var project = await WaitForChangeAsync(inputs, startup);
        project.RestartHash.Should().NotBe(startup.RestartHash);
    }

    [Fact]
    public async Task AtomicReplacementAndDeletionChangeTheInputManifest()
    {
        var settings = CreateSettings();
        using var inputs = new BackendInputs(settings);
        var before = await CaptureAsync(inputs);
        var replacement = settings.ContractFile + ".tmp";
        File.WriteAllText(replacement, "class NewContract;");
        File.Move(replacement, settings.ContractFile!, true);
        var replaced = await WaitForChangeAsync(inputs, before);
        replaced.ContractHash.Should().NotBe(before.ContractHash);
        File.Delete(settings.ContractFile!);
        var deleted = await WaitForChangeAsync(inputs, replaced);
        deleted.ContractHash.Should().Be("missing");
    }

    [Fact]
    public async Task NestedContractSourcesRequireRestart()
    {
        var settings = CreateSettings();
        var contracts = Path.Combine(Path.GetDirectoryName(settings.Project)!, "Contracts");
        Directory.CreateDirectory(contracts);
        settings = settings with { ContractFile = Path.Combine(contracts, "Contract.cs") };
        File.WriteAllText(settings.ContractFile, "class Contract;");
        var dto = Path.Combine(contracts, "Dto.cs");
        File.WriteAllText(dto, "record Dto(int Value);");
        using var inputs = new BackendInputs(settings);
        var before = await CaptureAsync(inputs);
        File.WriteAllText(dto, "record Dto(int Value, int Revision);");
        var after = await WaitForChangeAsync(inputs, before);
        after.RestartHash.Should().NotBe(before.RestartHash);
    }

    [Fact]
    public void OversizedInputsFailInsteadOfBeingTreatedAsSettled()
    {
        var settings = CreateSettings();
        File.WriteAllBytes(settings.ContractFile!, new byte[4 * 1024 * 1024 + 1]);
        using var inputs = new BackendInputs(settings);
        var capture = () => inputs.Capture();
        capture.Should().Throw<InvalidDataException>();
    }

    private static DevelopmentSettings CreateSettings()
    {
        var root = Path.Combine(Path.GetTempPath(), "spar-inputs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var project = Path.Combine(root, "Consumer.csproj");
        var startup = Path.Combine(root, "Program.cs");
        var contract = Path.Combine(root, "Contract.cs");
        File.WriteAllText(project, "<Project />");
        File.WriteAllText(startup, "class Startup;");
        File.WriteAllText(contract, "class Contract;");
        File.WriteAllText(Path.Combine(root, "Backend.cs"), "class Backend;");
        return new DevelopmentSettings
        {
            Project = project,
            ContractFile = contract,
            RestartFiles = [startup],
            ConfigurationFile = Path.Combine(root, "spar.dev.json"),
            SessionDirectory = Path.Combine(root, "sessions"),
            Frontend = new FrontendCommand { WorkingDirectory = Path.Combine(root, "frontend") }
        };
    }

    private static async Task<BackendSelection> WaitForChangeAsync(BackendInputs inputs, BackendSelection previous)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (true)
        {
            await Task.Delay(50, timeout.Token);
            var current = await CaptureAsync(inputs);
            if (current != previous)
            {
                return current;
            }
        }
    }

    private static async Task<BackendSelection> CaptureAsync(BackendInputs inputs)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (true)
        {
            var first = inputs.Capture();
            await Task.Delay(100, timeout.Token);
            if (first is not null && first == inputs.Capture())
            {
                return first;
            }
        }
    }
}
