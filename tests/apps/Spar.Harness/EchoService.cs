using System.Globalization;

namespace Spar.Harness;

public sealed record Item(string Name);

/// <summary>Every kind of wire value the generator supports, nested.</summary>
public sealed record Shapes(
    string Text,
    string? Note,
    bool Flag,
    int Count,
    long Big,
    double Ratio,
    Guid Key,
    DateTimeOffset At,
    int? Maybe,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string?> Cells,
    Item[] Items,
    Shapes? Parent);

public closed record EchoError
{
    public sealed record Refused(string Reason) : EchoError;

    public sealed record Empty : EchoError;
}

/// <summary>Echoes what it gets, so the output shows exactly what the serializers read and write.</summary>
[SparContract]
public sealed class EchoService
{
    [SparCommand]
    public Task<Shapes> ShapesAsync(Shapes request) => Task.FromResult(request);

    [SparCommand]
    public Task<Result<Item, EchoError>> CheckAsync(Item request) => Task.FromResult<Result<Item, EchoError>>(request.Name switch
    {
        "" => new EchoError.Empty(),
        "no" => new EchoError.Refused("said no"),
        _ => request,
    });

    [SparCommand]
    public Task<double> RatioAsync(Item request) => Task.FromResult(double.Parse(request.Name, CultureInfo.InvariantCulture));

    [SparCommand]
    public Task<long> BigAsync(Item request) => Task.FromResult(long.Parse(request.Name, CultureInfo.InvariantCulture));

    /// <summary>Throws, so the page gets the call's correlation id and no details.</summary>
    [SparCommand]
    public Task<Item> FailAsync(Item request) => throw new InvalidOperationException($"{request.Name} failed on purpose");

    /// <summary>Waits until the call is cancelled.</summary>
    [SparCommand]
    public async Task<Item> WaitAsync(Item request, CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return request;
    }
}
