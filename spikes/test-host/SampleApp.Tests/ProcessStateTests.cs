using System.Collections;
using System.Reflection;
using Spar;

namespace SampleApp.Tests;

[CollectionDefinition(nameof(ProcessWide), DisableParallelization = true)]
public sealed class ProcessWide;

/// <summary>What a process shares: Spar must not write to it or depend on it.</summary>
[Collection(nameof(ProcessWide))]
public sealed class ProcessStateTests(ITestOutputHelper output)
{
    [Fact]
    public async Task An_app_leaves_console_environment_and_directory_alone()
    {
        var directory = Environment.CurrentDirectory;
        var variables = Snapshot();
        var (stdout, stderr) = (Console.Out, Console.Error);
        using var capturedOut = new StringWriter();
        using var capturedError = new StringWriter();
        Console.SetOut(capturedOut);
        Console.SetError(capturedError);
        try
        {
            await using (var app = await TestApps.StartAsync())
            {
                var notes = new NotesClient(app);
                await notes.AddAsync("quiet");
                await notes.CallAsync("notes", "missing", "{}");
            }

            // Unreplaced external dependency: a logged failure, still nothing on the console.
            await using (var app = await Spar.Testing.SparTestApp.StartAsync(AppEntry.Run, cancellationToken: TestContext.Current.CancellationToken))
            {
                await new NotesClient(app).OrderCountAsync();
            }
        }
        finally
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
        }

        Assert.Equal("", capturedOut.ToString());
        Assert.Equal("", capturedError.ToString());
        Assert.Equal(directory, Environment.CurrentDirectory);
        Assert.Equal(variables, Snapshot());
        output.WriteLine("SPIKE two apps ran: nothing on stdout or stderr, same directory and environment");
    }

#if !XUNIT_AOT
    /// <summary>Static fields of the runtime stand-in that are not constants: each must be on the reviewed list.</summary>
    [Fact]
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Reflects over the runtime's fields: JIT lane only.")]
    public void Spar_keeps_no_unreviewed_static_state()
    {
        string[] reviewed =
        [
            "Spar.SparTestSession.CurrentSession",   // AsyncLocal: per test, by design
            "Spar.SparTestSession.<IsSupported>k__BackingField", // feature switch, read once
            "Spar.Windows.UiThread.SharedThread",    // the test process's one UI thread
            "Spar.Windows.WebViewWindow.s_classAtom", // window class, registered once per process
            "Spar.Linux.GtkThread.SharedThread",     // GTK is initialised once per process
            "Spar.SystemClipboard.Constructed",      // spike counter
            "Spar.MiniJson.<Default>k__BackingField", // immutable serializer metadata
            "Spar.MiniJson.s_defaultOptions",
        ];

        var found = typeof(SparApplication).Assembly.GetTypes()
            .Where(t => t.Namespace?.StartsWith("Spar", StringComparison.Ordinal) == true && !t.Name.StartsWith('<'))
            .SelectMany(t => t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(f => !f.IsLiteral)
                .Select(f => $"{t.FullName}.{f.Name}"))
            .ToList();

        output.WriteLine("SPIKE static fields: " + string.Join(", ", found));
        // Source-generated serializer metadata (encoded property names) is immutable.
        Assert.DoesNotContain(found.Except(reviewed), name => !name.StartsWith("Spar.MiniJson.PropName_", StringComparison.Ordinal));
    }
#endif

    private static SortedDictionary<string, string?> Snapshot() =>
        new(Environment.GetEnvironmentVariables().Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string?)e.Value), StringComparer.Ordinal);
}
