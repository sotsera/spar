using Spar;

var app = SparApplication.Create(args, spar =>
{
    // The default policy, with a style nonce for the nonce probe; MIDI is declared so a grant can be seen.
    spar.ContentSecurity(csp => csp.StyleNonce());
#if PROBE_LISTENER_OFF
    spar.ContentSecurity(csp => csp.Reporting(listener: CspReports.Off));
#endif
    spar.Permissions(p => p.Allow(Permission.Midi));
});
return app.Run();

namespace SecurityProbe
{
    /// <summary>The page asks which probe to run, then reports each result, which goes to standard error.</summary>
    [SparContract]
    public sealed class ProbeService
    {
        [SparCommand]
        public Task<string> ModeAsync() => Task.FromResult(Environment.GetEnvironmentVariable("SECURITY_PROBE_MODE") ?? "idle");

        [SparCommand]
        public Task ReportAsync(string text)
        {
            Console.Error.WriteLine($"probe: {text}");
            return Task.CompletedTask;
        }
    }
}
