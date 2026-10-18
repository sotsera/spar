using AwesomeAssertions;
using Spar.ContractGenerator;

namespace Sotsera.Spar.Bridge.Tests;

public sealed class GeneratorTests
{
    [Fact]
    public void FingerprintIgnoresFormattingAndChangesWithTheContract()
    {
        var source = ReadContract();
        var original = ContractCompiler.Generate(source, "TestContract.cs");
        var formatted = ContractCompiler.Generate("// A comment\n" + source.Replace(";", ";\n", StringComparison.Ordinal), "Other.cs");
        formatted.Fingerprint.Should().Be(original.Fingerprint);
        original.Fingerprint.Should().Be(new TestBackendBridge(null!).Fingerprint);
        original.JavaScript.Should().Contain(original.Fingerprint);
        original.TypeScript.Should().Contain("change(request: TestRequest, options?: CallOptions): Promise<TestReply>");
        foreach (var replacement in new[] { ("counter.change", "counter.other"), ("int Value", "bool Value"), ("\"change\"", "\"other\"") })
        {
            ContractCompiler.Generate(source.Replace(replacement.Item1, replacement.Item2, StringComparison.Ordinal), "Changed.cs")
                .Fingerprint.Should().NotBe(original.Fingerprint);
        }
    }

    [Theory]
    [InlineData("[SparContract]", "")]
    [InlineData("\"counter.change\"", "\"\"")]
    [InlineData("int Value", "double Value")]
    [InlineData("sealed record", "record")]
    [InlineData("ChangeAsync", "WaitAsync")]
    [InlineData("WaitAsync", "Change")]
    [InlineData("Task<TestReply>", "ValueTask<TestReply>")]
    [InlineData("TestRequest", "CallOptions")]
    [InlineData("ChangeAsync", "__proto__")]
    public void UnsupportedExportsFailWithSourceDiagnostics(string before, string after)
    {
        var source = ReadContract().Replace(before, after, StringComparison.Ordinal);
        var generate = () => ContractCompiler.Generate(source, "ConsumerContract.cs");
        generate.Should().Throw<InvalidOperationException>().WithMessage("ConsumerContract.cs(*: error SPAR001:*");
    }

    private static string ReadContract()
    {
        return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestContract.cs"));
    }
}
