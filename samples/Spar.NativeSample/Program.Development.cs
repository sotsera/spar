using Sotsera.Spar.Bridge;
using Sotsera.Spar.Development;
using Sotsera.Spar.Windows;

namespace Spar.NativeSample;

internal static partial class Program
{
    private static int RunDevelopment()
    {
        try
        {
            // Fingerprint is generated from this application's actual compiled contract.
            var fingerprint = new SampleBackendBridge(null!).Fingerprint;
            using var session = DevelopmentApplication.Connect(fingerprint);
            SparWindow? currentWindow = null;
            var backend = new SampleBackend(_ => { }, stage =>
            {
                if (stage == "reload")
                {
                    currentWindow!.ReloadDocument();
                }
                else if (stage == "close")
                {
                    currentWindow!.Close();
                }
            }, report => session.Report(report.Stage, report.Detail));
            SparApplication.Run(new SparWindowOptions
            {
                Title = "Spar development",
                Html = "<!doctype html><title>Spar development</title>",
                LoopbackContent = session.Content,
                Bridge = new SparBridge(new SampleBackendBridge(backend),
                    ["greeting.read", "counter.change", "delay.run", "sample.report"]),
                BridgeEvent = session.ObserveBridge,
                UserDataFolder = session.UserDataFolder,
                Created = window => { currentWindow = window; session.Attach(window); }
            });
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }
}
