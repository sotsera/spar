using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sotsera.Spar.Windows;

namespace Spar.NativeSample;

internal static class Program
{
    private static string? _runtimeFolder;

    [STAThread]
    private static int Main(string[] args)
    {
        var profile = args.Length > 0 ? args[0] : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sotsera", "Spar.NativeSample");
        var closeDuringStartup = args.Contains("--close-during-startup", StringComparer.Ordinal);
        var throwOnDocument = args.Contains("--throw-on-document", StringComparer.Ordinal);
        var closeFromWorker = args.Contains("--close-from-worker", StringComparer.Ordinal);
        if (args.Contains("--missing-runtime", StringComparer.Ordinal))
        {
            _runtimeFolder = Path.Combine(profile, "absent-webview-runtime");
        }
        if (args.Contains("--redirect-profile", StringComparer.Ordinal))
        {
            Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", Path.Combine(profile, "redirected-profile"));
        }
        try
        {
            SparApplication.Run(new SparWindowOptions
            {
                Title = "Spar native host",
                Html = """
                    <!doctype html>
                    <html lang="en">
                    <meta charset="utf-8">
                    <meta name="viewport" content="width=device-width,initial-scale=1">
                    <meta http-equiv="Content-Security-Policy"
                          content="default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'">
                    <title>Spar native host</title>
                    <style>
                      html { color-scheme: dark; font: 18px system-ui; background: #101827; color: #e6edf7 }
                      body { margin: 0; min-height: 100vh; display: grid; place-items: center }
                      main { max-width: 36rem; padding: 3rem }
                      .mark { color: #70e3bd; letter-spacing: .16em; font-weight: 700 }
                      h1 { font-size: clamp(2rem, 5vw, 3.5rem); line-height: 1.1 }
                      p { line-height: 1.6; color: #b3bfd2 }
                      button { font: inherit; color: #101827; background: #70e3bd; border: 0;
                               border-radius: .5rem; padding: .7rem 1rem; cursor: pointer }
                    </style>
                    <main>
                      <span class="mark">SPAR</span>
                      <h1>A small native beginning.</h1>
                      <p>A Win32 window, WebView2, and a .NET backend. This local page runs without a web server.</p>
                      <button id="counter">Local counter: 0</button>
                      <p>The typed bridge and development loop come next.</p>
                    </main>
                    <script>
                      let counter = 0;
                      document.querySelector('#counter').onclick = event => {
                        event.target.textContent = `Local counter: ${++counter}`;
                      };
                    </script>
                    </html>
                    """,
                UserDataFolder = profile,
                BrowserRuntimeFolder = _runtimeFolder,
                Created = window =>
                {
                    Write("window-created", window);
                    if (closeDuringStartup)
                    {
                        window.Close();
                    }
                },
                DocumentLoaded = window =>
                {
                    Write("document-loaded", window);
                    if (throwOnDocument)
                    {
                        throw new InvalidOperationException("Expected sample document callback failure.");
                    }

                    if (closeFromWorker)
                    {
                        _ = Task.Run(() => window.Post(() =>
                        {
                            Write("ui-dispatch", window);
                            window.Close();
                        }));
                    }
                }
            });
            Write("closed");
            return 0;
        }
        catch (Exception error)
        {
            Write("failed", error: error.ToString());
            return 1;
        }
    }

    private static void Write(string stage, SparWindow? window = null, string? error = null)
    {
        using var process = Process.GetCurrentProcess();
        var receipt = new NativeReceipt(stage, DateTimeOffset.UtcNow, Environment.ProcessId, Environment.CurrentManagedThreadId,
            process.StartTime.ToUniversalTime(), RuntimeFeature.IsDynamicCodeSupported ? "CoreCLR" : "NativeAOT",
            window?.Handle.ToInt64(), window?.BrowserVersion, window?.UserDataFolder,
            _runtimeFolder, error);
        Console.WriteLine(JsonSerializer.Serialize(receipt, NativeJsonContext.Default.NativeReceipt));
    }
}

internal sealed record NativeReceipt(string Stage, DateTimeOffset ObservedAt, int ProcessId, int ThreadId,
    DateTime ProcessStartedAtUtc, string Mode, long? Window, string? BrowserVersion, string? UserDataFolder,
    string? RuntimeFolder, string? Error);

[JsonSerializable(typeof(NativeReceipt))]
internal sealed partial class NativeJsonContext : JsonSerializerContext;
