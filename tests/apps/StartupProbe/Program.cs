using Microsoft.Extensions.DependencyInjection;
using Spar;
using StartupProbe;

// The first page's start (frontend/src/start.js, firstPageStarts), carried by the window's width: a first page has no
// address of its own to carry it. The tests set it one window at a time; 0, a plain fast start, by default.
var start = int.TryParse(Environment.GetEnvironmentVariable("STARTUP_PROBE_START"), out var picked) ? picked : 0;

var app = SparApplication.Create(args, spar =>
{
    // Colours no engine paints by itself (not white, #121212 or black), so a frame of either is the window's own.
    spar.Window(w => w
        .Background(light: "#eef3f8", dark: "#141a22")
        .Size(800 + (start * 40), 600));

    spar.Services.AddSingleton<Notes>();
});
return app.Run();

namespace StartupProbe
{
    /// <summary>What the page noted, in order, for the tests to read through <c>app.Services</c>.</summary>
    public sealed class Notes
    {
        private readonly Lock _gate = new();
        private readonly List<string> _notes = [];

        public IReadOnlyList<string> All
        {
            get
            {
                lock (_gate)
                {
                    return [.. _notes];
                }
            }
        }

        public void Add(string note)
        {
            lock (_gate)
            {
                _notes.Add(note);
            }
        }
    }

    /// <summary>The page's notes: what it saw of its own start (its timings, the splash, its scheme).</summary>
    [SparContract]
    public sealed class ProbeService(Notes notes)
    {
        [SparCommand]
        public Task NoteAsync(string text)
        {
            notes.Add(text);
            return Task.CompletedTask;
        }
    }
}
