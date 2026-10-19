using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sotsera.Spar.Bridge;
using Sotsera.Spar.Windows;

namespace Spar.NativeSample;

internal static partial class Program
{
    private static string? _runtimeFolder;

    [STAThread]
    private static int Main(string[] args)
    {
#if DEBUG
        if (args.Contains("--spar-dev", StringComparer.Ordinal))
        {
            return RunDevelopment();
        }
#endif

        var profile = args.Length > 0 ? args[0] : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sotsera", "Spar.NativeSample");
        var closeDuringStartup = args.Contains("--close-during-startup", StringComparer.Ordinal);
        var throwOnDocument = args.Contains("--throw-on-document", StringComparer.Ordinal);
        var closeFromWorker = args.Contains("--close-from-worker", StringComparer.Ordinal);
        var bridgeProbe = args.Contains("--bridge-probe", StringComparer.Ordinal);
        var bridgeClose = args.Contains("--bridge-probe-close", StringComparer.Ordinal);
        var probe = bridgeProbe || bridgeClose;
        if (args.Contains("--missing-runtime", StringComparer.Ordinal))
        {
            _runtimeFolder = Path.Combine(profile, "absent-webview-runtime");
        }

        if (args.Contains("--redirect-profile", StringComparer.Ordinal))
        {
            Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", Path.Combine(profile, "redirected-profile"));
        }

        SparWindow? currentWindow = null;
        var backend = new SampleBackend(stage => Write(stage), stage =>
        {
            if (stage == "reload")
            {
                currentWindow!.ReloadDocument();
            }
            else if (stage == "close")
            {
                currentWindow!.Close();
            }
        });
        var grants = new List<string> { "greeting.read", "counter.change", "delay.run" };
        if (probe)
        {
            grants.Add("sample.report");
        }

        try
        {
            SparApplication.Run(new SparWindowOptions
            {
                Title = "Spar native host",
                Html = SampleDocument.Create(probe, bridgeClose),
                Bridge = new SparBridge(new SampleBackendBridge(backend), grants),
                BridgeEvent = value =>
                {
                    Write("bridge-" + value.Stage, error: $"{value.Code}|{value.Operation}|{value.RequestId}|{value.Document}");
                    if (probe && value.Code == "navigation_denied")
                    {
                        backend.NavigationDenied();
                        currentWindow!.ReloadDocument();
                    }
                },
                UserDataFolder = profile,
                BrowserRuntimeFolder = _runtimeFolder,
                Created = window =>
                {
                    currentWindow = window;
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
            window?.Handle.ToInt64(), window?.BrowserVersion, window?.UserDataFolder, _runtimeFolder, error);
        Console.WriteLine(JsonSerializer.Serialize(receipt, NativeJsonContext.Default.NativeReceipt));
    }
}

internal sealed record NativeReceipt(string Stage, DateTimeOffset ObservedAt, int ProcessId, int ThreadId,
    DateTime ProcessStartedAtUtc, string Mode, long? Window, string? BrowserVersion, string? UserDataFolder,
    string? RuntimeFolder, string? Error);

[JsonSerializable(typeof(NativeReceipt))]
internal sealed partial class NativeJsonContext : JsonSerializerContext;
