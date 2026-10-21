using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sotsera.Spar.Bridge;
using Sotsera.Spar.Windows;

namespace Spar.NativeSample;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length > 1 || (args.Length == 1 && args[0].StartsWith("--", StringComparison.Ordinal)))
            {
                throw new ArgumentException("Usage: Spar.NativeSample.exe [browser-profile-directory]");
            }

            var profile = args.Length == 1 ? args[0] : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sotsera", "Spar.NativeSample");
            using var archive = typeof(Program).Assembly.GetManifestResourceStream("Spar.Production.Assets.zip")
                ?? throw new InvalidOperationException("Missing production assets. Rebuild and publish the application.");
            var assets = SparAssetBundle.Read(archive);
            var backend = new SampleBackend(stage => Write(stage), _ => { });
            var bridge = new SparBridge(new SampleBackendBridge(backend), ["greeting.read", "counter.change", "delay.run"]);
            SparApplication.Run(new SparWindowOptions
            {
                Title = "Spar",
                Assets = assets,
                Bridge = bridge,
                UserDataFolder = profile,
                BrowserRuntimeFolder = Environment.GetEnvironmentVariable("SPAR_WEBVIEW2_RUNTIME_FOLDER"),
                Created = window => Write("window-created", window),
                DocumentLoaded = window => Write("document-loaded", window),
                BridgeEvent = value => Write("bridge-" + value.Stage, detail:
                    $"{value.Code}|{value.Operation}|{value.RequestId}|{value.Document}")
            });
            Write("closed");
            return 0;
        }
        catch (Exception error)
        {
            Write("failed", detail: error.Message);
            return 1;
        }
    }

    private static void Write(string stage, SparWindow? window = null, string? detail = null)
    {
        using var process = Process.GetCurrentProcess();
        var receipt = new ProductionReceipt(stage, DateTimeOffset.UtcNow, process.Id, process.StartTime.ToUniversalTime(),
            RuntimeFeature.IsDynamicCodeSupported ? "CoreCLR" : "NativeAOT", window?.Handle.ToInt64(),
            window?.BrowserVersion, window?.UserDataFolder, detail);
        Console.WriteLine(JsonSerializer.Serialize(receipt, ProductionJsonContext.Default.ProductionReceipt));
    }
}

internal sealed record ProductionReceipt(string Stage, DateTimeOffset ObservedAt, int ProcessId,
    DateTime ProcessStartedAtUtc, string Mode, long? Window, string? BrowserVersion, string? UserDataFolder, string? Detail);

[JsonSerializable(typeof(ProductionReceipt))]
internal sealed partial class ProductionJsonContext : JsonSerializerContext;
