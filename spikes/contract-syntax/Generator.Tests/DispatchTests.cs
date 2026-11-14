namespace Generator.Tests;

/// <summary>Round trips through generated dispatch, compiled and loaded in memory.</summary>
public sealed class DispatchTests
{
    private const string Source = """
        using System.Runtime.CompilerServices;
        using System.Text;
        using Microsoft.Extensions.DependencyInjection;
        using Spar.Generated;

        namespace App;

        public sealed record Greeting(string Name, int Times);

        public closed record GreetError
        {
            public sealed record Empty : GreetError;
            public sealed record TooMany(int Max) : GreetError;
        }

        internal static class Logic
        {
            public static string Hello(Greeting g) => string.Concat(Enumerable.Repeat("hi " + g.Name + ";", g.Times));

            public static Result<string, GreetError> Checked(Greeting g) =>
                g.Name.Length == 0 ? new GreetError.Empty() : g.Times > 3 ? new GreetError.TooMany(3) : Hello(g);

            public static async IAsyncEnumerable<string> Repeat(Greeting g, [EnumeratorCancellation] CancellationToken ct)
            {
                for (var i = 0; i < g.Times; i++)
                {
                    await Task.Yield();
                    ct.ThrowIfCancellationRequested();
                    yield return g.Name + i;
                }
            }
        }

        [SparContract]
        public sealed class GreeterService
        {
            public Task<string> HelloAsync(Greeting request, CancellationToken ct) => Task.FromResult(Logic.Hello(request));
            public Task<Result<string, GreetError>> CheckedAsync(Greeting request) => Task.FromResult(Logic.Checked(request));
            public IAsyncEnumerable<string> RepeatAsync(Greeting request, CancellationToken ct) => Logic.Repeat(request, ct);
            public Task<Guid> EchoAsync(Guid id) => Task.FromResult(id);
            public Task<int> CountAsync(CancellationToken ct) => Task.FromResult(7);
        }

        [SparContract]
        public interface IGreeterApi
        {
            Task<string> HelloAsync(Greeting request, CancellationToken ct);
            Task<Result<string, GreetError>> CheckedAsync(Greeting request);
            IAsyncEnumerable<string> RepeatAsync(Greeting request, CancellationToken ct);
            Task<Guid> EchoAsync(Guid id);
            Task<int> CountAsync(CancellationToken ct);
        }

        public sealed class GreeterApi : IGreeterApi
        {
            public Task<string> HelloAsync(Greeting request, CancellationToken ct) => Task.FromResult(Logic.Hello(request));
            public Task<Result<string, GreetError>> CheckedAsync(Greeting request) => Task.FromResult(Logic.Checked(request));
            public IAsyncEnumerable<string> RepeatAsync(Greeting request, CancellationToken ct) => Logic.Repeat(request, ct);
            public Task<Guid> EchoAsync(Guid id) => Task.FromResult(id);
            public Task<int> CountAsync(CancellationToken ct) => Task.FromResult(7);
        }

        public static class Entry
        {
            public static async Task<string> RunAsync(string contract)
            {
                var services = new ServiceCollection();
                SparCatalog.AddSparContracts(services);
                services.AddScoped<IGreeterApi, GreeterApi>();
                await using var provider = services.BuildServiceProvider();
                var lines = new List<string>();
                async Task Call(string command, string payload) =>
                    lines.Add(Encoding.UTF8.GetString(await SparCatalog.InvokeAsync(provider, contract, command, Encoding.UTF8.GetBytes(payload), default)));

                await Call("hello", "{\"name\":\"ann\",\"times\":2}");
                await Call("checked", "{\"name\":\"\",\"times\":1}");
                await Call("checked", "{\"name\":\"bob\",\"times\":9}");
                await Call("checked", "{\"name\":\"bob\",\"times\":1}");
                await Call("echo", "\"6f9619ff-8b86-d011-b42d-00c04fc964ff\"");
                await Call("count", "");

                await using var stream = SparCatalog.OpenStream(provider, contract, "repeat", Encoding.UTF8.GetBytes("{\"name\":\"x\",\"times\":3}"), default);
                var buffer = new System.Buffers.ArrayBufferWriter<byte>();
                while ((await stream.ReadBatchAsync(buffer, 10, default)).Count > 0)
                {
                }

                lines.Add(Encoding.UTF8.GetString(buffer.WrittenSpan));
                return string.Join("\n", lines);
            }
        }
        """;

    [Fact]
    public async Task ClassAndInterfaceFormsDispatchIdentically()
    {
        var run = Harness.Run(Source);
        run.AssertClean();
        Assert.Equal(["greeter", "greeterApi"], run.Set.Contracts.Select(c => c.WireName));

        var assembly = Harness.Load(run.Output, out var context);
        try
        {
            var entry = assembly.GetType("App.Entry")!.GetMethod("RunAsync")!;
            var viaClass = await (Task<string>)entry.Invoke(null, ["greeter"])!;
            var viaInterface = await (Task<string>)entry.Invoke(null, ["greeterApi"])!;

            Assert.Equal(
                """
                "hi ann;hi ann;"
                {"ok":false,"error":{"code":"empty"}}
                {"ok":false,"error":{"code":"tooMany","max":3}}
                {"ok":true,"value":"hi bob;"}
                "6f9619ff-8b86-d011-b42d-00c04fc964ff"
                7
                ["x0","x1","x2"]
                """.ReplaceLineEndings("\n"),
                viaClass);
            Assert.Equal(viaClass, viaInterface);
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public void GeneratedSourcesUseOnlyLineFeeds()
    {
        var run = Harness.Run(Source);
        Assert.All(run.Sources.Values.Concat(run.Client.Values), text => Assert.DoesNotContain('\r', text));
    }
}
