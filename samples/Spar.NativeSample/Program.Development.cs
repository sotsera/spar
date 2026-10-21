using Sotsera.Spar.Development;

namespace Spar.NativeSample;

internal static partial class Program
{
    private static void RunDevelopment()
    {
        DevelopmentApplication.Run(
            title: "Spar development",
            fingerprint: new SampleBackendBridge(null!).Fingerprint,
            contractType: typeof(ISampleBackend),
            createBridge: CreateBridge);
    }
}
