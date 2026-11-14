#:property PublishAot=false
using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

Result<CopyPlan, PlanError> ok = new CopyPlan(1);
Result<CopyPlan, PlanError> bad = new PlanError.TableMissing("t");
Result<CopyPlan, PlanError> none = default;
Console.WriteLine($"typed Value: {ok.Value.Id}");
Console.WriteLine($"prop pattern: {ok is { IsSuccess: true }}, {bad is { IsSuccess: true }}");
Console.WriteLine($"null pattern: default {none is null}, ok {ok is null}");
Console.WriteLine(Describe(ok) + " / " + Describe(bad));
try { Describe(none); } catch (Exception e) { Console.WriteLine($"default switch: {e.GetType().Name}"); }
Console.WriteLine(Describe(await PlanAsync(false)) + " / " + Describe(await PlanAsync(true)));
Console.WriteLine(await PlanAsync(true) is PlanError.ConnectionNotFound { Id: "c" });

Result<int, PlanError> n = 5;
long before = GC.GetAllocatedBytesForCurrentThread();
int sum = 0;
for (int i = 0; i < 1000; i++) { Result<int, PlanError> r = i; sum += r switch { int v => v, PlanError => 0 }; }
Console.WriteLine($"alloc for 1000 int matches: {GC.GetAllocatedBytesForCurrentThread() - before} (sum {sum})");

static string Describe(Result<CopyPlan, PlanError> r) => r switch
{
    CopyPlan p => $"plan {p.Id}",
    PlanError.TableMissing t => $"missing {t.Table.Length}",
    PlanError.ConnectionNotFound c => $"no conn {c.Id}",
};

static async Task<Result<CopyPlan, PlanError>> PlanAsync(bool fail)
{
    await Task.Yield();
    if (fail)
    {
        return new PlanError.ConnectionNotFound("c");
    }

    return new CopyPlan(3);
}

public sealed record CopyPlan(int Id);
public closed record PlanError
{
    public sealed record ConnectionNotFound(string Id) : PlanError;
    public sealed record TableMissing(string Table) : PlanError;
}

[Union]
public readonly struct Result<T, TError> : Result<T, TError>.IUnionMembers
    where TError : class
{
    private readonly T _value;
    private readonly TError? _error;
    private readonly byte _state;

    private Result(T value) { _value = value; _error = null; _state = 1; }
    private Result(TError error) { _value = default!; _error = error; _state = 2; }

    public T Value => _state == 1 ? _value : throw new InvalidOperationException("not a success");
    public bool IsSuccess => _state == 1;

    object? IUnionMembers.Value => _state switch { 1 => _value, 2 => _error, _ => null };
    bool IUnionMembers.HasValue => _state != 0;
    bool IUnionMembers.TryGetValue([MaybeNullWhen(false)] out T value) { value = _value; return _state == 1; }
    bool IUnionMembers.TryGetValue([NotNullWhen(true)] out TError? value) { value = _error; return _state == 2; }

    public interface IUnionMembers
    {
        static Result<T, TError> Create(T value) => new(value);
        static Result<T, TError> Create(TError error) => new(error);
        object? Value { get; }
        bool HasValue { get; }
        bool TryGetValue([MaybeNullWhen(false)] out T value);
        bool TryGetValue([NotNullWhen(true)] out TError? value);
    }
}
