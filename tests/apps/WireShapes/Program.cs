using Microsoft.Extensions.DependencyInjection;
using Spar;
using WireShapes;
using WireShapes.Library;

var app = SparApplication.Create(args, spar =>
{
    spar.Services.AddSingleton<Ledger>();
    spar.Services.AddScoped<IRatesService, FixedRates>();
});
return app.Run();

namespace WireShapes
{
    public sealed record Item(string Name);

    /// <summary>The other kinds of the wire-rules table; DateTime is left out, since its warning fails this build.</summary>
    public sealed record Kinds(
        byte B,
        sbyte Sb,
        short S,
        ushort Us,
        uint Ui,
        ulong Ul,
        float F,
        decimal M,
        char C,
        DateOnly Day,
        TimeOnly Time,
        TimeSpan Span,
        byte[] Data,
        byte[]? Maybe,
        IReadOnlyList<ulong?> Ids,
        decimal? Price);

    /// <summary>Echoes the other kinds, so a test sees what their readers and writers do.</summary>
    [SparContract]
    public sealed class KindsService
    {
        [SparCommand]
        public Task<Kinds> EchoAsync(Kinds request) => Task.FromResult(request);

        /// <summary>A byte array is a scalar request: one base64 string.</summary>
        [SparCommand]
        public Task<int> LengthAsync(byte[] data) => Task.FromResult(data.Length);

        /// <summary>Parses a float, so a test can make the writer meet a value it must refuse.</summary>
        [SparCommand]
        public Task<float> SingleAsync(string text) => Task.FromResult(float.Parse(text, System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>The app's implementation of the library's rates contract: two fixed rates.</summary>
    public sealed class FixedRates : IRatesService
    {
        public Task<Result<long, RateError>> RateAsync(string currency, CancellationToken cancellationToken) =>
            Task.FromResult<Result<long, RateError>>(currency switch
            {
                "EUR" => 100,
                "USD" => 92,
                _ => new RateError.Unknown(currency),
            });
    }

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

    /// <summary>Echoes what it gets, so a test sees exactly what the serializers read and write.</summary>
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
        public Task<double> RatioAsync(Item request) => Task.FromResult(double.Parse(request.Name, System.Globalization.CultureInfo.InvariantCulture));

        [SparCommand]
        public Task<long> BigAsync(Item request) => Task.FromResult(long.Parse(request.Name, System.Globalization.CultureInfo.InvariantCulture));

        /// <summary>Waits until the call is cancelled, so a test can abort a call that is running.</summary>
        [SparCommand]
        public async Task<Item> WaitAsync(Item request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return request;
        }
    }
}
