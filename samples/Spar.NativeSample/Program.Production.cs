using Sotsera.Spar.Windows;

namespace Spar.NativeSample;

internal static partial class Program
{
    private static void RunProduction(string profile)
    {
        using var archive = typeof(Program).Assembly.GetManifestResourceStream("Spar.Production.Assets.zip")
            ?? throw new InvalidOperationException("Missing production assets. Rebuild and publish the application.");
        SparApplication.Run(new SparWindowOptions
        {
            Title = "Spar",
            Assets = SparAssetBundle.Read(archive),
            Bridge = CreateBridge(),
            UserDataFolder = profile
        });
    }
}
