using Spar;

var app = SparApplication.Create(args, spar =>
{
    // The default policy, with a style nonce for the nonce probe; MIDI is declared so a grant can be seen.
    spar.ContentSecurity(csp => csp.StyleNonce());
#if PROBE_LISTENER_OFF
    spar.ContentSecurity(csp => csp.Reporting(listener: CspReports.Off));
#endif
    spar.Permissions(p => p.Allow(Permission.Midi));

    // Every browser default loosened (plan section 3c), for the opt-in run of docs/evidence/m1b-security-defaults.md.
#if PROBE_OPT_INS
    spar.Browser(b => b
        .AllowDevTools()
        .AllowAcceleratorKeys()
        .AllowPasswordSaving()
        .AllowDownloads()
        .AllowHistoryNavigation()
        .AllowContextMenu()
        .AllowPagePicker()
        .AllowBrowserEnvironmentOverrides());
#endif

    // The profile folder from the run's environment, so a relative or network path can be tried.
    if (Environment.GetEnvironmentVariable("SECURITY_PROBE_PROFILE") is { Length: > 0 } profile)
    {
        spar.Folders(f => f.Profile(profile));
    }
});
return app.Run();

namespace SecurityProbe
{
    /// <summary>The page asks which probe to run, then reports each result, which goes to standard error.</summary>
    [SparContract]
    public sealed class ProbeService(SparFolders folders)
    {
        [SparCommand]
        public Task<string> ModeAsync() => Task.FromResult(Environment.GetEnvironmentVariable("SECURITY_PROBE_MODE") ?? "idle");

        [SparCommand]
        public Task ReportAsync(string text)
        {
            Console.Error.WriteLine($"probe: {text}");
            return Task.CompletedTask;
        }

        /// <summary>The folders app code sees through the SparFolders service.</summary>
        [SparCommand]
        public Task<string> FoldersAsync() =>
            Task.FromResult($"data root {folders.DataRoot}; profile {folders.Profile}; logs {folders.Logs}");
    }
}
