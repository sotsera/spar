using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Spar;
using WindowHost;

var app = SparApplication.Create(args, spar =>
{
    spar.Window(w => w.Size(1000, 700));

    // Window kinds the page opens (decision "M3d's decide-first questions", item 3): notes, any number; one panel, which
    // may call only host.echo of the app's commands.
    spar.Window("note", w => w.Route("/note").Size(600, 400));
    spar.Window("panel", w => w.Route("/panel").Size(500, 360).SingleInstance().Commands(c => c.Allow<HostService>("echo")));

    // The installation file beside the executable, when a test puts one there (decision "M3b's decide-first questions", item 6).
    spar.InstallationFile();

    // Every close request is recorded, so a test can see which reason reached the backend's guard; the test picks the
    // answer, and how long it takes, through Activity.
    spar.OnCloseRequested(async (request, services, _) =>
    {
        var activity = services.GetRequiredService<Activity>();
        activity.CloseRequested(request);
        if (activity.BackendGuardDelay > TimeSpan.Zero)
        {
            // Ignores its token on purpose: the session end's deadline must hold anyway.
            await Task.Delay(activity.BackendGuardDelay, CancellationToken.None);
        }

        return activity.BackendAnswer;
    });

    spar.Services.AddSingleton<Activity>();
});
return app.Run();

namespace WindowHost
{
    /// <summary>What the backend saw, for the tests to read through <c>app.Services</c>.</summary>
    public sealed class Activity : IAsyncDisposable
    {
        private readonly Lock _gate = new();
        private readonly List<CloseRequest> _closeRequests = [];
        private readonly List<string> _events = [];
        private int _running;

        /// <summary>What the backend's close guard answers; close unless a test says otherwise.</summary>
        public CloseDecision BackendAnswer { get; set; } = CloseDecision.Close;

        /// <summary>How long the backend's close guard takes, ignoring its token.</summary>
        public TimeSpan BackendGuardDelay { get; set; }

        /// <summary>How long disposing the app's services takes: a slow service's cleanup.</summary>
        public TimeSpan DisposeDelay { get; set; }

        /// <summary>When the services' disposal finished, or null while it has not.</summary>
        public DateTime? DisposedAt { get; private set; }

        /// <summary>Calls that have started and not ended yet.</summary>
        public int Running => Volatile.Read(ref _running);

        public IReadOnlyList<CloseRequest> CloseRequests
        {
            get
            {
                lock (_gate)
                {
                    return [.. _closeRequests];
                }
            }
        }

        /// <summary>"started hold", "cancelled hold", "finished slow" and so on, in order.</summary>
        public IReadOnlyList<string> Events
        {
            get
            {
                lock (_gate)
                {
                    return [.. _events];
                }
            }
        }

        public void CloseRequested(CloseRequest request)
        {
            lock (_gate)
            {
                _closeRequests.Add(request);
            }
        }

        public void Noted(string text) => Add("note " + text);

        public void Started(string name)
        {
            Interlocked.Increment(ref _running);
            Add("started " + name);
        }

        public void Ended(string how, string name)
        {
            Add(how + " " + name);
            Interlocked.Decrement(ref _running);
        }

        public async ValueTask DisposeAsync()
        {
            if (DisposeDelay > TimeSpan.Zero)
            {
                await Task.Delay(DisposeDelay, CancellationToken.None);
            }

            DisposedAt = DateTime.UtcNow;
        }

        private void Add(string text)
        {
            lock (_gate)
            {
                _events.Add(text);
            }
        }
    }

    public sealed record HostRow(int N, string Text);

    /// <summary>The calls the host tests start from the page.</summary>
    [SparContract]
    public sealed class HostService(Activity activity)
    {
        [SparCommand]
        public Task<string> EchoAsync(string text) => Task.FromResult(text);

        /// <summary>Records what the page says, such as its close guard's request, so a test reads it after the window closed.</summary>
        [SparCommand]
        public Task NoteAsync(string text)
        {
            activity.Noted(text);
            return Task.CompletedTask;
        }

        /// <summary>Runs until its call is cancelled: a long call that honours cancellation.</summary>
        [SparCommand]
        public async Task<string> HoldAsync(string name, CancellationToken cancellationToken)
        {
            activity.Started(name);
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return name;
            }
            catch (OperationCanceledException)
            {
                activity.Ended("cancelled", name);
                throw;
            }
        }

        /// <summary>The first <paramref name="count"/> rows, as fast as the page takes them: the desktop bridge's stream budget.</summary>
        [SparCommand]
        public async IAsyncEnumerable<HostRow> RowsAsync(int count, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            for (var n = 0; n < count; n++)
            {
                if (n % 1024 == 0)
                {
                    // A real reader awaits between pages of rows; the rest come synchronously.
                    await Task.Yield();
                }

                yield return new HostRow(n, $"row {n}");
            }
        }

        /// <summary>
        /// <paramref name="perSecond"/> ticks offered evenly over one second by a producer that never waits, then it waits:
        /// the desktop bridge's push budget. The buffer holds a whole second, so a page that keeps up loses none.
        /// </summary>
        [SparCommand]
        [SparSubscription(Capacity = 32768)]
        public async IAsyncEnumerable<long> BurstAsync(int perSecond, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var started = System.Diagnostics.Stopwatch.StartNew();
            for (long n = 0; n < perSecond; n++)
            {
                var due = TimeSpan.FromSeconds((double)n / perSecond);
                if (due > started.Elapsed + TimeSpan.FromMilliseconds(20))
                {
                    await Task.Delay(due - started.Elapsed, cancellationToken);
                }

                yield return n;
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        /// <summary>
        /// <paramref name="size"/> bytes, each its index modulo 251, as a <c>byte[]</c> handle the page reads on the
        /// origin: the desktop host's blob route and its budget.
        /// </summary>
        [SparCommand]
        public Task<SparBlob> MakeAsync(int size)
        {
            var bytes = new byte[size];
            for (var i = 0; i < size; i++)
            {
                bytes[i] = (byte)(i % 251);
            }

            return Task.FromResult(SparBlob.FromBytes(bytes, "application/octet-stream", "made.bin"));
        }

        /// <summary>A 1×1 PNG for any number, so an <c>&lt;img&gt;</c> shows a resource under the default policy.</summary>
        [SparCommand]
        [SparResource]
        public Task<SparBlob> PixelAsync(int n) => Task.FromResult(SparBlob.FromBytes(
            Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="),
            "image/png"));

        /// <summary>The size of a file the page was given, read in C# from its grant's path.</summary>
        [SparCommand]
        public Task<long> SizeAsync(FileGrant file) => Task.FromResult(new FileInfo(file.Path).Length);

        /// <summary>Takes <paramref name="milliseconds"/> whatever happens: a call that ignores its cancellation.</summary>
        [SparCommand]
        public async Task<string> SlowAsync(int milliseconds)
        {
            activity.Started("slow");
            await Task.Delay(milliseconds, CancellationToken.None);
            activity.Ended("finished", "slow");
            return "slow done";
        }
    }
}
