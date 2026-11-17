using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using Spar.Hosting;

namespace Spar.Runtime;

/// <summary>
/// What Spar's generated code hands <c>app.Run()</c> on a Windows too old to load the Windows host at all (below
/// Windows 10 1809, WebView2's own floor; .NET still runs on Windows Server 2016): it reports "OS too old" with exit
/// code 7, as the host reports a Windows older than Spar's minimum (plan section 3, "Missing prerequisites"). Standard
/// error and the per-user log get the line, the app's hook runs, and a message box shows unless the hook showed the
/// app's own message.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed partial class UnsupportedWindowsHost : ISparHost
{
    private const uint MessageBoxOk = 0x0;
    private const uint MessageBoxIconError = 0x10;
    private const uint MessageBoxSetForeground = 0x10000;

    public int Run(SparApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var title = app.Catalog.AppAssembly?.GetName().Name ?? "Spar";
        var problem = new StartupProblem(
            StartupProblemKind.OsTooOld,
            "Windows",
            Environment.OSVersion.Version.ToString(3),
            $"10.0.{StartupReport.MinimumWindowsBuild}",
            "This app needs Windows 10 version 22H2 or later. Update Windows, then start the app again.",
            null);

        var line = StartupReport.Describe(problem);
        Console.Error.WriteLine("spar fail: " + line);
        AppendToLog(SparFolders.ForApp(app.AppId, SparDevelopment.IsSupported, app.Builder.FolderOptions).Logs, line);
        if (SparDevelopment.IsAutomatedFromEnvironment())
        {
            return problem.ExitCode;
        }

        var ownMessage = StartupReport.AppShowedItsOwnMessage(
            app.Builder.StartupProblemHandler,
            problem,
            (message, exception) => Console.Error.WriteLine($"spar fail: {message}: {exception}"));
        if (!ownMessage)
        {
            _ = MessageBox(0, $"{title} can't start\n\n{problem.Details}", title, MessageBoxOk | MessageBoxIconError | MessageBoxSetForeground);
        }

        return problem.ExitCode;
    }

    private static void AppendToLog(string folder, string line)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var stamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
            File.AppendAllText(Path.Combine(folder, StartupReport.LogFileName), $"{stamp} spar fail: {line}\r\n");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Standard error already has the line; the app still exits with its code.
        }
    }

    [LibraryImport("user32", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBox(nint owner, string text, string caption, uint type);
}
