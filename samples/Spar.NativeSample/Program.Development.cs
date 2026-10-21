using Sotsera.Spar.Development;
using Sotsera.Spar.Windows;

namespace Spar.NativeSample;

internal static partial class Program
{
    private static void RunDevelopment()
    {
        var project = Environment.GetEnvironmentVariable("SPAR_DEV_PROJECT");
        var hasSessionOwner = Environment.GetEnvironmentVariable("SPAR_DEV_APP_PIPE") is not null;
        using var projectLaunch = project is null || hasSessionOwner ? null : DevelopmentProjectLaunch.Start(project,
            Environment.GetEnvironmentVariable("SPAR_DEV_COORDINATOR")
                ?? throw new InvalidOperationException("Project launch requires the development coordinator path."));
        var fingerprint = new SampleBackendBridge(null!).Fingerprint;
        using var session = DevelopmentApplication.Connect(fingerprint, typeof(ISampleBackend));
        SparApplication.Run(new SparWindowOptions
        {
            Title = "Spar development",
            Html = "<!doctype html><title>Spar development</title>",
            LoopbackContent = session.Content,
            EnableDevelopmentInspection = session.InspectionEnabled,
            EnableDevelopmentReadiness = true,
            Bridge = CreateBridge(),
            BridgeEvent = session.ObserveBridge,
            UserDataFolder = session.UserDataFolder,
            Created = session.Attach
        });
    }
}
