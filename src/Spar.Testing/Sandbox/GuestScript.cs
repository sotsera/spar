using System.Text;
using System.Xml.Linq;

namespace Spar.Testing;

/// <summary>
/// The text the runner hands Windows Sandbox: the configuration string of <c>wsb start --config</c>, and the guest
/// script (<c>run.cmd</c>) each job runs. Both are built here, apart from the process work, so their quoting is tested.
/// </summary>
internal static class GuestScript
{
    /// <summary>Where the host's staging folder appears in the guest: read-only.</summary>
    public const string GuestInput = @"C:\spar\in";

    /// <summary>Where the host's output folder appears in the guest: the one folder the guest writes to on the host.</summary>
    public const string GuestOutput = @"C:\spar\out";

    /// <summary>The guest's own disk, where each job's payload is copied and its data roots live.</summary>
    public const string GuestWork = @"C:\spar\work";

    /// <summary>The WebView2 Runtime's registration keys (machine-wide, 32-bit view), as Microsoft's distribution guide names them.</summary>
    public const string WebView2Key = @"HKLM\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";

    public const string WebView2StateKey = @"HKLM\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\ClientState\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";

    /// <summary>
    /// A SYSTEM command that points the guest's WebView2 registration at the Runtime whose files are in the guest: the
    /// version (<c>Clients\…\pv</c>) and, what the loader actually follows, the folder (<c>ClientState\…\EBWebView</c>;
    /// <c>docs/evidence/startup-prerequisites</c>). Windows Sandbox takes the files from the host and the registration from its
    /// base image, so after the host's Runtime updated the registration named a version that was not there and every app
    /// found no Runtime (2026-11-19: 157.0.4341.46 registered, 157.0.4341.60 on disk). It changes the disposable guest only.
    /// </summary>
    public static string WebView2Registration =>
        """cmd /c for /d %d in ("C:\Program Files (x86)\Microsoft\EdgeWebView\Application\1*") do if exist "%d\msedgewebview2.exe" (reg add """ +
        $"\"{WebView2Key}\" /v pv /t REG_SZ /d \"%~nxd\" /f & reg add \"{WebView2StateKey}\" /v EBWebView /t REG_SZ /d \"%d\" /f)";

    /// <summary>
    /// The configuration: network, clipboard, printers, camera and microphone off unless <paramref name="features"/> turns
    /// them on; <paramref name="input"/> read-only at <see cref="GuestInput"/>, <paramref name="output"/> writable at
    /// <see cref="GuestOutput"/>. No logon command: the runner starts everything with <c>wsb exec</c>.
    /// </summary>
    public static string Configuration(SandboxFeatures features, string input, string output)
    {
        static XElement Switch(string name, bool on) => new(name, on ? "Enable" : "Disable");
        static XElement Folder(string host, string guest, bool readOnly) => new(
            "MappedFolder",
            new XElement("HostFolder", host),
            new XElement("SandboxFolder", guest),
            new XElement("ReadOnly", readOnly ? "true" : "false"));

        var configuration = new XElement(
            "Configuration",
            Switch("Networking", features.Networking),
            Switch("ClipboardRedirection", features.Clipboard),
            Switch("PrinterRedirection", features.Printers),
            Switch("VideoInput", features.Camera),
            Switch("AudioInput", features.Microphone),
            new XElement("MappedFolders", Folder(input, GuestInput, readOnly: true), Folder(output, GuestOutput, readOnly: false)));
        return configuration.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>
    /// The script of one job, staged as <c>&lt;stage&gt;\run.cmd</c>. It copies the payload to the guest's own disk, sets
    /// the run's variables (the run id that marks a runner's guest, the artifacts folder on the guest's disk, the results
    /// folder in the mapped output), runs the executable with its output in the output folder, and writes the exit code
    /// last, through a temporary file moved into place, so a host that sees <c>exitcode.txt</c> sees all of it.
    /// </summary>
    public static string Job(SandboxJob job, string stage, string runId)
    {
        Validate(job.Executable, nameof(job.Executable));
        Validate(stage, nameof(stage));
        var work = $@"{GuestWork}\{stage}";
        var arguments = new StringBuilder();
        foreach (var argument in Arguments(job))
        {
            arguments.Append(' ').Append(Quote(argument));
        }

        var environment = new StringBuilder();
        foreach (var (name, value) in job.Environment)
        {
            Validate(name, "environment variable");
            Validate(value, name);
            environment.Append(CultureInvariant($"set \"{name}={value}\"\r\n"));
        }

        // Redirections come first ("> file echo text"): "echo 1> file" redirects stream 1, and parentheses break on the
        // ")" of "Program Files (x86)". Paths are fixed by the runner and checked above, so no value needs escaping.
        return $"""
            @echo off
            setlocal
            set "OUT={GuestOutput}\{stage}"
            mkdir "%OUT%" 2>nul
            mkdir "%OUT%\test-results" 2>nul
            > "%OUT%\guest.txt" echo begin %DATE% %TIME%
            >> "%OUT%\guest.txt" whoami
            robocopy "{GuestInput}\{stage}" "{work}\payload" /e /njh /njs /nfl /ndl /np > nul
            if errorlevel 8 (
              > "%OUT%\exitcode.tmp" echo 9009
              move /y "%OUT%\exitcode.tmp" "%OUT%\exitcode.txt" > nul
              exit /b 9009
            )
            mkdir "{work}\artifacts" 2>nul
            set "{SandboxGuest.RunVariable}={runId}"
            set "{TestArtifacts.ArtifactsVariable}={work}\artifacts"
            set "{TestArtifacts.ResultsVariable}=%OUT%\test-results"
            {environment}cd /d "{work}\payload"
            >> "%OUT%\guest.txt" echo start %TIME%
            "{work}\payload\{job.Executable}"{arguments} > "%OUT%\console.log" 2> "%OUT%\error.log"
            set RC=%ERRORLEVEL%
            >> "%OUT%\guest.txt" echo end %TIME% exit %RC%
            > "%OUT%\exitcode.tmp" echo %RC%
            move /y "%OUT%\exitcode.tmp" "%OUT%\exitcode.txt" > nul
            exit /b %RC%

            """.ReplaceLineEndings("\r\n");
    }

    /// <summary>A test run's arguments: the job's own, then the reporters (JSON lines as tests finish; TRX and XML at the end).</summary>
    internal static IEnumerable<string> Arguments(SandboxJob job)
    {
        foreach (var argument in job.Arguments)
        {
            yield return argument;
        }

        if (!job.IsTestRun)
        {
            yield break;
        }

        yield return "-noColor";
        yield return "-noLogo";
        yield return "-reporter";
        yield return "json";
        yield return "-result-trx";
        yield return @"%OUT%\results.trx";
        yield return "-result-xml";
        yield return @"%OUT%\results.xml";
    }

    /// <summary>
    /// One argument for <c>cmd</c> and <c>CommandLineToArgvW</c>: quoted when it has a space. Quotes, <c>%</c> outside
    /// the runner's own <c>%OUT%</c>, and <c>cmd</c>'s other special characters are refused rather than escaped.
    /// </summary>
    internal static string Quote(string argument)
    {
        Validate(argument.Replace("%OUT%", "", StringComparison.Ordinal), "argument");
        return argument.Length > 0 && argument.IndexOfAny([' ', '\t']) < 0 ? argument : $"\"{argument}\"";
    }

    /// <summary>
    /// Refuses what <c>cmd</c> would read as syntax inside a quoted value: a quote, <c>%</c>, <c>^</c>, <c>&amp;</c>,
    /// <c>|</c>, <c>&lt;</c>, <c>&gt;</c>, a line break.
    /// </summary>
    private static void Validate(string value, string what)
    {
        if (value.IndexOfAny(['"', '%', '^', '&', '|', '<', '>', '\r', '\n', '\0']) >= 0)
        {
            throw new ArgumentException($"The {what} '{value}' has a character the guest script cannot pass safely.", what);
        }
    }

    private static string CultureInvariant(FormattableString text) => FormattableString.Invariant(text);
}
