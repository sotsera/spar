using Spar;

var app = SparApplication.Create(args, spar => spar.Window(w => w.Size(900, 600)));
return app.Run();

namespace InvokeProbe
{
    /// <summary>The page's marks, written to standard error with the backend's clock (Unix milliseconds, sub-millisecond).</summary>
    [SparContract]
    public sealed class ProbeService
    {
        [SparCommand]
        public Task MarkAsync(string text)
        {
            var now = (DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds;
            Console.Error.WriteLine(FormattableString.Invariant($"mark {now:0.0} {text}"));
            return Task.CompletedTask;
        }
    }
}
