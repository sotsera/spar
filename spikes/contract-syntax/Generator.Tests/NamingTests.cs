using Spar.Generator;

namespace Generator.Tests;

public sealed class NamingTests
{
    [Theory]
    [InlineData("[SparContract] public sealed class CopyJobsService { }", "copyJobs")]
    [InlineData("[SparContract] public interface ICopyJobsService { }", "copyJobs")]
    [InlineData("[SparContract] public interface ICopyJobs { }", "copyJobs")]
    [InlineData("[SparContract] public interface Inventory { }", "inventory")]
    [InlineData("[SparContract] public interface IOStatsService { }", "oStats")]
    [InlineData("[SparContract] public sealed class IOStatsService { }", "ioStats")]
    [InlineData("[SparContract] public sealed class URLService { }", "url")]
    [InlineData("[SparContract] public sealed class Service { }", "service")]
    [InlineData("[SparContract] public sealed class Planner { }", "planner")]
    [InlineData("[SparContract(\"jobs\")] public sealed class CopyJobsService { }", "jobs")]
    [InlineData("[SparContract(\"jobs\")] public interface ICopyJobsService { }", "jobs")]
    public void ContractNames(string source, string expected)
    {
        var run = Harness.Run(source);
        run.AssertClean();
        Assert.Equal(expected, Assert.Single(run.Set.Contracts).WireName);
    }

    [Fact]
    public void CommandNames()
    {
        var run = Harness.Run("""
            [SparContract]
            public sealed class JobsService
            {
                public Task<int> GetPlanAsync() => Task.FromResult(1);
                public Task<int> Refresh() => Task.FromResult(1);
                public Task<int> URLValueAsync() => Task.FromResult(1);
                public Task<int> IDAsync() => Task.FromResult(1);
                [SparCommand("explicitName")] public Task<int> SomethingAsync() => Task.FromResult(1);
                public static Task<int> StaticAsync() => Task.FromResult(1);
                internal Task<int> InternalAsync() => Task.FromResult(1);
                public override string ToString() => "jobs";
            }
            """);

        run.AssertClean();
        Assert.Equal(["getPlan", "refresh", "urlValue", "id", "explicitName"], run.Set.Contracts.Items[0].Commands.Select(c => c.WireName));
    }

    [Theory]
    [InlineData("Id", "id")]
    [InlineData("URLValue", "urlValue")]
    [InlineData("URL", "url")]
    [InlineData("connectionId", "connectionId")]
    [InlineData("X", "x")]
    public void CamelCase(string name, string expected) => Assert.Equal(expected, Naming.ToCamelCase(name));
}
