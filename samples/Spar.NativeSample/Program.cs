using Sotsera.Spar.Bridge;
using Sotsera.Spar.Windows;

namespace Spar.NativeSample;

internal static partial class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
#if DEBUG
            if (args.Length != 1 || args[0] != "--spar-dev")
            {
                throw new ArgumentException("Start the application through spar.dev.json or the Spar development launch profile.");
            }

            RunDevelopment();
#else
            if (args.Length > 1 || (args.Length == 1 && args[0].StartsWith("--", StringComparison.Ordinal)))
            {
                throw new ArgumentException("Usage: Spar.NativeSample.exe [browser-profile-directory]");
            }

            RunProduction(args.Length == 1 ? args[0] : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sotsera", "Spar.NativeSample"));
#endif
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static SparBridge CreateBridge()
    {
        return new SparBridge(new SampleBackendBridge(new SampleBackend()), ["greeting.read", "counter.change"]);
    }
}
