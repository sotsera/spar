using AwesomeAssertions;
using DevelopmentFingerprintFixture;
using Spar.ContractGenerator;
using Spar.NativeSample;
using Sotsera.Spar.Development;

namespace Sotsera.Spar.Development.Tests;

public sealed class DevelopmentContractTests
{
    [Fact]
    public void DebuggerBoundaryRejectsPatchableContractsAndAcceptsImmutableSample()
    {
        var validate = () => DevelopmentApplication.RequireImmutableDebuggerContract(typeof(IFingerprintContract).Assembly.Location);
        validate.Should().Throw<InvalidOperationException>().WithMessage("*DebugType=none*");
        DevelopmentApplication.RequireImmutableDebuggerContract(typeof(ISampleBackend).Assembly.Location);
    }

    [Fact]
    public void CompiledContractMatchesGeneratorIncludingNullabilityAndUnusedRecords()
    {
        using var stream = typeof(DevelopmentContractTests).Assembly.GetManifestResourceStream("FingerprintContract.cs")!;
        using var reader = new StreamReader(stream);
        var source = reader.ReadToEnd();
        var generated = ContractCompiler.Generate(source, "FingerprintContract.cs");
        var compiled = DevelopmentContract.GetFingerprint(typeof(IFingerprintContract));

        compiled.Should().Be(generated.Fingerprint);
        var changed = ContractCompiler.Generate(source.Replace("UnusedDto(string? Value)",
            "UnusedDto(string Value)", StringComparison.Ordinal), "Changed.cs");
        compiled.Should().NotBe(changed.Fingerprint);
    }

    [Fact]
    public void CompiledSampleMatchesEmbeddedWireGlue()
    {
        var compiled = DevelopmentContract.GetFingerprint(typeof(ISampleBackend));
        using var stream = typeof(SampleBackendBridge).Assembly.GetManifestResourceStream("Spar.Generated.Client.js")!;
        using var reader = new StreamReader(stream);

        reader.ReadToEnd().Should().Contain(compiled);
    }
}
