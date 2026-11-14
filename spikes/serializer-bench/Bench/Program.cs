using Bench;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.CsProj;
using BenchmarkDotNet.Toolchains.DotNetCli;
using BenchmarkDotNet.Toolchains.NativeAot;
using Perfolizer.Horology;

if (args is ["verify"])
{
    return Correctness.Run();
}

// Both jobs use the same measurement settings: 3 separate processes, each with warm-up and 15 measured iterations.
var measured = Job.Default
    .DontEnforcePowerPlan()
    .WithLaunchCount(3)
    .WithWarmupCount(6)
    .WithIterationCount(15)
    .WithIterationTime(TimeInterval.FromMilliseconds(250));

// BenchmarkDotNet 0.15.8 has no .NET 11 monikers: its SDK validator rejects a custom runtime and its NativeAOT
// generator rejects net11.0 when it infers the instruction set. The jobs therefore carry the .NET 10 runtime
// labels, while the toolchains build net11.0. The benchmark processes report .NET 11.0.0 in their environment info.
var coreClr = measured
    .WithRuntime(CoreRuntime.Core10_0)
    .WithToolchain(CsProjCoreToolchain.From(new NetCoreAppSettings("net11.0", null, ".NET 11.0")))
    .WithId("CoreCLR");

var nativeAot = measured
    .WithRuntime(NativeAotRuntime.Net10_0)
    .WithToolchain(NativeAotToolchain.CreateBuilder()
        .UseNuGet()
        .IlcInstructionSet("native")
        .TargetFrameworkMoniker("net11.0")
        .DisplayName("NativeAOT 11.0")
        .ToToolchain())
    .WithId("NativeAOT");

var config = DefaultConfig.Instance
    .WithArtifactsPath(Environment.GetEnvironmentVariable("BENCH_ARTIFACTS")
        ?? Path.Combine(AppContext.BaseDirectory, "BenchmarkDotNet.Artifacts"));
config = Environment.GetEnvironmentVariable("BENCH_JOB") switch
{
    "CoreCLR" => config.AddJob(coreClr),
    "NativeAOT" => config.AddJob(nativeAot),
    _ => config.AddJob(coreClr).AddJob(nativeAot),
};

BenchmarkSwitcher.FromAssembly(typeof(SmallRequest).Assembly).Run(args, config);
return 0;
