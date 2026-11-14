// Micro-benchmarks of the four Result forms: create and read a success, create an error and switch over its case,
// and await a command that completes synchronously through ValueTask. CoreCLR only; the NativeAOT numbers come from
// the size apps. DontEnforcePowerPlan keeps BenchmarkDotNet from changing the Windows power plan.
extern alias FormClass;
extern alias FormStruct;
extern alias FormUnionClass;
extern alias FormUnionStruct;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.CsProj;
using BenchmarkDotNet.Toolchains.DotNetCli;
using ClassResult = FormClass::Spar.Result<Bench.Plan, Bench.PlanError>;
using StructResult = FormStruct::Spar.Result<Bench.Plan, Bench.PlanError>;
using UnionClassResult = FormUnionClass::Spar.Result<Bench.Plan, Bench.PlanError>;
using UnionStructResult = FormUnionStruct::Spar.Result<Bench.Plan, Bench.PlanError>;

var job = Job.Default
    .DontEnforcePowerPlan()
    .WithRuntime(CoreRuntime.Core10_0)
    .WithToolchain(CsProjCoreToolchain.From(new NetCoreAppSettings("net11.0", null, ".NET 11.0")))
    .WithLaunchCount(2)
    .WithWarmupCount(5)
    .WithIterationCount(15)
    .WithId("CoreCLR");

var config = DefaultConfig.Instance
    .AddJob(job)
    .WithArtifactsPath(Environment.GetEnvironmentVariable("BENCH_ARTIFACTS")
        ?? Path.Combine(AppContext.BaseDirectory, "BenchmarkDotNet.Artifacts"));

BenchmarkRunner.Run<Bench.ResultBenchmarks>(config, args);

namespace Bench
{
    public sealed record Plan(int Steps);

    public closed record PlanError
    {
        public sealed record ConnectionNotFound(string ConnectionId) : PlanError;

        public sealed record TableMissing(string Table) : PlanError;
    }

    [MemoryDiagnoser]
    [CategoriesColumn]
    [GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
    public class ResultBenchmarks
    {
        private static readonly Plan s_plan = new(3);
        private static readonly PlanError s_error = new PlanError.TableMissing("dbo.Orders");

        [Benchmark(Baseline = true)]
        [BenchmarkCategory("success")]
        public int ClassSuccess() => Read(Class(fail: false));

        [Benchmark]
        [BenchmarkCategory("success")]
        public int StructSuccess() => Read(Struct(fail: false));

        [Benchmark]
        [BenchmarkCategory("success")]
        public int UnionClassSuccess() => Read(UnionClass(fail: false));

        [Benchmark]
        [BenchmarkCategory("success")]
        public int UnionStructSuccess() => Read(UnionStruct(fail: false));

        [Benchmark(Baseline = true)]
        [BenchmarkCategory("error")]
        public int ClassError() => Read(Class(fail: true));

        [Benchmark]
        [BenchmarkCategory("error")]
        public int StructError() => Read(Struct(fail: true));

        [Benchmark]
        [BenchmarkCategory("error")]
        public int UnionClassError() => Read(UnionClass(fail: true));

        [Benchmark]
        [BenchmarkCategory("error")]
        public int UnionStructError() => Read(UnionStruct(fail: true));

        [Benchmark]
        [BenchmarkCategory("error")]
        public int UnionStructErrorSwitch() => Switch(UnionStruct(fail: true));

        [Benchmark(Baseline = true)]
        [BenchmarkCategory("valuetask")]
        public async ValueTask<int> ClassAwait() => Read(await ClassCommandAsync());

        [Benchmark]
        [BenchmarkCategory("valuetask")]
        public async ValueTask<int> StructAwait() => Read(await StructCommandAsync());

        [Benchmark]
        [BenchmarkCategory("valuetask")]
        public async ValueTask<int> UnionClassAwait() => Read(await UnionClassCommandAsync());

        [Benchmark]
        [BenchmarkCategory("valuetask")]
        public async ValueTask<int> UnionStructAwait() => Read(await UnionStructCommandAsync());

        private static ClassResult Class(bool fail) => fail ? s_error : s_plan;

        private static StructResult Struct(bool fail) => fail ? s_error : s_plan;

        private static UnionClassResult UnionClass(bool fail) => fail ? s_error : s_plan;

        private static UnionStructResult UnionStruct(bool fail) => fail ? s_error : s_plan;

        private static async ValueTask<ClassResult> ClassCommandAsync()
        {
            await Task.CompletedTask;
            return s_plan;
        }

        private static async ValueTask<StructResult> StructCommandAsync()
        {
            await Task.CompletedTask;
            return s_plan;
        }

        private static async ValueTask<UnionClassResult> UnionClassCommandAsync()
        {
            await Task.CompletedTask;
            return s_plan;
        }

        private static async ValueTask<UnionStructResult> UnionStructCommandAsync()
        {
            await Task.CompletedTask;
            return s_plan;
        }

        // The reads a generated writer does: is it an error, then the case or the value.
        private static int Read(ClassResult result) => result.TryGetError(out var error) ? Code(error) : result.Value.Steps;

        private static int Read(StructResult result) => result.TryGetError(out var error) ? Code(error) : result.Value.Steps;

        private static int Read(UnionClassResult result) => result.TryGetError(out var error) ? Code(error) : result.Value.Steps;

        private static int Read(UnionStructResult result) => result.TryGetError(out var error) ? Code(error) : result.Value.Steps;

        private static int Switch(UnionStructResult result) => result switch
        {
            Plan plan => plan.Steps,
            PlanError.ConnectionNotFound => -1,
            PlanError.TableMissing => -2,
        };

        private static int Code(PlanError error) => error switch
        {
            PlanError.ConnectionNotFound => -1,
            PlanError.TableMissing => -2,
        };
    }
}
