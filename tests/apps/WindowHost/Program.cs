using Microsoft.Extensions.DependencyInjection;
using Spar;
using WindowHost;

var app = SparApplication.Create(args, spar =>
{
    spar.Window(w => w.Size(1000, 700));

    // Every close request is recorded, so a test can see which reason reached the backend's guard.
    spar.OnCloseRequested((request, services) =>
    {
        services.GetRequiredService<Activity>().CloseRequested(request);
        return CloseDecision.Close;
    });

    spar.Services.AddSingleton<Activity>();
});
return app.Run();

namespace WindowHost
{
    /// <summary>What the backend saw, for the tests to read through <c>app.Services</c>.</summary>
    public sealed class Activity
    {
        private readonly Lock _gate = new();
        private readonly List<CloseRequest> _closeRequests = [];
        private readonly List<string> _events = [];
        private int _running;

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

        private void Add(string text)
        {
            lock (_gate)
            {
                _events.Add(text);
            }
        }
    }

    /// <summary>The calls the host tests start from the page.</summary>
    [SparContract]
    public sealed class HostService(Activity activity)
    {
        [SparCommand]
        public Task<string> EchoAsync(string text) => Task.FromResult(text);

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
