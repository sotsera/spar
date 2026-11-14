using System.Buffers;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Library;
using Spar;
using LibResult = Spar.Result<string, Library.LibError>;

namespace Spar050.Harness;

/// <summary>
/// What a reader sees for the constructs the corpus classifies as runtime-only or reports with a warning. Each probe
/// prints "default (throws …)", "success", "error" or the exception the construct itself throws.
/// </summary>
internal static class RuntimeProbes
{
    private static readonly LibResult s_ok = "value";
    private static LibResult s_exchanged = "value";

    public static int Run()
    {
        Console.WriteLine($"# Runtime probes, .NET {Environment.Version}, {RuntimeInformation.FrameworkDescription}");
        Probe("default(Result).IsSuccess", () => Describe(default));
        Probe("default(Result).ToString()", () => default(LibResult).ToString());
        Probe("switch over default(Result)", () => SwitchOver(default));
        Probe("Equals(default(Result)) on a default / on a value", () =>
            $"{default(LibResult).Equals(default(LibResult))} / {s_ok.Equals(default(LibResult))}");
        Probe("Library Factory.Make() (unannotated default)", () => Describe(Factory.Make()));
        Probe("Library Generic.Make<Result>() (unannotated default!)", () => Describe(Generic.Make<LibResult>()));
        Probe("Library new Holder().Last (unassigned public field)", () => Describe(new Holder().Last));
        Probe("Interlocked.Exchange(ref field, value)", () => Describe(Interlocked.Exchange(ref s_exchanged, s_ok)));
        Probe("MemoryMarshal.Cast<byte, Result>", () => MemoryMarshal.Cast<byte, LibResult>(new byte[64]).Length.ToString());
        Probe("Unsafe.As<Zeroed, Result>", () =>
        {
            var zeroed = new Shadow();
            return Describe(Unsafe.As<Shadow, LibResult>(ref zeroed));
        });
        Probe("List<Result>.Enumerator.Current before MoveNext", () =>
        {
            using var enumerator = new List<LibResult> { s_ok }.GetEnumerator();
            return Describe(enumerator.Current);
        });
        Probe("MaxBy over an empty sequence of Results", () => Describe(new List<LibResult>().MaxBy(r => r.IsSuccess)));
        Probe("FirstOrDefault() over an empty sequence", () => Describe(new List<LibResult>().FirstOrDefault()));
        Probe("new Lazy<Result>().Value", () => Describe(new Lazy<LibResult>().Value));
        Probe("new ThreadLocal<Result>().Value", () =>
        {
            using var local = new ThreadLocal<LibResult>();
            return Describe(local.Value);
        });
        Probe("new AsyncLocal<Result>().Value", () => Describe(new AsyncLocal<LibResult>().Value));
        Probe("new StrongBox<Result>().Value", () => Describe(new StrongBox<LibResult>().Value));
        Probe("[ThreadStatic] set by the static constructor, read on another thread", ThreadStaticOnOtherThread);
        Probe("static initializer reading a later static field", () => Describe(InitializerOrder.First));
        Probe("await default(ValueTask<Result>)", () => Describe(AwaitDefault().GetAwaiter().GetResult()));
        Probe("Array.Resize growth slot", () =>
        {
            LibResult[] results = [s_ok];
            Array.Resize(ref results, 2);
            return Describe(results[1]);
        });
        Probe("ArrayPool Rent after a Return without clearing", ReuseWithoutClear);
        Probe("ImmutableArray builder Count = 2", () =>
        {
            var builder = ImmutableArray.CreateBuilder<LibResult>(2);
            builder.Count = 2;
            return Describe(builder[1]);
        });
        Probe("CollectionsMarshal.GetValueRefOrAddDefault on a missing key", () =>
        {
            var results = new Dictionary<string, LibResult>();
            ref var slot = ref CollectionsMarshal.GetValueRefOrAddDefault(results, "k", out _);
            return Describe(slot);
        });
        Probe("RuntimeHelpers.GetUninitializedObject(typeof(Result))", () =>
            Describe((LibResult)RuntimeHelpers.GetUninitializedObject(typeof(LibResult))));
        Probe("Activator.CreateInstance<Result>()", () => Describe(Activator.CreateInstance<LibResult>()));
        Probe("Nullable<Result>.Value when null", () => Describe(NoResult()!.Value));
        return 0;
    }

    private static void Probe(string name, Func<string> probe)
    {
        string outcome;
        try
        {
            outcome = probe();
        }
        catch (Exception e)
        {
            outcome = $"{e.GetType().Name}: {e.Message}";
        }

        Console.WriteLine($"{name} => {outcome}");
    }

    private static string Describe(LibResult result)
    {
        try
        {
            return result.IsSuccess ? $"success ({result.Value})" : $"error ({result.Error})";
        }
        catch (InvalidOperationException e) when (e.Message.StartsWith("default(Result)", StringComparison.Ordinal))
        {
            return "default (throws InvalidOperationException)";
        }
    }

    private static string SwitchOver(LibResult result)
    {
        return result switch
        {
            string value => value,
            LibError.Missing missing => missing.Key,
        };
    }

    private static async Task<LibResult> AwaitDefault() => await default(ValueTask<LibResult>);

    private static LibResult? NoResult() => null;

    private static string ThreadStaticOnOtherThread()
    {
        var onThisThread = Describe(PerThread.Current);
        var onOther = string.Empty;
        var thread = new Thread(() => onOther = Describe(PerThread.Current));
        thread.Start();
        thread.Join();
        return $"this thread: {onThisThread}; other thread: {onOther}";
    }

    private static string ReuseWithoutClear()
    {
        var pool = ArrayPool<LibResult>.Create();
        var first = pool.Rent(4);
        first[0] = "left by the previous renter";
        pool.Return(first);
        var second = pool.Rent(4);
        return $"same array: {ReferenceEquals(first, second)}; [0] = {Describe(second[0])}; [1] = {Describe(second[1])}";
    }

    private readonly struct Shadow
    {
        public readonly object? Value = null;
        public readonly object? Error = null;
        public readonly byte State = 0;
        public readonly bool HasValue = false;

        public Shadow()
        {
        }
    }

    private static class PerThread
    {
        [ThreadStatic]
        private static LibResult t_current;

        static PerThread()
        {
            t_current = "set by the static constructor";
        }

        public static LibResult Current => t_current;
    }

    private static class InitializerOrder
    {
        private static readonly LibResult s_first = s_second;
        private static readonly LibResult s_second = "second";

        public static LibResult First => s_first;

        public static LibResult Second => s_second;
    }
}
